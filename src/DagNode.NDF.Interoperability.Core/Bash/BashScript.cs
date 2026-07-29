using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using DagNode.NDF.Interoperability.Model;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Bash;

#region Delegate definitions
/// <summary>Runs before the function working directory is created, letting the caller veto the path.</summary>
/// <param name="workDirPath">Directory about to be created.</param>
public delegate void OnBeforeCreateFunctionWorkDirDelegate(string workDirPath);

/// <summary>Runs once the function working directory exists, for seeding it with extra content.</summary>
/// <param name="workDirPath">Directory that now exists.</param>
public delegate void OnAfterCreateFunctionWorkDirDelegate(string workDirPath);

#endregion Delegate definitions

/// <summary>
/// A long-lived bash process with one script sourced into it, so a run of function calls pays the
/// sourcing cost once. Each call is dispatched asynchronously, writes its streams to per-call
/// {prefix} files under the configured working directory, and is matched back to its caller by a
/// marker line on stdout; the captured output is then parsed into the requested .NET type.
/// Create instances with <see cref="CreateAsync(BashScriptSettings, BashProcessSettings, FunctionWorkDirSettings, ILogger, CancellationTokenSource)"/>
/// and dispose them to stop the process. For one-off calls that do not justify a resident process,
/// use <see cref="FunctionDirect"/> instead.
/// </summary>
public class BashScript : IDisposable
{
	#region Constructor settings
	
	/// <summary>
	/// The configuration being used.
	/// </summary>
	public BashScriptSettings BashScriptSettings { get; }

	/// <summary>
	/// Configure the process running bash scripts.
	/// </summary>
	public BashProcessSettings BashProcessSettings { get; }
	
	/// <summary>
	/// Configuration of the log files created by functions.
	/// Every function generates it's own logfile when executed by default.
	/// TODO: Add global setting to disable this behavior
	/// </summary>
	public FunctionWorkDirSettings FunctionWorkDirSettings { get; }
	
	#endregion Constructor settings
	#region Configuration properties
	
	/// <summary>
	/// When enabled, writes additional info to the console.
	/// Default: false
	/// </summary>
	public bool IsDebug { get => BashScriptSettings.IsDebug; }
	
	/// <summary>
	/// The script file being interpreted.
	/// </summary>
	public AbsolutePath ScriptFilePath { get => BashScriptSettings.ScriptFilePath; }
	
	/// <summary>
	/// A random [a-zA-Z0-9]{4} string generated for every BashScript instance.
	/// Used to mark script function logfiles called through the instance.
	/// Overwrite the value with any arbitrary string to append different tag
	/// to script logfiles, Ex.: ConsoleApp-223707-072-get_array-LvB6.log
	/// </summary>
	public string InstanceMarkerTag { get; set; } = "0000";

	#endregion Exposed configuration properties
	#region Private fields
	
	private readonly FunctionProcessor _functionProcessor;
    private readonly CancellationTokenSource _cts;
    private readonly ILogger _logger;
    
    #endregion Private fields
    #region Lifecycle hooks
    
    /// <summary>
    /// Runs before the working directory is created.
    /// Example usage: Validate location of the working directory
    /// </summary>
    public OnBeforeCreateFunctionWorkDirDelegate? OnBeforeCreateFunctionWorkDir { get; set; }
    /// <summary>
    /// Runs after the working directory is created.
    /// Example usage: Setup additional content necessary for running functions from the script.
    /// </summary>
    public OnAfterCreateFunctionWorkDirDelegate? OnAfterCreateFunctionWorkDir { get; set; }

    /// <summary>
    /// As the handler is async and constructors do not support async,
    /// the BashScript instance can be created with CreateWithScriptReadyAsyncHandler factory
    /// from where the handler is being called after successful BashScript instantiation. 
    /// Bash script file is sourced, so all it's functions should be available.
    /// </summary>
    public event AsyncTypedEventHandler<BashScript, ScriptReadyEventArgs>? EventHandlerScriptSourcedAsync;
    /// <summary>
    /// Optional setup hook running before every bash script function call.
    /// </summary>
    public event AsyncTypedEventHandler<BashScript, FunctionStartEventArgs>? EventHandlerFunctionStartAsync;
    /// <summary>
    /// Optional hook running after bash script function call.
    /// Inspect status code and line results captured by stream output reader before it is converted to strongly typed .NET type.
    /// </summary>
    public event AsyncTypedEventHandler<BashScript, FunctionFinishedEventArgs>? EventHandlerFunctionFinishedAsync;
    
    #endregion Lifecycle hooks
    #region Configure function work directory
    /// <summary>
    /// Directory this instance's function calls write their {prefix} files to, as resolved by
    /// <see cref="FunctionWorkDirSettings.ConfigureFunctionWorkDir"/> and verified to exist during construction.
    /// </summary>
    public AbsolutePath ConfiguredFunctionWorkDir { get => _configuredFunctionWorkDir; }
    private AbsolutePath _configuredFunctionWorkDir;
    private string ConfigureAndValidateFunctionWorkDir(string scriptFileNameNormalized)
    {
	    try {
		    // Get the directory path from the delegate.
		    var functionWorkDirPath = FunctionWorkDirSettings.ConfigureFunctionWorkDir(
			    new ConfigureFunctionWorkDirEventArgs(scriptFileNameNormalized, this.InstanceMarkerTag,
				    this.BashScriptSettings, this.FunctionWorkDirSettings));
            
		    if (string.IsNullOrWhiteSpace(functionWorkDirPath)) {
			    throw new ArgumentException("Configured working directory path cannot be null or empty.");
		    }
		    
		    // Optional pre-creation hook for user-defined actions.
		    OnBeforeCreateFunctionWorkDir?.Invoke(functionWorkDirPath);

		    // Validate or create FunctionWorkDir.
		    EnsureDirectoryExists(functionWorkDirPath, nameof(FunctionWorkDirSettings.FunctionBaseWorkDir));
		    
		    // Validate or create Process PID directory.
		    EnsureDirectoryExists(FunctionWorkDirSettings.FunctionBasePidDir, nameof(FunctionWorkDirSettings.FunctionBasePidDir));
		    
		    // Optional after-creation hook for user-defined actions
		    OnAfterCreateFunctionWorkDir?.Invoke(functionWorkDirPath);

		    return functionWorkDirPath;
	    }
	    catch (Exception ex) {
		    _logger.LogError(ex, "Error configuring or creating working directory");
		    throw new InteroperabilityException(ex, "Error configuring or creating working directory");
	    }
    }
    private void EnsureDirectoryExists(string path, string name)
    {
	    if (Directory.Exists(path)) {
		    if (IsDebug) _logger.LogDebug("Using existing {Name} directory: {Path}", name, path);
	    } else {
		    if (IsDebug) _logger.LogDebug("Creating {Name} directory: {Path}", name ,path);
		    Directory.CreateDirectory(path);
	    }
    }
    
    #endregion Configure working directory
    #region Factory overloads
    /// <summary>Creates and starts an instance for a script named relative to the program directory.</summary>
    /// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
    /// <param name="bashProcessSettings">Process configuration; defaults are used when null.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cts">Token source cancelling the resident process; a new one is created when null.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(string scriptFileName,
	    BashProcessSettings? bashProcessSettings,
	    ILogger? logger = null, CancellationTokenSource? cts = null)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFileName),
		    bashProcessSettings, FunctionWorkDirSettings.CreateFactoryDefault, logger, cts).ConfigureAwait(false);

    /// <summary>Creates and starts an instance, overriding only where the function files are written.</summary>
    /// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
    /// <param name="scriptLoggerSettings">Working directory configuration; defaults are used when null.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cts">Token source cancelling the resident process; a new one is created when null.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(string scriptFileName,
	    FunctionWorkDirSettings? scriptLoggerSettings = null,
	    ILogger? logger = null, CancellationTokenSource? cts = null)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFileName),
		    BashProcessSettings.CreateFactoryDefault, scriptLoggerSettings, logger, cts).ConfigureAwait(false);

    /// <summary>Creates and starts an instance with both process and working directory configured.</summary>
    /// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
    /// <param name="bashProcessSettings">Process configuration.</param>
    /// <param name="functionWorkDirSettings">Working directory configuration.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cts">Token source cancelling the resident process; a new one is created when null.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(string scriptFileName,
	    BashProcessSettings bashProcessSettings, FunctionWorkDirSettings functionWorkDirSettings,
	    ILogger? logger = null, CancellationTokenSource? cts = null)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFileName),
		    bashProcessSettings, functionWorkDirSettings, logger, cts).ConfigureAwait(false);

    /// <summary>Creates and starts an instance for a script at an absolute path.</summary>
    /// <param name="scriptFilePath">Absolute path to the bash script.</param>
    /// <param name="bashProcessSettings">Process configuration; defaults are used when null.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cts">Token source cancelling the resident process; a new one is created when null.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(AbsolutePath scriptFilePath,
	    BashProcessSettings? bashProcessSettings,
	    ILogger? logger = null, CancellationTokenSource? cts = null)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFilePath),
		    bashProcessSettings, FunctionWorkDirSettings.CreateFactoryDefault, logger, cts).ConfigureAwait(false);

    /// <summary>Creates and starts an instance at an absolute path, overriding only the working directory.</summary>
    /// <param name="scriptFilePath">Absolute path to the bash script.</param>
    /// <param name="scriptLoggerSettings">Working directory configuration; defaults are used when null.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cts">Token source cancelling the resident process; a new one is created when null.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(AbsolutePath scriptFilePath,
	    FunctionWorkDirSettings? scriptLoggerSettings = null,
	    ILogger? logger = null, CancellationTokenSource? cts = null)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFilePath),
		    BashProcessSettings.CreateFactoryDefault, scriptLoggerSettings, logger, cts).ConfigureAwait(false);

    /// <summary>Creates and starts an instance at an absolute path with both settings configured.</summary>
    /// <param name="scriptFilePath">Absolute path to the bash script.</param>
    /// <param name="bashProcessSettings">Process configuration.</param>
    /// <param name="functionWorkDirSettings">Working directory configuration.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cts">Token source cancelling the resident process; a new one is created when null.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(AbsolutePath scriptFilePath,
	    BashProcessSettings bashProcessSettings, FunctionWorkDirSettings functionWorkDirSettings,
	    ILogger? logger = null, CancellationTokenSource? cts = null)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFilePath),
		    bashProcessSettings, functionWorkDirSettings, logger, cts).ConfigureAwait(false);
    #endregion Factory overloads
    #region Async factory
    /// <summary>
    /// Default factory method. Validates the script, prepares the working and PID directories,
    /// starts the resident bash process and sources the script, so the returned instance is ready
    /// to take function calls. Construction is asynchronous because sourcing is.
    /// </summary>
    /// <param name="bashScriptSettings">Which script to source and how its calls are tagged.</param>
    /// <param name="bashProcessSettings">Process configuration; defaults are used when null.</param>
    /// <param name="functionWorkDirSettings">Working directory configuration; defaults are used when null.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/> when null.</param>
    /// <param name="cts">Token source cancelling the resident process; a new one is created when null.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    /// <exception cref="InteroperabilityException">When <paramref name="bashScriptSettings"/> is null, the script is missing or unreadable, or the working directory cannot be prepared.</exception>
    public static async Task<BashScript> CreateAsync(
	    BashScriptSettings bashScriptSettings,
	    BashProcessSettings? bashProcessSettings = null,	
	    FunctionWorkDirSettings? functionWorkDirSettings = null,
	    ILogger? logger = null,
	    CancellationTokenSource? cts = null)
    {
	    if (bashScriptSettings == null) throw new InteroperabilityException(nameof(bashScriptSettings));
	    cts ??= new CancellationTokenSource();
	    logger ??= LoggingFactory.CreateLogger<BashScript>();
	    var bashScript = new BashScript(bashScriptSettings, bashProcessSettings, functionWorkDirSettings, logger, cts);
	    // Start bash processes and check for enqueued function calls
	    await bashScript.StartAsync().ConfigureAwait(false);
	    return bashScript;
    }
    
    #endregion Async factory
    #region Constructor logic
    
    private BashScript(
	    BashScriptSettings bashScriptSettings,
	    BashProcessSettings? bashProcessSettings = null,
	    FunctionWorkDirSettings? functionWorkDirSettings = null,
	    ILogger? logger = null,
	    CancellationTokenSource? cts = null)
    {
	    BashScriptSettings = bashScriptSettings ?? throw new ArgumentException(nameof(bashScriptSettings));
	    BashProcessSettings = bashProcessSettings ?? BashProcessSettings.CreateFactoryDefault;
	    FunctionWorkDirSettings = functionWorkDirSettings ?? FunctionWorkDirSettings.CreateFactoryDefault;
	    _logger = logger ?? LoggingFactory.CreateLogger<BashScript>();
	    // IsDebug gates LogDebug calls, so it tracks what the configured logger will actually emit.
	    BashScriptSettings.IsDebug = _logger.IsEnabled(LogLevel.Debug);
	    _cts = cts ?? new CancellationTokenSource();
	    if (bashScriptSettings.UseInstanceMarkerTag) {
		    InstanceMarkerTag = Helpers.GenerateRandomString(bashScriptSettings.InstanceMarkerTagLength);
	    }
	    // Basic check we have a valid accessible bash script
	    Validation.CheckScriptFile(bashScriptSettings.ScriptFilePath);

	    // Ensure the working directory is configured and accessible before proceeding
	    string workDir = ConfigureAndValidateFunctionWorkDir(bashScriptSettings.ScriptFileNameNormalized);
	    _configuredFunctionWorkDir = AbsolutePath.Create(workDir);
	    
        // Create a new function processor instance, this does not start bash processes yet
        _functionProcessor ??= new FunctionProcessor(this, _logger, _cts);
    }
    
    #endregion Constructor logic
    
    /// <summary>
    /// Blocking wrapper over CallFunctionAsync&lt;T&gt;, for callers that are not themselves async.
    /// Every await on this path uses ConfigureAwait(false), so blocking here does not deadlock on a
    /// synchronization context; it does hold the calling thread for the whole bash call. A handler
    /// attached to <see cref="EventHandlerFunctionStartAsync"/> or
    /// <see cref="EventHandlerFunctionFinishedAsync"/> that resumes on a captured context
    /// reintroduces the deadlock, since it runs inside this call. From async code call
    /// CallFunctionAsync&lt;T&gt; instead.
    /// </summary>
    /// <typeparam name="T">Type the function's output is parsed into.</typeparam>
    /// <param name="functionName">Name of a function defined in the sourced script.</param>
    /// <param name="functionArgs">Arguments passed to the function; each is quoted, so spaces are safe.</param>
    /// <returns>The function's output parsed as <typeparamref name="T"/>.</returns>
    public T CallFunction<T>(string functionName, params string[] functionArgs)
	    => CallFunctionAsync<T>(functionName, functionArgs)
		    .GetAwaiter()
		    .GetResult();

    /// <summary>
    /// Get strongly typed value from the combination of function standard output and standard error streams.
    /// Standard or error outputs can be suppressed by configuring BashProcessSettings.RedirectStandardOutput
    /// or BashProcessSettings.RedirectStandardError (true/false). This method calls RunFunctionAsync,
    /// then tries to parse function output to the concrete type. 
    /// </summary>
    /// <param name="functionName">Name of the bash function to execute, the function must be present in the script configured for this instance</param>
    /// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
    /// <param name="asyncBefore">Run custom async hook just before the bash function is executed.</param>
    /// <param name="asyncThen">Run custom async hook just after the bash function is executed.</param>
    /// <param name="callOptions"></param>
    /// <param name="timeout"></param>
    /// <typeparam name="T">Type of the function result</typeparam>
    /// <returns>Strongly typed function result</returns>
    /// <exception cref="NotSupportedException"></exception>
    public async Task<T> CallFunctionAsync<T>(string functionName, string[]? functionArgs = null,
	    Func<FunctionStartEventArgs, Task>? asyncBefore = null, Func<FunctionFinishedEventArgs, Task>? asyncThen = null,
	    CallOptions? callOptions = null, TimeSpan? timeout = null)
    {
	    
	    callOptions ??= CallOptions.CreateFactoryDefault;
	    FunctionResult functionResult = await RunFunctionAsync(functionName, functionArgs,
		    asyncBefore, asyncThen, callOptions, timeout).ConfigureAwait(false);
	    
	    string? str = GetResultStringFromFunctionResult(functionResult, callOptions.ReadResultFrom);
	    if (str == null && typeof(T) != typeof(bool)) throw new InteroperabilityException("Function result is null");
	    string resultString = str!;
	    
        return typeof(T) switch {
            var t when t == typeof(string) => (T)(object)resultString,
            var t when t == typeof(int) => ParseInt<T>(resultString, functionName),
            var t when t == typeof(long) => ParseLong<T>(resultString, functionName),
            var t when t == typeof(double) => ParseDouble<T>(resultString, functionName),
            var t when t == typeof(decimal) => ParseDecimal<T>(resultString, functionName),
            var t when t == typeof(bool) => (T)(object)(functionResult.ExitCode == 0), // Exit code 0 indicates success
            var t when t == typeof(string[]) => (T)(object)resultString.Split('\n').ToArray(),
            var t when t == typeof(List<string>) => (T)(object)resultString.Split('\n').ToList(),
            { IsEnum: true } => (T) Enum.Parse(typeof(T), resultString),
            _ => throw new NotSupportedException($"The type '{typeof(T)}' is not supported")
        };
    }

    private static string? GetResultStringFromFunctionResult(FunctionResult result, FunctionResultLocation resultLocation)
    {
	    // Read function results from user defined location,
	    // standard output written to {prefix}.out by default
	    return resultLocation.LocationType switch {
		    ResultLocationType.Default or ResultLocationType.PrefixOut => result.StandardOutput,
		    ResultLocationType.PrefixErr => result.StandardError,
		    ResultLocationType.PrefixLog => result.CustomLog,
		    ResultLocationType.CustomPath => result.CustomFile,
		    _ => throw new NotImplementedException(nameof(resultLocation))
	    };
    }

    /// <summary>
    /// Execute the bash function.
    /// You can implement result processing logic using EventHandlerFunctionFinishedAsync.
    /// </summary>
    /// <param name="functionName"></param>
    /// <param name="functionArgs"></param>
    /// <param name="asyncBefore">Run custom async hook just before the bash function is executed.</param>
    /// <param name="asyncThen">Run custom async hook just after the bash function is executed.</param>
    /// <param name="options"></param>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public async Task<FunctionResult> RunFunctionAsync(
	    string functionName, string[]? functionArgs,
	    Func<FunctionStartEventArgs, Task>? asyncBefore = null,
	    Func<FunctionFinishedEventArgs, Task>? asyncThen = null,
	    CallOptions? options = null,
	    TimeSpan? timeout = null)
    {
	    Validation.CheckFunctionName(functionName);

	    //TODO: Keep track of subprocess PIDs and implement function timeout
	    if (timeout != null) throw new NotImplementedException(nameof(timeout));

	    var callOptions = options ?? CallOptions.CreateFactoryDefault;

	    // Assign the same sequence number to files generated by the function, thread safe
	    long sequenceNumber = this.GetIncrementedFunctionCallSequenceNumber(functionName);

	    string functionMarkerTag = FunctionWorkDirSettings.ConfigureFunctionMarkerTag(
		    new ConfigureFunctionMarkerEventArgs( // Using sequence number in function marker tag (thread safe)
				functionName.ToAsciiNoWhitespace(), sequenceNumber, this.InstanceMarkerTag,
				this.BashScriptSettings, this.FunctionWorkDirSettings));
	    
	    if (!Directory.Exists(_configuredFunctionWorkDir)) throw new InteroperabilityException($"Function directory {_configuredFunctionWorkDir} does not exist");
	    var functionFiles = FunctionFiles // Configure {prefix}.in {prefix}.out {prefix}.err file paths
		    .ConfigureFilePaths(_configuredFunctionWorkDir, callOptions, functionMarkerTag);
	    
	    var functionStartEventArgs = new FunctionStartEventArgs(
		    callOptions, _configuredFunctionWorkDir, functionFiles,
		    functionName, functionMarkerTag, sequenceNumber, functionArgs
		    //timeout
		    );
		
	    // Run optional hooks
	    if (asyncBefore != null) await asyncBefore(functionStartEventArgs).ConfigureAwait(false); // Optional function-specific hook
	    if (EventHandlerFunctionStartAsync != null) {
		    // Invoked for all functions
		    await EventHandlerFunctionStartAsync.Invoke(this, functionStartEventArgs).ConfigureAwait(false);
		}
	    
		// Enqueue function call
		// Construct function call command started by bash, moved to FunctionProcessor.RunEnqueuedFunctionCallsAsync
		// string bashProcessArgs = $"{BashProcess.FUNCTION_START_ASYNC_WRAPPER} {functionMarkerTag} "{redirectionCmd}" {functionName} \"{prefixPath}\" {quotedFunctionArgs}";
		_functionProcessor.EnqueueCallFunctionItemThreadSafe(functionStartEventArgs);
		
		// Wait for the result asynchronously,
		// set automatically by FunctionProcessor when it becomes available
		FunctionResult result = await functionStartEventArgs.FunctionResultCompletionSource.Task.ConfigureAwait(false);
		
	    var functionFinishedEventArgs = new FunctionFinishedEventArgs {
		    FunctionStartEventArgs = functionStartEventArgs,
		    ExitCode = result.ExitCode,
		    Result = result,
		    Parameters = new Dictionary<string, object>()
	    };
	    if (asyncThen != null) await asyncThen(functionFinishedEventArgs).ConfigureAwait(false); // Optional function-specific hook
	    if (EventHandlerFunctionFinishedAsync != null) {
		    // Invoked for all functions
		    await EventHandlerFunctionFinishedAsync.Invoke(this, functionFinishedEventArgs).ConfigureAwait(false);   
	    }
		
	    return result;
    }

    /// <summary>
    /// Re-sources the configured script and any global function scripts into the running bash
    /// process, then raises <see cref="EventHandlerScriptSourcedAsync"/>. Already performed during
    /// <see cref="CreateAsync(BashScriptSettings, BashProcessSettings, FunctionWorkDirSettings, ILogger, CancellationTokenSource)"/>;
    /// call it again to pick up edits to the script file.
    /// </summary>
    /// <returns>A task completing once every script is sourced and the hook has run.</returns>
    public async Task SourceScriptFilesAsync()
    {
	    await _functionProcessor.SourceScriptFilesAsync().ConfigureAwait(false);
	    // Optional hook when all script files are successfully sourced
	    if (EventHandlerScriptSourcedAsync != null) {
		    await EventHandlerScriptSourcedAsync.Invoke(this, new ScriptReadyEventArgs()).ConfigureAwait(false);   
	    }
    }
    
	#region Private TryParsers
    private static T ParseInt<T>(string output, string functionName)
    {
        if (!int.TryParse(output, out int number))
            throw new InvalidOperationException($"The value '{output}' returned by function '{functionName}' is not a valid int");
        return (T)(object)number;
    }
    
    private static T ParseLong<T>(string output, string functionName)
    {
	    if (!long.TryParse(output, out long number))
		    throw new InvalidOperationException($"The value '{output}' returned by function '{functionName}' is not a valid long");
	    return (T)(object)number;
    }
    
    private static T ParseDouble<T>(string output, string functionName)
    {
	    if (!double.TryParse(output, out double number))
		    throw new InvalidOperationException($"The value '{output}' returned by function '{functionName}' is not a valid double");
	    return (T)(object)number;
    }
    
    private static T ParseDecimal<T>(string output, string functionName)
    {
	    if (!decimal.TryParse(output, out decimal number))
		    throw new InvalidOperationException($"The value '{output}' returned by function '{functionName}' is not a valid decimal");
	    return (T)(object)number;
    }
    
    #endregion Private TryParsers
    #region Function sequence numbers

    private ConcurrentDictionary<string, long> _functionSequenceNumbers = new();
    private long GetIncrementedFunctionCallSequenceNumber(string key) =>
	    _functionSequenceNumbers.AddOrUpdate(
		    key,
		    addValue: 1, // Default value if key does not exist
		    updateValueFactory: (existingKey, currentValue) => currentValue + 1 // Increment existing value
	    );
    
    #endregion Function sequence numbers
    private async Task StartAsync() => await _functionProcessor.StartAsync().ConfigureAwait(false);
    /// <summary>
    /// Renders the function calls this instance has dispatched, in order, for diagnostics.
    /// </summary>
    /// <returns>A human-readable trace of the call history.</returns>
    public string GetTrace() => _functionProcessor.PrintFunctionCallTrace();
    
	#region Dispose
	
    /// <summary>
    /// Cancels outstanding function calls and shuts down the resident bash process. Calls still in
    /// flight do not complete, so await them before disposing.
    /// </summary>
    public void Dispose()
    {
	    if (!_cts.IsCancellationRequested) _cts.Cancel();
        _functionProcessor.Dispose();
    }
    
    #endregion Dispose
}

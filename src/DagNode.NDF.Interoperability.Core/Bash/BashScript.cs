using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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
/// Create instances with <see cref="CreateAsync(BashScriptSettings, BashProcessSettings, FunctionWorkDirSettings, ILogger, CancellationToken)"/>
/// and dispose them to stop the process. <see cref="DisposeAsync"/> first lets calls already
/// dispatched finish; the synchronous <see cref="Dispose"/> cancels them instead. For one-off calls
/// that do not justify a resident process, use <see cref="FunctionDirect"/> instead.
/// </summary>
public class BashScript : IDisposable, IAsyncDisposable
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
    // Owned, never accepted from a caller: it controls this instance's resident bash process, and a
    // shared source would let unrelated code tear the session down. Per-call and startup cancellation
    // arrive as a CancellationToken instead, linked with this source where they are awaited.
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly ILogger _logger;
    private bool _disposed;

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
    /// <param name="cancellationToken">Cancels startup and script sourcing; the resident process is stopped by disposing the instance.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(string scriptFileName,
	    BashProcessSettings? bashProcessSettings,
	    ILogger? logger = null, CancellationToken cancellationToken = default)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFileName),
		    bashProcessSettings, FunctionWorkDirSettings.CreateFactoryDefault, logger, cancellationToken).ConfigureAwait(false);

    /// <summary>Creates and starts an instance, overriding only where the function files are written.</summary>
    /// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
    /// <param name="scriptLoggerSettings">Working directory configuration; defaults are used when null.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cancellationToken">Cancels startup and script sourcing; the resident process is stopped by disposing the instance.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(string scriptFileName,
	    FunctionWorkDirSettings? scriptLoggerSettings = null,
	    ILogger? logger = null, CancellationToken cancellationToken = default)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFileName),
		    BashProcessSettings.CreateFactoryDefault, scriptLoggerSettings, logger, cancellationToken).ConfigureAwait(false);

    /// <summary>Creates and starts an instance with both process and working directory configured.</summary>
    /// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
    /// <param name="bashProcessSettings">Process configuration.</param>
    /// <param name="functionWorkDirSettings">Working directory configuration.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cancellationToken">Cancels startup and script sourcing; the resident process is stopped by disposing the instance.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(string scriptFileName,
	    BashProcessSettings bashProcessSettings, FunctionWorkDirSettings functionWorkDirSettings,
	    ILogger? logger = null, CancellationToken cancellationToken = default)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFileName),
		    bashProcessSettings, functionWorkDirSettings, logger, cancellationToken).ConfigureAwait(false);

    /// <summary>Creates and starts an instance for a script at an absolute path.</summary>
    /// <param name="scriptFilePath">Absolute path to the bash script.</param>
    /// <param name="bashProcessSettings">Process configuration; defaults are used when null.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cancellationToken">Cancels startup and script sourcing; the resident process is stopped by disposing the instance.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(AbsolutePath scriptFilePath,
	    BashProcessSettings? bashProcessSettings,
	    ILogger? logger = null, CancellationToken cancellationToken = default)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFilePath),
		    bashProcessSettings, FunctionWorkDirSettings.CreateFactoryDefault, logger, cancellationToken).ConfigureAwait(false);

    /// <summary>Creates and starts an instance at an absolute path, overriding only the working directory.</summary>
    /// <param name="scriptFilePath">Absolute path to the bash script.</param>
    /// <param name="scriptLoggerSettings">Working directory configuration; defaults are used when null.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cancellationToken">Cancels startup and script sourcing; the resident process is stopped by disposing the instance.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(AbsolutePath scriptFilePath,
	    FunctionWorkDirSettings? scriptLoggerSettings = null,
	    ILogger? logger = null, CancellationToken cancellationToken = default)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFilePath),
		    BashProcessSettings.CreateFactoryDefault, scriptLoggerSettings, logger, cancellationToken).ConfigureAwait(false);

    /// <summary>Creates and starts an instance at an absolute path with both settings configured.</summary>
    /// <param name="scriptFilePath">Absolute path to the bash script.</param>
    /// <param name="bashProcessSettings">Process configuration.</param>
    /// <param name="functionWorkDirSettings">Working directory configuration.</param>
    /// <param name="logger">Logger for this instance; falls back to <see cref="LoggingFactory"/>.</param>
    /// <param name="cancellationToken">Cancels startup and script sourcing; the resident process is stopped by disposing the instance.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    public static async Task<BashScript> CreateAsync(AbsolutePath scriptFilePath,
	    BashProcessSettings bashProcessSettings, FunctionWorkDirSettings functionWorkDirSettings,
	    ILogger? logger = null, CancellationToken cancellationToken = default)
	    => await CreateAsync(BashScriptSettings.CreateFactoryDefault(scriptFilePath),
		    bashProcessSettings, functionWorkDirSettings, logger, cancellationToken).ConfigureAwait(false);
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
    /// <param name="cancellationToken">Cancels startup and script sourcing; the resident process is stopped by disposing the instance.</param>
    /// <returns>A started instance with the script already sourced.</returns>
    /// <exception cref="InteroperabilityException">When <paramref name="bashScriptSettings"/> is null, the script is missing or unreadable, or the working directory cannot be prepared.</exception>
    public static async Task<BashScript> CreateAsync(
	    BashScriptSettings bashScriptSettings,
	    BashProcessSettings? bashProcessSettings = null,
	    FunctionWorkDirSettings? functionWorkDirSettings = null,
	    ILogger? logger = null,
	    CancellationToken cancellationToken = default)
    {
	    if (bashScriptSettings == null) throw new InteroperabilityException(nameof(bashScriptSettings));
	    logger ??= LoggingFactory.CreateLogger<BashScript>();
	    var bashScript = new BashScript(bashScriptSettings, bashProcessSettings, functionWorkDirSettings, logger);
	    try {
		    // Start bash processes and check for enqueued function calls
		    await bashScript.StartAsync(cancellationToken).ConfigureAwait(false);
	    } catch {
		    // Startup threw or was cancelled (a wedged tmpfs mount, or a script on a slow/hung network
		    // share) after the bash process or tmpfs may have been acquired. Dispose the half-built
		    // instance so nothing leaks, then rethrow: CreateAsync returns a started instance or nothing.
		    bashScript.Dispose();
		    throw;
	    }
	    return bashScript;
    }

    #endregion Async factory
    #region Constructor logic

    private BashScript(
	    BashScriptSettings bashScriptSettings,
	    BashProcessSettings? bashProcessSettings = null,
	    FunctionWorkDirSettings? functionWorkDirSettings = null,
	    ILogger? logger = null)
    {
	    BashScriptSettings = bashScriptSettings ?? throw new ArgumentException(nameof(bashScriptSettings));
	    BashProcessSettings = bashProcessSettings ?? BashProcessSettings.CreateFactoryDefault;
	    FunctionWorkDirSettings = functionWorkDirSettings ?? FunctionWorkDirSettings.CreateFactoryDefault;
	    _logger = logger ?? LoggingFactory.CreateLogger<BashScript>();
	    // IsDebug gates LogDebug calls, so it tracks what the configured logger will actually emit.
	    BashScriptSettings.IsDebug = _logger.IsEnabled(LogLevel.Debug);
	    if (bashScriptSettings.UseInstanceMarkerTag) {
		    InstanceMarkerTag = Helpers.GenerateRandomString(bashScriptSettings.InstanceMarkerTagLength);
	    }
	    // Basic check we have a valid accessible bash script
	    Validation.CheckScriptFile(bashScriptSettings.ScriptFilePath);

	    // Ensure the working directory is configured and accessible before proceeding
	    string workDir = ConfigureAndValidateFunctionWorkDir(bashScriptSettings.ScriptFileNameNormalized);
	    _configuredFunctionWorkDir = AbsolutePath.Create(workDir);
	    
        // Create a new function processor instance, this does not start bash processes yet
        _functionProcessor ??= new FunctionProcessor(this, _logger, _lifetimeCts);
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
    /// Runs the function and converts its captured output to <typeparamref name="T"/>. Built-in
    /// conversions cover <see cref="string"/>, the numeric types, <see cref="bool"/> (exit code 0
    /// means true), an enum (parsed case-insensitively after trimming), and
    /// <see cref="string"/>[]/<see cref="List{T}"/>/<see cref="IEnumerable{T}"/> of string split on
    /// newline. Supply <paramref name="resultParser"/> to take over the conversion for any type, including
    /// a custom bool predicate over stdout rather than the exit code. For a collection split on a separator
    /// other than newline, or whose items parse to a type other than <see cref="string"/>, use
    /// <see cref="CallFunctionEnumerableAsync{TItem}"/>.
    /// </summary>
    /// <remarks>Safe to call concurrently, but concurrent calls run at the same time in bash: a function
    /// that reads or writes shared state (a fixed file, an external service) must be reentrant, or the
    /// caller must serialize those calls. See <see cref="RunFunctionAsync"/>.</remarks>
    /// <param name="functionName">Name of the bash function to execute, present in this instance's script.</param>
    /// <param name="functionArgs">Arguments passed to the function; each is quoted, so spaces are safe.</param>
    /// <param name="asyncBefore">Async hook run just before the function is dispatched.</param>
    /// <param name="asyncThen">Async hook run just after the function returns.</param>
    /// <param name="callOptions">Stream redirection and where the result is read from; defaults when null.</param>
    /// <param name="resultParser">When set, converts the <see cref="FunctionResult"/> itself, bypassing the built-in switch.</param>
    /// <param name="timeout">Stops waiting for the result after this duration; null waits indefinitely. See <see cref="RunFunctionAsync"/>.</param>
    /// <param name="cancellationToken">Cancels waiting for the result.</param>
    /// <typeparam name="T">Type the function's output is parsed into.</typeparam>
    /// <returns>The function's output parsed as <typeparamref name="T"/>.</returns>
    /// <exception cref="NotSupportedException">When <typeparamref name="T"/> has no built-in conversion and no <paramref name="resultParser"/> is supplied.</exception>
    /// <exception cref="TimeoutException">When <paramref name="timeout"/> elapses before the result arrives.</exception>
    public async Task<T> CallFunctionAsync<T>(string functionName, string[]? functionArgs = null,
	    Func<FunctionStartEventArgs, Task>? asyncBefore = null, Func<FunctionFinishedEventArgs, Task>? asyncThen = null,
	    CallOptions? callOptions = null,
	    Func<FunctionResult, T>? resultParser = null,
	    TimeSpan? timeout = null,
	    CancellationToken cancellationToken = default)
    {
	    callOptions ??= CallOptions.CreateFactoryDefault;
	    FunctionResult functionResult = await RunFunctionAsync(functionName, functionArgs,
		    asyncBefore, asyncThen, callOptions, timeout, cancellationToken).ConfigureAwait(false);

	    // A caller-supplied parser owns the whole conversion, so it can read any stream or the exit code.
	    if (resultParser != null) return resultParser(functionResult);

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
            var t when t == typeof(string[]) => (T)(object)SplitResult(resultString, "\n"),
            var t when t == typeof(List<string>) => (T)(object)SplitResult(resultString, "\n").ToList(),
            var t when t == typeof(IEnumerable<string>) => (T)(object)SplitResult(resultString, "\n"),
            { IsEnum: true } => (T) Enum.Parse(typeof(T), resultString.Trim(), ignoreCase: true),
            _ => throw new NotSupportedException($"The type '{typeof(T)}' is not supported")
        };
    }

    /// <summary>
    /// Runs the function, splits its captured output on <paramref name="resultSeparator"/> (newline by
    /// default, so one record per line), drops the first <paramref name="skipLines"/> records (a header),
    /// and projects each remaining record with <paramref name="lineParser"/> into
    /// <typeparamref name="TItem"/>. Projection is deferred — each
    /// record is parsed as the returned sequence is enumerated, so a <paramref name="lineParser"/> that
    /// throws surfaces on enumeration, not on the await; materialize with <c>ToList()</c> to pull the whole
    /// result. With no <paramref name="lineParser"/>, <typeparamref name="TItem"/> must be
    /// <see cref="string"/>.
    /// </summary>
    /// <param name="functionName">Name of the bash function to execute, present in this instance's script.</param>
    /// <param name="functionArgs">Arguments passed to the function; each is quoted, so spaces are safe.</param>
    /// <param name="resultSeparator">Separator the captured output is split on; "\n" (one record per line) by default. Other delimited outputs split too, e.g. " " for <c>"${arr[@]}"</c> or ":" for a PATH-like value.</param>
    /// <param name="skipLines">Number of leading records to drop before projecting; 1 skips a classic header row, 0 (default) drops none.</param>
    /// <param name="lineParser">Projects each record into <typeparamref name="TItem"/>; identity when null (string items only). For a CSV row, split the record on "," inside this delegate.</param>
    /// <param name="asyncBefore">Async hook run just before the function is dispatched.</param>
    /// <param name="asyncThen">Async hook run just after the function returns.</param>
    /// <param name="callOptions">Stream redirection and where the result is read from; defaults when null.</param>
    /// <param name="timeout">Stops waiting for the result after this duration; null waits indefinitely.</param>
    /// <param name="cancellationToken">Cancels waiting for the result.</param>
    /// <typeparam name="TItem">Type each record is projected into.</typeparam>
    /// <returns>The captured output split and lazily projected into <typeparamref name="TItem"/> items.</returns>
    /// <exception cref="InteroperabilityException">When <paramref name="lineParser"/> is null and <typeparamref name="TItem"/> is not <see cref="string"/>.</exception>
    /// <exception cref="TimeoutException">When <paramref name="timeout"/> elapses before the result arrives.</exception>
    public async Task<IEnumerable<TItem>> CallFunctionEnumerableAsync<TItem>(
	    string functionName, string[]? functionArgs = null,
	    string resultSeparator = "\n",
	    int skipLines = 0,
	    Func<string, TItem>? lineParser = null,
	    Func<FunctionStartEventArgs, Task>? asyncBefore = null, Func<FunctionFinishedEventArgs, Task>? asyncThen = null,
	    CallOptions? callOptions = null,
	    TimeSpan? timeout = null,
	    CancellationToken cancellationToken = default)
    {
	    Func<string, TItem> parseItem = lineParser ?? StringIdentityParser<TItem>();
	    callOptions ??= CallOptions.CreateFactoryDefault;
	    FunctionResult functionResult = await RunFunctionAsync(functionName, functionArgs,
		    asyncBefore, asyncThen, callOptions, timeout, cancellationToken).ConfigureAwait(false);
	    string? str = GetResultStringFromFunctionResult(functionResult, callOptions.ReadResultFrom);
	    if (str == null) throw new InteroperabilityException("Function result is null");
	    // Not materialized: each record is projected as the caller enumerates. ToList() to pull them all.
	    return SplitResult(str, resultSeparator).Skip(skipLines).Select(parseItem);
    }

    // Fail closed: without a lineParser only string segments can be produced, so a non-string TItem is a
    // caller error named at the point it is requested rather than an invalid cast deep in enumeration.
    private static Func<string, TItem> StringIdentityParser<TItem>()
    {
	    if (typeof(TItem) != typeof(string))
		    throw new InteroperabilityException(
			    $"A lineParser is required to produce '{typeof(TItem)}' items; without one only string items are produced");
	    return static s => (TItem)(object)s;
    }

    private static string[] SplitResult(string resultString, string resultSeparator) =>
	    resultString.Split(new[] { resultSeparator }, StringSplitOptions.None);

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
    /// Dispatches the function and returns its <see cref="FunctionResult"/> once the marker line for
    /// this call arrives on stdout. <paramref name="timeout"/> and <paramref name="cancellationToken"/>
    /// bound the wait; on either, the wait ends and the function's own process tree is terminated too,
    /// unless <see cref="BashScriptSettings.TerminateFunctionProcessTreeOnTimeout"/> is off, in which
    /// case the function keeps running in bash. Inspect the result through
    /// <see cref="EventHandlerFunctionFinishedAsync"/>.
    /// </summary>
    /// <remarks>
    /// Calls execute concurrently. Each runs in its own bash subshell (one subprocess per call) with its
    /// own isolated {prefix} stream files and a marker-matched result, so dispatching several calls at once
    /// — without awaiting the previous one — is safe and every caller gets its own result. The library does
    /// not serialize the functions themselves: two concurrent calls to a function that reads or writes state
    /// shared outside its subshell — a fixed file, a database, an external service, a device — run at the
    /// same time and can interleave or corrupt that state. Such a function must be reentrant, or the caller
    /// must serialize those calls (await each before issuing the next, or guard them with its own lock).
    /// </remarks>
    /// <param name="functionName">Name of the bash function to execute, present in this instance's script.</param>
    /// <param name="functionArgs">Arguments passed to the function; each is quoted, so spaces are safe.</param>
    /// <param name="asyncBefore">Async hook run just before the function is dispatched.</param>
    /// <param name="asyncThen">Async hook run just after the function returns.</param>
    /// <param name="options">Stream redirection and where the result is read from; defaults when null.</param>
    /// <param name="timeout">Stops waiting for the result after this duration; null falls back to <see cref="BashScriptSettings.DefaultFunctionCallTimeout"/>, and <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> waits indefinitely.</param>
    /// <param name="cancellationToken">Cancels waiting for the result.</param>
    /// <returns>The result captured from the function's streams and exit code.</returns>
    /// <exception cref="TimeoutException">When <paramref name="timeout"/> elapses before the result arrives.</exception>
    public async Task<FunctionResult> RunFunctionAsync(
	    string functionName, string[]? functionArgs = null,
	    Func<FunctionStartEventArgs, Task>? asyncBefore = null,
	    Func<FunctionFinishedEventArgs, Task>? asyncThen = null,
	    CallOptions? options = null,
	    TimeSpan? timeout = null,
	    CancellationToken cancellationToken = default)
    {
	    // An explicit per-call timeout wins; otherwise fall back to the instance default. Either form
	    // of "no timeout" (unset default or Timeout.InfiniteTimeSpan) collapses to null = wait forever.
	    TimeSpan? effectiveTimeout = timeout ?? BashScriptSettings.DefaultFunctionCallTimeout;
	    if (effectiveTimeout == Timeout.InfiniteTimeSpan) effectiveTimeout = null;

	    var callOptions = options ?? CallOptions.CreateFactoryDefault;
	    FunctionStartEventArgs functionStartEventArgs =
		    await DispatchAsync(functionName, functionArgs, callOptions, asyncBefore, isStreaming: false, cancellationToken).ConfigureAwait(false);

		// Wait for the result FunctionProcessor sets when the call's marker line arrives. netstandard2.1
		// has no Task.WaitAsync(token), so cancel the wait by registering on the completion source: the
		// token is linked with the instance lifetime (Dispose unblocks it) and, when given, a timeout.
		// The function stays enqueued/running regardless; a later result is a no-op TrySetResult.
		FunctionResult result = await WaitForResultAsync(functionStartEventArgs, effectiveTimeout, cancellationToken).ConfigureAwait(false);

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

    // Builds a call's per-call state, runs the start hooks, and enqueues it, returning its start args.
    // Shared by RunFunctionAsync (which then awaits the result marker) and StreamFunctionAsync (which
    // follows the result file). isStreaming marks the call so the processor leaves its file unread and
    // uncleaned.
    private async Task<FunctionStartEventArgs> DispatchAsync(
	    string functionName, string[]? functionArgs, CallOptions callOptions,
	    Func<FunctionStartEventArgs, Task>? asyncBefore, bool isStreaming, CancellationToken cancellationToken)
    {
	    Validation.CheckFunctionName(functionName);
	    cancellationToken.ThrowIfCancellationRequested();

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
		    functionName, functionMarkerTag, sequenceNumber, functionArgs) { IsStreaming = isStreaming };

	    // Run optional hooks
	    if (asyncBefore != null) await asyncBefore(functionStartEventArgs).ConfigureAwait(false); // Optional function-specific hook
	    if (EventHandlerFunctionStartAsync != null) {
		    // Invoked for all functions
		    await EventHandlerFunctionStartAsync.Invoke(this, functionStartEventArgs).ConfigureAwait(false);
	    }

	    _functionProcessor.EnqueueCallFunctionItemThreadSafe(functionStartEventArgs);
	    return functionStartEventArgs;
    }

    /// <summary>
    /// Dispatches the function and streams its output line by line as it is produced, so a large or
    /// open-ended producer (<c>tail -f</c>, <c>cat bigfile</c>) is read with bounded memory instead of
    /// buffered whole. Reads the file named by <paramref name="callOptions"/>'s <c>ReadResultFrom</c>
    /// (<c>{prefix}.out</c> by default), following it past end-of-file — polling every
    /// <see cref="BashScriptSettings.StreamFollowPollInterval"/> — until the call finishes (a finite command)
    /// or the caller stops enumerating. Each newline-terminated record is projected with
    /// <paramref name="lineParser"/> into <typeparamref name="TItem"/>; a partial trailing line is held until
    /// its newline arrives. Breaking the <c>await foreach</c> or cancelling terminates the bash function's
    /// process tree so a <c>tail -f</c> actually stops, then the file is cleaned per the effective
    /// <see cref="FunctionFileCleanup"/>. With no <paramref name="lineParser"/>, <typeparamref name="TItem"/>
    /// must be <see cref="string"/>.
    /// </summary>
    /// <param name="functionName">Name of the bash function to execute, present in this instance's script.</param>
    /// <param name="functionArgs">Arguments passed to the function; each is quoted, so spaces are safe.</param>
    /// <param name="skipLines">Number of leading records to drop before projecting; 1 skips a header row.</param>
    /// <param name="lineParser">Projects each record into <typeparamref name="TItem"/>; identity when null (string items only).</param>
    /// <param name="callOptions">Stream redirection and which file is followed; defaults when null.</param>
    /// <param name="cancellationToken">Stops following and terminates the function's process tree.</param>
    /// <typeparam name="TItem">Type each record is projected into.</typeparam>
    /// <returns>The output's records, projected and yielded as the function writes them.</returns>
    /// <exception cref="InteroperabilityException">When <paramref name="lineParser"/> is null and <typeparamref name="TItem"/> is not <see cref="string"/>.</exception>
    public async IAsyncEnumerable<TItem> StreamFunctionAsync<TItem>(
	    string functionName, string[]? functionArgs = null,
	    int skipLines = 0,
	    Func<string, TItem>? lineParser = null,
	    CallOptions? callOptions = null,
	    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
	    Func<string, TItem> parseItem = lineParser ?? StringIdentityParser<TItem>();
	    callOptions ??= CallOptions.CreateFactoryDefault;
	    FunctionStartEventArgs startArgs =
		    await DispatchAsync(functionName, functionArgs, callOptions, null, isStreaming: true, cancellationToken).ConfigureAwait(false);
	    Task completion = startArgs.FunctionResultCompletionSource.Task;
	    string filePath = ResolveStreamFilePath(startArgs, callOptions);
	    TimeSpan poll = BashScriptSettings.StreamFollowPollInterval;
	    int skipped = 0;
	    try {
		    await foreach (string line in FollowLinesAsync(filePath, completion, poll, cancellationToken).ConfigureAwait(false)) {
			    if (skipped < skipLines) { skipped++; continue; }
			    yield return parseItem(line);
		    }
	    } finally {
		    // A finite producer already set completion; a still-running one (tail -f) is terminated so it
		    // stops, and its now-orphaned call is dropped from the running registry. The file is cleaned per
		    // the configured mode (AfterCall deletes now, OnDispose defers, Never keeps).
		    if (!completion.IsCompleted) {
			    await _functionProcessor.TerminateCallAsync(startArgs.FunctionMarkerTag, BashScriptSettings.FunctionTerminationGracePeriod).ConfigureAwait(false);
			    _functionProcessor.AbandonCall(startArgs);
		    }
		    _functionProcessor.ApplyFileCleanup(startArgs);
	    }
    }

    private static string ResolveStreamFilePath(FunctionStartEventArgs startArgs, CallOptions callOptions) =>
	    callOptions.ReadResultFrom.LocationType switch {
		    ResultLocationType.PrefixErr => startArgs.ErrFilePath,
		    ResultLocationType.PrefixLog => startArgs.LogFilePath,
		    ResultLocationType.CustomPath => callOptions.ReadResultFrom.CustomPath!,
		    _ => startArgs.OutFilePath // Default / PrefixOut
	    };

    // Follows a file to end-of-file and past it: at EOF, stops once the producer has completed (draining any
    // final bytes and emitting a trailing partial line), otherwise waits poll and re-reads what was appended.
    // Only newline-terminated records are emitted while following, so a half-written line is never split.
    private static async IAsyncEnumerable<string> FollowLinesAsync(
	    string filePath, Task completion, TimeSpan poll,
	    [EnumeratorCancellation] CancellationToken cancellationToken)
    {
	    // Bash creates the file on its first write; wait for it, but give up if the call ends without one.
	    while (!File.Exists(filePath)) {
		    if (cancellationToken.IsCancellationRequested || completion.IsCompleted) yield break;
		    await Task.Delay(poll, cancellationToken).ConfigureAwait(false);
	    }
	    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
	    using var reader = new StreamReader(stream);
	    var pending = new StringBuilder();
	    var buffer = new char[4096];
	    while (true) {
		    int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
		    if (read > 0) {
			    foreach (string line in ExtractLines(buffer, read, pending)) yield return line;
			    continue;
		    }
		    // EOF.
		    if (completion.IsCompleted) {
			    int tail;
			    while ((tail = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0) {
				    foreach (string line in ExtractLines(buffer, tail, pending)) yield return line;
			    }
			    if (pending.Length > 0) yield return pending.ToString().TrimEnd('\r');
			    yield break;
		    }
		    if (cancellationToken.IsCancellationRequested) yield break;
		    await Task.Delay(poll, cancellationToken).ConfigureAwait(false);
	    }
    }

    // Splits newly read chars on '\n', emitting completed lines and keeping the trailing partial in pending.
    private static IEnumerable<string> ExtractLines(char[] buffer, int count, StringBuilder pending)
    {
	    for (int i = 0; i < count; i++) {
		    char c = buffer[i];
		    if (c == '\n') { yield return pending.ToString().TrimEnd('\r'); pending.Clear(); }
		    else pending.Append(c);
	    }
    }

    /// <summary>
    /// Awaits the call's completion source, ending the wait early when <paramref name="cancellationToken"/>
    /// or <paramref name="timeout"/> fires, or when the instance is disposed. On a timeout or caller
    /// cancellation the bash function keeps running, so its process tree is terminated (unless
    /// <see cref="BashScriptSettings.TerminateFunctionProcessTreeOnTimeout"/> is off); disposal is left to
    /// session teardown. A timeout surfaces as <see cref="TimeoutException"/>; caller cancellation and
    /// disposal surface as <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<FunctionResult> WaitForResultAsync(
	    FunctionStartEventArgs startArgs, TimeSpan? timeout, CancellationToken cancellationToken)
    {
	    var completion = startArgs.FunctionResultCompletionSource;
	    using var timeoutCts = timeout is { } t ? new CancellationTokenSource(t) : null;
	    using var linkedCts = timeoutCts is null
		    ? CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, cancellationToken)
		    : CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, cancellationToken, timeoutCts.Token);
	    using (linkedCts.Token.Register(static s => ((TaskCompletionSource<FunctionResult>)s!).TrySetCanceled(), completion)) {
		    try {
			    return await completion.Task.ConfigureAwait(false);
		    } catch (OperationCanceledException) {
			    bool timedOut = timeoutCts is { IsCancellationRequested: true };
			    bool lifetimeCancelled = _lifetimeCts.IsCancellationRequested;
			    // The function keeps running after the wait ends; reap its tree unless the whole session is
			    // being disposed (which reaps everything) or the caller opted out.
			    if (!lifetimeCancelled && BashScriptSettings.TerminateFunctionProcessTreeOnTimeout) {
				    await _functionProcessor.TerminateCallAsync(
					    startArgs.FunctionMarkerTag, BashScriptSettings.FunctionTerminationGracePeriod).ConfigureAwait(false);
			    }
			    // A timeout that is not also caller/lifetime cancellation is reported as a timeout.
			    if (timedOut && !cancellationToken.IsCancellationRequested && !lifetimeCancelled) {
				    throw new TimeoutException(
					    $"Function '{startArgs.FunctionName}' did not return within {timeout!.Value.TotalMilliseconds}ms");
			    }
			    throw;
		    }
	    }
    }

    /// <summary>
    /// Re-sources the configured script and any global function scripts into the running bash
    /// process, then raises <see cref="EventHandlerScriptSourcedAsync"/>. Already performed during
    /// <see cref="CreateAsync(BashScriptSettings, BashProcessSettings, FunctionWorkDirSettings, ILogger, CancellationToken)"/>;
    /// call it again to pick up edits to the script file.
    /// </summary>
    /// <param name="cancellationToken">Cancels the sourcing.</param>
    /// <returns>A task completing once every script is sourced and the hook has run.</returns>
    public async Task SourceScriptFilesAsync(CancellationToken cancellationToken = default)
    {
	    await _functionProcessor.SourceScriptFilesAsync(cancellationToken).ConfigureAwait(false);
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
    private async Task StartAsync(CancellationToken cancellationToken = default)
	    => await _functionProcessor.StartAsync(cancellationToken).ConfigureAwait(false);
    /// <summary>
    /// Renders the function calls this instance has dispatched, in order, for diagnostics.
    /// </summary>
    /// <returns>A human-readable trace of the call history.</returns>
    public string GetTrace() => _functionProcessor.PrintFunctionCallTrace();

	#region Dispose

    /// <summary>
    /// Graceful shutdown: lets calls already dispatched finish (bounded, see
    /// <see cref="FunctionProcessor"/>) before cancelling the instance lifetime and stopping the
    /// resident bash process. Prefer this from async code, via <c>await using</c>. Use the synchronous
    /// <see cref="Dispose"/> to cancel in-flight calls instead of waiting for them.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
	    if (_disposed) return;
	    _disposed = true;
	    // Drain before cancelling: the dispatch loop and result readers run on _lifetimeCts, so the
	    // in-flight calls can only complete while it is still live.
	    await _functionProcessor.DisposeAsync().ConfigureAwait(false);
	    if (!_lifetimeCts.IsCancellationRequested) _lifetimeCts.Cancel();
	    _lifetimeCts.Dispose();
    }

    /// <summary>
    /// Forceful shutdown: cancels outstanding function calls and stops the resident bash process
    /// without waiting for calls in flight to finish. Use <see cref="DisposeAsync"/> to let them
    /// complete first.
    /// </summary>
    public void Dispose()
    {
	    if (_disposed) return;
	    _disposed = true;
	    if (!_lifetimeCts.IsCancellationRequested) _lifetimeCts.Cancel();
        _functionProcessor.Dispose();
        _lifetimeCts.Dispose();
    }

    #endregion Dispose
}

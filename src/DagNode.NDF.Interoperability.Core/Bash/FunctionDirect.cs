using System.Diagnostics;
using DagNode.NDF.Interoperability.Model;

namespace DagNode.NDF.Interoperability.Bash;

/// <summary>
/// Each GetType method in this class runs `bash -c "source script.sh" before executing the function`.
/// For calls to multiple functions from a single script, use `BashScript` class with generic CallFunctionAsync.
/// </summary>
public class FunctionDirect
{
	/// <summary>
	/// Writes the composed <c>bash -c</c> command line to the console before each call.
	/// Independent of <see cref="Model.Bash.BashScriptSettings.IsDebug"/> and of ILogger.
	/// </summary>
	public static bool IsDebug { get; set; } = false;

	/// <summary>Interpreter every call is launched with. Defaults to /usr/bin/bash.</summary>
	public static string BashPath { get; set; } = "/usr/bin/bash";
	
	/// <summary>
	/// Passed to every function as 1st argument!
	/// Usage: local log_file_path="$1"
	/// </summary>
	public static string BaseLogDirectory { get; set; } = "/tmp";

	/*
		💭 Calling Bash functions from .NET Core,
		specifically with regard to handling function return types
		   such as strings, numbers, booleans, and arrays.

		⚡ ACTIONS:

		    Determine how to call Bash functions from .NET Core console applications.
		    Handle capturing scalar values (strings, numbers, booleans) and arrays from Bash in .NET Core.
		    Suggest best practices for managing different return types using command-line calls and processing their output.
		    Provide code examples for calling Bash functions and parsing the results in .NET Core.]

		📝 OUTPUT: The .NET Core console app can invoke Bash scripts using System.Diagnostics.Process for subprocess execution.
		   For handling return values from Bash functions:

		    Scalar values: Use echo or printf in Bash, capture output via StandardOutput.
		    Booleans: Leverage Bash's exit codes (0 for true, non-zero for false).
		    Arrays: Return newline-separated values or populate an array in the caller’s context.

		Here's how to call and capture Bash function return values in .NET Core.
	*/

	/// <summary>
	/// Call Bash script function with string return value, optionally enable or disable standard output or error streams.
	/// </summary>
	/// <param name="bashScriptPath">Absolute path to the bash script file</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script configured for this instance</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <param name="redirectStandardOutput">Optionally suppress reading results from standard output stream</param>
	/// <param name="redirectStandardError">Optionally suppress reading results from standard error stream</param>
	/// <param name="logFilePath">Optional non-default absolute path to the logfile for this function call</param>
	/// <returns>A value returned by reading function stream outputs</returns>
	/// <exception cref="InteroperabilityException"></exception>
	public static async Task<string> GetStringAsync(AbsolutePath bashScriptPath, string functionName, string[]? functionArgs = null,
		bool redirectStandardOutput = true, bool redirectStandardError = true, AbsolutePath? logFilePath = null)
	{
		AbsolutePath logFilePathAbsolute = AbsolutePath.Create(GetLogFilePath(bashScriptPath, functionName, logFilePath));
		ProcessStartInfo processStartInfo = CreateBashProcessStartInfo(logFilePathAbsolute, bashScriptPath, functionName, functionArgs,
			redirectStandardOutput, redirectStandardError);
		using Process process = Process.Start(processStartInfo) ?? throw new InteroperabilityException(
			$"Error starting process for function {functionName}");
		using var reader = process.StandardOutput;
		return await reader.ReadToEndAsync().ConfigureAwait(false); // Capture and return output
	}

	/// <summary>
	/// Overload of <see cref="GetStringAsync(AbsolutePath, string, string[], bool, bool, AbsolutePath)"/> taking a script filename relative to the program directory.
	/// </summary>
	/// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <param name="redirectStandardOutput">Optionally suppress reading results from standard output stream</param>
	/// <param name="redirectStandardError">Optionally suppress reading results from standard error stream</param>
	/// <param name="logFilePath">Optional non-default absolute path to the logfile for this function call</param>
	/// <returns>Raw standard output of the function.</returns>
	public static async Task<string> GetStringAsync(string scriptFileName, string functionName, string[]? functionArgs = null,
		bool redirectStandardOutput = true, bool redirectStandardError = true, AbsolutePath? logFilePath = null)
	{
		var scriptPath = AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory());
		return await GetStringAsync(scriptPath, functionName, functionArgs, redirectStandardOutput, redirectStandardError, logFilePath).ConfigureAwait(false);
	}

	/// <summary>
	/// Calls the function and parses its standard output as the enum <typeparamref name="T"/>, case-insensitively.
	/// </summary>
	/// <typeparam name="T">Enum type the output is parsed into.</typeparam>
	/// <param name="bashScriptPath">Absolute path to the bash script file</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed enum value.</returns>
	/// <exception cref="InvalidOperationException">When the output does not name a member of <typeparamref name="T"/>.</exception>
	public static async Task<T> GetEnumAsync<T>(AbsolutePath bashScriptPath, string functionName, string[]? functionArgs = null) where T : struct, IConvertible
	{
		var value = await GetStringAsync(bashScriptPath, functionName, functionArgs).ConfigureAwait(false);
		return value.ParseEnum<T>();
	}

	/// <summary>
	/// Overload of <see cref="GetEnumAsync{T}(AbsolutePath, string, string[])"/> taking a script filename relative to the program directory.
	/// </summary>
	/// <typeparam name="T">Enum type the output is parsed into.</typeparam>
	/// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed enum value.</returns>
	/// <exception cref="InvalidOperationException">When the output does not name a member of <typeparamref name="T"/>.</exception>
	public static async Task<T> GetEnumAsync<T>(string scriptFileName, string functionName, string[]? functionArgs = null) where T : struct, IConvertible
		=> await GetEnumAsync<T>(AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory()), functionName, functionArgs).ConfigureAwait(false);

	# region Parse number from GetString
	
	/// <summary>
	/// Calls the function and parses its standard output as an int.
	/// </summary>
	/// <param name="bashScriptPath">Absolute path to the bash script file</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed value.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started, or the output does not parse.</exception>
	public static async Task<int> GetIntAsync(AbsolutePath bashScriptPath, string functionName, string[]? functionArgs = null)
	{
		var value = await GetStringAsync(bashScriptPath, functionName, functionArgs).ConfigureAwait(false);
		if (!int.TryParse(value, out int number))
			throw new InteroperabilityException(
				$"The value '{value}' returned by function '{functionName}' is not int");
		return number;
	}

	/// <summary>
	/// Overload of <see cref="GetIntAsync(AbsolutePath, string, string[])"/> taking a script filename relative to the program directory.
	/// </summary>
	/// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed value.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started, or the output does not parse.</exception>
	public static async Task<int> GetIntAsync(string scriptFileName, string functionName, string[]? functionArgs = null)
		=> await GetIntAsync(AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory()), functionName, functionArgs).ConfigureAwait(false);

	/// <summary>
	/// Calls the function and parses its standard output as a long.
	/// </summary>
	/// <param name="bashScriptPath">Absolute path to the bash script file</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed value.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started, or the output does not parse.</exception>
	public static async Task<long> GetLongAsync(AbsolutePath bashScriptPath, string functionName, string[]? functionArgs = null)
	{
		var value = await GetStringAsync(bashScriptPath, functionName, functionArgs).ConfigureAwait(false);
		if (!long.TryParse(value, out long number))
			throw new InteroperabilityException(
				$"The value '{value}' returned by function '{functionName}' is not long");
		return number;
	}
	
	/// <summary>
	/// Overload of <see cref="GetLongAsync(AbsolutePath, string, string[])"/> taking a script filename relative to the program directory.
	/// </summary>
	/// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed value.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started, or the output does not parse.</exception>
	public static async Task<long> GetLongAsync(string scriptFileName, string functionName, string[]? functionArgs = null)
		=> await GetLongAsync(AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory()), functionName, functionArgs).ConfigureAwait(false);

	/// <summary>
	/// Calls the function and parses its standard output as a double.
	/// </summary>
	/// <param name="bashScriptPath">Absolute path to the bash script file</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed value.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started, or the output does not parse.</exception>
	public static async Task<double> GetDoubleAsync(AbsolutePath bashScriptPath, string functionName, string[]? functionArgs = null)
	{
		var value = await GetStringAsync(bashScriptPath, functionName, functionArgs).ConfigureAwait(false);
		if (!double.TryParse(value, out double number))
			throw new InteroperabilityException(
				$"The value '{value}' returned by function '{functionName}' is not double");
		return number;
	}
	
	/// <summary>
	/// Overload of <see cref="GetDoubleAsync(AbsolutePath, string, string[])"/> taking a script filename relative to the program directory.
	/// </summary>
	/// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed value.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started, or the output does not parse.</exception>
	public static async Task<double> GetDoubleAsync(string scriptFileName, string functionName, string[]? functionArgs = null)
		=> await GetDoubleAsync(AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory()), functionName, functionArgs).ConfigureAwait(false);

	/// <summary>
	/// Calls the function and parses its standard output as a decimal.
	/// </summary>
	/// <param name="bashScriptPath">Absolute path to the bash script file</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed value.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started, or the output does not parse.</exception>
	public static async Task<decimal> GetDecimalAsync(AbsolutePath bashScriptPath, string functionName, string[]? functionArgs = null)
	{
		var value = await GetStringAsync(bashScriptPath, functionName, functionArgs).ConfigureAwait(false);
		if (!decimal.TryParse(value, out decimal number))
			throw new InteroperabilityException(
				$"The value '{value}' returned by function '{functionName}' is not decimal");
		return number;
	}
	
	/// <summary>
	/// Overload of <see cref="GetDecimalAsync(AbsolutePath, string, string[])"/> taking a script filename relative to the program directory.
	/// </summary>
	/// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>The parsed value.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started, or the output does not parse.</exception>
	public static async Task<decimal> GetDecimalAsync(string scriptFileName, string functionName, string[]? functionArgs = null)
		=> await GetDecimalAsync(AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory()), functionName, functionArgs).ConfigureAwait(false);
	
	# endregion Parse number from GetString

	/// <summary>
	/// Handling Booleans: Bash functions like is_even() can return an exit code.
	/// You can check the exit code in .NET Core like so
	/// Recommendation: return 0 is Success => returns true
	/// any other code => false
	/// </summary>
	/// <param name="bashScriptPath"></param>
	/// <param name="functionName"></param>
	/// <param name="functionArgs"></param>
	/// <returns></returns>
	/// <exception cref="InteroperabilityException"></exception>
	public static bool GetBool(AbsolutePath bashScriptPath, string functionName, string[]? functionArgs = null)
	{
		AbsolutePath logFilePathAbsolute = AbsolutePath.Create(GetLogFilePath(bashScriptPath, functionName));
		ProcessStartInfo processStartInfo = CreateBashProcessStartInfo(logFilePathAbsolute, bashScriptPath, functionName, functionArgs);
		using Process? process = Process.Start(processStartInfo);
		//TODO: https://gist.github.com/tazlord/496e16698d4c4f90ea674dc2fdeb964a
		// Based on https://gist.github.com/AlexMAS/276eed492bc989e13dcce7c78b9e179d
		if (process == null)
			throw new InteroperabilityException($"Error starting process for function {functionName}");
		process.WaitForExit();
		return process.ExitCode == 0; // True if success, false otherwise
	}
	
	/// <summary>
	/// Blocking overload taking a script filename relative to the program directory.
	/// </summary>
	/// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
	/// <param name="functionName">Name of the bash function to execute.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>True when the function exited with status 0.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started.</exception>
	public static bool GetBool(string scriptFileName, string functionName, string[]? functionArgs = null)
		=> GetBool(AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory()), functionName, functionArgs);

	/// <summary>
	/// Exit-status form of <see cref="GetBool(AbsolutePath, string, string[])"/> that awaits the
	/// process instead of blocking the calling thread. Prefer it over <see cref="GetBool(AbsolutePath, string, string[])"/>
	/// from async code, which otherwise holds a thread for the lifetime of the bash call.
	/// </summary>
	/// <param name="bashScriptPath">Absolute path to the bash script file</param>
	/// <param name="functionName">Name of the bash function to execute.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <param name="cts">Token source cancelling the wait; a new one is created when null.</param>
	/// <returns>True when the function exited with status 0.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started.</exception>
	public static async Task<bool> GetBoolAsync(AbsolutePath bashScriptPath, string functionName,
		string[]? functionArgs = null, CancellationTokenSource? cts = null)
	{
		AbsolutePath logFilePathAbsolute = AbsolutePath.Create(GetLogFilePath(bashScriptPath, functionName));
		ProcessStartInfo processStartInfo = CreateBashProcessStartInfo(logFilePathAbsolute, bashScriptPath, functionName, functionArgs);
		using Process process = Process.Start(processStartInfo) ?? throw new InteroperabilityException(
			$"Error starting process for function {functionName}");
		await process.WaitForExitAsync(cts).ConfigureAwait(false);
		return process.ExitCode == 0; // True if success, false otherwise
	}

	/// <summary>
	/// Non-blocking overload taking a script filename relative to the program directory.
	/// </summary>
	/// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
	/// <param name="functionName">Name of the bash function to execute.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <param name="cts">Token source cancelling the wait; a new one is created when null.</param>
	/// <returns>True when the function exited with status 0.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started.</exception>
	public static async Task<bool> GetBoolAsync(string scriptFileName, string functionName,
		string[]? functionArgs = null, CancellationTokenSource? cts = null)
		=> await GetBoolAsync(AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory()),
			functionName, functionArgs, cts).ConfigureAwait(false);

	/// <summary>
	/// Handling Arrays (newline-separated): If Bash returns an array of strings,
	/// capture the output into an array in .NET Core:
	/// </summary>
	/// <param name="bashScriptPath"></param>
	/// <param name="functionName"></param>
	/// <param name="functionArgs"></param>
	/// <returns></returns>
	/// <exception cref="InteroperabilityException"></exception>
	public static async Task<List<string>> GetArrayAsync(AbsolutePath bashScriptPath, string functionName, string[]? functionArgs = null)
	{
		AbsolutePath logFilePathAbsolute = AbsolutePath.Create(GetLogFilePath(bashScriptPath, functionName));
		using Process process = Process.Start(CreateBashProcessStartInfo(
			                        logFilePathAbsolute, bashScriptPath, functionName, functionArgs))
		                        ?? throw new InteroperabilityException(
			                        $"Error starting process for function {functionName}");
		using var reader = process.StandardOutput;
		string output = await reader.ReadToEndAsync().ConfigureAwait(false);
		return output.Split(['\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
	}
	
	/// <summary>
	/// Overload of <see cref="GetArrayAsync(AbsolutePath, string, string[])"/> taking a script filename relative to the program directory.
	/// </summary>
	/// <param name="scriptFileName">Filename of the bash script, resolved against the current directory.</param>
	/// <param name="functionName">Name of the bash function to execute, the function must be present in the script.</param>
	/// <param name="functionArgs">Array of args passed to the bash function, args may contain spaces as every arg is wrapped in double quotation marks</param>
	/// <returns>One entry per non-empty newline-separated line of standard output.</returns>
	/// <exception cref="InteroperabilityException">When the bash process cannot be started, or the output does not parse.</exception>
	public static async Task<List<string>> GetArrayAsync(string scriptFileName, string functionName, string[]? functionArgs = null)
		=> await GetArrayAsync(AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory()), functionName, functionArgs).ConfigureAwait(false);

	#region Private

	/// <summary>
	/// How It Works
	///   Sourcing the Script:
	///       source {scriptPath} ensures that the functions from the specified script are available in the current shell session.
	///   Dynamic Function Invocation:
	///       {functionName} {functionArgs} dynamically calls the specified function with any provided arguments.
	///   Process Management:
	///       ProcessStartInfo is configured to capture StandardOutput for scalar and array outputs.
	///       ExitCode is used for boolean logic.
	///   Error Handling:
	///       If the script or function fails, the error message is captured from StandardError and returned as an exception.
	/// 
	///   Sourcing (source) dynamically loads any Bash script's functions into the shell session.
	///   Direct function calls avoid the need for intermediary scripts or additional layers, simplifying debugging and maintenance.
	/// </summary>
	/// <param name="scriptFilePath"></param>
	/// <param name="functionName"></param>
	/// <param name="functionArgs"></param>
	/// <param name="redirectStandardOutput"></param>
	/// <param name="redirectStandardError"></param>
	/// <param name="logFilePath">Optional non-default absolute path to the logfile for this function call</param>
	/// <returns></returns>
	private static ProcessStartInfo CreateBashProcessStartInfo(AbsolutePath logFilePath, AbsolutePath scriptFilePath, string functionName, string[]? functionArgs = null,
		bool redirectStandardOutput = true, bool redirectStandardError = true)
	{
		Validation.CheckScriptFile(scriptFilePath);
		Validation.CheckFunctionName(functionName);
		
		string quotedFunctionArgs = string.Join(" ", functionArgs?.Select(arg =>  $"\\\"{arg}\\\"") ?? Array.Empty<string>());
		string processArgs = $"-c \"source \\\"{scriptFilePath}\\\" && {functionName} \\\"{logFilePath}\\\" {quotedFunctionArgs}\"";
		if (IsDebug) Console.WriteLine($"Running {BashPath} {processArgs}");
		
		var info = new ProcessStartInfo() {
			FileName = BashPath,
			Arguments = processArgs,
			RedirectStandardOutput = redirectStandardOutput,
			RedirectStandardError = redirectStandardError,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		return info;
	}

	private static AbsolutePath GetLogFilePath(AbsolutePath scriptFilePath, string functionName, AbsolutePath? logFilePath = null)
	{
		if (logFilePath == null) {
			string scriptNameNormalized = Path.GetFileName(scriptFilePath).ToAsciiNoWhitespace();
			string scriptSubdirectory = $"{scriptNameNormalized}-{scriptFilePath.ToString().ToSha1Hash().Substring(0,5)}";
			string logDirectoryPath = Path.Combine(BaseLogDirectory, $"@bashfunctions-static-{DateTime.Now:yyyyMMdd}", scriptSubdirectory);
			string logFilePathAbsolute = Path.Combine(logDirectoryPath, $"{functionName.ToAsciiNoWhitespace()}-{Helpers.GenerateRandomString(4)}.log");
			return AbsolutePath.Create(logFilePathAbsolute);
		}
		string logFileDirectory = Path.GetDirectoryName(logFilePath);
		if (!Directory.Exists(logFileDirectory))
			throw new InteroperabilityException("Log file directory does not exist");
		
		return AbsolutePath.Create(logFilePath);
	}

	#endregion

	/*
	🍏 RESULT: In .NET Core, you can interact with Bash scripts by capturing output via ProcessStartInfo.
	  Scalar values can be handled by reading from StandardOutput. Boolean values are processed by examining
	  the exit code. For arrays, capture newline-separated output and split it into a .NET array.
	*/
}

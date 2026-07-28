using NDF.Interoperability.Model;
using NDF.Interoperability.Model.Bash;

namespace NDF.Interoperability.Bash.ConsoleApp;

class Program
{
	static async Task Main(string[] args)
	{
		//var d = InlineScripts.InlineAll();
		
		Console.WriteLine("NDF.Interoperability.Core: Started checking Bash function calls");
		Console.WriteLine("----------------------------------------------------------------");

		// BashScript.cs .. CallFunction<T>(<bash_script.sh>, <function_name>, [...args])
		await RunBashScriptGenericCallFunctionTests().ConfigureAwait(false);

		// Note: Each method call to SingleFunctionDirect runs `bash -c "source script.sh && script.sh ...args"`.
		// For multiple calls to a single script, use `BashScript.cs` above to source the file only once.
		// await RunSingleFunctionTests().ConfigureAwait(false);

		Console.WriteLine("Done.");
	}

	private static async Task RunBashScriptGenericCallFunctionTests()
	{
		var cts = new CancellationTokenSource();
		
		using var bashScript = await BashScript.CreateAsync(
			bashScriptSettings: new BashScriptSettings("functions.sh") { IsDebug = false },
			bashProcessSettings: BashProcessSettings.CreateFactoryDefault,
			functionWorkDirSettings: new FunctionWorkDirSettings {
				// UseCustomFunctionWorkDir = true,
				// FunctionWorkDirType = DirectoryType.Custom,
				// FunctionBaseWorkDir = AbsolutePath.Create("/tmp"),
				// ConfigureFunctionWorkDir = args => {
				// 	string scriptSubdirectory = $"{args.ScriptFileNameNormalized}-{args.BashScriptSettings.ScriptFilePathHashSha1.Substring(0,5)}";
				// 	string logDirectoryPath = Path.Combine(args.FunctionWorkDirSettings.FunctionBaseWorkDir, $"@functions-{DateTime.Now:yyyyMMdd}", scriptSubdirectory);
				// 	return logDirectoryPath;
				// },
				// ConfigureFunctionMarkerTag = args =>
				// 	$"{DateTime.Now:HHmmss-fff}-{args.FunctionNameNormalized}-{args.ScriptInstanceMarkerTag}-{args.FunctionCallSequenceNumber}.log"
			});
		
		// Overwrite default work directory creation logic
		bashScript.OnBeforeCreateFunctionWorkDir = functionWorkDirPath => {
			// Console.WriteLine($"Preparing to create directory: {functionWorkDirPath}");
			// if (functionWorkDirPath.Contains("restricted"))
			// 	throw new InvalidOperationException("Cannot use restricted directories.");
		};
		
		// Setup additional contents necessary for running functions from the script
		bashScript.OnAfterCreateFunctionWorkDir = workDirPath => {
			// Console.WriteLine($"Created log directory at {workDirPath}");
		};
		
		bashScript.EventHandlerFunctionStartAsync += async (sender, e) => {
			// Console.WriteLine($"Running bash command: {sender.BashScriptSettings.ScriptFilePathAbsolute} {e.BashCommand}");
		};
		bashScript.EventHandlerFunctionFinishedAsync += async (sender, e) => {
			// A hook with raw script output lines and exit status code, before it is strongly typed as .NET types
			// string result = e.OutputLines.Count switch {
			// 	0 => string.Empty,
			// 	1 => $", Output: {e.OutputLines[0]}",
			// 	_ => $", Output:{string.Join('\n', e.OutputLines)}"
			// };
			// Console.WriteLine($"Finished with code: {e.ExitCode}{result}");
		};

		// Call a function returning a scalar value
		string result = await bashScript.CallFunctionAsync<string>("get_string").ConfigureAwait(false);
		Console.WriteLine($"get_string: {result}");

		// Call a function returning enum values
		ProcessingState enumVal = await bashScript.CallFunctionAsync<ProcessingState>("get_enum_value").ConfigureAwait(false);
		Console.WriteLine($"get_enum_value: {enumVal}");

		// Call a function returning a number
		int numberInt = await bashScript.CallFunctionAsync<int>("get_int").ConfigureAwait(false);
		Console.WriteLine($"get_int: {numberInt}");
		long numberLong = await bashScript.CallFunctionAsync<long>("get_long").ConfigureAwait(false);
		Console.WriteLine($"get_long: {numberLong}");
		double numberDouble = await bashScript.CallFunctionAsync<double>("get_double").ConfigureAwait(false);
		Console.WriteLine($"get_double: {numberDouble}");
		decimal numberDec = await bashScript.CallFunctionAsync<decimal>("get_decimal").ConfigureAwait(false);
		Console.WriteLine($"get_decimal: {numberDec}");

		// Call a boolean function
		bool isEven = await bashScript.CallFunctionAsync<bool>("is_even", ["42"]).ConfigureAwait(false);
		Console.WriteLine($"is_even 42: {isEven}");
		bool isOdd = await bashScript.CallFunctionAsync<bool>("is_odd", ["42"]).ConfigureAwait(false);
		Console.WriteLine($"is_odd 42: {isOdd}");

		// Pass multiple args with spaces and return it as space concatenated result
		string withSpaces =
			await bashScript.CallFunctionAsync<string>("get_string_from_args_with_spaces", ["Plan 9", "from", "Outer Space"]).ConfigureAwait(false);
		Console.WriteLine($"get_string_from_args_with_spaces: {withSpaces}");

		// Call a function returning an array (newline-separated)
		List<string> array = await bashScript.CallFunctionAsync<List<string>>("get_array").ConfigureAwait(false);
		Console.WriteLine($"get_array: {string.Join(", ", array)}");

		Console.WriteLine("----------------------------------------------------------------");
	}

	private static async Task RunSingleFunctionTests()
	{
		string resultString = await FunctionDirect.GetStringAsync("functions.sh", "get_string").ConfigureAwait(false);
		Console.WriteLine($"GetString: {resultString}");

		ProcessingState resultEnum = await FunctionDirect.GetEnumAsync<ProcessingState>("functions.sh", "get_enum_value").ConfigureAwait(false);
		Console.WriteLine($"GetEnum: {resultEnum}");

		int numberInt = await FunctionDirect.GetIntAsync("functions.sh", "get_int").ConfigureAwait(false);
		Console.WriteLine($"GetInt: {numberInt}");

		long numberLong = await FunctionDirect.GetLongAsync("functions.sh", "get_long").ConfigureAwait(false);
		Console.WriteLine($"GetLong: {numberLong}");

		double numberDouble = await FunctionDirect.GetDoubleAsync("functions.sh", "get_double").ConfigureAwait(false);
		Console.WriteLine($"GetDouble: {numberDouble}");

		decimal numberDecimal = await FunctionDirect.GetDecimalAsync("functions.sh", "get_decimal").ConfigureAwait(false);
		Console.WriteLine($"GetDecimal: {numberDecimal}");

		bool isEven = FunctionDirect.GetBool("functions.sh", "is_even", ["42"]);
		Console.WriteLine($"GetBool: {isEven}");

		bool isOdd = FunctionDirect.GetBool("functions.sh", "is_odd", ["42"]);
		Console.WriteLine($"GetBool: {isOdd}");

		string resultWithArgs = await FunctionDirect.GetStringAsync("functions.sh",
			"get_string_from_args_with_spaces", ["Plan 9", "from", "Outer Space"]).ConfigureAwait(false);
		Console.WriteLine($"GetValueFromArgs: {resultWithArgs}");

		List<string> arrayEcho = await FunctionDirect.GetArrayAsync("functions.sh", "get_array").ConfigureAwait(false);
		Console.WriteLine($"GetArray: {string.Join(", ", arrayEcho)}");

		Console.WriteLine("----------------------------------------------------------------");
	}

	private enum ProcessingState
	{
		Default,
		Processing,
		Finished
	}
}

namespace DagNode.NDF.Interoperability.Model.Bash;

public class FunctionInputLocation
{
	/// <summary>
	/// Read function args by default in all cases.
	/// Optionally, specify location type for stdin file. 
	/// </summary>
	public InputLocationType LocationType { get; private set; }
	
	/// <summary>
	/// Only necessary if using LocationType.CustomPath.
	/// Absolute path to the file which will be read as function standard input. 
	/// </summary>
	public AbsolutePath? CustomPath { get; private set; }
	
	#region Private constructors
	private FunctionInputLocation(InputLocationType inputLocationType)
	{
		if (inputLocationType == InputLocationType.TakeFromCustomPath) throw new ArgumentException("Use customPath overload when specifying custom input file path", nameof(inputLocationType));
		LocationType = inputLocationType == InputLocationType.Default ? InputLocationType.FunctionArgs : inputLocationType;
	}
	
	private FunctionInputLocation(string customPath)
	{
		LocationType = InputLocationType.TakeFromCustomPath;
		CustomPath = AbsolutePath.Create(customPath);
	}
	
	#endregion Private constructors
	#region Factory methods
	
	/// <summary>
	/// (FunctionArgs) Read function args.
	/// </summary>
	public static FunctionInputLocation Default {
		get => new(InputLocationType.Default);
	}
	/// <summary>
	/// (Default) Read function args.
	/// </summary>
	public static FunctionInputLocation FunctionArgs {
		get => new(InputLocationType.FunctionArgs);
	}
	/// <summary>
	/// Read function args, same as Default.
	/// Additionally, changes standard input (stdin) to {prefix}.in for the duration of the function call.
	/// </summary>
	public static FunctionInputLocation TakePrefixIn {
		get => new(InputLocationType.TakePrefixIn);
	}
	/// <summary>
	/// Read function args, same as Default.
	/// Additionally, changes standard input (stdin) to {prefix}.log for the duration of the function call.
	/// </summary>
	public static FunctionInputLocation TakePrefixLog {
		get => new(InputLocationType.TakePrefixLog);
	}
	
	/// <summary>
	/// Read function args, same as Default.
	/// Additionally, changes standard input (stdin) to CustomPath for the duration of the function call.
	/// For cases when the function should read input file from custom location.
	/// </summary>
	/// <param name="customPath">Absolute path to the input file.</param>
	/// <returns></returns>
	public static FunctionInputLocation TakeCustomPath(string customPath) => new FunctionInputLocation(customPath);
	
	#endregion Factory methods
	
}

public enum InputLocationType
{
	/// <summary>
	/// (FunctionArgs) Function args, available with "$1".."$n".
	/// </summary>
	Default,
	/// <summary>
	/// (Default) Function args, available with "$1".."$n".
	/// </summary>
	FunctionArgs,
	/// <summary>
	/// Read FunctionArgs and take PrefixIn.
	/// The redirection &lt; {prefix}.in changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.in.
	/// The function might process the arguments using "$1", "$2", etc., while simultaneously reading from stdin if needed. 
	/// </summary>
	TakePrefixIn,
	/// <summary>
	/// Read FunctionArgs and take PrefixLog.
	/// The redirection &lt; {prefix}.log changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.log.
	/// The function might process the arguments using "$1", "$2", etc., while simultaneously reading from stdin if needed. 
	/// </summary>
	TakePrefixLog,
	/// <summary>
	/// Read FunctionArgs and take contents of CustomPath.
	/// The redirection &lt; {customPath} changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {customPath}.
	/// The function might process the arguments using "$1", "$2", etc., while simultaneously reading from stdin if needed. 
	/// </summary>
	TakeFromCustomPath,
}

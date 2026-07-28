namespace NDF.Interoperability.Model.Bash;

public class FunctionResultErrLocation
{
	/// <summary>
	/// The default is to read function results from standard output written to the {prefix}.out file.
	/// If the function provide results on standard error, change the type to PrefixErr.
	/// Alternatively, use PrefixLog to automatically read results from {prefix}.log.
	/// In case you are using PrefixLog: {prefix}.log is passed to every function as first arg $1,
	/// and the function must explicitly write to > "{$1}.log" for BashScript to be able
	/// to interpret result from such log file automatically. 
	/// </summary>
	public ResultLocationType LocationType { get; private set; }
	
	/// <summary>
	/// Only necessary if using LocationType.CustomPath.
	/// Absolute path to the file from which BashScript will try to interpret results when the function is finished. 
	/// </summary>
	public AbsolutePath? CustomPath { get; private set; }
	
	#region Private constructors
	private FunctionResultErrLocation(ResultLocationType resultLocationType)
	{
		if (resultLocationType == ResultLocationType.CustomPath) throw new ArgumentException("Use customPath overload when specifying custom result file path", nameof(resultLocationType));
		LocationType = resultLocationType == ResultLocationType.Default ? ResultLocationType.PrefixErr : resultLocationType;
	}
	
	private FunctionResultErrLocation(string customPath)
	{
		LocationType = ResultLocationType.CustomPath;
		CustomPath = AbsolutePath.Create(customPath);
	}
	
	#endregion Private constructors
	#region Factory methods
	
	/// <summary>
	/// Read ExitCode and contents of PrefixErr.
	/// </summary>
	public static FunctionResultErrLocation Default {
		get => new(ResultLocationType.Default);
	}
	/// <summary>
	/// Only read ExitCode.
	/// </summary>
	public static FunctionResultErrLocation None {
		get => new(ResultLocationType.ExitCode);
	}
	/// <summary>
	/// Read ExitCode and contents of PrefixOut.
	/// </summary>
	public static FunctionResultErrLocation PrefixOut {
		get => new(ResultLocationType.PrefixOut);
	}
	/// <summary>
	/// Read ExitCode and contents of PrefixErr (same as Default).
	/// </summary>
	public static FunctionResultErrLocation PrefixErr {
		get => new(ResultLocationType.PrefixErr);
	}
	/// <summary>
	/// Read ExitCode and contents of PrefixLog.
	/// </summary>
	public static FunctionResultErrLocation PrefixLog {
		get => new(ResultLocationType.PrefixLog);
	}
	
	/// <summary>
	/// In case the function result err is interpreted from non-default location.
	/// Reads ExitCode and contents of CustomPath.
	/// </summary>
	/// <param name="customPath">Absolute path to a file evaluated as result when the function exits.</param>
	/// <returns></returns>
	public static FunctionResultErrLocation FromCustomPath(string customPath) =>
		new FunctionResultErrLocation(customPath);
	
	#endregion Factory methods
	
}


using DagNode.NDF.Interoperability.Bash;

namespace DagNode.NDF.Interoperability.Model.Bash;

public class FunctionQuery(CallOptions callOptions, AbsolutePath prefixPath, FunctionFiles functionFiles, string functionName, string functionMarkerTag, long sequenceNumber, string[]? functionArgs)
{
	public string FunctionCallAsyncWrapper { get; set; } = GlobalScripts.FUNCTION_NAME___run_function__with__stdout_end_marker__async;
	public string FunctionMarkerTag { get; } = functionMarkerTag;
	public string FunctionName { get; } = functionName;
	public long SequenceNumber { get; } = sequenceNumber;
	public string PrefixPath { get; set; } = prefixPath;

	public string[]? FunctionArgs { get; set; } = functionArgs;
	/// <summary>
	/// Wraps every function arg in double quotation marks to allow spaces.
	/// </summary>
	public string GetQuotedFunctionArgs() {
		string quotedFunctionArgs = string.Join(" ", FunctionArgs?.Select(arg => $"\"{arg}\"") ?? []);
		if (quotedFunctionArgs == "\"\"") quotedFunctionArgs = string.Empty;
		return $" {quotedFunctionArgs}";
	}

	private CallOptions CallOptions { get; } = callOptions;
	
	/// <summary>
	/// Replace {prefix}.in {prefix}.out {prefix}.err with correct path.
	/// TODO: Check &gt;&amp;3 to prevent using eval(function_name ...args ${stream_redirection})
	/// </summary>
	/// <returns></returns>
	public string GetStreamRedirectionWithReplacedPrefix() => CallOptions.StreamRedirection.Replace("{prefix}", PrefixPath);
	public string GetStreamRedirectionWithReplacedPrefixAsQuotedArg() =>
		$"\"{
			CallOptions.StreamRedirection
				//.Replace("{prefix}", PrefixPath)
				.Replace(@"{prefix}.out", functionFiles.ResultOut)
				.Replace(@"{prefix}.err", functionFiles.ResultErr)
				.Replace(@"{prefix}.log", functionFiles.ResultLog)
				.Replace(@"{prefix}.in", functionFiles.InputIn)
		}\"";

	public string Compile() => ToString();
	
	//TODO: Use stream redirection at end, how to echo to standard output then? Optionally keep track/watch prefix paths
	//public override string ToString() => $"{FunctionCallAsyncWrapper} {FunctionMarkerTag} {FunctionName} {PrefixPath}{GetQuotedFunctionArgs()} {GetStreamRedirectionWithReplacedPrefix()}";
	
	// Using StreamRedirection of call_function wrapper arg and eval "$@ ${stream_redirection}"
	public override string ToString() => $"{FunctionCallAsyncWrapper} {FunctionMarkerTag} {GetStreamRedirectionWithReplacedPrefixAsQuotedArg()} {FunctionName} {PrefixPath}{GetQuotedFunctionArgs()}";
}

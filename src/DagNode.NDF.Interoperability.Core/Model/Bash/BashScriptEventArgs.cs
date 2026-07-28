using System.Diagnostics;
using DagNode.NDF.Interoperability.Bash;

namespace DagNode.NDF.Interoperability.Model.Bash;

public class ScriptReadyEventArgs : EventArgs
{
	public IDictionary<string, object>? Parameters { get; set; } = new Dictionary<string, object>();
}

public class FunctionStartEventArgs(CallOptions callOptions, AbsolutePath prefixPath, FunctionFiles functionFiles,
	string functionName, string uniqueFunctionMarkerTag, long sequenceNumber, string[]? functionArgs
	/* TimeSpan? timeout */) : EventArgs
{
	public CallOptions CallOptions { get; } = callOptions;
	public FunctionQuery FunctionQuery { get; } = new (callOptions, prefixPath, functionFiles, functionName, uniqueFunctionMarkerTag, sequenceNumber, functionArgs);
	public string FunctionName { get => FunctionQuery.FunctionName; }
	public string[]? FunctionArgs { get => FunctionQuery.FunctionArgs; }
	public string FunctionMarkerTag { get => FunctionQuery.FunctionMarkerTag; }
	public long SequenceNumber { get => FunctionQuery.SequenceNumber; }
	
	public FunctionFiles FunctionFiles { get; set; } = functionFiles;
	public AbsolutePath OutFilePath { get => FunctionFiles.ResultOut; }
	public AbsolutePath ErrFilePath { get => FunctionFiles.ResultErr; }
	public AbsolutePath LogFilePath { get => FunctionFiles.ResultLog; }
	public AbsolutePath InFilePath { get => FunctionFiles.InputIn; }
	
	/// <summary>
	/// Set by function processor when the result is ready. 
	/// </summary>
	public TaskCompletionSource<FunctionResult> FunctionResultCompletionSource { get; } = new();

	//TODO: To implement function call timeouts, we need to keep track of spawned subprocess PIDs
	//public TimeSpan? Timeout { get; set; } = timeout;

	public IDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
}

public class FunctionFinishedEventArgs : EventArgs {
	public FunctionStartEventArgs FunctionStartEventArgs { get; set; }
	public int ExitCode { get; set; }
	public FunctionResult Result { get; set; }
	public IDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
}

public class ConfigureFunctionWorkDirEventArgs(
	string scriptFileNameNormalized, string scriptInstanceMarkerTag,
	BashScriptSettings bashScriptSettings, FunctionWorkDirSettings functionWorkDirSettings
	) : EventArgs
{
	public string ScriptFileNameNormalized { get; } = scriptFileNameNormalized;
	public string ScriptInstanceMarkerTag { get; } = scriptInstanceMarkerTag;
	public BashScriptSettings BashScriptSettings { get; } = bashScriptSettings;
	public FunctionWorkDirSettings FunctionWorkDirSettings { get; } = functionWorkDirSettings;
	
	public IDictionary<string, object>? Parameters { get; } = new Dictionary<string, object>();
}
public class ConfigureFunctionMarkerEventArgs(
	string functionNameNormalized, long functionCallSequenceNumber, string scriptInstanceMarkerTag,
	BashScriptSettings bashScriptSettings, FunctionWorkDirSettings functionWorkDirSettings) : EventArgs
{
	// Function call specific
	public string FunctionNameNormalized { get; } = functionNameNormalized;
	public long FunctionCallSequenceNumber { get; } = functionCallSequenceNumber;
	
	// BashScript instance-specific
	public string ScriptInstanceMarkerTag { get; } = scriptInstanceMarkerTag;
	public BashScriptSettings BashScriptSettings { get; } = bashScriptSettings;
	public FunctionWorkDirSettings FunctionWorkDirSettings { get; } = functionWorkDirSettings;
	
	public IDictionary<string, object>? Parameters { get; } = new Dictionary<string, object>();
}

public class FunctionResultReadyEventArgs(IFunctionResultMetadata metadata) : EventArgs
{
	public IFunctionResultMetadata Metadata { get; } = metadata;
}

public class ErrorStreamReceivedEventArgs(string error) : EventArgs
{
	public string Error { get; } = error;
}

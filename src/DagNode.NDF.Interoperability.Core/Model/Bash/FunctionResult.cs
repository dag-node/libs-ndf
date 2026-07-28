using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Model;

public class FunctionResult(int exitCode,
	string? inputIn, string? standardOutput, string? standardError, string? customLog,
	string? customFile)
{
	public int ExitCode { get; } = exitCode;
	public string? InputIn { get; } = inputIn;
	public string? StandardOutput { get; } = standardOutput;
	public string? StandardError { get; } = standardError;
	public string? CustomLog { get; } = customLog;
	public string? CustomFile { get; set; } = customFile;
}

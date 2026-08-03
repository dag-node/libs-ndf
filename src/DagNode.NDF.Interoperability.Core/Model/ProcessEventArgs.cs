using System.Runtime.InteropServices;

namespace DagNode.NDF.Interoperability.Model;

public class ReadOutputStreamEventArgs(StreamReader standardOutput, CancellationToken ct)
	: EventArgs
{
	public StreamReader StandardOutput { get; } = standardOutput;
	public CancellationToken CancellationToken { get; } = ct;
}

public class ReadErrorStreamEventArgs(StreamReader standardError, CancellationToken ct)
	: EventArgs
{
	public StreamReader StandardError { get; } = standardError;
	public CancellationToken CancellationToken { get; } = ct;
}

/// <summary>Raised when the bash process exits without the library asking it to (crash, OOM kill, a hit
/// resource limit). <see cref="ExitCode"/> is null when it could not be read.</summary>
public class ProcessExitedEventArgs(int? exitCode) : EventArgs
{
	public int? ExitCode { get; } = exitCode;
}

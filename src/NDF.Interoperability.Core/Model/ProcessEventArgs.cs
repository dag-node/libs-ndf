using System.Runtime.InteropServices;

namespace NDF.Interoperability.Model;

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

using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Model;

/// <summary>
/// Everything a single bash function call produced: the exit status plus the contents of the
/// {prefix}.in, {prefix}.out, {prefix}.err and {prefix}.log files its stream redirection wrote.
/// A stream the redirection discarded (sent to /dev/null) or never created arrives as null,
/// so callers distinguish "empty output" from "no such stream".
/// </summary>
/// <param name="exitCode">Exit status returned by the bash function; 0 means success.</param>
/// <param name="inputIn">Contents of {prefix}.in, set only by the [SUBSTITUTE] and [TAKE IN] redirections.</param>
/// <param name="standardOutput">Contents of the file stdout was redirected to, {prefix}.out by default.</param>
/// <param name="standardError">Contents of the file stderr was redirected to, {prefix}.err by default.</param>
/// <param name="customLog">Contents of {prefix}.log, which the function writes to explicitly via its "$1" argument.</param>
/// <param name="customFile">Contents of a file read from a <see cref="FunctionResultLocation"/> with a custom path.</param>
public class FunctionResult(int exitCode,
	string? inputIn, string? standardOutput, string? standardError, string? customLog,
	string? customFile)
{
	/// <summary>Exit status returned by the bash function; 0 means success.</summary>
	public int ExitCode { get; } = exitCode;

	/// <summary>
	/// Contents of {prefix}.in fed to the call, or null when the redirection took no input file.
	/// </summary>
	public string? InputIn { get; } = inputIn;

	/// <summary>
	/// Contents of the file stdout was redirected to ({prefix}.out by default), or null when
	/// the redirection discarded stdout.
	/// </summary>
	public string? StandardOutput { get; } = standardOutput;

	/// <summary>
	/// Contents of the file stderr was redirected to ({prefix}.err by default), or null when
	/// the redirection discarded stderr or folded it into stdout with 2&gt;&amp;1.
	/// </summary>
	public string? StandardError { get; } = standardError;

	/// <summary>
	/// Contents of {prefix}.log, the file a function writes to itself using the {prefix} passed
	/// as its "$1" argument. Null when the function wrote nothing there.
	/// </summary>
	public string? CustomLog { get; } = customLog;

	/// <summary>
	/// Contents of a file read from a <see cref="FunctionResultLocation"/> configured with
	/// <see cref="ResultLocationType.CustomPath"/>. Null when no custom path was configured.
	/// </summary>
	public string? CustomFile { get; set; } = customFile;
}

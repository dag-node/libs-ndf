namespace DagNode.NDF.Interoperability.Model.Bash;

public class BashProcessSettings
{
	// Static factory method to provide instance with default configuration
	public static BashProcessSettings CreateFactoryDefault => new() {
		// Configure defaults
	};
	
	/// <summary>
	/// Path to the bash executable used to run scripts.
	/// </summary>
	public string BashPath { get; set; } = "/usr/bin/bash";
	/// <summary>
	/// A list of bash args.
	/// Use bash --help to get a list of available args.
	/// </summary>
	public string ProcessArgs { get; set; } = string.Empty;
	/// <summary>
	/// Environment variables passed to the script (empty by default).
	/// Use Helpers.GetSystemEnvironmentVariables to get a filtered collection of system environment variables if required.
	/// </summary>
	public Dictionary<string, string> ProcessEnvironmentVariables { get; set; } = new();
	
	/// <summary>
	/// Cancel all subshells when error is received on any bash process standard error stream.
	/// Safe to keep enabled, as normally there should be no errors on this stream.
	/// Standard errors from function calls are redirected to files by default.
	/// </summary>
	public bool TerminateOnErrorStreamReceived { get; set; } = true;

	/// <summary>
	/// Launch the resident bash in its own session via <see cref="SetsidPath"/> (default true), so it
	/// leads its own process group. The <c>___global__on_stop</c> group kill and the <c>ProcessTree</c>
	/// force-kill then reap the whole bash subtree without reaching the .NET process's group. Disable only
	/// where <c>setsid</c> is unavailable, accepting that group-scoped cleanup would target the launcher's
	/// group.
	/// </summary>
	public bool UseOwnSession { get; set; } = true;

	/// <summary>Path to the <c>setsid</c> binary used when <see cref="UseOwnSession"/> is set.</summary>
	public string SetsidPath { get; set; } = "/usr/bin/setsid";

	/// <summary>
	/// How long <c>Dispose</c> waits for bash to exit after sending <c>exit</c> before force-killing its
	/// process tree (default 2s). In-flight calls are already drained by
	/// <see cref="DagNode.NDF.Interoperability.Bash.BashScript.DisposeAsync"/>, so this only covers bash
	/// winding down; the force-kill is the belt for a wedged process.
	/// </summary>
	public TimeSpan DisposeExitGracePeriod { get; set; } = TimeSpan.FromSeconds(2);
}

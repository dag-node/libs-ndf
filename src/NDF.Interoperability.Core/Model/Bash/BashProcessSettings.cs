namespace NDF.Interoperability.Model.Bash;

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
}

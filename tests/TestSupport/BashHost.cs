using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DagNode.NDF.Interoperability.Tests;

/// <summary>
/// Runs a real <c>bash</c> over its stdin, the way the library drives one, and captures the
/// result. Each run is its own process in its own process group, so a script that signals its
/// group cannot reach the test runner.
/// </summary>
public static class BashHost
{
	/// <summary>True when a real bash can be executed on this platform.</summary>
	public static bool IsAvailable { get; } =
		RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists(BashPath);

	public const string BashPath = "/usr/bin/bash";

	public sealed record Run(string StandardOutput, string StandardError, int ExitCode)
	{
		public IReadOnlyList<string> OutputLines =>
			StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
	}

	/// <summary>
	/// Writes <paramref name="commandLines"/> to a fresh bash's stdin, one line each, and returns
	/// what it wrote back. Mirrors the library's transport: one command per line.
	/// </summary>
	public static Run Execute(params string[] commandLines)
	{
		// setsid puts bash in a process group of its own, so ___global__on_stop and any other
		// group-wide signal stays inside the test.
		var startInfo = new ProcessStartInfo("/usr/bin/setsid", [BashPath]) {
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		using var process = Process.Start(startInfo)
			?? throw new InvalidOperationException("bash did not start");

		var standardOutput = new StringBuilder();
		var standardError = new StringBuilder();
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) standardOutput.AppendLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) standardError.AppendLine(e.Data); };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		foreach (var commandLine in commandLines) process.StandardInput.WriteLine(commandLine);
		process.StandardInput.Close();

		if (!process.WaitForExit(milliseconds: 30_000)) {
			process.Kill(entireProcessTree: true);
			throw new TimeoutException("bash did not exit within 30s");
		}
		process.WaitForExit(); // Flush the asynchronous output handlers

		return new Run(standardOutput.ToString(), standardError.ToString(), process.ExitCode);
	}

	/// <summary>
	/// Sources <paramref name="functionCode"/> from a temporary file and returns the definition
	/// bash holds afterwards, which is the reference a transported body is compared against.
	/// </summary>
	public static string DeclareFromFile(string functionCode, string functionName)
	{
		using var scriptFile = new TemporaryDirectory();
		string path = Path.Combine(scriptFile.Path, "subject.sh");
		File.WriteAllText(path, functionCode);
		return Execute($"source '{path}'", $"declare -f {functionName}").StandardOutput;
	}
}

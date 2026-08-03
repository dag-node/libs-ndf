using DagNode.NDF.Interoperability.Bash;
using DagNode.NDF.Interoperability.Model;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Tests.Integration.Bash;

/// <summary>
/// A per-call timeout ends the wait and terminates that call's whole bash process tree — the call's
/// own subshell and the grandchildren it spawned — without taking the test runner down with it (the
/// resident bash runs in its own setsid session).
/// </summary>
[TestClass]
public class FunctionTerminationTests
{
	[TestInitialize]
	public void RequireBash() => BashRequirement.SkipUnlessAvailable();

	// $1 is the per-call work directory. $BASHPID here is the call's root subshell (the PID reported by
	// ___BEGIN_FN__). The backgrounded sleep is a grandchild of that subshell; both must be reaped.
	private const string SlowTreeScript =
		"""
		#!/usr/bin/bash
		function ndf_slow_tree() {
		    local workdir="$1"
		    sleep 300 &
		    echo "$!" > "$workdir/child.pid"
		    echo "$BASHPID" > "$workdir/self.pid"
		    sleep 300
		}
		""";

	[TestMethod]
	public async Task TimeoutTerminatesTheCallSubshellAndItsGrandchild()
	{
		using var scripts = new TemporaryDirectory("terminate");
		string scriptPath = scripts.WriteFile("slow.sh", SlowTreeScript + "\n");

		var settings = new BashScriptSettings(AbsolutePath.Create(scriptPath)) {
			IsDebug = false,
			// Short SIGTERM->SIGKILL grace so the test does not linger.
			FunctionTerminationGracePeriod = TimeSpan.FromMilliseconds(300),
		};

		string workDir;
		await using (var bashScript = await BashScript.CreateAsync(settings)) {
			workDir = bashScript.ConfiguredFunctionWorkDir.ToString();

			TimeoutException? timeout = null;
			try {
				await bashScript.CallFunctionAsync<string>("ndf_slow_tree", timeout: TimeSpan.FromMilliseconds(700));
			} catch (TimeoutException ex) {
				timeout = ex;
			}
			Assert.IsNotNull(timeout, "the call should have timed out");

			int selfPid = ReadReportedPid(Path.Combine(workDir, "self.pid"));
			int childPid = ReadReportedPid(Path.Combine(workDir, "child.pid"));
			Assert.IsTrue(selfPid > 0, "the function did not report its own PID");
			Assert.IsTrue(childPid > 0, "the function did not report its grandchild PID");

			await AssertProcessGoneAsync(selfPid, "the call subshell");
			await AssertProcessGoneAsync(childPid, "the grandchild");
		}

		// The resident bash was disposed and the test is still running: its setsid session kept the
		// teardown signals off the test runner's process group.
		TryDelete(workDir);
	}

	private static int ReadReportedPid(string path)
	{
		for (int attempt = 0; attempt < 50; attempt++) {
			if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out int pid)) return pid;
			Thread.Sleep(20);
		}
		return -1;
	}

	private static async Task AssertProcessGoneAsync(int pid, string what)
	{
		for (int attempt = 0; attempt < 100; attempt++) { // up to ~5s
			if (!IsAlive(pid)) return;
			await Task.Delay(50);
		}
		Assert.Fail($"{what} (pid {pid}) was still alive after termination");
	}

	// Alive means present in /proc and not a zombie/dead state, so a reaped or killed process reads gone.
	private static bool IsAlive(int pid)
	{
		try {
			string stat = File.ReadAllText($"/proc/{pid}/stat");
			int lastParen = stat.LastIndexOf(')');
			if (lastParen < 0 || lastParen + 2 >= stat.Length) return false;
			char state = stat[lastParen + 2];
			return state != 'Z' && state != 'X';
		} catch {
			return false; // No /proc entry: gone.
		}
	}

	private static void TryDelete(string dir)
	{
		try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
	}
}

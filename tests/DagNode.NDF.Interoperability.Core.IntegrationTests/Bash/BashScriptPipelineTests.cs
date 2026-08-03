using DagNode.NDF.Interoperability.Bash;
using DagNode.NDF.Interoperability.Model;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Tests.Integration.Bash;

/// <summary>
/// The consumer-facing pipeline end to end: one script sourced into one resident bash, then typed
/// function calls — scalars, collections, enums, booleans, args with spaces — parsed back into .NET,
/// run sequentially and in parallel, plus the graceful-drain disposal contract. These are the first
/// tests to drive <see cref="BashScript"/> end to end (the other integration tests use the raw
/// <c>BashHost</c>).
///
/// <see cref="ManyCreateDisposeCyclesStayStable"/> is the regression for the stdout-reader lifetime fix:
/// binding the reader to the transient startup token once left a bash with no reader after a create/dispose
/// cycle, so its first call hung. <see cref="UnexpectedBashExitFailsPendingCallInsteadOfHanging"/> covers
/// the fail-fast path a crashed or resource-limited bash now takes instead of hanging its callers.
/// </summary>
[TestClass]
public class BashScriptPipelineTests
{
	private enum ProcessingState { Idle, Processing, Done }

	[TestInitialize]
	public void RequireBash() => BashRequirement.SkipUnlessAvailable();

	// $1 is the per-call work directory; user args start at $2. kill_bash SIGKILLs the resident bash's whole
	// process group ($$ is the setsid-isolated group id, so this stays off the test runner), taking the call's
	// own subshell with it so no result marker escapes — standing in for an OOM kill or a hit resource limit.
	private const string Script =
		"""
		#!/usr/bin/bash
		function get_hello() { echo "Hello, World!"; }
		function get_int() { echo 42; }
		function double_arg() { echo $(( $2 * 2 )); }
		function get_lines() { printf 'alpha\nbeta\ngamma\n'; }
		function get_path() { printf '/usr/bin:/bin:/usr/local/bin'; }
		function get_nums() { printf '1\n2\n3\n'; }
		function get_people_csv() { printf 'name,age\nAlice,30\nBob,25\n'; }
		function echo_args() { shift; echo "$*"; }
		function is_zero() { [ "$2" -eq 0 ]; }
		function get_state() { printf '  procESSing  \n'; }
		function answer_yes() { echo yes; }
		function slow_echo() { sleep "$2"; echo drained; }
		function kill_bash() { kill -s KILL -- -$$; }
		function stream_lines() { for i in $(seq 1 5); do echo "line-$i"; done; }
		function stream_follow() { local i=0; while true; do i=$((i+1)); echo "tick-$i"; sleep 0.05; done; }
		function follow_file() { stdbuf -oL tail -n +1 -f "$2"; }
		""";

	private static async Task<BashScript> CreateAsync(TemporaryDirectory scripts)
	{
		string scriptPath = scripts.WriteFile("api.sh", Script + "\n");
		return await BashScript.CreateAsync(
			new BashScriptSettings(AbsolutePath.Create(scriptPath)) { IsDebug = false });
	}

	[TestMethod]
	public async Task SourcesOnceAndConvertsEveryReturnType()
	{
		using var scripts = new TemporaryDirectory("api-types");
		await using var bash = await CreateAsync(scripts);

		Assert.AreEqual("Hello, World!", await bash.CallFunctionAsync<string>("get_hello"));
		Assert.AreEqual(42, await bash.CallFunctionAsync<int>("get_int"));
		Assert.AreEqual(84L, await bash.CallFunctionAsync<long>("double_arg", ["42"]));

		CollectionAssert.AreEqual(
			new[] { "alpha", "beta", "gamma" },
			await bash.CallFunctionAsync<string[]>("get_lines"));
		CollectionAssert.AreEqual(
			new List<string> { "alpha", "beta", "gamma" },
			await bash.CallFunctionAsync<List<string>>("get_lines"));

		Assert.AreEqual(
			"Plan 9 from Outer Space",
			await bash.CallFunctionAsync<string>("echo_args", ["Plan 9", "from", "Outer Space"]));

		Assert.IsTrue(await bash.CallFunctionAsync<bool>("is_zero", ["0"]));
		Assert.IsFalse(await bash.CallFunctionAsync<bool>("is_zero", ["7"]));

		// Enum parsing trims and is case-insensitive: "  procESSing  " -> Processing.
		Assert.AreEqual(ProcessingState.Processing, await bash.CallFunctionAsync<ProcessingState>("get_state"));
	}

	[TestMethod]
	public async Task SupportsCustomParserOnTheGenericCall()
	{
		using var scripts = new TemporaryDirectory("api-custom");
		await using var bash = await CreateAsync(scripts);

		// A caller-supplied parser owns the whole conversion (here a stdout predicate rather than exit code).
		bool yes = await bash.CallFunctionAsync<bool>("answer_yes",
			resultParser: result => result.StandardOutput?.Trim() == "yes");
		Assert.IsTrue(yes);
	}

	private sealed record Person(string Name, int Age);

	[TestMethod]
	public async Task CallFunctionEnumerableSplitsProjectsAndSkipsHeader()
	{
		using var scripts = new TemporaryDirectory("api-enumerable");
		await using var bash = await CreateAsync(scripts);

		// String items need the explicit type argument; the separator defaults to newline (one record per line).
		IEnumerable<string> lines = await bash.CallFunctionEnumerableAsync<string>("get_lines");
		CollectionAssert.AreEqual(new[] { "alpha", "beta", "gamma" }, lines.ToArray());

		// A delimited scalar (PATH-like) splits on its own separator, not newline.
		IEnumerable<string> dirs = await bash.CallFunctionEnumerableAsync<string>("get_path", resultSeparator: ":");
		CollectionAssert.AreEqual(new[] { "/usr/bin", "/bin", "/usr/local/bin" }, dirs.ToArray());

		// Typed items: lineParser projects each record; TItem is inferred from it.
		IEnumerable<int> nums = await bash.CallFunctionEnumerableAsync("get_nums", lineParser: int.Parse);
		CollectionAssert.AreEqual(new[] { 1, 2, 3 }, nums.ToArray());

		// CSV is newline-delimited rows of comma-delimited fields: split on newline, skip the header row,
		// and split each row on ',' inside the lineParser into a typed record.
		IEnumerable<Person> people = await bash.CallFunctionEnumerableAsync("get_people_csv",
			skipLines: 1,
			lineParser: row => { var f = row.Split(','); return new Person(f[0], int.Parse(f[1])); });
		CollectionAssert.AreEqual(new[] { new Person("Alice", 30), new Person("Bob", 25) }, people.ToArray());

		// Without a lineParser only string items can be produced, so a non-string TItem fails closed.
		await Assert.ThrowsExactlyAsync<InteroperabilityException>(
			() => bash.CallFunctionEnumerableAsync<int>("get_nums"));
	}

	[TestMethod]
	public async Task RunsManyCallsInParallelOnOneResidentProcess()
	{
		using var scripts = new TemporaryDirectory("api-parallel");
		await using var bash = await CreateAsync(scripts);

		Task<long>[] calls = Enumerable.Range(1, 16)
			.Select(n => bash.CallFunctionAsync<long>("double_arg", [n.ToString()]))
			.ToArray();
		long[] results = await Task.WhenAll(calls);

		CollectionAssert.AreEqual(Enumerable.Range(1, 16).Select(n => (long)(n * 2)).ToArray(), results);
	}

	[TestMethod]
	public async Task DisposeAsyncDrainsAnInFlightCall()
	{
		using var scripts = new TemporaryDirectory("api-drain");
		var bash = await CreateAsync(scripts);

		// Start a ~400ms call, let it reach the running queue, then dispose: DisposeAsync must let it
		// finish rather than cancel it.
		Task<string> inFlight = bash.CallFunctionAsync<string>("slow_echo", ["0.4"]);
		await Task.Delay(100);

		await bash.DisposeAsync();

		Assert.AreEqual("drained", await inFlight, "DisposeAsync should have drained the in-flight call");
	}

	[TestMethod]
	public async Task ManyCreateDisposeCyclesStayStable()
	{
		// Regression for the stdout-reader lifetime fix: each create/dispose cycle stands up a fresh bash
		// with its own reader; the reader must not be bound to the startup token the sourcing path disposes
		// on return, or a later cycle's bash is left unread and its first call hangs. Every cycle here both
		// starts (sourcing markers must be read) and calls (a result marker must be read), so a lost reader
		// surfaces as a hang the run's timeout catches. Bash/fd/thread counts stay flat across the loop.
		for (int i = 0; i < 25; i++) {
			using var scripts = new TemporaryDirectory($"api-seq-{i}");
			await using var bash = await CreateAsync(scripts);
			Assert.AreEqual(42, await bash.CallFunctionAsync<int>("get_int"), $"cycle {i}");
		}
	}

	[TestMethod]
	public async Task StreamFunctionYieldsFiniteOutputLineByLine()
	{
		using var scripts = new TemporaryDirectory("api-stream");
		await using var bash = await CreateAsync(scripts);

		var lines = new List<string>();
		await foreach (string line in bash.StreamFunctionAsync<string>("stream_lines")) lines.Add(line);
		CollectionAssert.AreEqual(new[] { "line-1", "line-2", "line-3", "line-4", "line-5" }, lines);

		// Typed projection and skipLines stream as well.
		var nums = new List<int>();
		await foreach (int n in bash.StreamFunctionAsync("get_nums", skipLines: 1, lineParser: int.Parse)) nums.Add(n);
		CollectionAssert.AreEqual(new[] { 2, 3 }, nums);
	}

	[TestMethod]
	public async Task StreamFunctionFollowsAliveProducerAndTerminatesOnBreak()
	{
		using var scripts = new TemporaryDirectory("api-stream-follow");
		await using var bash = await CreateAsync(scripts);

		// stream_follow never ends on its own (like tail -f); reaching three ticks and breaking proves the
		// output is yielded as produced (a wait-for-marker design would hang here), and the await foreach
		// disposing terminates the still-running function's process tree instead of leaking it.
		var ticks = new List<string>();
		await foreach (string line in bash.StreamFunctionAsync<string>("stream_follow")) {
			ticks.Add(line);
			if (ticks.Count >= 3) break;
		}
		Assert.AreEqual(3, ticks.Count);
		Assert.IsTrue(ticks[0].StartsWith("tick-", StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task StreamFunctionFollowsRealTailF()
	{
		using var scripts = new TemporaryDirectory("api-tailf");
		string scriptPath = scripts.WriteFile("api.sh", Script + "\n");
		await using var bash = await BashScript.CreateAsync(new BashScriptSettings(AbsolutePath.Create(scriptPath)));

		// A real file that a background writer appends to while the streamed function `tail -f`s it. The
		// function uses `stdbuf -oL tail` so tail line-buffers into its redirected {prefix}.out (otherwise it
		// block-buffers to a non-tty and the stream would see nothing until ~4KB). `tail -n +1 -f` reads from
		// the first line and then follows, so the records arrive in order regardless of the startup timing.
		string feed = Path.Combine(scripts.Path, "feed.txt");
		File.WriteAllText(feed, ""); // tail -f needs the file to exist when it opens

		using var writerCts = new CancellationTokenSource();
		Task writer = Task.Run(async () => {
			for (int i = 1; i <= 50 && !writerCts.IsCancellationRequested; i++) {
				await File.AppendAllTextAsync(feed, $"append-{i}\n", writerCts.Token).ConfigureAwait(false);
				try { await Task.Delay(30, writerCts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
			}
		});

		var captured = new List<string>();
		await foreach (string line in bash.StreamFunctionAsync<string>("follow_file", [feed])) {
			captured.Add(line);
			if (captured.Count >= 5) break; // stop following: terminates tail -f's process tree
		}

		writerCts.Cancel();
		try { await writer; } catch { /* cancelled */ }

		CollectionAssert.AreEqual(new[] { "append-1", "append-2", "append-3", "append-4", "append-5" }, captured);
	}

	private static int CountResultFiles(string workDir) =>
		Directory.Exists(workDir) ? Directory.GetFiles(workDir).Length : 0;

	[TestMethod]
	public async Task FunctionFileCleanupHonorsTheConfiguredMode()
	{
		// AfterCall: the call's {prefix} files are gone as soon as the call returns.
		using (var scripts = new TemporaryDirectory("api-cleanup-after")) {
			string path = scripts.WriteFile("api.sh", Script + "\n");
			var settings = new BashScriptSettings(AbsolutePath.Create(path)) { FunctionFileCleanup = FunctionFileCleanup.AfterCall };
			await using var bash = await BashScript.CreateAsync(settings);
			await bash.CallFunctionAsync<int>("get_int");
			Assert.AreEqual(0, CountResultFiles(bash.ConfiguredFunctionWorkDir), "AfterCall should delete the call's files immediately");
		}

		// OnDispose (default): files persist through the session, then are cleared on dispose.
		using (var scripts = new TemporaryDirectory("api-cleanup-dispose")) {
			string path = scripts.WriteFile("api.sh", Script + "\n");
			var bash = await BashScript.CreateAsync(new BashScriptSettings(AbsolutePath.Create(path)));
			await bash.CallFunctionAsync<int>("get_int");
			string workDir = bash.ConfiguredFunctionWorkDir;
			Assert.IsTrue(CountResultFiles(workDir) > 0, "OnDispose should keep the files during the session");
			await bash.DisposeAsync();
			Assert.AreEqual(0, CountResultFiles(workDir), "OnDispose should clear the files on dispose");
		}

		// Never: files remain after dispose (cleaned up here to keep the run hermetic).
		using (var scripts = new TemporaryDirectory("api-cleanup-never")) {
			string path = scripts.WriteFile("api.sh", Script + "\n");
			var settings = new BashScriptSettings(AbsolutePath.Create(path)) { FunctionFileCleanup = FunctionFileCleanup.Never };
			var bash = await BashScript.CreateAsync(settings);
			await bash.CallFunctionAsync<int>("get_int");
			string workDir = bash.ConfiguredFunctionWorkDir;
			await bash.DisposeAsync();
			Assert.IsTrue(CountResultFiles(workDir) > 0, "Never should leave the files on disk");
			try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
		}
	}

	[TestMethod]
	public async Task UnexpectedBashExitFailsPendingCallInsteadOfHanging()
	{
		using var scripts = new TemporaryDirectory("api-crash");
		await using var bash = await CreateAsync(scripts);

		// kill_bash SIGKILLs the resident bash mid-call. The result can never arrive, so the call must fail
		// with a meaningful error rather than wait forever; the timeout is a backstop that turns a
		// regression (a hang) into a distinct, non-blocking failure.
		var ex = await Assert.ThrowsExactlyAsync<InteroperabilityException>(
			() => bash.CallFunctionAsync<string>("kill_bash", timeout: TimeSpan.FromSeconds(30)));
		StringAssert.Contains(ex.Message, "exited unexpectedly");
	}
}

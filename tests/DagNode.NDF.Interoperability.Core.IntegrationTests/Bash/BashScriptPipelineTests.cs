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
		function get_csv() { printf 'one,two,three'; }
		function echo_args() { shift; echo "$*"; }
		function is_zero() { [ "$2" -eq 0 ]; }
		function get_state() { printf '  procESSing  \n'; }
		function answer_yes() { echo yes; }
		function slow_echo() { sleep "$2"; echo drained; }
		function kill_bash() { kill -s KILL -- -$$; }
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
	public async Task SupportsCustomParserAndSeparator()
	{
		using var scripts = new TemporaryDirectory("api-custom");
		await using var bash = await CreateAsync(scripts);

		// A caller-supplied parser owns the whole conversion (here a stdout predicate rather than exit code).
		bool yes = await bash.CallFunctionAsync<bool>("answer_yes",
			resultParser: result => result.StandardOutput?.Trim() == "yes");
		Assert.IsTrue(yes);

		string[] parts = await bash.CallFunctionAsync<string[]>("get_csv", resultSeparator: ",");
		CollectionAssert.AreEqual(new[] { "one", "two", "three" }, parts);
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
	public async Task UnexpectedBashExitFailsPendingCallInsteadOfHanging()
	{
		using var scripts = new TemporaryDirectory("api-crash");
		await using var bash = await CreateAsync(scripts);

		// kill_bash SIGKILLs the resident bash mid-call. The result can never arrive, so the call must fail
		// with a meaningful error rather than wait forever; the timeout is a backstop that turns a
		// regression (a hang) into a distinct, non-blocking failure.
		var ex = await Assert.ThrowsExceptionAsync<InteroperabilityException>(
			() => bash.CallFunctionAsync<string>("kill_bash", timeout: TimeSpan.FromSeconds(30)));
		StringAssert.Contains(ex.Message, "exited unexpectedly");
	}
}

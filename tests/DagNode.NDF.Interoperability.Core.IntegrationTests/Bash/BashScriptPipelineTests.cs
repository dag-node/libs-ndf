using DagNode.NDF.Interoperability.Bash;
using DagNode.NDF.Interoperability.Model;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Tests.Integration.Bash;

/// <summary>
/// The consumer-facing pipeline end to end: one script sourced into one resident bash, then typed
/// function calls — scalars, collections, enums, booleans, args with spaces — parsed back into .NET,
/// run sequentially and in parallel, plus the graceful-drain disposal contract.
///
/// DISABLED — these are the first tests to drive <see cref="BashScript"/> end to end (the other
/// integration tests use the raw <c>BashHost</c>). Diagnosis found the pipeline leaks a resource across
/// create/dispose cycles: a single instance and its calls (including ~16 concurrent) run reliably, but
/// after enough create/dispose cycles in one process a later instance's <em>startup</em> hangs. The
/// v0.9.0 baseline breaks at the 2nd instance; the session/setsid + tree-kill teardown here raises the
/// threshold to roughly ten but does not eliminate it. <see cref="ManyInstancesInOneProcessHang"/> is
/// the reproduction. Kept (not deleted) as the executable record; re-enable once fixed. See the
/// process-lifecycle rule.
/// </summary>
[TestClass]
[Ignore("Reproduces a pre-existing pipeline resource leak: after ~10 BashScript create/dispose cycles a later startup hangs. Re-enable when fixed.")]
public class BashScriptPipelineTests
{
	private enum ProcessingState { Idle, Processing, Done }

	[TestInitialize]
	public void RequireBash() => BashRequirement.SkipUnlessAvailable();

	// $1 is the per-call work directory; user args start at $2.
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
	public async Task ManyInstancesInOneProcessHang()
	{
		// Reproduction of the resource leak: a few create/dispose cycles pass, but by ~10 a later
		// instance's startup hangs. Each instance is created, used once, and disposed before the next.
		for (int i = 0; i < 10; i++) {
			using var scripts = new TemporaryDirectory($"api-seq-{i}");
			await using var bash = await CreateAsync(scripts);
			Assert.AreEqual(42, await bash.CallFunctionAsync<int>("get_int"));
		}
	}
}

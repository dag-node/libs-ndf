using DagNode.NDF.Interoperability.Bash;
using DagNode.NDF.Interoperability.Model;

namespace DagNode.NDF.Interoperability.Tests.Integration.Bash;

/// <summary>
/// The generated sourcing commands, a real bash, and the marker parser driven together: what the
/// library writes to stdin is what the parser reads back.
/// </summary>
[TestClass]
public class GlobalScriptSourcingTests
{
	[TestInitialize]
	public void RequireBash() => BashRequirement.SkipUnlessAvailable();

	[TestMethod]
	public void SourcesTheGlobalFunctionsTheLibraryDependsOn()
	{
		// The signal traps are deliberately not installed here: they signal the whole process
		// group, which is why BashHost gives each run a group of its own.
		var run = BashHost.Execute(
			GlobalScripts.SOURCE_FUNCTION___global__on_stop,
			GlobalScripts.SOURCE_FUNCTION___run_function__with__stdout_end_marker__async,
			$"declare -F {GlobalScripts.FUNCTION_NAME___run_function__with__stdout_end_marker__async}");

		var sourcingResults = run.OutputLines
			.Select(FunctionParser.ReadSourcingResultAsync)
			.Where(result => result is not null)
			.ToList();

		Assert.AreEqual(2, sourcingResults.Count);
		foreach (string? result in sourcingResults)
			StringAssert.Contains(result!, FunctionParser.SOURCED_SUCCESSFULLY);
	}

	[TestMethod]
	public void RunsAFunctionThroughTheWrapperAndReportsItsResult()
	{
		var run = BashHost.Execute(
			GlobalScripts.SOURCE_FUNCTION___run_function__with__stdout_end_marker__async,
			GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(
				"ndf_subject", "function ndf_subject() { echo running; }"),
			$"{GlobalScripts.FUNCTION_NAME___run_function__with__stdout_end_marker__async} ndf_subject-1 \"\" ndf_subject",
			"wait");

		string? endMarkerLine = run.OutputLines
			.FirstOrDefault(line => line.StartsWith(FunctionParser.FUNCTION_END_MARKER, StringComparison.Ordinal));

		Assert.IsNotNull(endMarkerLine, "the wrapper reported no function end marker");
		Assert.IsTrue(FunctionParser.TryParseFunctionResultMetadata(endMarkerLine, out var metadata));
		Assert.IsNotNull(metadata);
		Assert.AreEqual("ndf_subject-1", metadata.FunctionMarkerTag);
		Assert.AreEqual(0, metadata.ExitCode);
		CollectionAssert.Contains(run.OutputLines.ToArray(), "running");
	}

	[TestMethod]
	public void ReportsAScriptFileItSourcedFromAPathHoldingSpaces()
	{
		using var scripts = new TemporaryDirectory("with space");
		string scriptPath = scripts.WriteFile("fn one.sh", "function ndf_from_file() { echo from-file; }\n");

		var run = BashHost.Execute(
			LinuxUtils.ValidateAndSourceBashScriptWithSourcingResult(AbsolutePath.Create(scriptPath)),
			"ndf_from_file");

		string? sourcingLine = run.OutputLines
			.FirstOrDefault(line => line.StartsWith(FunctionParser.SOURCING_END_MARKER, StringComparison.Ordinal));

		Assert.IsNotNull(sourcingLine, "sourcing reported no result line");
		StringAssert.Contains(FunctionParser.ReadSourcingResultAsync(sourcingLine)!, scriptPath);
		CollectionAssert.Contains(run.OutputLines.ToArray(), "from-file");
	}

	[TestMethod]
	public void ReportsFailureForAScriptFileThatIsNotThere()
	{
		var run = BashHost.Execute(
			LinuxUtils.ValidateAndSourceBashScriptWithSourcingResult(
				AbsolutePath.Create("/nonexistent/ndf/missing.sh")));

		string sourcingLine = run.OutputLines
			.First(line => line.StartsWith(FunctionParser.SOURCING_END_MARKER, StringComparison.Ordinal));

		Assert.ThrowsExactly<InteroperabilityException>(
			() => FunctionParser.ReadSourcingResultAsync(sourcingLine));
	}
}

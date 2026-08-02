using DagNode.NDF.Interoperability.Bash;
using DagNode.NDF.Interoperability.Model;

namespace DagNode.NDF.Interoperability.Tests.Unit.Bash;

/// <summary>
/// Marker lines are asserted whole rather than by reconstructing field offsets, so a change to
/// the layout fails here instead of agreeing with itself.
/// </summary>
[TestClass]
public class FunctionParserTests
{
	// ___END_FN__ {startNs} {endNs} {duration} {markerTag} {exitCode}
	private const string FunctionEndLine =
		"___END_FN__ 1673638457000000000 1673638458000000000 00:00:01.000000 my_function-B0Ab-1 0";

	[TestMethod]
	public void ReadsEveryFieldOfAFunctionEndMarker()
	{
		Assert.IsTrue(FunctionParser.TryParseFunctionResultMetadata(FunctionEndLine, out var metadata));

		Assert.IsNotNull(metadata);
		Assert.AreEqual("my_function-B0Ab-1", metadata.FunctionMarkerTag);
		Assert.AreEqual(0, metadata.ExitCode);
		Assert.AreEqual(TimeSpan.FromSeconds(1), metadata.Duration);
		Assert.AreEqual(new DateTime(2023, 1, 13, 19, 34, 17, DateTimeKind.Utc), metadata.FunctionStartTimeUtc);
		Assert.AreEqual(new DateTime(2023, 1, 13, 19, 34, 18, DateTimeKind.Utc), metadata.FunctionEndTimeUtc);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow("ordinary script output")]
	[DataRow("___END_SOURCE_FN__ /opt/script.sh SOURCED_SUCCESSFULLY")]
	public void PassesOverALineThatIsNotAFunctionEndMarker(string line)
	{
		Assert.IsFalse(FunctionParser.TryParseFunctionResultMetadata(line, out var metadata));
		Assert.IsNull(metadata);
	}

	[TestMethod]
	public void RejectsAFunctionEndMarkerMissingItsExitCode()
	{
		string withoutExitCode = FunctionEndLine[..FunctionEndLine.LastIndexOf(' ')];

		Assert.ThrowsExactly<InteroperabilityException>(
			() => FunctionParser.TryParseFunctionResultMetadata(withoutExitCode, out _));
	}

	[TestMethod]
	[DataRow("___END_SOURCE_FN__ ___global__on_stop SOURCED_SUCCESSFULLY", "___global__on_stop")]
	[DataRow("___END_SOURCE_FN__ /opt/scripts/functions.sh SOURCED_SUCCESSFULLY", "/opt/scripts/functions.sh")]
	[DataRow("___END_SOURCE_FN__ /opt/my scripts/fn one.sh SOURCED_SUCCESSFULLY", "/opt/my scripts/fn one.sh")]
	public void NamesTheSourcedObjectEvenWhereItsPathHoldsSpaces(string line, string expectedObject)
	{
		string? result = FunctionParser.ReadSourcingResultAsync(line);

		Assert.IsNotNull(result);
		StringAssert.Contains(result, expectedObject);
		StringAssert.Contains(result, FunctionParser.SOURCED_SUCCESSFULLY);
	}

	[TestMethod]
	[DataRow("___END_SOURCE_FN__ ndf_fn SOURCING_FAILED")]
	[DataRow("___END_SOURCE_FN__ ndf_fn ERROR_IN_FUNCTION")]
	[DataRow("___END_SOURCE_FN__ /opt/my scripts/fn one.sh SOURCING_FAILED")]
	public void RaisesOnAnOutcomeOtherThanSuccess(string line) =>
		Assert.ThrowsExactly<InteroperabilityException>(() => FunctionParser.ReadSourcingResultAsync(line));

	[TestMethod]
	public void PassesOverALineThatIsNotASourcingResult() =>
		Assert.IsNull(FunctionParser.ReadSourcingResultAsync("some long line of ordinary script output"));
}

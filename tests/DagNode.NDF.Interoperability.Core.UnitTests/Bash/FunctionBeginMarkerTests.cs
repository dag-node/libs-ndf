using DagNode.NDF.Interoperability.Bash;

namespace DagNode.NDF.Interoperability.Tests.Unit.Bash;

/// <summary>
/// The <c>___BEGIN_FN__ {markerTag} {pid}</c> line is token-parsed (marker tag carries no spaces, pid
/// is the last token). Asserted over whole lines so a layout change fails here.
/// </summary>
[TestClass]
public class FunctionBeginMarkerTests
{
	[TestMethod]
	public void ParsesMarkerTagAndPid()
	{
		Assert.IsTrue(FunctionParser.TryParseFunctionBegin(
			"___BEGIN_FN__ get_string-B0Ab-1 48213", out string tag, out int pid));
		Assert.AreEqual("get_string-B0Ab-1", tag);
		Assert.AreEqual(48213, pid);
	}

	[TestMethod]
	[DataRow("___END_FN__ 1 2 00:00:01.000000 x-1 0", DisplayName = "a different marker")]
	[DataRow("___BEGIN_FN__ only_marker_no_pid", DisplayName = "no pid token")]
	[DataRow("___BEGIN_FN__  48213", DisplayName = "empty marker tag")]
	[DataRow("___BEGIN_FN__ x-1 not_a_number", DisplayName = "non-numeric pid")]
	[DataRow("___BEGIN_FN__", DisplayName = "marker only")]
	[DataRow("", DisplayName = "empty line")]
	[DataRow("prefix ___BEGIN_FN__ x-1 42", DisplayName = "marker not at start")]
	public void RejectsMalformedLines(string line)
	{
		Assert.IsFalse(FunctionParser.TryParseFunctionBegin(line, out string tag, out int pid));
		Assert.AreEqual(string.Empty, tag);
		Assert.AreEqual(0, pid);
	}
}

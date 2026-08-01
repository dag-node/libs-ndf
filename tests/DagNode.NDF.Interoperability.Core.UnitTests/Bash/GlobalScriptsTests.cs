using System.Text;
using DagNode.NDF.Interoperability.Bash;

namespace DagNode.NDF.Interoperability.Tests.Unit.Bash;

/// <summary>
/// The runtime half of the sourcing-transport guarantee: what the generated command says, and
/// which inputs it refuses. The boundary half executes the same command in a real bash.
/// </summary>
[TestClass]
public class GlobalScriptsTests
{
	private const string ValidBody = "function ndf_valid() { echo hi; }";

	[TestMethod]
	[DataRow("f; rm -rf /")]
	[DataRow("f$(id)")]
	[DataRow("f`id`")]
	[DataRow("f'; echo pwned; '")]
	[DataRow("1f")]
	[DataRow("f-g")]
	[DataRow("f.g")]
	[DataRow("f g")]
	[DataRow("")]
	public void RefusesAFunctionNameOutsideTheIdentifierAllowlist(string functionName)
	{
		var refusal = Assert.ThrowsExactly<ArgumentException>(
			() => GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(functionName, ValidBody));

		Assert.AreEqual("functionName", refusal.ParamName);
	}

	[TestMethod]
	[DataRow("f")]
	[DataRow("_f")]
	[DataRow("ndf_run_1")]
	[DataRow("___global__on_stop")]
	public void AcceptsABashIdentifier(string functionName)
	{
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(functionName, ValidBody);

		StringAssert.Contains(command, functionName);
	}

	[TestMethod]
	[DynamicData(nameof(BashCorpus.AllNames), typeof(BashCorpus), DynamicDataDisplayName = nameof(BashCorpus.DisplayName), DynamicDataDisplayNameDeclaringType = typeof(BashCorpus))]
	public void GeneratesExactlyOneLineWhateverTheBodyContains(string subjectName)
	{
		var subject = BashCorpus.ByName(subjectName);

		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(
			subject.FunctionName, subject.Body);

		// The bash process reads one command per line, so a body carrying newlines still has to
		// arrive as a single line.
		Assert.IsFalse(command.Contains('\n'), "the generated command spans more than one line");
		Assert.IsFalse(command.Contains('\r'), "the generated command carries a carriage return");
	}

	[TestMethod]
	[DynamicData(nameof(BashCorpus.AllNames), typeof(BashCorpus), DynamicDataDisplayName = nameof(BashCorpus.DisplayName), DynamicDataDisplayNameDeclaringType = typeof(BashCorpus))]
	public void CarriesTheBodyAsBase64RatherThanAsShellText(string subjectName)
	{
		var subject = BashCorpus.ByName(subjectName);

		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(
			subject.FunctionName, subject.Body);

		string payload = Base64PayloadOf(command);
		Assert.AreEqual(subject.Body, Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
	}

	[TestMethod]
	public void NormalizesWindowsLineEndingsBeforeEncoding()
	{
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(
			"ndf_crlf", "function ndf_crlf() {\r\n  echo hi\r\n}");

		string body = Encoding.UTF8.GetString(Convert.FromBase64String(Base64PayloadOf(command)));
		Assert.IsFalse(body.Contains('\r'), "a carriage return reached the encoded body");
	}

	[TestMethod]
	public void ReportsEachSourcingOutcomeWithTheMarkerTheParserReads()
	{
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult("ndf_valid", ValidBody);

		StringAssert.Contains(command, $"{FunctionParser.SOURCING_END_MARKER} ndf_valid {FunctionParser.SOURCED_SUCCESSFULLY}");
		StringAssert.Contains(command, $"{FunctionParser.SOURCING_END_MARKER} ndf_valid {FunctionParser.SOURCING_FAILED}");
		StringAssert.Contains(command, $"{FunctionParser.SOURCING_END_MARKER} ndf_valid {FunctionParser.ERROR_IN_FUNCTION}");
	}

	[TestMethod]
	public void ValidatesTheDecodedBodyAndTheDefinitionItProduces()
	{
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult("ndf_valid", ValidBody);

		StringAssert.Contains(command, "bash -n");                // Syntax gate, ahead of sourcing
		StringAssert.Contains(command, "declare -F ndf_valid");   // Post-condition of sourcing
	}

	/// <summary>Reads the single-quoted base64 argument out of the generated command.</summary>
	private static string Base64PayloadOf(string command)
	{
		int start = command.IndexOf('\'') + 1;
		int end = command.IndexOf('\'', start);
		return command[start..end];
	}
}

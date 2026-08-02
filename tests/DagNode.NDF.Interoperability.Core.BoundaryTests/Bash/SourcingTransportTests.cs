using System.Text;
using DagNode.NDF.Interoperability.Bash;

namespace DagNode.NDF.Interoperability.Tests.Boundary.Bash;

/// <summary>
/// The boundary half of the sourcing-transport guarantee: the generated command is executed by a
/// real bash and the definition it produces is compared against sourcing the same body from a
/// file. The unit tests assert what the command says; these assert what bash does with it.
/// </summary>
[TestClass]
public class SourcingTransportTests
{
	[TestInitialize]
	public void RequireBash() => BashRequirement.SkipUnlessAvailable();

	[TestMethod]
	[DynamicData(nameof(BashCorpus.AllNames), typeof(BashCorpus), DynamicDataDisplayName = nameof(BashCorpus.DisplayName), DynamicDataDisplayNameDeclaringType = typeof(BashCorpus))]
	public void DefinesTheSameFunctionAsSourcingTheBodyFromAFile(string subjectName)
	{
		var subject = BashCorpus.ByName(subjectName);
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(
			subject.FunctionName, subject.Body);

		var run = BashHost.Execute(command, $"declare -f {subject.FunctionName}");

		CollectionAssert.Contains(
			run.OutputLines.ToArray(),
			$"{FunctionParser.SOURCING_END_MARKER} {subject.FunctionName} {FunctionParser.SOURCED_SUCCESSFULLY}");

		string transported = string.Join('\n', run.OutputLines.Skip(1));
		string sourcedFromFile = string.Join('\n',
			BashHost.DeclareFromFile(subject.Body, subject.FunctionName)
				.Split('\n', StringSplitOptions.RemoveEmptyEntries));
		// Both sides empty would compare equal while proving nothing.
		StringAssert.Contains(sourcedFromFile, subject.FunctionName);
		Assert.AreEqual(sourcedFromFile, transported, "the transported body differs from the body sourced from a file");
	}

	[TestMethod]
	public void CarriesEscapeSequencesWithoutInterpretingThem()
	{
		var subject = BashCorpus.ByName("escape-sequences");
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(
			subject.FunctionName, subject.Body);

		var run = BashHost.Execute(command, subject.FunctionName);

		// The octal escape reaches the function as text; a transport interpreting it prints "x/y".
		CollectionAssert.Contains(run.OutputLines.ToArray(), @"x\0057y");
		CollectionAssert.Contains(run.OutputLines.ToArray(), @"C:\Users\test");
	}

	[TestMethod]
	public void KeepsAHereDocsNewlinesAndIndentation()
	{
		var subject = BashCorpus.ByName("here-doc");
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(
			subject.FunctionName, subject.Body);

		var run = BashHost.Execute(command, subject.FunctionName);

		CollectionAssert.Contains(run.OutputLines.ToArray(), "one\ttabbed");
		CollectionAssert.Contains(run.OutputLines.ToArray(), "  two indented");
	}

	[TestMethod]
	[DataRow("'; touch ESCAPED; echo '")]
	[DataRow("$(touch ESCAPED)")]
	[DataRow("`touch ESCAPED`")]
	[DataRow("\"; touch ESCAPED; \"")]
	[DataRow("\\'; touch ESCAPED")]
	public void HoldsAPayloadInsideItsQuotingHoweverTheBodyIsShaped(string hostileFragment)
	{
		// The fragment is data inside a function body: reaching the surrounding shell would mean
		// the payload escaped the single quotes the command wraps it in.
		string body = $"function ndf_hostile() {{ echo {hostileFragment}; }}";
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult("ndf_hostile", body);

		using var workingDirectory = new TemporaryDirectory("hostile");
		BashHost.Execute($"cd '{workingDirectory.Path}'", command);

		Assert.IsFalse(File.Exists(Path.Combine(workingDirectory.Path, "ESCAPED")),
			"the payload ran in the surrounding shell instead of staying inside the encoded body");
		Assert.IsFalse(command.Contains('\n'), "the generated command spans more than one line");
	}

	[TestMethod]
	public void RefusesABodyThatDoesNotParseBeforeAnyOfItRuns()
	{
		using var workingDirectory = new TemporaryDirectory("syntax");
		// The side effect precedes the syntax error, so it runs only if the gate lets the body execute.
		string body = "touch RAN\nfunction ndf_broken() {\n  if [ -d /tmp ]; then\n}";
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult("ndf_broken", body);

		var run = BashHost.Execute($"cd '{workingDirectory.Path}'", command);

		CollectionAssert.Contains(
			run.OutputLines.ToArray(),
			$"{FunctionParser.SOURCING_END_MARKER} ndf_broken {FunctionParser.ERROR_IN_FUNCTION}");
		Assert.IsFalse(File.Exists(Path.Combine(workingDirectory.Path, "RAN")),
			"the body ran despite failing the syntax gate");
	}

	[TestMethod]
	public void ReportsFailureWhenTheBodyDefinesSomethingElse()
	{
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(
			"ndf_expected", "function ndf_other() { echo hi; }");

		var run = BashHost.Execute(command);

		CollectionAssert.Contains(
			run.OutputLines.ToArray(),
			$"{FunctionParser.SOURCING_END_MARKER} ndf_expected {FunctionParser.SOURCING_FAILED}");
	}

	[TestMethod]
	public void LeavesNoVariableBehindInTheSourcingShell()
	{
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult(
			"ndf_valid", "function ndf_valid() { echo hi; }");

		var run = BashHost.Execute(command, "compgen -v | grep -c '^__ndf_' || true");

		Assert.AreEqual("0", run.OutputLines[^1]);
	}

	[TestMethod]
	public void CarriesABodyLargerThanASingleReadBuffer()
	{
		var body = new StringBuilder("function ndf_big() {\n");
		for (int i = 0; i < 4000; i++) body.Append("  local value_").Append(i).Append('=').Append(i).Append('\n');
		body.Append('}');
		string command = GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult("ndf_big", body.ToString());

		var run = BashHost.Execute(command, "declare -F ndf_big > /dev/null && echo DEFINED");

		Assert.IsFalse(command.Contains('\n'), "the generated command spans more than one line");
		CollectionAssert.Contains(run.OutputLines.ToArray(), "DEFINED");
	}
}

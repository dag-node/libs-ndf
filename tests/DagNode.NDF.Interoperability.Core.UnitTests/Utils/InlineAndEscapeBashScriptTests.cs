namespace DagNode.NDF.Interoperability.Tests.Unit.Utils;

/// <summary>
/// The deprecated inlining path fails closed: it names the construct it cannot preserve instead
/// of emitting a line that behaves differently from the script it came from.
/// </summary>
#pragma warning disable CS0618 // The subject is obsolete by design; these tests hold it to its contract
[TestClass]
public class InlineAndEscapeBashScriptTests
{
	[TestMethod]
	[DynamicData(nameof(BashCorpus.TransportOnlyNames), typeof(BashCorpus), DynamicDataDisplayName = nameof(BashCorpus.DisplayName), DynamicDataDisplayNameDeclaringType = typeof(BashCorpus))]
	public void RefusesABodyOnlyTheBase64TransportCarries(string subjectName)
	{
		var subject = BashCorpus.ByName(subjectName);

		var refusal = Assert.ThrowsExactly<ArgumentException>(
			() => LinuxUtils.InlineAndEscapeBashScript(subject.Body));

		Assert.AreEqual("script", refusal.ParamName);
	}

	[TestMethod]
	[DataRow("cat <<'EOT'\nbody\nEOT", "here-doc")]
	[DataRow("case \"$1\" in\n  a) echo A ;;\nesac", "case statement")]
	[DataRow("printf 'a\\tb\\n'", "backslash")]
	[DataRow("echo \"one\ntwo\"", "spanning lines")]
	public void NamesTheConstructItRefuses(string script, string expectedInMessage)
	{
		var refusal = Assert.ThrowsExactly<ArgumentException>(() => LinuxUtils.InlineAndEscapeBashScript(script));

		StringAssert.Contains(refusal.Message, expectedInMessage, StringComparison.OrdinalIgnoreCase);
	}

	[TestMethod]
	[DataRow("ls /tmp |\nwc -l")]
	[DataRow("[ -d /tmp ] &&\necho yes")]
	[DataRow("if [ -d /tmp ]; then\n  echo yes\nfi")]
	[DataRow("for i in 1 2 3\ndo\n  echo $i\ndone")]
	public void RefusesALineThatCannotTakeATrailingSemicolon(string script) =>
		Assert.ThrowsExactly<ArgumentException>(() => LinuxUtils.InlineAndEscapeBashScript(script));

	[TestMethod]
	public void KeepsAHashThatBashReadsAsDataAndDropsTheCommentAfterIt()
	{
		string inlined = Unescape(LinuxUtils.InlineAndEscapeBashScript(
			"local trimmed=${1#/tmp/} # drop the prefix\necho \"id#1\"\necho \"$#\""));

		StringAssert.Contains(inlined, "${1#/tmp/}");
		StringAssert.Contains(inlined, "\"id#1\"");
		StringAssert.Contains(inlined, "\"$#\"");
		Assert.IsFalse(inlined.Contains("drop the prefix"), "the trailing comment survived inlining");
	}

	[TestMethod]
	public void SeparatesEveryStatementIncludingOneEndingInAnExpansion()
	{
		string inlined = LinuxUtils.InlineAndEscapeBashScript("local trimmed=${1#/tmp/}\necho done");

		// Without the separator the two statements would run together as one command.
		StringAssert.Contains(inlined, "};");
	}

	[TestMethod]
	public void EscapesTheCharactersItsReaderWouldOtherwiseConsume()
	{
		string inlined = LinuxUtils.InlineAndEscapeBashScript("echo \"$HOME\"");

		StringAssert.Contains(inlined, @"\""");
		StringAssert.Contains(inlined, @"\$");
	}

	private static string Unescape(string inlined) =>
		inlined.Replace(@"\""", "\"").Replace(@"\$", "$");
}
#pragma warning restore CS0618

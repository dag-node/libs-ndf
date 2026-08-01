namespace DagNode.NDF.Interoperability.Tests;

/// <summary>
/// Function bodies exercising the constructs a transport has to carry. Shared by the unit tests
/// that assert refusals on generated text and the boundary tests that execute the same bodies in
/// a real bash, so both ends measure one corpus.
/// </summary>
public static class BashCorpus
{
	public sealed record Subject(string Name, string FunctionName, string Body);

	/// <summary>Bodies the base64 transport carries and inlining cannot express.</summary>
	public static readonly IReadOnlyList<Subject> TransportOnly = [
		new("here-doc", "ndf_heredoc",
			"""
			function ndf_heredoc() {
			  cat <<'EOT'
			one	tabbed
			  two indented
			EOT
			}
			"""),
		new("case-statement", "ndf_case",
			"""
			function ndf_case() {
			  case "$1" in
			    a) echo A ;;
			    b) echo B ;;
			    *) echo other ;;
			  esac
			}
			"""),
		new("escape-sequences", "ndf_escapes",
			"""
			function ndf_escapes() {
			  printf 'a\tb\n'
			  echo 'x\0057y'
			  grep -E '\\d+' /dev/null || true
			  echo "C:\Users\test"
			}
			"""),
		new("string-spanning-lines", "ndf_multiline",
			"""
			function ndf_multiline() {
			  echo "one
			two"
			}
			"""),
		new("trailing-pipe", "ndf_pipe",
			"""
			function ndf_pipe() {
			  echo one two three |
			  wc -w
			}
			"""),
	];

	/// <summary>Bodies both transports carry, where `#` is data rather than a comment.</summary>
	public static readonly IReadOnlyList<Subject> Portable = [
		new("suffix-expansion", "ndf_suffix",
			"""
			function ndf_suffix() {
			  local trimmed=${1#/tmp/}
			  echo "$trimmed"
			}
			"""),
		new("argument-count", "ndf_argcount",
			"""
			function ndf_argcount() {
			  echo "$# args"
			}
			"""),
		new("hash-inside-string", "ndf_hash",
			"""
			function ndf_hash() {
			  echo "id#1" # a real comment
			}
			"""),
	];

	public static IEnumerable<Subject> All => [.. TransportOnly, .. Portable];

	/// <summary>Subject names as MSTest data rows, one name per row.</summary>
	public static IEnumerable<object[]> TransportOnlyNames =>
		TransportOnly.Select(subject => new object[] { subject.Name });

	public static IEnumerable<object[]> AllNames =>
		All.Select(subject => new object[] { subject.Name });

	/// <summary>Renders a data row as the subject name, so a failure names the case.</summary>
	public static string DisplayName(System.Reflection.MethodInfo method, object[] data) =>
		$"{method.Name}({data[0]})";

	public static Subject ByName(string name) =>
		All.Single(subject => subject.Name == name);
}

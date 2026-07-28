using System.Text.RegularExpressions;
using DagNode.NDF.Interoperability;

namespace DagNode.NDF.Interoperability;

public class Validation
{
	// Static readonly Regex instance, created once and used across all threads
	private static readonly Regex BashFunctionNameRegex = new (@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);
	private static bool IsValidBashFunctionName(string name) => BashFunctionNameRegex.IsMatch(name);

	public static void CheckScriptFile(string scriptFilePath)
	{
		if (string.IsNullOrEmpty(scriptFilePath)) {
			throw new ArgumentException("Script file path cannot be null or empty", nameof(scriptFilePath));
		}
		if (!LinuxUtils.IsBashScript(scriptFilePath)) {
			throw new FileNotFoundException($"The file does not look like a bash script", scriptFilePath);
		}
	}

	public static void CheckFunctionName(string functionName)
	{
		if (string.IsNullOrWhiteSpace(functionName)) {
			throw new ArgumentException($"Function name cannot be null or empty", nameof(functionName));
		}
		if (!IsValidBashFunctionName(functionName)) {
			throw new ArgumentException($"Function name contains invalid characters: '{functionName}'", nameof(functionName));
		}
	}
}

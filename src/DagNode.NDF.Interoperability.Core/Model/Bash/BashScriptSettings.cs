namespace DagNode.NDF.Interoperability.Model.Bash;

// To achieve a default implementation with static abstract members that can also be overridden,
// you can use an interface with static abstract methods and provide an implementation in the concrete class.
// Here's how you can structure it in C# 11 or later:
// public interface IConfigurable<out T> where T : IConfigurable<T>
// {
// 	// Static abstract method for creating a default instance.
// 	static abstract T Default(string scriptFilePath);
// 	// Optional: Define shared behavior here if needed.
// }

/// <summary>
/// Identifies the bash script a <see cref="DagNode.NDF.Interoperability.Bash.BashScript"/> instance sources and controls how its
/// function calls are tagged. Construct it from a filename relative to the program directory or from
/// an absolute path; the derived hash and normalised name are computed once here and reused to build
/// per-call working directories and {prefix} marker tags.
/// </summary>
public class BashScriptSettings // : IConfigurable<BashScriptSettings>
{
	/// <summary>
	/// Static factory method to setup instance with default configuration.
	/// Constructs full absolute path to the scriptFileName relative to program directory.
	/// </summary>
	/// <param name="scriptFileName">Filename of the script.sh used to evaluate bash functions.</param>
	/// <returns>Settings pointing at the resolved absolute path, with all other values left at their defaults.</returns>
	public static BashScriptSettings CreateFactoryDefault(string scriptFileName)
		=> new(scriptFileName) {
			// Configure defaults
		};

	/// <summary>
	/// Static factory method to setup instance with default configuration.
	/// </summary>
	/// <param name="scriptFilePath">Full absolute path to the script.sh used to evaluate bash functions.</param>
	/// <returns>Settings for that script, with all other values left at their defaults.</returns>
	public static BashScriptSettings CreateFactoryDefault(AbsolutePath scriptFilePath)
		=> new(scriptFilePath) {
			// Configure defaults
		};
	
	/// <summary>
	/// Enable console messages about parameters passed to function calls.
	/// Not related to BashProcessArgs --debug or --verbose
	/// </summary>
	public bool IsDebug { get; set; } = false;

	/// <summary>Absolute path to the script whose functions this instance calls.</summary>
	public AbsolutePath ScriptFilePath { get; }

	/// <summary>
	/// Optional paths to bash script files with global functions.
	/// Global functions become available inside ScriptFilePath script.
	/// </summary>
	public IList<AbsolutePath> GlobalFunctionScriptFilePaths { get; set; } = new List<AbsolutePath>();
	/// <summary>
	/// SHA-1 of <see cref="ScriptFilePath"/>. Its first five characters name the per-script
	/// subdirectory under the working directory, keeping same-named scripts in different
	/// locations apart.
	/// </summary>
	public string ScriptFilePathHash { get; set; }

	/// <summary>File name of the script, including extension.</summary>
	public string ScriptFileName { get; }

	/// <summary>
	/// <see cref="ScriptFileName"/> reduced to ASCII with whitespace stripped, so it is safe to
	/// embed in directory names and {prefix} marker tags.
	/// </summary>
	public string ScriptFileNameNormalized { get; }

	/// <summary>
	/// Length of the random instance marker tag [a-zA-Z0-9]{InstanceMarkerTagLength} (default four chars)
	/// Created automatically for every new BashScript instance.
	/// </summary>
	public int InstanceMarkerTagLength { get; set; } = 4;
	/// <summary>
	/// Whether to generate and append instance marker tag to script function log filenames (default true).
	/// Useful to distinguish related function log files produced by a single BashScript instance.
	/// </summary>
	public bool UseInstanceMarkerTag { get; set; } = true;

	/// <summary>
	/// Basic configuration of the bash script runner.
	/// </summary>
	/// <param name="scriptFileName">Filename of the bash script relative to program directory.</param>
	public BashScriptSettings(string scriptFileName):
		this(AbsolutePath.Create(scriptFileName.GetFullPathFromCurrentDirectory())) { }
	/// <summary>
	/// Basic configuration of the bash script runner.
	/// </summary>
	/// <param name="scriptFilePath">Absolute path to the bash script.</param>
	/// <exception cref="ArgumentException">When <paramref name="scriptFilePath"/> is null or blank.</exception>
	public BashScriptSettings(AbsolutePath scriptFilePath)
	{
		if (string.IsNullOrWhiteSpace(scriptFilePath)) {
			throw new ArgumentException("Script file name or path to the script is required", nameof(scriptFilePath));
		}
		ScriptFilePath = scriptFilePath;
		ScriptFilePathHash = scriptFilePath.ToString()!.ToSha1Hash();
		ScriptFileName = Path.GetFileName(scriptFilePath);
		ScriptFileNameNormalized = ScriptFileName.ToAsciiNoWhitespace();
	}
}

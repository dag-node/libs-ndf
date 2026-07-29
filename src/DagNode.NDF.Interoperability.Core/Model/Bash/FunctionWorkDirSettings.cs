using System.Reflection;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Model;

/// <summary>Builds the directory path that holds one script's per-call {prefix} files.</summary>
/// <param name="args">Normalised script name, instance marker tag and the settings in effect.</param>
/// <returns>Absolute directory path; it is created if it does not exist.</returns>
public delegate string ConfigureFunctionWorkDirDelegate(ConfigureFunctionWorkDirEventArgs args);

/// <summary>Builds the {prefix} marker tag naming one function call's .in/.out/.err/.log files.</summary>
/// <param name="args">Normalised function name, instance marker tag and call sequence number.</param>
/// <returns>Marker tag without a file extension; must not contain spaces or path separators.</returns>
public delegate string ConfigureFunctionMarkerTagDelegate(ConfigureFunctionMarkerEventArgs args);

/// <summary>
/// Decides where a script's function calls put their files: which base directory holds the
/// per-call {prefix}.in, .out, .err and .log files, where PID files go, and how the working
/// directory and {prefix} tag are named. Defaults place everything under /tmp/tmpfsbs.
/// </summary>
public class FunctionWorkDirSettings
{
	/// <summary>Settings with every value at its default: a daily working directory under /tmp/tmpfsbs.</summary>
	public static FunctionWorkDirSettings CreateFactoryDefault => new() {
		// Configure defaults
	};

	/// <summary>
	/// Which well-known base directory <see cref="FunctionBaseWorkDir"/> resolves to.
	/// Set it to <see cref="DirectoryType.Custom"/> together with <see cref="UseCustomFunctionWorkDir"/>
	/// to supply a path of your own.
	/// </summary>
	public DirectoryType FunctionWorkDirType { get; set; } = DirectoryType.TmpfsBs;

	/// <summary>Directory kind holding the per-call PID files. Always <see cref="DirectoryType.TmpfsBs"/>.</summary>
	public static DirectoryType FunctionPidDirType { get => DirectoryType.TmpfsBs; }

	/// <summary>Default location of the per-call PID files.</summary>
	public static readonly string s_functionBasePidDirDefault = "/tmp/tmpfsbs/function-pids";

	/// <summary>
	/// Directory receiving one {prefix}.pid file per function call, used to track and stop
	/// running functions.
	/// </summary>
	public static string FunctionBasePidDir { get => s_functionBasePidDirDefault; }

	/// <summary>
	/// Provide a path to base directory where bash functions will store it's standard output and standard error.
	/// Ex.: @logs (default), other options can be /tmp or script location specific directory or /var/log
	/// Default: LogBaseDirectoryType.TmpfsBS (/tmp/tmpfsbs)
	/// </summary>
	public AbsolutePath FunctionBaseWorkDir {
		get {
			return FunctionWorkDirType switch {
				DirectoryType.Default or DirectoryType.TmpfsBs => s_functionWorkDirDefault,
				DirectoryType.AppDirectory => AbsolutePath.Create(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? string.Empty, "@bashscript")),
				DirectoryType.SystemLogs => AbsolutePath.Create("/var/log"),
				DirectoryType.SystemTemp => AbsolutePath.Create("/tmp"),
				DirectoryType.SystemTempPersistent => AbsolutePath.Create("/var/tmp"),
				DirectoryType.Custom => UseCustomFunctionWorkDir
					? _customFunctionWorkDir ?? throw new InteroperabilityException($"{nameof(FunctionBaseWorkDir)} must be set when using {nameof(FunctionWorkDirType)}.Custom and {nameof(UseCustomFunctionWorkDir)} is enabled")
					: s_functionWorkDirDefault,
				_ => AbsolutePath.Create(s_functionWorkDirDefault)
				//TODO: Add UserHomeDirectory
			};
		}
		set { _customFunctionWorkDir = value; }
	}
	/// <summary>
	/// Whether a path assigned to <see cref="FunctionBaseWorkDir"/> is honoured. Left false, the
	/// setter is recorded but <see cref="DirectoryType.Custom"/> still falls back to the default,
	/// so both this flag and the Custom type must be set to take effect.
	/// </summary>
	public bool UseCustomFunctionWorkDir {get; set; } = false;
	private AbsolutePath? _customFunctionWorkDir;

	/// <summary>Base directory used unless <see cref="FunctionWorkDirType"/> selects another.</summary>
	public static readonly AbsolutePath s_functionWorkDirDefault = AbsolutePath.Create("/tmp/tmpfsbs");
	
	/// <summary>
	/// Create new @functions-yyyyMMdd subdirectory in s_functionWorkDirDefault on a daily basis.
	/// </summary>
	public static readonly ConfigureFunctionWorkDirDelegate s_configureFunctionWorkDirDefault = args => {
			string scriptSubdirectory = $"{args.ScriptFileNameNormalized}-{args.BashScriptSettings.ScriptFilePathHash.Substring(0,5)}";
			string functionWorkDirPath = Path.Combine(s_functionWorkDirDefault, $"@functions-{DateTime.Now:yyyyMMdd}", scriptSubdirectory);
			return functionWorkDirPath;
		};
	/// <summary>
	/// Names the working directory for one script's function calls. Assign a delegate to place
	/// the files elsewhere; <see cref="s_configureFunctionWorkDirDefault"/> is the default layout.
	/// </summary>
	public ConfigureFunctionWorkDirDelegate ConfigureFunctionWorkDir {
		get { return s_configureFunctionWorkDirDefault; }
		set { _configureFunctionWorkDirDelegate = value; }
	}
	private ConfigureFunctionWorkDirDelegate? _configureFunctionWorkDirDelegate;

	// TODO: Update XML doc description
	/// <summary>
	/// Function used to create {prefix}.{out|err|log} function specific files.
	/// This {prefix} is generated dynamically for every function call.
	/// Default value: $"{DateTime.Now:HHmmss-fff}-{args.NormalizedFunctionName}{scriptMarkerTag}-{functionCallSequenceNumber}"
	/// Two files {prefix}.out and {prefix}.err with standard function outputs are generated automatically.
	/// The generated {prefix} value is passed to every bash function as first arg available with "$1".
	/// Contents written to {prefix}.log are read automatically when the function is finished, write any custom results there.
	/// A PID file is created automatically for every function call in /tmp/bashscript.pids/{prefix}.pid
	/// </summary>
	public static readonly ConfigureFunctionMarkerTagDelegate s_configureFunctionMarkerTagDefault = args => {
			string contextMarkerAsciiNoWhitespace = args.ScriptInstanceMarkerTag.ToAsciiNoWhitespace();
			string scriptInstanceMarkerTag = !string.IsNullOrWhiteSpace(contextMarkerAsciiNoWhitespace)
				? $"-{contextMarkerAsciiNoWhitespace}" : string.Empty;
			return $"{DateTime.Now:HHmmss-fff}-{args.FunctionNameNormalized}{scriptInstanceMarkerTag}-{args.FunctionCallSequenceNumber}"; // .log, .out, .err
		};
	/// <summary>
	/// Names the {prefix} tag for one function call. Assign a delegate to change the naming
	/// scheme; <see cref="s_configureFunctionMarkerTagDefault"/> is used when none is set.
	/// </summary>
	public ConfigureFunctionMarkerTagDelegate ConfigureFunctionMarkerTag {
		get => _configureFunctionMarkerTagDelegate ?? s_configureFunctionMarkerTagDefault;
		set => _configureFunctionMarkerTagDelegate = value;
	}
	private ConfigureFunctionMarkerTagDelegate? _configureFunctionMarkerTagDelegate;
}

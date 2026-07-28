using System.Reflection;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Model;

public delegate string ConfigureFunctionWorkDirDelegate(ConfigureFunctionWorkDirEventArgs args);
public delegate string ConfigureFunctionMarkerTagDelegate(ConfigureFunctionMarkerEventArgs args);

public class FunctionWorkDirSettings
{
	// Static factory method to provide instance with default configuration
	public static FunctionWorkDirSettings CreateFactoryDefault => new() {
		// Configure defaults
	};
	
	public DirectoryType FunctionWorkDirType { get; set; } = DirectoryType.TmpfsBs;
	public static DirectoryType FunctionPidDirType { get => DirectoryType.TmpfsBs; }
	public static readonly string s_functionBasePidDirDefault = "/tmp/tmpfsbs/function-pids";
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
	public bool UseCustomFunctionWorkDir {get; set; } = false;
	private AbsolutePath? _customFunctionWorkDir;
	public static readonly AbsolutePath s_functionWorkDirDefault = AbsolutePath.Create("/tmp/tmpfsbs");
	
	/// <summary>
	/// Create new @functions-yyyyMMdd subdirectory in s_functionWorkDirDefault on a daily basis.
	/// </summary>
	public static readonly ConfigureFunctionWorkDirDelegate s_configureFunctionWorkDirDefault = args => {
			string scriptSubdirectory = $"{args.ScriptFileNameNormalized}-{args.BashScriptSettings.ScriptFilePathHash.Substring(0,5)}";
			string functionWorkDirPath = Path.Combine(s_functionWorkDirDefault, $"@functions-{DateTime.Now:yyyyMMdd}", scriptSubdirectory);
			return functionWorkDirPath;
		};
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
	public ConfigureFunctionMarkerTagDelegate ConfigureFunctionMarkerTag {
		get => _configureFunctionMarkerTagDelegate ?? s_configureFunctionMarkerTagDefault;
		set => _configureFunctionMarkerTagDelegate = value;
	}
	private ConfigureFunctionMarkerTagDelegate? _configureFunctionMarkerTagDelegate;
}

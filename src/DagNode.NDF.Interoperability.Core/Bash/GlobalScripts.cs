using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DagNode.NDF.Interoperability.Model;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Bash;

public static class GlobalScripts
{
	// Declared first: the SOURCE_FUNCTION_* initializers below run in declaration order and read it
	private static readonly Regex s_bashIdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

	// Prevent resource leaks from subprocesses when terminating main bash process
	//private static readonly string INLINE_FUNCTION___global__on_stop=$$"""function {{FUNCTION_NAME___global__on_stop}}() { trap - SIGINT SIGTERM SIGHUP EXIT; echo \"___END_PROCESS__ \$\$ by \$1\"; kill -s SIGINT -- -\$(ps -o pgid= -p \$\$) 2>/dev/null && sleep 2; pgrep -g \$\$ >/dev/null 2>&1 && kill -s SIGKILL -- -\$(ps -o pgid= -p \$\$) 2>/dev/null; }""";
	private const string FUNCTION_NAME___global__on_stop = "___global__on_stop";
	// $$ is the group id directly because the resident bash is launched under setsid (its own session
	// leader), so this needs neither `ps -o pgid=` nor `pgrep` — binaries a sandbox may forbid. It
	// signals only bash's own group, never the .NET launcher's group. The SIGINT->SIGKILL grace on this
	// session-teardown path is a fixed 2s; the per-call termination grace
	// (BashScriptSettings.FunctionTerminationGracePeriod) is applied C#-side and is the one callers tune.
	private static readonly string FUNCTION___global__on_stop
		= $$"""
		  function {{FUNCTION_NAME___global__on_stop}}() {
		    trap - SIGINT SIGTERM SIGHUP EXIT
		    kill -s SIGINT -- -$$ 2>/dev/null && sleep 2
		    kill -s SIGKILL -- -$$ 2>/dev/null
		  }
		  """;
	public static readonly string SOURCE_FUNCTION___global__on_stop =
		ValidateAndSourceInlineFunctionWithSourcingResult(
			FUNCTION_NAME___global__on_stop,
			FUNCTION___global__on_stop);
	// Setting up signal traps after sourcing __global__on_stop
	public static readonly string RUN_SETUP___global__on_stop = $"""for sig in EXIT SIGTERM SIGINT SIGHUP; do trap '{FUNCTION_NAME___global__on_stop} "$sig"' "$sig"; done""";
	
	// private static string INLINE_FUNCTION___run_function__with__stdout_end_marker__async = $$"""{{{FUNCTION_NAME___run_function__with__stdout_end_marker__async}}() { function_marker=\$1; shift; { local duration_ns timespan start_time end_time exit_code; start_time=\$(date -u +%s%N); \"\$@\"; exit_code=\$?; end_time=\$(date -u +%s%N); duration_ns=\$((end_time - start_time)); timespan=\$(printf \"%02d:%02d:%02d.%06d\" \$((duration_ns / 3600000000000)) \$(((duration_ns / 60000000000) % 60)) \$(((duration_ns / 1000000000) % 60)) \$(((duration_ns / 1000) % 1000000))); echo \"{{FunctionParser.FUNCTION_END_MARKER}} \${start_time} \${end_time} \${timespan} \${function_marker} \${exit_code}\"; } &; }""";
	public const string FUNCTION_NAME___run_function__with__stdout_end_marker__async = "___run_function__with__stdout_end_marker__async";
	// The backgrounded subshell records its own PID ($BASHPID) to {pidDir}/{function_marker}.pid on the
	// tmpfs, and removes it when the call finishes — so an absent file means "already done" and C# skips
	// the kill. After backgrounding, the wrapper echoes ___BEGIN_FN__ {function_marker} {pid} on stdout so
	// C# records the PID in-memory (its concurrent registry) without waiting on the file. Either source
	// lets C# terminate just this call's process tree on timeout/cancellation.
	private static string FUNCTION___run_function__with__stdout_end_marker__async
		= $$"""
		function {{FUNCTION_NAME___run_function__with__stdout_end_marker__async}}() {
		    local function_marker=$1 # Extract function marker tag
		    local stream_redirection=$2 # FunctionQuery.GetStreamRedirectionWithReplacedPrefixAsQuotedArg
		    local function_name=$3 # Name of bash function which will be called, the script file must be sourced
		    shift 3 # Remove marker tag from arguments, also remove the second "StreamRedirection" parameter and function name
		    local pid_file="{{FunctionWorkDirSettings.FunctionBasePidDir}}/${function_marker}.pid"
		    {
		        echo "$BASHPID" > "${pid_file}" # Record this call's own PID so C# can kill just this call's tree
		        local duration_ns timespan start_time end_time exit_code
		        start_time=$(date -u +%s%N)
		        local function_args=""
				for arg in "$@"; do function_args+="\"$arg\" "; done
				local bash_cmd="${function_name} ${function_args} ${stream_redirection}"
		        eval "${bash_cmd}"
		        exit_code=$?
		        end_time=$(date -u +%s%N)
		        duration_ns=$((end_time - start_time))
		        timespan=$(printf "%02d:%02d:%02d.%06d" \
		            $((duration_ns / 3600000000000)) \
		            $(((duration_ns / 60000000000) % 60)) \
		            $(((duration_ns / 1000000000) % 60)) \
		            $(((duration_ns / 1000) % 1000000)))
		        echo "___END_FN__ ${start_time} ${end_time} ${timespan} ${function_marker} ${exit_code}"
		        rm -f "${pid_file}" # Call finished: drop the pid file so a later kill is a no-op
		    } &
		    echo "{{FunctionParser.FUNCTION_BEGIN_MARKER}} ${function_marker} $!" # Report the backgrounded PID to C#
		}
		""";
	public static readonly string SOURCE_FUNCTION___run_function__with__stdout_end_marker__async =
		ValidateAndSourceInlineFunctionWithSourcingResult(
			FUNCTION_NAME___run_function__with__stdout_end_marker__async,
			FUNCTION___run_function__with__stdout_end_marker__async);
	
	/// <summary>
	/// Builds the single line of bash that defines <paramref name="functionName"/> in the running process
	/// and reports the outcome as a <see cref="FunctionParser.SOURCING_END_MARKER"/> line on stdout.
	/// Carries <paramref name="functionCode"/> base64-encoded, so the body reaches bash byte-exact.
	/// </summary>
	/// <param name="functionName">Bash identifier the sourced code must define; allowlisted to <c>[A-Za-z_][A-Za-z0-9_]*</c>.</param>
	/// <param name="functionCode">Function definition, verbatim. Any bash construct is permitted, including here-docs, <c>case</c> and backslash escapes.</param>
	/// <returns>One line of bash, safe to write to the bash process stdin.</returns>
	/// <exception cref="ArgumentException">When <paramref name="functionName"/> is not a bash identifier.</exception>
	public static string ValidateAndSourceInlineFunctionWithSourcingResult(string functionName, string functionCode)
	{
		// Transport is base64 rather than LinuxUtils.InlineAndEscapeBashScript. Inlining costs two lossy
		// passes over the body: flattening to one line cannot express here-docs, `case ... ;;` or any line
		// ending in an operator, and `echo -e` rewrites \t, \0nnn, \x and \\ in the data on the way in.
		// The base64 alphabet holds no shell metacharacters, so a single-quoted payload needs no escaping
		// and arrives byte-exact on the one line the stdin protocol allows.
		if (!s_bashIdentifierRegex.IsMatch(functionName))
			throw new ArgumentException($"Not a bash identifier: '{functionName}'", nameof(functionName));
		string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(functionCode.Replace("\r\n", "\n")));
		string srcVar = $"__ndf_src_{functionName}";
		// Decoding once and gating that variable makes `bash -n` validate exactly the bytes `source` runs.
		// `declare -F` holds sourcing to its postcondition: `source` also succeeds on an empty payload.
		return $$"""
			  {{srcVar}}="$(base64 -d <<< '{{payload}}')" && bash -n <<< "${{srcVar}}" 2>/dev/null || { echo "{{FunctionParser.SOURCING_END_MARKER}} {{functionName}} {{FunctionParser.ERROR_IN_FUNCTION}}"; exit 1; }; source <(printf '%s\n' "${{srcVar}}") && declare -F {{functionName}} >/dev/null && echo "{{FunctionParser.SOURCING_END_MARKER}} {{functionName}} {{FunctionParser.SOURCED_SUCCESSFULLY}}" || echo "{{FunctionParser.SOURCING_END_MARKER}} {{functionName}} {{FunctionParser.SOURCING_FAILED}}"; unset {{srcVar}}
			  """; // not suppressing source ... 2>/dev/null
	}

	#region Deprecated inlining

	/// <summary>
	/// Renders the shipped <c>Scripts/*.sh</c> function files as escaped one-liners, keyed by function name.
	/// </summary>
	[Obsolete("Sourcing carries function bodies as base64; see ValidateAndSourceInlineFunctionWithSourcingResult. Retained as a codegen aid for hand-written one-liners.")]
	public static Dictionary<string, string> InlineAll()
	{
		var r = new Dictionary<string, string>();
		var fn001Path = AbsolutePath.Create(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? string.Empty,
			"Scripts", $"{FUNCTION_NAME___run_function__with__stdout_end_marker__async}.sh"));
		var fn001nlined = ConvertBashScriptToInline(fn001Path, FUNCTION_NAME___run_function__with__stdout_end_marker__async);
		r.Add(FUNCTION_NAME___run_function__with__stdout_end_marker__async, fn001nlined);
		return r;
	}
	
	/// <summary>
	/// Reads the bash script at <paramref name="scriptFilePath"/> as an escaped one-liner with
	/// <paramref name="functionName"/> replaced by its raw-string interpolation hole, ready to paste into a
	/// <c>$$"""..."""</c> literal. Throws when the script uses constructs inlining cannot preserve.
	/// </summary>
	/// <exception cref="ArgumentException">When the script cannot be inlined without corrupting it.</exception>
	[Obsolete("Sourcing carries function bodies as base64; see ValidateAndSourceInlineFunctionWithSourcingResult. Retained as a codegen aid for hand-written one-liners.")]
	public static string ConvertBashScriptToInline(AbsolutePath scriptFilePath, string functionName)
	{
		using var sr = new StreamReader(scriptFilePath);
		string functionContents = sr.ReadToEnd();
		string functionInlined = LinuxUtils.InlineAndEscapeBashScript(functionContents);
		return functionInlined.Replace(functionName, $"{{{{{{{{{{{{{functionName}}}}}}}}}}}}}");
	}

	#endregion Deprecated inlining
}

using System.Reflection;
using DagNode.NDF.Interoperability.Model;

namespace DagNode.NDF.Interoperability.Bash;

public static class GlobalScripts
{
	// Prevent resource leaks from subprocesses when terminating main bash process
	//private static readonly string INLINE_FUNCTION___global__on_stop=$$"""function {{FUNCTION_NAME___global__on_stop}}() { trap - SIGINT SIGTERM SIGHUP EXIT; echo \"___END_PROCESS__ \$\$ by \$1\"; kill -s SIGINT -- -\$(ps -o pgid= -p \$\$) 2>/dev/null && sleep 2; pgrep -g \$\$ >/dev/null 2>&1 && kill -s SIGKILL -- -\$(ps -o pgid= -p \$\$) 2>/dev/null; }""";
	private const string FUNCTION_NAME___global__on_stop = "___global__on_stop";
	private static readonly string FUNCTION___global__on_stop
		= $$"""
		  function {{FUNCTION_NAME___global__on_stop}}() {
		    trap - SIGINT SIGTERM SIGHUP EXIT
		    kill -s SIGINT -- -"$(ps -o pgid= -p $$)" 2>/dev/null && sleep 2
		    pgrep -g $$ >/dev/null 2>&1 && kill -s SIGKILL -- -"$(ps -o pgid= -p $$)" 2>/dev/null
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
	private static string FUNCTION___run_function__with__stdout_end_marker__async
		= $$"""
		function {{FUNCTION_NAME___run_function__with__stdout_end_marker__async}}() {
		    local function_marker=$1 # Extract function marker tag
		    local stream_redirection=$2 # FunctionQuery.GetStreamRedirectionWithReplacedPrefixAsQuotedArg
		    local function_name=$3 # Name of bash function which will be called, the script file must be sourced
		    shift 3 # Remove marker tag from arguments, also remove the second "StreamRedirection" parameter and function name
		    {
		        local duration_ns timespan start_time end_time exit_code
		        start_time=$(date -u +%s%N)
		        local function_args=""
				for arg in "$@"; do function_args+="\"$arg\" "; done
				local bash_cmd="${function_name} ${function_args} ${stream_redirection}"
				# echo "FunctionWrapper: ${bash_cmd}" # >/dev/tty .NET process.StandardOutput is probably not tty
		        # TODO: how to write to redirected file streams and simultaneously echo __END_FN__ to standard output without using eval?
		        # eval "$@ ${stream_redirection}"
		        eval "${bash_cmd}"
		        exit_code=$?
		        end_time=$(date -u +%s%N)
		        duration_ns=$((end_time - start_time))
		        timespan=$(printf "%02d:%02d:%02d.%06d" \
		            $((duration_ns / 3600000000000)) \
		            $(((duration_ns / 60000000000) % 60)) \
		            $(((duration_ns / 1000000000) % 60)) \
		            $(((duration_ns / 1000) % 1000000)))
				# exec 3>&1 # save the original stdout (connected to .NET process.StandardOutput) to file descriptor 3
				# Print function finished marker to the original standard output
		        echo "___END_FN__ ${start_time} ${end_time} ${timespan} ${function_marker} ${exit_code}"
		        # echo "___END_FN__ ..." >&3 #TODO: use kind of >&3 instead of eval "$@ ${stream_redirection}" the named pipe number may be different
				# exec 3>&- # close file descriptor 3 (cleanup)
		    } &
		}
		""";
	public static readonly string SOURCE_FUNCTION___run_function__with__stdout_end_marker__async =
		ValidateAndSourceInlineFunctionWithSourcingResult(
			FUNCTION_NAME___run_function__with__stdout_end_marker__async,
			FUNCTION___run_function__with__stdout_end_marker__async);
	
	//public static string ValidateAndSourceInlineFunctionWithSourcingResult(string functionName, string inlineFunction) => $$"""{{functionName}}=$(echo -e "{{inlineFunction}}") echo -e "${{{functionName}}}" > /dev/null || { echo "{{FunctionParser.SOURCING_END_MARKER}} {{functionName}} {{FunctionParser.ERROR_IN_FUNCTION}}"; exit 1; }; source <(echo -e "${{{functionName}}}") && echo "{{FunctionParser.SOURCING_END_MARKER}} {{functionName}} {{FunctionParser.SOURCED_SUCCESSFULLY}}" || echo "{{FunctionParser.SOURCING_END_MARKER}} {{functionName}} {{FunctionParser.SOURCING_FAILED}}" 2> /dev/null""";
	public static string ValidateAndSourceInlineFunctionWithSourcingResult(string functionName, string functionCode)
	{
		string inlineFunction = LinuxUtils.InlineAndEscapeBashScript(functionCode);
		return $$"""
			  {{functionName}}=$(echo -e "{{inlineFunction}}"); echo -e "${{{functionName}}}" > /dev/null || { echo "{{FunctionParser.SOURCING_END_MARKER}} {{functionName}} {{FunctionParser.ERROR_IN_FUNCTION}}"; exit 1; }; source <(echo -e "${{{functionName}}}") && echo "{{FunctionParser.SOURCING_END_MARKER}} {{functionName}} {{FunctionParser.SOURCED_SUCCESSFULLY}}" || echo "{{FunctionParser.SOURCING_END_MARKER}} {{functionName}} {{FunctionParser.SOURCING_FAILED}}"
			  """; // not suppressing source ... 2>/dev/null
	}

	public static Dictionary<string, string> InlineAll()
	{
		var r = new Dictionary<string, string>();
		var fn001Path = AbsolutePath.Create(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? string.Empty,
			"Scripts", $"{FUNCTION_NAME___run_function__with__stdout_end_marker__async}.sh"));
		var fn001nlined = ConvertBashScriptToInline(fn001Path, FUNCTION_NAME___run_function__with__stdout_end_marker__async);
		r.Add(FUNCTION_NAME___run_function__with__stdout_end_marker__async, fn001nlined);
		return r;
	}
	
	public static string ConvertBashScriptToInline(AbsolutePath scriptFilePath, string functionName)
	{
		using var sr = new StreamReader(scriptFilePath);
		string functionContents = sr.ReadToEnd();
		string functionInlined = LinuxUtils.InlineAndEscapeBashScript(functionContents);
		return functionInlined.Replace(functionName, $"{{{{{{{{{{{{{functionName}}}}}}}}}}}}}");
	}
}

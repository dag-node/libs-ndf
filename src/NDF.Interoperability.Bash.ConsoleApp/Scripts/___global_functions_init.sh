#!/usr/bin/bash

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




source <(cat <<'EOF'
function ___global__on_stop() {
  trap - SIGINT SIGTERM SIGHUP EXIT
  kill -s SIGINT -- -"$(ps -o pgid= -p $$)" 2>/dev/null && sleep 2
  pgrep -g $$ >/dev/null 2>&1 && kill -s SIGKILL -- -"$(ps -o pgid= -p $$)" 2>/dev/null
}
EOF
) && echo "___END_SOURCE_FN__ ___global__on_stop SOURCED_SUCCESSFULLY" || echo "___END_SOURCE_FN__ ___global__on_stop SOURCING_FAILED" 2> /dev/null;
for sig in EXIT SIGTERM SIGINT SIGHUP; do trap '___global__on_stop "$sig"' "$sig"; done

source <(cat <<'EOF'
function ___run_function__with__stdout_end_marker__async() {
	function_marker=$1
	stream_redirection=$2
	shift 2 # remove function marker and stream redirection from the arguments
	{
		local duration_ns timespan start_time end_time exit_code
		start_time=$(date -u +%s%N)
		"$@" ${stream_redirection}
		exit_code=$?
		end_time=$(date -u +%s%N)
		duration_ns=$((end_time - start_time))
		timespan=$(printf "%02d:%02d:%02d.%06d" \
			$((duration_ns / 3600000000000)) \
			$(((duration_ns / 60000000000) % 60)) \
			$(((duration_ns / 1000000000) % 60)) \
			$(((duration_ns / 1000) % 1000000)))
		echo "___END_FN__ ${start_time} ${end_time} ${timespan} ${function_marker} ${exit_code}"
	} &
}
EOF
) && echo "___END_SOURCE_FN__ ___run_function__with__stdout_end_marker__async SOURCED_SUCCESSFULLY" || echo "___END_SOURCE_FN__ ___run_function__with__stdout_end_marker__async SOURCING_FAILED" 2> /dev/null

source "functions.sh" && echo "___END_SOURCE_FN__ functions.sh SOURCED_SUCCESSFULLY" || echo "___END_SOURCE_FN__ functions.sh SOURCING_FAILED" 2> /dev/null

# ___run_function__with__stdout_end_marker__async 192119-974-get_string-2qAQ-1 get_string /tmp/tmpfs-bs/192119-974-get_string-2qAQ-1  1>/tmp/tmpfs-bs/192119-974-get_string-2qAQ-1.out 2>/tmp/tmpfs-bs/192119-974-get_string-2qAQ-1.err

# ___run_function__with__stdout_end_marker__async 192119-974-get_string-2qAQ-1 "1>/tmp/tmpfs-bs/192119-974-get_string-2qAQ-2.out 2>/tmp/tmpfs-bs/192119-974-get_string-2qAQ-2.err" get_string /tmp/tmpfs-bs/192119-974-get_string-2qAQ-1

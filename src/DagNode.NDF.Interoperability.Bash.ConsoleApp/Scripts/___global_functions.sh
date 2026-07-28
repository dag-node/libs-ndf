#!/usr/bin/bash
set +H # Disable history expansion

# Global functions ######################################################
function ___global__trap_with_arg() {
	local func="$1"
	shift
	for sig in "$@"; do
		trap "$func $sig" "$sig"
	done
}
# Function to handle cleanup
function ___global__stop() {
	local received_sig="$1"
	echo "Received $received_sig, terminating all child processes..."
	trap - SIGINT SIGTERM SIGHUP EXIT # Clear traps to avoid re-triggering
	kill -- -$$ 2>/dev/null           # Kill all processes in the current group
	exit 0                            # Exit cleanly
}
___global__trap_with_arg '___global__stop' EXIT SIGINT SIGTERM SIGHUP

function ___global__graceful_stop() {
	trap - SIGINT SIGTERM SIGHUP EXIT # Clear traps to avoid re-triggering
	local received_sig="$1"
	echo "___PROCESS_END__ PID $$ Received ${received_sig}, terminating..."
	# When a PID is negative, the signal is sent to all processes in the process group with the provided ID

	# Send SIGINT to the process group so the subprocesses can terminate gracefully
	kill -s SIGINT -- -$$ 2>/dev/null # Explicitly specifies the shell's PID group

	# Wait for a grace period (e.g., 2 seconds)
	# Provides time for processes to clean up and exit after receiving SIGINT.
	# local grace_period=2
	sleep 2 # "$grace_period"

	# Check if any child processes are still running
	if pgrep -g $$ >/dev/null 2>&1; then
		# echo "Some processes did not terminate, sending SIGKILL..."
		# Forcefully terminate any remaining processes
		# Ensures that all subprocesses are terminated, preventing resource leaks.
		kill -s SIGKILL -- -$$ 2>/dev/null
	fi
	# Exit the script
	exit 0
}

function ___global__graceful_stop() {
	trap - SIGINT SIGTERM SIGHUP EXIT
	kill -s SIGINT -- -"$(ps -o pgid= -p $$)" 2>/dev/null && sleep 2
	pgrep -g $$ >/dev/null 2>&1 && kill -s SIGKILL -- -"$(ps -o pgid= -p $$)" 2>/dev/null
}

# Set traps, allow different actions for different signals if needed.
trap '___global__graceful_stop' EXIT
trap '___global__graceful_stop' SIGINT
trap '___global__graceful_stop' SIGTERM
trap '___global__graceful_stop' SIGHUP


# Minimal silent graceful termination of all subprocesses with sigkill fallback after two seconds

# The wrapper function for asynchronous execution with stdout end marker
___global__run_function__with__stdout_end_marker__async="function run_function__with__stdout_end_marker__async() {
    function_marker=\$1
    shift
    { 
        local duration_ns timespan start_time end_time exit_code
        start_time=\$(date -u +%s%N)
        \"\$@\"  # Execute the function passed as argument
        exit_code=\$?
        end_time=\$(date -u +%s%N)
        duration_ns=\$((end_time - start_time))
        timespan=\$(printf \"%02d:%02d:%02d.%06d\" \$((duration_ns / 3600000000000)) \$(((duration_ns / 60000000000) % 60)) \$(((duration_ns / 1000000000) % 60)) \$(((duration_ns / 1000) % 1000000)))
        echo \"___END_FN__ \${start_time} \${end_time} \${timespan} \${function_marker} \${exit_code}\"
    } &    # Run function asynchronously in background
}"

# Set up traps to terminate child processes when the main process exits
___global__trap_with_arg '___global__stop' SIGINT SIGTERM EXIT

# Now, if you want to source the above functions in a process:
# Assuming you're sourcing this script in your main process, use the following command:

# Source the functions (this can be inside your .NET Core process)
# You should source this script to make the functions available:
source <(echo -e "$___global__run_function__with__stdout_end_marker__async")

# Example of calling the function in the main process
# run_function__with__stdout_end_marker__async "myFunctionMarker" some_command arg1 arg2
run_function__with__stdout_end_marker__async "0000" sleep 4

# Simulate a long-running process in the foreground
echo "Main process running with PID $$"
wait # This will wait for all background processes to finish

#set +H # Disable history expansion to allow #!/bin/bash inside echo -e
## Global inline functions ######################################################
#___global__trap_with_arg="function ___global__trap_with_arg() { local func=\"\$1\"; shift; for sig in \"\$@\"; do trap \"\$func \$sig\" \"\$sig\"; done; }"
#___global__stop="function ___global__stop() { trap - SIGINT EXIT; printf '\n%s\n' \"received \$1, killing child processes\"; kill -s SIGINT 0; }"
#___global__run_function__with__stdout_end_marker__async="function run_function__with__stdout_end_marker__async() { function_marker=\$1; shift; { local duration_ns timespan start_time end_time exit_code; start_time=\$(date -u +%s%N); \"\$@\"; exit_code=\$?; end_time=\$(date -u +%s%N); duration_ns=\$((end_time - start_time)); timespan=\$(printf \"%02d:%02d:%02d.%06d\" \$((duration_ns / 3600000000000)) \$(((duration_ns / 60000000000) % 60)) \$(((duration_ns / 1000000000) % 60)) \$(((duration_ns / 1000) % 1000000))); echo \"___END_FN__ \${start_time} \${end_time} \${timespan} \${function_marker} \${exit_code}\"; } &; }"
#___global__all_functions=$(echo -e "#!/usr/bin/bash\n${___global__trap_with_arg}\n${___global__stop}\n${___global__run_function__with__stdout_end_marker__async}")
## Source all global functions by 1. validating there is no error in functions provided 2. source all and print result
#/usr/bin/bash -c "echo -e \"${___global__all_functions}\" > /dev/null || { echo SOURCING_FAILED; exit 1; }; source <(echo -e \"${___global__all_functions}\") && echo SOURCED_SUCCESSFULLY || echo SOURCING_FAILED; ___global__trap_with_arg '___global__stop' EXIT SIGINT SIGTERM SIGHUP && echo SIGNAL_OK || echo TRAP_FAILED; " 2> /dev/null
## Set traps
#/usr/bin/bash ___global__trap_with_arg '___global__stop' EXIT SIGINT SIGTERM SIGHUP

## Global inline functions ######################################################
#___global__trap_with_arg="function ___global__trap_with_arg() { local func=\"\$1\"; shift; for sig in \"\$@\"; do trap \"\$func \$sig\" \"\$sig\"; done; }"
#___global__stop="function ___global__stop() { trap - SIGINT EXIT; printf '\n%s\n' \"received \$1, killing child processes\"; kill -s SIGINT 0; }"
##___global__all_functions=$(echo -e "#!/usr/bin/bash\n${___global__trap_with_arg}\n${___global__stop}\n${___global__run_function__with__stdout_end_marker__async}")
#___global__all_functions=$(echo -e "#!/usr/bin/bash\n${___global__trap_with_arg}\n${___global__stop}")
## Source all global functions by 1. validating there is no error in functions provided 2. source all and print result
#echo -e "${___global__all_functions}" > /dev/null || { echo SOURCING_FAILED; exit 1; }
#source <(echo -e "${___global__all_functions}") && echo SOURCED_SUCCESSFULLY || echo SOURCING_FAILED 2>/dev/null
#___global__run_function__with__stdout_end_marker__async="function run_function__with__stdout_end_marker__async() { function_marker=\$1; shift; { local duration_ns timespan start_time end_time exit_code; start_time=\$(date -u +%s%N); \"\$@\"; exit_code=\$?; end_time=\$(date -u +%s%N); duration_ns=\$((end_time - start_time)); timespan=\$(printf \"%02d:%02d:%02d.%06d\" \$((duration_ns / 3600000000000)) \$(((duration_ns / 60000000000) % 60)) \$(((duration_ns / 1000000000) % 60)) \$(((duration_ns / 1000) % 1000000))); echo \"___END_FN__ \${start_time} \${end_time} \${timespan} \${function_marker} \${exit_code}\"; } & }"
##___global__trap_with_arg '___global__stop' EXIT SIGINT SIGTERM SIGHUP && echo GLOBAL_SIGNAL_TRAP_OK || echo GLOBAL_SIGNAL_TRAP_FAILED


___global__on_stop=$(cat <<'EOF'
function ___global__on_stop() {
    trap - SIGINT SIGTERM SIGHUP EXIT
    echo "___END_PROCESS__ $$ by $1"
    kill -s SIGINT -- -$(ps -o pgid= -p $$) 2>/dev/null
    sleep 2
    if pgrep -g $$ >/dev/null 2>&1; then
        kill -s SIGKILL -- -$(ps -o pgid= -p $$) 2>/dev/null
    fi
}
EOF
)

echo "${___global__on_stop}" > /dev/null || {
    echo "___END_SOURCE_FN__ ___global__on_stop ERROR_IN_FUNCTION"
    exit 1
}

source <(echo "${___global__on_stop}") && \
    echo "___END_SOURCE_FN__ ___global__on_stop SOURCED_SUCCESSFULLY" || \
    echo "___END_SOURCE_FN__ ___global__on_stop SOURCING_FAILED" 2>/dev/null

#TODO: Use
text_block=$(cat <<'EOF'
...
EOF
)
source <(echo "${text_block}")


source "functions.sh" && echo "___END_SOURCE_FN__ functions.sh SOURCED_SUCCESSFULLY" || echo "___END_SOURCE_FN__ functions.sh SOURCING_FAILED" 2> /dev/null

___global__on_stop=$(echo -e "function ___global__on_stop() { trap - SIGINT SIGTERM SIGHUP EXIT; kill -s SIGINT -- -\"\$(ps -o pgid= -p \$\$)\" 2>/dev/null && sleep 2; pgrep -g \$\$ >/dev/null 2>&1 && kill -s SIGKILL -- -\"\$(ps -o pgid= -p \$\$)\" 2>/dev/null; }"); echo -e "${___global__on_stop}" > /dev/null || { echo "___END_SOURCE_FN__ ___global__on_stop ERROR_IN_FUNCTION"; exit 1; }; source <(echo -e "${___global__on_stop}") && echo "___END_SOURCE_FN__ ___global__on_stop SOURCED_SUCCESSFULLY" || echo "___END_SOURCE_FN__ ___global__on_stop SOURCING_FAILED" 2> /dev/null

___run_function__with__stdout_end_marker__async=$(echo -e "function ___run_function__with__stdout_end_marker__async() { function_marker=\$1; stream_redirection=\$2; shift 2; { local duration_ns timespan start_time end_time exit_code; start_time=\$(date -u +%s%N); \"\$@\" \${stream_redirection} exit_code=\$?; end_time=\$(date -u +%s%N); duration_ns=\$((end_time - start_time)); timespan=\$(printf \"%02d:%02d:%02d.%06d\" \$((duration_ns / 3600000000000)) \$(((duration_ns / 60000000000) % 60)) \$(((duration_ns / 1000000000) % 60)) \$(((duration_ns / 1000) % 1000000))); echo \"___END_FN__ \${start_time} \${end_time} \${timespan} \${function_marker} \${exit_code}\"; } & }"); echo -e "${___run_function__with__stdout_end_marker__async}" > /dev/null || { echo "___END_SOURCE_FN__ ___run_function__with__stdout_end_marker__async ERROR_IN_FUNCTION"; exit 1; }; source <(echo -e "${___run_function__with__stdout_end_marker__async}") && echo "___END_SOURCE_FN__ ___run_function__with__stdout_end_marker__async SOURCED_SUCCESSFULLY" || echo "___END_SOURCE_FN__ ___run_function__with__stdout_end_marker__async SOURCING_FAILED" 2> /dev/null

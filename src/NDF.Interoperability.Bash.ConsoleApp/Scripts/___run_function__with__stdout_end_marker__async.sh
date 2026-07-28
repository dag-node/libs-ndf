#!/bin/bash

# TODO: When the main process is terminated, it does not kill subshells by default!
# Ensure all subshells are killed when main process is terminated
# https://stackoverflow.com/questions/8363519/how-do-i-terminate-all-the-subshell-processes
# https://stackoverflow.com/questions/360201/how-do-i-kill-background-processes-jobs-when-my-shell-script-exits/2173421#2173421
# https://stackoverflow.com/questions/360201/how-do-i-kill-background-processes-jobs-when-my-shell-script-exits/28333938#28333938

# This works for me (collaborative effort with the commenters):
# trap "trap - SIGTERM && kill -- -$$" SIGINT SIGTERM EXIT
# The inner trap - SIGTERM will reset the current script SIGTERM response to the default kill behavior.
# Then, when kill -- -$$ is executed, the current script will receive SIGTERM and exit normally.
trap_with_arg() { # from https://stackoverflow.com/a/2183063/804678
	local func="$1"
	shift
	for sig in "$@"; do
		trap "$func $sig" "$sig"
	done
}
stop() {
	trap - SIGINT EXIT # Clears the trap so the stop function doesn't get called recursively when the process finally exits.
	printf '\n%s\n' "received $1, killing child processes"
	kill -s SIGINT 0
}
trap_with_arg 'stop' EXIT SIGINT SIGTERM SIGHUP

function graceful_stop() {
	local received_sig="$1"
	echo "Received $received_sig, terminating all child processes..."
	trap - SIGINT SIGTERM SIGHUP EXIT # Clear traps to avoid re-triggering
	# When a PID is negative, the signal is sent to all processes in the process group with the provided ID

	# Send SIGINT to the process group
	kill -s SIGINT -- -$$ 2>/dev/null

	# Wait for a grace period (e.g., 2 seconds)
	# Provides time for processes to clean up and exit after receiving SIGINT.
	local grace_period=2
	echo "Waiting for processes to terminate..."
	sleep "$grace_period"

	# Check if any child processes are still running
	if pgrep -g $$ >/dev/null 2>&1; then
		echo "Some processes did not terminate, sending SIGKILL..."
		# Forcefully terminate any remaining processes
		# Ensures that all subprocesses are terminated, preventing resource leaks.
		kill -s SIGKILL -- -$$ 2>/dev/null
	else
		echo "All processes terminated gracefully."
	fi

	# Exit the script
	exit 0
}

run_function__with__stdout_end_marker__async() {
	function_marker=$1
	shift # remove function marker from the arguments
	{
		local duration_ns timespan start_time end_time exit_code

		# Execute the function
		start_time=$(date -u +%s%N) # capture UTC time before the function starts
		"$@"                        # stdout and stderr to files redirection is part of function args

		exit_code=$?              # capture function's exit code
		end_time=$(date -u +%s%N) # capture UTC time after the function finished

		# Calculate duration in nanoseconds
		duration_ns=$((end_time - start_time))
		# Format output for C# TimeSpan
		timespan=$(printf "%02d:%02d:%02d.%06d" \
			$((duration_ns / 3600000000000)) \
			$(((duration_ns / 60000000000) % 60)) \
			$(((duration_ns / 1000000000) % 60)) \
			$(((duration_ns / 1000) % 1000000)))

		# Notify function finished
		echo "___END_FN__ ${start_time} ${end_time} ${timespan} ${function_marker} ${exit_code}" # output to stdout
	} &                                                                                       # wrap function execution + exit code in a subshell, run as background task

	# TODO: Keep track of subprocess PIDs
	# local pid=$!  # Capture the PID of the background subshell, thread safe
	# echo $pid > "${fifo}.pid"  # Write the PID to the file
}

# in-line version (TODO: Update inline version with trap kill subshells)
# run_function__with__stdout_end_marker__async() { function_marker=$1; shift; { local start_time end_time duration_ns timespan exit_code; start_time=$(date -u +%s%N); "$@"; exit_code=$?; end_time=$(date -u +%s%N); duration_ns=$((end_time - start_time)); timespan=$(printf "%02d:%02d:%02d.%06d" $((duration_ns / 3600000000000)) $(((duration_ns / 60000000000) % 60)) $(((duration_ns / 1000000000) % 60)) $(((duration_ns / 1000) % 1000000))); echo "___END_FN__ ${start_time} ${end_time} ${timespan} ${function_marker} ${exit_code}"; } & }

# The bash -c command must be properly escaped!
# -c "__run_function__with__stdout_end_marker__async() { function_marker=\$1; shift; { local start_time end_time duration_ns timespan exit_code; start_time=\$(date -u +%s%N); \"\$@\"; exit_code=\$?; end_time=\$(date -u +%s%N); duration_ns=\$((end_time - start_time)); timespan=\$(printf \"%02d:%02d:%02d.%06d\" \$((duration_ns / 3600000000000)) \$(((duration_ns / 60000000000) % 60)) \$(((duration_ns / 1000000000) % 60)) \$(((duration_ns / 1000) % 1000000))); echo \"{{FunctionParser.FUNCTION_END_MARKER}} \${start_time} \${end_time} \${timespan} \${function_marker} \${exit_code}\"; } & }; { echo SOURCING_FAILED; exit 1; }; source <(__run_function__with__stdout_end_marker__async() { function_marker=\$1; shift; { local start_time end_time duration_ns timespan exit_code; start_time=\$(date -u +%s%N); \"\$@\"; exit_code=\$?; end_time=\$(date -u +%s%N); duration_ns=\$((end_time - start_time)); timespan=\$(printf \"%02d:%02d:%02d.%06d\" \$((duration_ns / 3600000000000)) \$(((duration_ns / 60000000000) % 60)) \$(((duration_ns / 1000000000) % 60)) \$(((duration_ns / 1000) % 1000000))); echo \"{{FunctionParser.FUNCTION_END_MARKER}} \${start_time} \${end_time} \${timespan} \${function_marker} \${exit_code}\"; } & }) && echo SOURCED_SUCCESSFULLY || echo SOURCING_FAILED" 2>/dev/null

# Explanation of Escaping
#
#    Dollar Signs ($):
#        $ needs to be escaped as \$ to ensure it’s passed literally to bash -c.
#
#    Double Quotes ("):
#        Inner quotes used in the command (printf "%02d:%02d:%02d.%06d") must remain unescaped.
#        Outer quotes wrapping the -c argument should remain unescaped.
#
#    Backslashes (\):
#        Ensure proper escaping of variable references (${variable} → \${variable}).
#
#    Special Characters:
#        Characters such as {, }, (, and ) do not need escaping inside the -c argument.

/usr/bin/bash -c "trap_with_arg() { local func=\"\$1\"; shift; for sig in \"\$@\"; do; trap \"\$func \$sig\" \"\$sig\"; done; } stop() { trap - SIGINT EXIT; printf '\\n%s\\n' \"received \$1, killing child processes\"; kill -s SIGINT 0; } trap_with_arg 'stop' EXIT SIGINT SIGTERM SIGHUP; run_function__with__stdout_end_marker__async() { function_marker=\$1; shift; { local duration_ns timespan start_time end_time exit_code; start_time=\$(date -u +%s%N); \"\$@\"; exit_code=\$?; end_time=\$(date -u +%s%N); duration_ns=\$((end_time - start_time)); timespan=\$(printf \"%02d:%02d:%02d.%06d\" \$((duration_ns / 3600000000000)) \$(((duration_ns / 60000000000) % 60)) \$(((duration_ns / 1000000000) % 60)) \$(((duration_ns / 1000) % 1000000))); echo \"___END_FN__ \${start_time} \${end_time} \${timespan} \${function_marker} \${exit_code}\"; } &; }"

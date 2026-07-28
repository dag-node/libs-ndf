#!/usr/bin/bash
# Ensure all subprocesses spawned by functions
# are killed when the main process receives a signal. 
___trap_with_arg() {
  local func="$1"; shift
  for sig in "$@"; do
    trap "$func $sig" "$sig"
  done
}
___stop() {
  trap - SIGINT EXIT
  printf '\n%s\n' "received $1, killing child processes"
  kill -s SIGINT 0
}

# Set traps
___trap_with_arg '___stop' EXIT SIGINT SIGTERM SIGHUP

# Summary of Behavior:
#
#    Signal Handling and Traps:
#        The trap_with_arg() ensures the stop function is called when the main process receives signals (EXIT, SIGINT, etc.).
#
#    Subprocess Management:
#        Subprocesses are killed by kill -s SIGINT 0 when stop is invoked, provided they are in the same process group.
#
#    Practical Outcome:
#        Sourcing the trap_with_arg() and stop() functions with the trap configuration and running subsequent trap_with_arg command within the same bash process as functions (which spawn subshells) ensures the trap logic applies to the current shell and all subshells given the processes are running within the same process group. When the main process shell receives a signal, all subprocesses spawned by those functions are killed.


___global__trap_start="___global__trap_with_arg '___global__trap_stop' EXIT SIGINT SIGTERM SIGHUP"

private static string GetFunctionSourcingCommand(string inlinedFunction)
=> $$$$$$"""-c '{{{{{{inlinedFunction}}}}}} > /dev/null || { echo {{{{{{SOURCING_FAILED}}}}}}; exit 1; }; source <({{{{{{inlinedFunction}}}}}}) && echo {{{{{{SOURCED_SUCCESSFULLY}}}}}} || echo {{{{{{SOURCING_FAILED}}}}}}' 2>/dev/null"""; // SOURCED_SUCCESSFULLY or SOURCING_FAILED

/usr/bin/bash -c '{{{{{{inlinedFunction}}}}}} > /dev/null || { echo {{{{{{SOURCING_FAILED}}}}}}; exit 1; }; source <({{{{{{inlinedFunction}}}}}}) && echo {{{{{{SOURCED_SUCCESSFULLY}}}}}} || echo {{{{{{SOURCING_FAILED}}}}}}' 2>/dev/null

set +H # Disable history expansion to allow #!/bin/bash inside echo -e
# Global inline functions ######################################################
___global__trap_with_arg="function ___global__trap_with_arg() { local func=\"\$1\"; shift; for sig in \"\$@\"; do trap \"\$func \$sig\" \"\$sig\"; done; }"
___global__stop="function ___global__stop() { trap - SIGINT EXIT; printf '\n%s\n' \"received \$1, killing child processes\"; kill -s SIGINT 0; }"
___global__all_functions=$(echo -e "#!/usr/bin/bash\n${___global__trap_with_arg}\n${___global__stop}")
___global__run_function__with__stdout_end_marker__async="function run_function__with__stdout_end_marker__async() { function_marker=\$1; shift; { local duration_ns timespan start_time end_time exit_code; start_time=\$(date -u +%s%N); \"\$@\"; exit_code=\$?; end_time=\$(date -u +%s%N); duration_ns=\$((end_time - start_time)); timespan=\$(printf \"%02d:%02d:%02d.%06d\" \$((duration_ns / 3600000000000)) \$(((duration_ns / 60000000000) % 60)) \$(((duration_ns / 1000000000) % 60)) \$(((duration_ns / 1000) % 1000000))); echo \"___END_FN__ \${start_time} \${end_time} \${timespan} \${function_marker} \${exit_code}\"; } &; }";
# Source all global functions by 1. validating there is no error in functions provided 2. source all and print result
echo -e "${___global__all_functions}" > /dev/null || { echo SOURCING_FAILED; exit 1; }; source <(echo -e "${___global__all_functions}") && echo SOURCED_SUCCESSFULLY || echo SOURCING_FAILED 2> /dev/null
# Set traps
/usr/bin/bash ___global__trap_with_arg '___global__stop' EXIT SIGINT SIGTERM SIGHUP
# ##############################################################################


___global__trap_with_arg '___global__trap_stop' EXIT SIGINT SIGTERM SIGHUP


# ##########################3
If your function definitions are extensive or multiline and you need a clearer structure, you might opt to write the functions into a temporary file and then source that file:

set +H # Disable history expansion

# Write functions to a temporary file
tmp_script=$(mktemp)
echo -e "#!/usr/bin/bash\n${___global__trap_with_arg}\n${___global__trap_stop}" > "$tmp_script"

# Source the temporary file and validate result
/usr/bin/bash -c "source $tmp_script && echo SOURCED_SUCCESSFULLY || echo SOURCING_FAILED" 2>/dev/null

# Clean up
rm -f "$tmp_script"

#!/bin/bash
# Each call to run_function_async:
#    (...) runs command in a subshell, ensuring we capture correct exit code
#    & Spawns specified command in background without blocking main bash thread
#    Sends the PID to a .pid file.
#    The function's exit code is written to the FIFO upon completion.
function run_function_async {
    local fifo="$1"
    shift  # Remove FIFO from arguments
    ("$@" & echo $! > "$fifo.pid") &  # Run function in background and save it's PID to FIFO
}
# Not using this file, inlined directly in BashProcess.cs
# FunctionRunFunctionAsyncInline

# TODO: Compare performance
#

run_function_async() { fifo=$1; shift; ("$@" ; echo $? > "$fifo") & } 
run_function_sync() { fifo=$1; shift; ("$@" ; echo $? > "$fifo") }

run_function_sync() { fifo=$1; shift; ("$@" & echo $! > "$fifo.pid" | tee "__END__$fifo $?" > "$fifo") }
("$@" & echo $! > "$fifo.pid")
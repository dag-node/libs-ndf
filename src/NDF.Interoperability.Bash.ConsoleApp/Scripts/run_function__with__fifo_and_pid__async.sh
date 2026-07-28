#!/bin/bash
# Use named pipes to read function exit code from fifo
# mkfifo  
run_function__with__stdout_end_marker__async() {
	fifo=$1
	shift  # Remove FIFO from arguments	
	{
		"$@" # Execute the function
		exit_code=$?  # Capture the function's exit code
		echo $exit_code > "$fifo"  # Write the exit code to the FIFO
	} &
	local pid=$!  # Capture the PID of the background subshell, thread safe
	echo $pid > "${fifo}.pid"  # Write the PID to the file
}

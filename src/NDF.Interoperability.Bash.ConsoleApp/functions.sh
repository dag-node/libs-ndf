#!/bin/bash
# Functions from this script called by .NET Core

# Example of a function returning string value
function get_string() {
	echo "Hello, World!"  # String
}

# Function to return a valid enum value
function get_enum_value() {
    echo "Processing"  # Must match .NET Core application Enum type value
}

# Function to return args with spaces
function get_string_from_args_with_spaces() {
	local arg1="$1" # Path to a logfile is automatically passed in as first argument
	local arg2="$2" # Args passed in by the client start at arg2 ...
	local arg3="$3"
	local arg4="$4"	
	echo "${arg2} ${arg3} ${arg4}"  # Test args with spaces
}

# Example of a function returning numbers
function get_int() {	
	echo 42  # Number
}

# Function to return a valid long value (integer in range of Int64)
function get_long() {
    echo "9223372036854775807"  # Max value of Int64
}

# Function to return a valid double value
function get_double() {
    echo "3.14159"  # Pi as a double
}

# Function to return a valid decimal value
# Function to return a decimal value with 1 followed by maximum decimal places (28 digits)
function get_decimal() {
    echo "1.0000000001000000000100000001"  # 1 followed by 28 decimal places
}

# Example of a function returning bool
function is_even() {
	local log_file_path="$1"
	local num=$2
	(( num % 2 == 0 )) && return 0 || return 1
}
function is_odd() {
	local log_file_path="$1"
	local num=$2
	(( num % 2 == 1 )) && return 0 || return 1
}

# Example of a function returning array
function get_array() {
	echo "one"
	echo "two"
	echo "three"
}

# TODO: Check different array return types
#function populate_array() {
#	local -n arr_ref=$1  # Use nameref
#	arr_ref=("apple" "orange" "banana")
#}


#	result=$(get_string)
#	number=$(get_int)
#	echo "Result: $result, Number: $number"
#	
#	if is_even 4; then
#		echo "Even"
#	elses
#		echo "Odd"
#	fi
#	
#	# Stream substitution
#	mapfile -t array < <(get_array)  # Read into array
#	echo "${array[@]}"
#	
#	declare -a my_array
#	populate_array my_array
#	echo "${my_array[@]}"

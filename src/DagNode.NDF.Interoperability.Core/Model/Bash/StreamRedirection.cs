namespace DagNode.NDF.Interoperability.Model.Bash;

public static class StreamRedirection
{
	/// <summary>
	/// (Default) Writes stdout to {prefix}.out and stderr to {prefix}.err
	/// </summary>
	public const string RedirectToFiles = "1>{prefix}.out 2>{prefix}.err";
	/// <summary>
	/// [DISCARD]: Discards stdout and stderr, does not create any files
	/// </summary>
	public const string DiscardAllToDevNull = "&>/dev/null";
	/// <summary>
	/// [DISCARD]: Redirects stdout to {prefix}.out, discards stderr
	/// </summary>
	public const string UseOutDiscardErr = "1>{prefix}.out 2>/dev/null";
	/// <summary>
	/// [DISCARD]:Redirects stderr to {prefix}.err, discards stdout
	/// </summary>
	public const string UseErrDiscardOut = "2>{prefix}.err 1>/dev/null";
	/// <summary>
	/// [REDIRECT]: Redirects both stdout and stderr to the same {prefix}.out
	/// </summary>
	public const string RedirectBothToOut = "1>{prefix}.out 2>&1";
	/// <summary>
	/// [REDIRECT]: Redirects both stderr and stdout to the same {prefix}.err
	/// </summary>
	public const string RedirectBothToErr = "2>{prefix}.err 1>&2";
	
	/// <summary>
	/// [SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Bash splits the result of $(&lt; {prefix}.in) into words based on whitespace (spaces, tabs, newlines).
	/// Writes stdout to {prefix}.out and stderr to {prefix}.err
	/// </summary>
	public const string SubstituteInWriteBoth = "$(< {prefix}.in) 1>{prefix}.out 2>{prefix}.err";
	/// <summary>
	/// [SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Bash splits the result of $(&lt; {prefix}.in) into words based on whitespace (spaces, tabs, newlines).
	/// Discards both stdout and stderr
	/// </summary>
	public const string SubstituteInDiscardBoth = "$(< {prefix}.in) &>/dev/null";
	/// <summary>
	/// [SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Bash splits the result of $(&lt; {prefix}.in) into words based on whitespace (spaces, tabs, newlines).
	/// Writes stdout to {prefix}.out
	/// </summary>
	public const string SubstituteInUseOutDiscardErr = "$(< {prefix}.in) 1>{prefix}.out 2>/dev/null";
	/// <summary>
	/// [SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Bash splits the result of $(&lt; {prefix}.in) into words based on whitespace (spaces, tabs, newlines).
	/// Writes stderr to {prefix}.err
	/// </summary>
	public const string SubstituteInUseErrDiscardOut = "$(< {prefix}.in) 2>{prefix}.err 1>/dev/null";
	
	/// <summary>
	/// [QUOTED SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Will treat the entire file contents as a single substituted function argument.
	/// Writes stdout to {prefix}.out and stderr to {prefix}.err
	/// </summary>
	public const string SubstituteQuotedInWriteBoth = "\"$(< {prefix}.in)\" 1>{prefix}.out 2>{prefix}.err";
	/// <summary>
	/// [QUOTED SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Will treat the entire file contents as a single substituted function argument.
	/// Discards both stdout and stderr
	/// </summary>
	public const string SubstituteQuotedInDiscardBoth = "\"$(< {prefix}.in)\" &>/dev/null";
	/// <summary>
	/// [QUOTED SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Will treat the entire file contents as a single substituted function argument.
	/// Writes stdout to {prefix}.out
	/// </summary>
	public const string SubstituteQuotedInUseOutDiscardErr = "\"$(< {prefix}.in)\" 1>{prefix}.out 2>/dev/null";
	/// <summary>
	/// [QUOTED SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Will treat the entire file contents as a single substituted function argument.
	/// Writes stderr to {prefix}.err
	/// </summary>
	public const string SubstituteQuotedInUseErrDiscardOut = "\"$(< {prefix}.in)\" 2>{prefix}.err 1>/dev/null";
	
	/// <summary>
	/// [TAKE IN]: Takes stdin from file, writes stdout to {prefix}.out and stderr to {prefix}.err.
	/// Changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.in.
	/// </summary>
	public const string TakeInWriteBoth = "< {prefix}.in 1>{prefix}.out 2>{prefix}.err";
	/// <summary>
	/// [TAKE IN]: Takes stdin from file, discards both stdout and stderr.
	/// Changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.in.
	/// </summary>
	public const string TakeInDiscardBoth = "< {prefix}.in &>/dev/null";
	/// <summary>
	/// [TAKE IN]: Takes stdin from file, writes stdout to {prefix}.out.
	/// Changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.in.
	/// </summary>
	public const string TakeInUseOutDiscardErr = "< {prefix}.in 1>{prefix}.out 2>/dev/null";
	/// <summary>
	/// [TAKE IN]: Takes stdin from file, writes stderr to {prefix}.err.
	/// Changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.in.
	/// </summary>
	public const string TakeInUseErrDiscardOut = "< {prefix}.in 2>{prefix}.err 1>/dev/null";
	
	/// <summary>
	/// (RedirectToFiles) Writes stdout to {prefix}.out and stderr to {prefix}.err.
	/// </summary>
	public const string Default = RedirectToFiles;
}

public enum StreamRedirectionType
{
	/// <summary>
	/// (Default) Writes stdout to {prefix}.out and stderr to {prefix}.err
	/// </summary>
	RedirectToFiles, // 1> {prefix}.out 2> {prefix}.err";
	/// <summary>
	/// [DISCARD]: Discards stdout and stderr, does not create any files
	/// </summary>
	DiscardAllToDevNull, // &> /dev/null";
	/// <summary>
	/// [DISCARD]: Redirects stdout to {prefix}.out, discards stderr
	/// </summary>
	UseOutDiscardErr, // 1> {prefix}.out 2> /dev/null";
	/// <summary>
	/// [DISCARD]:Redirects stderr to {prefix}.err, discards stdout
	/// </summary>
	UseErrDiscardOut, // 2> {prefix}.err 1> /dev/null";
	/// <summary>
	/// [REDIRECT]: Redirects both stdout and stderr to the same {prefix}.out
	/// </summary>
	RedirectBothToOut, // 1> {prefix}.out 2> &1";
	/// <summary>
	/// [REDIRECT]: Redirects both stderr and stdout to the same {prefix}.err
	/// </summary>
	RedirectBothToErr, // "2> {prefix}.err 1> &2";
	
	/// <summary>
	/// [SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Bash splits the result of $(&lt; {prefix}.in) into words based on whitespace (spaces, tabs, newlines).
	/// Writes stdout to {prefix}.out and stderr to {prefix}.err
	/// </summary>
	SubstituteInWriteBoth, // "$(< {prefix}.in) 1> {prefix}.out 2> {prefix}.err";
	/// <summary>
	/// [SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Bash splits the result of $(&lt; {prefix}.in) into words based on whitespace (spaces, tabs, newlines).
	/// Discards both stdout and stderr
	/// </summary>
	SubstituteInDiscardBoth, // "$(< {prefix}.in) &> /dev/null";
	/// <summary>
	/// [SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Bash splits the result of $(&lt; {prefix}.in) into words based on whitespace (spaces, tabs, newlines).
	/// Writes stdout to {prefix}.out
	/// </summary>
	SubstituteInUseOutDiscardErr, // "$(< {prefix}.in) 1> {prefix}.out 2> /dev/null";
	/// <summary>
	/// [SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Bash splits the result of $(&lt; {prefix}.in) into words based on whitespace (spaces, tabs, newlines).
	/// Writes stderr to {prefix}.err
	/// </summary>
	SubstituteInUseErrDiscardOut, // "$(< {prefix}.in) 2> {prefix}.err 1> /dev/null";
	
	/// <summary>
	/// [QUOTED SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Will treat the entire file contents as a single substituted function argument.
	/// Writes stdout to {prefix}.out and stderr to {prefix}.err
	/// </summary>
	SubstituteQuotedInWriteBoth, // "\"$(< {prefix}.in)\" 1> {prefix}.out 2> {prefix}.err";
	/// <summary>
	/// [QUOTED SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Will treat the entire file contents as a single substituted function argument.
	/// Discards both stdout and stderr
	/// </summary>
	SubstituteQuotedInDiscardBoth, // "\"$(< {prefix}.in)\" &> /dev/null";
	/// <summary>
	/// [QUOTED SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Will treat the entire file contents as a single substituted function argument.
	/// Writes stdout to {prefix}.out
	/// </summary>
	SubstituteQuotedInUseOutDiscardErr, // "\"$(< {prefix}.in)\" 1> {prefix}.out 2> /dev/null";
	/// <summary>
	/// [QUOTED SUBSTITUTE]: Reads {prefix}.in into memory and passes its contents as arguments to the function.
	/// Will treat the entire file contents as a single substituted function argument.
	/// Writes stderr to {prefix}.err
	/// </summary>
	SubstituteQuotedInUseErrDiscardOut, // "\"$(< {prefix}.in)\" 2> {prefix}.err 1> /dev/null";
	
	/// <summary>
	/// [TAKE IN]: Takes stdin from file, writes stdout to {prefix}.out and stderr to {prefix}.err.
	/// Changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.in.
	/// </summary>
	TakeInWriteBoth, // "< {prefix}.in 1> {prefix}.out 2> {prefix}.err";
	/// <summary>
	/// [TAKE IN]: Takes stdin from file, discards both stdout and stderr.
	/// Changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.in.
	/// </summary>
	TakeInDiscardBoth, // "< {prefix}.in &> /dev/null";
	/// <summary>
	/// [TAKE IN]: Takes stdin from file, writes stdout to {prefix}.out.
	/// Changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.in.
	/// </summary>
	TakeInUseOutDiscardErr, // "< {prefix}.in 1> {prefix}.out 2> /dev/null";
	/// <summary>
	/// [TAKE IN]: Takes stdin from file, writes stderr to {prefix}.err.
	/// Changes the standard input (stdin) for the duration of the function call.
	/// Inside the function, commands that read from stdin (e.g., read, cat, grep, etc.) will read data from {prefix}.in.
	/// </summary>
	TakeInUseErrDiscardOut, // "< {prefix}.in 2> {prefix}.err 1> /dev/null";
	
	/// <summary>
	/// (RedirectToFiles) Writes stdout to {prefix}.out and stderr to {prefix}.err.
	/// </summary>
	Default // RedirectToFiles;
}

namespace DagNode.NDF.Interoperability.Model.Bash;

/// <summary>
/// Per-call configuration pairing a bash stream redirection with the file locations the result is
/// read back from. The redirection string is appended to the generated function call, so
/// <c>1&gt;{prefix}.out 2&gt;{prefix}.err</c> writes the two streams to separate files while
/// <c>1&gt;{prefix}.out 2&gt;&amp;1</c> folds stderr into stdout and leaves nothing at {prefix}.err.
/// Build instances through the factory methods: they keep the result locations consistent with the
/// redirection, so a discarded stream is not later read from a file that was never written.
/// </summary>
public class CallOptions
{
	/// <summary>
	/// By default, function results are being read from standard output,
	/// stored in ResultLocationType.PrefixOut.
	/// </summary>
	public FunctionResultLocation ReadResultFrom { get; set; } = FunctionResultLocation.PrefixOut;
	
	/// <summary>
	/// Use customized stream redirection.
	/// </summary>
	public string StreamRedirection { get; private set; }
	
	/// <summary>
	/// Function standard output, {prefix}.out by default
	/// </summary>
	public FunctionResultLocation ResultOutFileLocation { get; private set; }
	
	/// <summary>
	/// Function standard error, {prefix}.err by default.
	/// </summary>
	public FunctionResultLocation ResultErrFileLocation { get; private set; }
	
	/// <summary>
	/// Optional {prefix}.log, {prefix} is passed as "$1" first arg to every function.
	/// </summary>
	public FunctionResultLocation ResultLogFileLocation { get; private set; }
	
	/// <summary>
	/// For cases when stream redirection utilizes standard input.
	/// Create a file with name {prefix}.in before the function is started.
	/// Ideally inside asyncBefore hook, alternatively utilize global EventHandlerFunctionStartAsync.
	/// </summary>
	public FunctionInputLocation InputFileLocation { get; private set; }
	
	#region Private constructors
	private CallOptions(string streamRedirection)
		: this(FunctionInputLocation.Default,
			resultOutFileLocation: FunctionResultLocation.Default,
			FunctionResultLocation.Default,
			FunctionResultLocation.Default,
			streamRedirection) { }
	
	private CallOptions(FunctionInputLocation inputFileLocation, string streamRedirection = Bash.StreamRedirection.RedirectToFiles)
		: this(inputFileLocation,
			FunctionResultLocation.Default,
			FunctionResultLocation.Default,
			FunctionResultLocation.Default,
			streamRedirection) { }
	
	private CallOptions(FunctionInputLocation inputFileLocation,
		FunctionResultLocation resultOutFileLocation,
		FunctionResultLocation resultErrFileLocation,
		FunctionResultLocation resultLogFileLocation,
		string streamRedirection = Bash.StreamRedirection.Default)
	{
		if (string.IsNullOrWhiteSpace(streamRedirection)) throw new ArgumentNullException(nameof(streamRedirection));
		if (!streamRedirection.Contains('>') && !streamRedirection.Contains('<')) {
			//TODO: Better validation the string provided is actually a stream redirection string
			throw new ArgumentException("Invalid stream redirection string", nameof(streamRedirection));
		}
		StreamRedirection = streamRedirection;
		InputFileLocation = inputFileLocation;
		ResultOutFileLocation = resultOutFileLocation;
		ResultErrFileLocation = resultErrFileLocation;
		ResultLogFileLocation = resultLogFileLocation;
		
		// The default is to read function results from standard output written to the {prefix}.out file.
		// When using generic CallFunction<bool>, the result is function ExitCode by default (not PrefixOut)
		if (resultOutFileLocation == FunctionResultLocation.Default || resultOutFileLocation is { LocationType: ResultLocationType.Default }) {
			resultOutFileLocation.SetPublicProperty("LocationType", ResultLocationType.PrefixOut);	
		}
		if (resultErrFileLocation == FunctionResultLocation.Default || resultErrFileLocation is { LocationType: ResultLocationType.Default }) {
			resultErrFileLocation.SetPublicProperty("LocationType", ResultLocationType.PrefixErr);	
		}
		if (resultLogFileLocation == FunctionResultLocation.Default || resultLogFileLocation is { LocationType: ResultLocationType.Default }) {
			resultLogFileLocation.SetPublicProperty("LocationType", ResultLocationType.PrefixOut);	
		}
		
		if (inputFileLocation == FunctionInputLocation.Default || inputFileLocation is { LocationType: InputLocationType.Default }) {
			inputFileLocation.SetPublicProperty("LocationType", InputLocationType.FunctionArgs);	
		}
		
		// It may be required to read function results from different location than the default {prefix}.out:
		if (resultOutFileLocation.LocationType == ResultLocationType.CustomPath) {
			if (resultOutFileLocation.CustomPath is null || string.IsNullOrWhiteSpace(resultOutFileLocation.CustomPath))
				throw new ArgumentException("CustomPath can't be empty when using ResultLocationType.WriteToCustomPath", nameof(resultOutFileLocation));
		}
		
		// It may be required to pass custom file to function stdin instead of default FunctionArgs only:
		if (inputFileLocation.LocationType == InputLocationType.TakeFromCustomPath) {
			if (inputFileLocation.CustomPath is null || string.IsNullOrWhiteSpace(inputFileLocation.CustomPath))
				throw new ArgumentException("CustomPath can't be empty when using InputLocationType.TakeFromCustomPath", nameof(inputFileLocation));
		}
	}
	
	#endregion Private constructors
	#region Factory methods

	/// <summary>
	/// Default options: <c>1&gt;{prefix}.out 2&gt;{prefix}.err</c>, reading the result from {prefix}.out.
	/// </summary>
	public static CallOptions CreateFactoryDefault => WithStreamRedirection(StreamRedirectionType.RedirectToFiles);

	///  <summary>
	///  Preferred  factory method when using non-default stream redirection.
	///  Configures input and output file redirection defaults as expected
	///  (based on StreamRedirectionType)
	///  Configure result file locations to match stream redirections.
	///  </summary>
	///  <param name="streamRedirectionType">StreamRedirectionType</param>
	///  <param name="inputFileIn">Optionally override FunctionInputLocation</param>
	///  <param name="resultFileOut">Optionally override FunctionResultLocation</param>
	///  <param name="resultFileErr">Optionally override FunctionResultLocation</param>
	///  <param name="resultFileLog">Optionally override FunctionResultLocation</param>
	///  <returns>Options whose result locations already match the streams the chosen redirection writes.</returns>
	public static CallOptions WithStreamRedirection(
		StreamRedirectionType streamRedirectionType,
		FunctionInputLocation? inputFileIn = null,
		FunctionResultLocation? resultFileOut = null,
		FunctionResultLocation? resultFileErr = null,
		FunctionResultLocation? resultFileLog = null)
	{
		switch (streamRedirectionType) {
			case StreamRedirectionType.RedirectToFiles: //  1> {prefix}.out 2> {prefix}.err"
				inputFileIn ??= FunctionInputLocation.FunctionArgs;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.DiscardAllToDevNull: //  &> /dev/null"
				inputFileIn ??= FunctionInputLocation.FunctionArgs;
				resultFileOut ??= FunctionResultLocation.None;
				resultFileErr ??= FunctionResultLocation.None;
				resultFileLog ??= FunctionResultLocation.None;
				break;
			case StreamRedirectionType.UseOutDiscardErr: //  1> {prefix}.out 2> /dev/null"
				inputFileIn ??= FunctionInputLocation.FunctionArgs;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.None;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.UseErrDiscardOut: //  2> {prefix}.err 1> /dev/null"
				inputFileIn ??= FunctionInputLocation.FunctionArgs;
				resultFileOut ??= FunctionResultLocation.None;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.RedirectBothToOut: //  1> {prefix}.out 2> &1"
				inputFileIn ??= FunctionInputLocation.FunctionArgs;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.None;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.RedirectBothToErr: //  "2> {prefix}.err 1> &2"
				inputFileIn ??= FunctionInputLocation.FunctionArgs;
				resultFileOut ??= FunctionResultLocation.None;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.SubstituteInWriteBoth: //  "$(< {prefix}.in) 1> {prefix}.out 2> {prefix}.err"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.SubstituteInDiscardBoth: //  "$(< {prefix}.in) &> /dev/null"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.None;
				resultFileErr ??= FunctionResultLocation.None;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.SubstituteInUseOutDiscardErr: //  "$(< {prefix}.in) 1> {prefix}.out 2> /dev/null"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.None;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.SubstituteInUseErrDiscardOut: //  "$(< {prefix}.in) 2> {prefix}.err 1> /dev/null"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.None;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.SubstituteQuotedInWriteBoth: //  "\"$(< {prefix}.in)\" 1> {prefix}.out 2> {prefix}.err"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.SubstituteQuotedInDiscardBoth: //  "\"$(< {prefix}.in)\" &> /dev/null"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.None;
				resultFileErr ??= FunctionResultLocation.None;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.SubstituteQuotedInUseOutDiscardErr: //  "\"$(< {prefix}.in)\" 1> {prefix}.out 2> /dev/null"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.None;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.SubstituteQuotedInUseErrDiscardOut: //  "\"$(< {prefix}.in)\" 2> {prefix}.err 1> /dev/null"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.None;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.TakeInWriteBoth: //  "< {prefix}.in 1> {prefix}.out 2> {prefix}.err"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.TakeInDiscardBoth: //  "< {prefix}.in &> /dev/null"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.TakeInUseOutDiscardErr: //  "< {prefix}.in 1> {prefix}.out 2> /dev/null"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.None;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			case StreamRedirectionType.TakeInUseErrDiscardOut: //  "< {prefix}.in 2> {prefix}.err 1> /dev/null"
				inputFileIn ??= FunctionInputLocation.TakePrefixIn;
				resultFileOut ??= FunctionResultLocation.None;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
			default:  // RedirectToFiles
				inputFileIn ??= FunctionInputLocation.FunctionArgs;
				resultFileOut ??= FunctionResultLocation.PrefixOut;
				resultFileErr ??= FunctionResultLocation.PrefixErr;
				resultFileLog ??= FunctionResultLocation.PrefixLog;
				break;
		}

		string streamRedirection = streamRedirectionType.ToStreamRedirection();
		return new CallOptions(inputFileIn, resultFileOut, resultFileErr, resultFileLog, streamRedirection);
	}

	///  <summary>
	///  Customize location of function results.
	///  Configure result file locations to match stream redirections.
	///  </summary>
	///  <param name="streamRedirection">Takes StreamRedirectionType as string, or any arbitrary stream redirection with custom paths or {prefix} placeholders, which are replaced with function marker string and {.in|.out|.err|.log} file extension automatically.</param>
	///  <param name="inputFileIn">Override default FunctionInputLocation</param>
	///  <param name="resultFileOut">Override default FunctionResultLocation</param>
	///  <param name="resultFileErr">Override default FunctionResultLocation</param>
	///  <param name="resultFileLog">Override default FunctionResultLocation</param>
	///  <returns>Options using the given redirection verbatim, unless the string names a <see cref="StreamRedirectionType"/>.</returns>
	///  <exception cref="ArgumentNullException">When <paramref name="streamRedirection"/> is null or blank.</exception>
	///  <exception cref="ArgumentException">When <paramref name="streamRedirection"/> contains neither &gt; nor &lt; and so is not a redirection.</exception>
	public static CallOptions WithStreamRedirection(
		string streamRedirection = "Default",
		FunctionInputLocation? inputFileIn = null,
		FunctionResultLocation? resultFileOut = null,
		FunctionResultLocation? resultFileErr = null,
		FunctionResultLocation? resultFileLog = null)
	{
		// In case the user provides StreamRedirection as StreamRedirectionType string value, convert it to the corresponding enum
		if (streamRedirection.TryParseEnum<StreamRedirectionType>(out var streamRedirectionType)) {
			// Call factory method which configures input and output files automatically based on StreamRedirectionType
			return CallOptions.WithStreamRedirection(streamRedirectionType, inputFileIn, resultFileOut, resultFileErr, resultFileLog);
		}
		return new CallOptions(
			inputFileIn ?? FunctionInputLocation.Default,
			resultFileOut ?? FunctionResultLocation.Default,
			resultFileErr ?? FunctionResultLocation.Default,
			resultFileLog ?? FunctionResultLocation.Default,
			streamRedirection);
	}

	/// <summary>
	/// Options that read nothing back: all three result locations are
	/// <see cref="FunctionResultLocation.None"/>, leaving only <see cref="FunctionResult.ExitCode"/>.
	/// </summary>
	/// <returns>Options for a call whose output is irrelevant.</returns>
	public static CallOptions WithExitCodeOnly()
		=> new CallOptions(
			FunctionInputLocation.Default,
			FunctionResultLocation.None,
			FunctionResultLocation.None,
			FunctionResultLocation.None,
			Bash.StreamRedirection.Default);
	
	/// <summary>
	/// Options reading the three result files from explicit locations. Nothing cross-checks them
	/// against <paramref name="streamRedirection"/>, so a location must name a file the redirection
	/// actually writes.
	/// </summary>
	/// <param name="resultOutLocation">Where the standard output result is read from.</param>
	/// <param name="resultErrLocation">Where the standard error result is read from.</param>
	/// <param name="resultLogLocation">Where the {prefix}.log result is read from.</param>
	/// <param name="streamRedirection">Redirection appended to the call, <c>1&gt;{prefix}.out 2&gt;{prefix}.err</c> by default.</param>
	/// <returns>Options with the given locations and redirection.</returns>
	public static CallOptions WithResultLocation(
		FunctionResultLocation resultOutLocation,
		FunctionResultLocation resultErrLocation,
		FunctionResultLocation resultLogLocation,
		string streamRedirection = Bash.StreamRedirection.Default)
		=> new CallOptions(
			FunctionInputLocation.Default,
			resultOutLocation,
			resultErrLocation,
			resultLogLocation,
			streamRedirection);
	
	/// <summary>
	/// Options feeding the call from an input file. Pair it with a [SUBSTITUTE] or [TAKE IN]
	/// redirection such as <c>&lt; {prefix}.in 1&gt;{prefix}.out 2&gt;{prefix}.err</c>, and write
	/// {prefix}.in before the function starts.
	/// </summary>
	/// <param name="inputLocation">Where the function's input is taken from.</param>
	/// <param name="streamRedirection">Redirection appended to the call, <c>1&gt;{prefix}.out 2&gt;{prefix}.err</c> by default.</param>
	/// <returns>Options with the given input location, results left at their defaults.</returns>
	public static CallOptions WithInputLocation(FunctionInputLocation inputLocation, string streamRedirection = Bash.StreamRedirection.Default)
		=> new CallOptions(inputLocation, streamRedirection);
	
	/// <summary>
	/// Options with every location and the redirection set explicitly. Nothing is inferred, so the
	/// locations must match the streams <paramref name="streamRedirection"/> writes.
	/// </summary>
	/// <param name="inputLocation">Where the function's input is taken from.</param>
	/// <param name="resultOutLocation">Where the standard output result is read from.</param>
	/// <param name="resultErrLocation">Where the standard error result is read from.</param>
	/// <param name="resultLogLocation">Where the {prefix}.log result is read from.</param>
	/// <param name="streamRedirection">Redirection appended to the call; "Default" resolves to <c>1&gt;{prefix}.out 2&gt;{prefix}.err</c>.</param>
	/// <returns>Options exactly as specified.</returns>
	public static CallOptions Custom(
		FunctionInputLocation inputLocation,
		FunctionResultLocation resultOutLocation,
		FunctionResultLocation resultErrLocation,
		FunctionResultLocation resultLogLocation,
		string streamRedirection = "Default")
		=> new CallOptions(
			inputLocation,
			resultOutLocation,
			resultErrLocation,
			resultLogLocation,
			streamRedirection);

	#endregion Factory methods
}

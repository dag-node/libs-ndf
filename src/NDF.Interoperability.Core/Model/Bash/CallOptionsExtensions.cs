namespace NDF.Interoperability.Model.Bash;

public static class CallOptionsExtensions
{
	public static bool HasInFile(this CallOptions options)
	{
		var location = options.InputFileLocation;
		if (location.CustomPath is not null) return true;
		return location is { LocationType:
			InputLocationType.Default or
			InputLocationType.TakePrefixIn or
			InputLocationType.TakePrefixLog};
	}
	
	public static bool HasOutFile(this CallOptions options)
	{
		var location = options.ResultOutFileLocation;
		if (location.CustomPath is not null) return true;
		return location is { LocationType: ResultLocationType.Default or ResultLocationType.PrefixOut};
	}
	
	public static bool HasErrFile(this CallOptions options)
	{
		var location = options.ResultErrFileLocation;
		if (location.CustomPath is not null) return true;
		return location is { LocationType: ResultLocationType.Default or ResultLocationType.PrefixErr };
	}

	public static bool HasLogFile(this CallOptions options)
	{
		var location = options.ResultLogFileLocation;
		if (location.CustomPath is not null) return true;
		return location is { LocationType: ResultLocationType.Default or ResultLocationType.PrefixLog };
	}

	public static string ToStreamRedirection(this StreamRedirectionType self)
	{
		switch (self) {
			case StreamRedirectionType.RedirectToFiles: return StreamRedirection.RedirectToFiles; //  1> {prefix}.out 2> {prefix}.err"
			case StreamRedirectionType.DiscardAllToDevNull: return StreamRedirection.DiscardAllToDevNull; //  &> /dev/null"
			case StreamRedirectionType.UseOutDiscardErr: return StreamRedirection.UseOutDiscardErr; //  1> {prefix}.out 2> /dev/null"
			case StreamRedirectionType.UseErrDiscardOut: return StreamRedirection.UseErrDiscardOut; //  2> {prefix}.err 1> /dev/null"
			case StreamRedirectionType.RedirectBothToOut: return StreamRedirection.RedirectBothToOut; //  1> {prefix}.out 2> &1"
			case StreamRedirectionType.RedirectBothToErr: return StreamRedirection.RedirectBothToErr; //  "2> {prefix}.err 1> &2"
			case StreamRedirectionType.SubstituteInWriteBoth: return StreamRedirection.SubstituteInWriteBoth; //  "$(< {prefix}.in) 1> {prefix}.out 2> {prefix}.err"
			case StreamRedirectionType.SubstituteInDiscardBoth: return StreamRedirection.SubstituteInDiscardBoth; //  "$(< {prefix}.in) &> /dev/null"
			case StreamRedirectionType.SubstituteInUseOutDiscardErr: return StreamRedirection.SubstituteInUseOutDiscardErr; //  "$(< {prefix}.in) 1> {prefix}.out 2> /dev/null"
			case StreamRedirectionType.SubstituteInUseErrDiscardOut: return StreamRedirection.SubstituteInUseErrDiscardOut; //  "$(< {prefix}.in) 2> {prefix}.err 1> /dev/null"
			case StreamRedirectionType.SubstituteQuotedInWriteBoth: return StreamRedirection.SubstituteQuotedInWriteBoth; //  "\"$(< {prefix}.in)\" 1> {prefix}.out 2> {prefix}.err"
			case StreamRedirectionType.SubstituteQuotedInDiscardBoth: return StreamRedirection.SubstituteQuotedInDiscardBoth; //  "\"$(< {prefix}.in)\" &> /dev/null"
			case StreamRedirectionType.SubstituteQuotedInUseOutDiscardErr: return StreamRedirection.SubstituteQuotedInUseOutDiscardErr; //  "\"$(< {prefix}.in)\" 1> {prefix}.out 2> /dev/null"
			case StreamRedirectionType.SubstituteQuotedInUseErrDiscardOut: return StreamRedirection.SubstituteQuotedInUseErrDiscardOut; //  "\"$(< {prefix}.in)\" 2> {prefix}.err 1> /dev/null"
			case StreamRedirectionType.TakeInWriteBoth: return StreamRedirection.TakeInWriteBoth; //  "< {prefix}.in 1> {prefix}.out 2> {prefix}.err"
			case StreamRedirectionType.TakeInDiscardBoth: return StreamRedirection.TakeInDiscardBoth; //  "< {prefix}.in &> /dev/null"
			case StreamRedirectionType.TakeInUseOutDiscardErr: return StreamRedirection.TakeInUseOutDiscardErr; //  "< {prefix}.in 1> {prefix}.out 2> /dev/null"
			case StreamRedirectionType.TakeInUseErrDiscardOut: return StreamRedirection.TakeInUseErrDiscardOut; //  "< {prefix}.in 2> {prefix}.err 1> /dev/null"
			default: return StreamRedirection.Default; // RedirectToFiles
		}
	}
}

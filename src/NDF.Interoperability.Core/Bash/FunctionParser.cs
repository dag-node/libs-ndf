using System.Globalization;
using NDF.Interoperability.Model;
using NDF.Interoperability.Model.Bash;

namespace NDF.Interoperability.Bash;

public class FunctionParser
{
	// ___END_FN__ {startTimeNs} {endTimeNs} {duration} {functionMarkerTag} {exitCode}
	// ___END_FN__ 1673638457000000000 1673638458000000000 00:00:01.000000 normalized_function_name-B0Ab-1 0
	// [    11   ] [       19        ] [       19        ] [      15     ] [   arbitrary str no spaces   ] [arbitrary int]
	public static readonly string FUNCTION_END_MARKER = "___END_FN__"; // 11
	private static readonly int MIN_FUNCTION_END_LINE_LENGTH = 71; // 64 + 6 + 1

	private static readonly int FUNCTION_END_MARKER_LENGTH = 11; // FUNCTION_END_MARKER.Length
	private static readonly int START_TIME_NS_START = 12; // FUNCTION_END_MARKER_LENGTH + 1
	private static readonly int START_TIME_NS_END = 31; // START_TIME_NS_START + 19
	private static readonly int END_TIME_NS_START = 32; //START_TIME_NS_END + 1
	private static readonly int END_TIME_NS_END = 51; // END_TIME_NS_START + 19
	private static readonly int DURATION_TIMESPAN_START = 52; // END_TIME_NS_END + 1
	private static readonly int DURATION_TIMESPAN_END = 67; // DURATION_TIMESPAN_START + 15
	private static readonly int FUNCTION_MARKER_AND_EXIT_CODE_START = 68; // DURATION_TIMESPAN_END + 1
	
	public static readonly DateTime UNIX_EPOCH_START = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	
	/// <summary>
	/// Recognize function end markers ___END_FN__ in standard output.
	/// Parse components of the function end marker line.
	/// </summary>
	/// <param name="streamOutputLine">Standard output of the bash process executing functions.</param>
	/// <returns>Components of the function end marker line</returns>
	/// <exception cref="InteroperabilityException">When ___END_FN__ line is invalid.</exception>
	public static bool TryParseFunctionResultMetadata(string streamOutputLine, out FunctionResultMetadata? functionResultMetadata)
	{
		functionResultMetadata = null;
		
		// Skip non ___END_FN__ lines 1.
		if (string.IsNullOrWhiteSpace(streamOutputLine) ||
		    streamOutputLine.Length < MIN_FUNCTION_END_LINE_LENGTH) return false;
		
		// Convert the string to a ReadOnlySpan<char> for efficient slicing
		ReadOnlySpan<char> lineSpan = streamOutputLine.AsSpan();
		// Extract components using known positions and delimiters
		ReadOnlySpan<char> endFnMarkerSpan = lineSpan.Slice(0, FUNCTION_END_MARKER_LENGTH); // Length of "___END_FN__"
		
		// Skip non ___END_FN__ lines 2.
		bool hasFunctionEndMarker = FUNCTION_END_MARKER == endFnMarkerSpan.ToString();
		if (!hasFunctionEndMarker) return false;
		
		// Parse components of ___END_FN__ line...
		ReadOnlySpan<char> startTimeNsSpan = lineSpan.Slice(START_TIME_NS_START, 19); // Start position + length of nanoseconds
		if (!long.TryParse(startTimeNsSpan, out long startTimeNs)) throw new InteroperabilityException("Invalid function end marker start time ns");
		
		ReadOnlySpan<char> endTimeNsSpan = lineSpan.Slice(END_TIME_NS_START, 19); // Start position + length of nanoseconds
		if (!long.TryParse(endTimeNsSpan, out long endTimeNs)) throw new InteroperabilityException("Invalid function end marker end time ns");
		
		// Convert nanoseconds since epoch to UTC DateTime
		DateTime startTimeUtc = UNIX_EPOCH_START.AddTicks(startTimeNs / 100); // 1 tick = 100 nanoseconds
		DateTime endTimeUtc = UNIX_EPOCH_START.AddTicks(endTimeNs / 100);
		
		ReadOnlySpan<char> durationTimeSpanSpan = lineSpan.Slice(DURATION_TIMESPAN_START, 15); // Start position + length of timespan
		if (!TimeSpan.TryParseExact(durationTimeSpanSpan, @"hh\:mm\:ss\.ffffff",
			    CultureInfo.InvariantCulture, out TimeSpan duration))
			throw new InteroperabilityException("Invalid function end marker duration");
		
		// Function marker and exit code are arbitrary length strings without spaces separated by a single space
		ReadOnlySpan<char> functionMarkerAndExitCodeSpan = lineSpan.Slice(FUNCTION_MARKER_AND_EXIT_CODE_START, lineSpan.Length - FUNCTION_MARKER_AND_EXIT_CODE_START);
		int separatorIndex = functionMarkerAndExitCodeSpan.IndexOf(' '); // Find the space separator
		if (separatorIndex == -1) throw new InteroperabilityException($"Invalid function end marker or missing exit code");
		// Extract functionMarker and exitCode
		string functionMarkerTag = functionMarkerAndExitCodeSpan.Slice(0, separatorIndex).ToString();
		ReadOnlySpan<char> exitCodeSpan = functionMarkerAndExitCodeSpan.Slice(separatorIndex + 1);
		if (!int.TryParse(exitCodeSpan, out int exitCode)) throw new InteroperabilityException("Invalid function end marker exit code");

		functionResultMetadata = new FunctionResultMetadata(
			startTimeUtc,
			endTimeUtc,
			duration,
			functionMarkerTag,
			exitCode);
		return true;
	}

	public const string SOURCING_END_MARKER = "___END_SOURCE_FN__";
	public const string ERROR_IN_FUNCTION = "ERROR_IN_FUNCTION";
	public const string SOURCED_SUCCESSFULLY = "SOURCED_SUCCESSFULLY";
	public const string SOURCING_FAILED = "SOURCING_FAILED";
	private const int MIN_END_SOURCE_FN_LINE_LENGTH = 36;
	private const int END_SOURCE_FN_MARKER_LENGTH = 18;
	
	public static string? ReadSourcingResultAsync(string sourcingResultLine) {
		// {FunctionParser.SOURCING_END_MARKER} {FunctionParser.SOURCED_SUCCESSFULLY} {functionName}
		// {FunctionParser.SOURCING_END_MARKER} {FunctionParser.SOURCED_SUCCESSFULLY} {scriptFilePath}
		// Skip non ___END_SOURCE_FN__ lines 1.
		if (string.IsNullOrWhiteSpace(sourcingResultLine) ||
		    sourcingResultLine.Length < MIN_END_SOURCE_FN_LINE_LENGTH) return null;
		
		// Convert the string to a ReadOnlySpan<char> for efficient slicing
		ReadOnlySpan<char> lineSpan = sourcingResultLine.AsSpan();
		// Extract components using known positions and delimiters
		ReadOnlySpan<char> endFnMarkerSpan = lineSpan.Slice(0, END_SOURCE_FN_MARKER_LENGTH); // Length of "___END_SOURCE_FN__"
		
		// Skip non ___END_FN__ lines 2.
		bool hasFunctionEndMarker = SOURCING_END_MARKER == endFnMarkerSpan.ToString();
		if (!hasFunctionEndMarker) return null;
		
		var split = sourcingResultLine.Split(' ');
		if (split.Length != 3) throw new InteroperabilityException($"Invalid sourcing result line: {sourcingResultLine}");
		var sourcedObject = split[1];
		var sourcingResult = split[2];
		string message = $"{sourcingResult}: '{sourcedObject}'";
		if (sourcingResult != FunctionParser.SOURCED_SUCCESSFULLY) throw new InteroperabilityException(message);
		return message;
	}
}

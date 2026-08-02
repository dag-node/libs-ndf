using System.Globalization;
using DagNode.NDF.Interoperability.Model;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Bash;

public class FunctionParser
{
	// ___END_FN__ {startTimeNs} {endTimeNs} {duration} {functionMarkerTag} {exitCode}
	// ___END_FN__ 1673638457000000000 1673638458000000000 00:00:01.000000 normalized_function_name-B0Ab-1 0
	// [    11   ] [       19        ] [       19        ] [      15     ] [   arbitrary str no spaces   ] [arbitrary int]
	public static readonly string FUNCTION_END_MARKER = "___END_FN__"; // 11
	private const int MIN_FUNCTION_END_LINE_LENGTH = 71; // 64 + 6 + 1

	// Each offset derives from the one above it, so the table stays consistent with the layout comment.
	private const int FUNCTION_END_MARKER_LENGTH = 11; // FUNCTION_END_MARKER.Length
	private const int TIME_NS_LENGTH = 19;
	private const int DURATION_TIMESPAN_LENGTH = 15;
	private const int START_TIME_NS_START = FUNCTION_END_MARKER_LENGTH + 1; // 12
	private const int START_TIME_NS_END = START_TIME_NS_START + TIME_NS_LENGTH; // 31
	private const int END_TIME_NS_START = START_TIME_NS_END + 1; // 32
	private const int END_TIME_NS_END = END_TIME_NS_START + TIME_NS_LENGTH; // 51
	private const int DURATION_TIMESPAN_START = END_TIME_NS_END + 1; // 52
	private const int DURATION_TIMESPAN_END = DURATION_TIMESPAN_START + DURATION_TIMESPAN_LENGTH; // 67
	private const int FUNCTION_MARKER_AND_EXIT_CODE_START = DURATION_TIMESPAN_END + 1; // 68
	
	public static readonly DateTime UNIX_EPOCH_START = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	
	/// <summary>
	/// Recognize function end markers ___END_FN__ in standard output.
	/// Parse components of the function end marker line.
	/// </summary>
	/// <param name="streamOutputLine">Standard output of the bash process executing functions.</param>
	/// <param name="functionResultMetadata">Parsed marker components when the line is a valid ___END_FN__ marker; otherwise null.</param>
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
		ReadOnlySpan<char> startTimeNsSpan = lineSpan.Slice(START_TIME_NS_START, TIME_NS_LENGTH); // Start position + length of nanoseconds
		if (!long.TryParse(startTimeNsSpan, out long startTimeNs)) throw new InteroperabilityException("Invalid function end marker start time ns");
		
		ReadOnlySpan<char> endTimeNsSpan = lineSpan.Slice(END_TIME_NS_START, TIME_NS_LENGTH); // Start position + length of nanoseconds
		if (!long.TryParse(endTimeNsSpan, out long endTimeNs)) throw new InteroperabilityException("Invalid function end marker end time ns");
		
		// Convert nanoseconds since epoch to UTC DateTime
		DateTime startTimeUtc = UNIX_EPOCH_START.AddTicks(startTimeNs / 100); // 1 tick = 100 nanoseconds
		DateTime endTimeUtc = UNIX_EPOCH_START.AddTicks(endTimeNs / 100);
		
		ReadOnlySpan<char> durationTimeSpanSpan = lineSpan.Slice(DURATION_TIMESPAN_START, DURATION_TIMESPAN_LENGTH); // Start position + length of timespan
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
		// {SOURCING_END_MARKER} {functionName} {SOURCED_SUCCESSFULLY}
		// {SOURCING_END_MARKER} {scriptFilePath} {SOURCED_SUCCESSFULLY}
		// The sourced object is whatever lies between the two, so a path holding spaces still parses.
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
		
		int resultStart = sourcingResultLine.LastIndexOf(' ') + 1;
		int objectStart = END_SOURCE_FN_MARKER_LENGTH + 1;
		if (resultStart <= objectStart) throw new InteroperabilityException($"Invalid sourcing result line: {sourcingResultLine}");
		var sourcedObject = sourcingResultLine.Substring(objectStart, resultStart - 1 - objectStart);
		var sourcingResult = sourcingResultLine.Substring(resultStart);
		string message = $"{sourcingResult}: '{sourcedObject}'";
		if (sourcingResult != FunctionParser.SOURCED_SUCCESSFULLY) throw new InteroperabilityException(message);
		return message;
	}
}

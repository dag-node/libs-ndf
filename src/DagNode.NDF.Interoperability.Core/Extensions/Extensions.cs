using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DagNode.NDF.Interoperability;

#region Delegates

/// <summary>
/// Enables asynchronous operations with a specific return type.
/// </summary>
/// <typeparam name="TSender">Represents the sender of the event, usually the object triggering the event.</typeparam>
/// <typeparam name="TEventArgs">Represents the event arguments derived from EventArgs.</typeparam>
/// <typeparam name="TResult">Represents the type of result that will be returned within the Task.</typeparam>
public delegate Task<TResult> AsyncTypedEventHandler<in TSender, in TEventArgs, TResult>(
	TSender sender, TEventArgs args) where TEventArgs : EventArgs;

/// <summary>
/// Enables asynchronous operations with a specific return type.
/// </summary>
/// <typeparam name="TSender">Represents the sender of the event, usually the object triggering the event.</typeparam>
/// <typeparam name="TEventArgs">Represents the event arguments derived from EventArgs.</typeparam>
public delegate Task AsyncTypedEventHandler<in TSender, in TEventArgs>(
	TSender sender, TEventArgs args) where TEventArgs : EventArgs;

#endregion Delegates

public static class Extensions
{
	#region Enumerations

	public static T ParseEnum<T>(this string value, bool ignoreCase = true) where T : struct, IConvertible
	{
		if (Enum.TryParse<T>(value, ignoreCase, out var result)) return result;
		throw new InvalidOperationException($"Unknown enum value '{value}'");
	}
	
	public static bool TryParseEnum<T>(this string value, out T result) where T : struct, IConvertible
	{
		try {
			if (Enum.TryParse<T>(value, out result)) return true;
		} catch (Exception) {
			result = default;
		}
		return false;
	}

	public static bool IsValidEnumValue(this Type enumType, string value)
	{
		if (enumType is not { IsEnum: true }) {
			throw new ArgumentException("Provided type is not an enum", nameof(enumType));
		}

		// Check if the string value is a valid enum value for the given enum type
		return Enum.GetNames(enumType).Contains(value);
	}

	#endregion Enumeration
	# region Text
	
	private static readonly Regex s_whiteSpaceRegex = new (@"\s+", RegexOptions.Compiled);
	public static string NoWhitespace(this string input, char replacement = '_')
		=> s_whiteSpaceRegex.Replace(input, replacement.ToString());
	public static string ToAsciiNoWhitespace(this string fileName)
		=> fileName.ToAscii('_').NoWhitespace('_');
	public static string ToAscii(this string input, char replacement = '_')
	{
		if (string.IsNullOrEmpty(input)) return input;
		// Normalize the string to decompose characters with accents
		var normalized = input.Normalize(NormalizationForm.FormD);
		// Use StringBuilder to construct the result string
		var sb = new StringBuilder(input.Length);
		foreach (var ch in normalized) {
			// Get the Unicode category for the character
			var unicodeCategory = char.GetUnicodeCategory(ch);
			// If it's a non-spacing mark (like accent marks), we skip it
			if (unicodeCategory == UnicodeCategory.NonSpacingMark) continue;
			// Add the character to the result
			sb.Append(ch);
		}
		// Convert the string to a plain ASCII string (i.e., remove any remaining non-ASCII chars)
		var asciiString = sb.ToString();
		return RemoveNonAsciiCharacters(asciiString, replacement);
	}

	private static string RemoveNonAsciiCharacters(string input, char replacement = '-')
	{
		var sb = new StringBuilder();
		foreach (var ch in input) {
			// Only keep characters that are within the ASCII range
			if (ch <= 127) {
				sb.Append(ch);
			} else {
				// You can either skip non-ASCII chars or replace them with a placeholder
				// For instance, replacing them with a space or an underscore
				sb.Append(replacement);
			}
		}
		return sb.ToString();
	}
	
	private static readonly Regex s_messageTemplateRegex = new (@"{(\w+)}", RegexOptions.Compiled);
	
	/// <summary>
	/// Replace named placeholders with corresponding values from args.
	/// </summary>
	/// <param name="messageTemplate"></param>
	/// <param name="args"></param>
	/// <returns></returns>
	public static string FormatWithNamedArgs(this string messageTemplate, params object[] args)
	{
		var namedArgs = new Dictionary<string, string>();
		for (int i = 0; i < args.Length; i++) {
			namedArgs[$"Arg{i}"] = args[i].ToString() ?? string.Empty;
		}
		return s_messageTemplateRegex.Replace(messageTemplate, match
			=> namedArgs.TryGetValue(match.Groups[1].Value, out string? arg) ? arg : match.Value);
	}
	
	/// <summary>
	/// Replaces all consecutive spaces with a single space.
	/// </summary>
	public static string ToSingleSpaced(this string input) =>
		s_singleWhiteSpaceRegex.Replace(input, " ");
	private static readonly Regex s_singleWhiteSpaceRegex = new (@"[ ]+", RegexOptions.Compiled);

	/// <summary>
	/// Replaces a mix of consecutive semicolons or whitespace with a single semicolon wrapped in spaces.
	/// </summary>
	public static string AddInlineSemicolons(this string input)
	{
		var sb = new StringBuilder(input.Length);
		foreach (var line in input.SplitToLines()) {
			var trimmedLine = line.TrimEnd();
			var lastLineChar = trimmedLine[^1];
			if (lastLineChar is '{' or '}') {
				sb.Append(trimmedLine).Append(' ');
			} else {
				var withoutSemicolons = trimmedLine.TrimEnd(';', ' ');
				sb.Append(withoutSemicolons).Append(';').Append(' ');
			}
		}
		return sb.ToString().TrimEnd();
	}
	//s_replaceConsecutiveSemicolonsRegex.Replace(input, " ; ");
	//private static readonly Regex s_replaceConsecutiveSemicolonsRegex = new (@"[\s;]+(;{2,})[\s;]*", RegexOptions.Compiled);
	
	/// <summary>
	/// Removes comments from a multiline string. 
	/// Removes single-line comments (//...) and multi-line comments (/*...*/).
	/// </summary>
	public static string XxxRemoveCSharpCommentsNotTested(this string input) =>
		s_removeCSharpCommentsRegex.Replace(input, string.Empty).Trim();
	private static readonly Regex s_removeCSharpCommentsRegex = new(@"(//.*?$)|(/\*.*?\*/)",
		RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

	/// <summary>
	/// Remove lines with only whitespace and a comment or everything after '#' on each line.
	/// Also removes whitespace only lines.
	/// </summary>
	public static string RemoveBashComments(this string input)
	{
		var lines = input.SplitToLines();
		for (int i = 0; i < lines.Count; i++) {
			lines[i] = s_removeBashComments.Replace(lines[i], string.Empty).Trim();
		}
		return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
	}
	private static readonly Regex s_removeBashComments = new (@"^\s*#.*$|#.*$", RegexOptions.Compiled);
	
	/// <summary>
	/// Splits the string into multiple lines based on a given regex pattern, ensuring that
	/// the new line is inserted only at the end of the line and not inside strings or regexes.
	/// </summary>
	public static List<string> SplitToLines(this string input)
	{
		var result = new List<string>();
		int lastIndex = 0;
		// Split by the regex, but ensure that the new lines are only added at correct positions
		foreach (Match match in s_splitToLinesRegex.Matches(input)) {
			string line = input.Substring(lastIndex, match.Index - lastIndex);
			result.Add(line);
			lastIndex = match.Index;
		}
		// Add the last segment after the final match
		if (lastIndex < input.Length) {
			result.Add(input[lastIndex..]);
		}
		return result;
	}
	// Split after actual newlines but not part of printf or similar)
	private static readonly Regex s_splitToLinesRegex = new (@"(?<=\n)", RegexOptions.Compiled); 
	
	/// <summary>
	/// Concatenates Bash multiline continuation lines (those ending with \)
	/// by replacing \ and a new line character with a single space.  
	/// </summary>
	public static string ConcatenateBashMultilineContinuations(this string input) =>
		s_concatenateBashMultilineCommandsRegex.Replace(input, " ");
	// Line continuation backslash has to be the very last character before the end of line character
	private static readonly Regex s_concatenateBashMultilineCommandsRegex = new(@"\\\n\s*", RegexOptions.Compiled | RegexOptions.Multiline);

	/// <summary>
	/// Replaces every character a terminal or a line-oriented reader would act on with its \uXXXX
	/// escape: the C0 controls (which include CR, LF, tab and the ESC that opens an ANSI sequence),
	/// DEL, the C1 controls (which include U+0085 NEL) and the Unicode line separators U+2028 and
	/// U+2029. The result therefore holds no control characters and occupies exactly one line.
	/// Returns the input instance unchanged, without allocating, when there is nothing to escape.
	/// </summary>
	/// <param name="value">Arbitrary text, typically captured from a subprocess.</param>
	/// <returns>The text with every control character escaped, or an empty string when null.</returns>
	public static string EscapeControlCharacters(this string? value)
	{
		if (string.IsNullOrEmpty(value)) return string.Empty;

		int first = IndexOfControlCharacter(value!);
		if (first < 0) return value!; // Common case: nothing to escape, keep the original instance

		var sb = new StringBuilder(value!.Length + 16);
		sb.Append(value, 0, first);
		for (int i = first; i < value.Length; i++) {
			char c = value[i];
			if (IsControlCharacter(c)) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
			else sb.Append(c);
		}
		return sb.ToString();
	}

	private static int IndexOfControlCharacter(string value)
	{
		for (int i = 0; i < value.Length; i++) {
			if (IsControlCharacter(value[i])) return i;
		}
		return -1;
	}

	private static bool IsControlCharacter(char c) =>
		c <= '\u001F'                            // C0 controls: NUL..US, includes tab, LF, CR and ESC
		|| (c >= '\u007F' && c <= '\u009F')      // DEL and the C1 controls, which include U+0085 NEL
		|| c == '\u2028' || c == '\u2029';       // LINE SEPARATOR, PARAGRAPH SEPARATOR

	/// <summary>
	/// Renders text as one log record, refusing rather than repairing. What a script writes to its
	/// streams is untrusted input once it reaches a log: the script controls its own output, and a
	/// reader downstream cannot tell which bytes the library wrote and which the script did.
	///
	/// Escaping the characters known to be dangerous is a blocklist, and Unicode is too large to
	/// enumerate confidently — bidi overrides, zero-width joiners, private-use and unassigned code
	/// points all render in ways that are hard to predict. So the test is an allowlist instead:
	/// letters, marks, numbers, punctuation, symbols and space separators, judged by
	/// <see cref="CharUnicodeInfo.GetUnicodeCategory(string, int)"/> over whole code points so that
	/// astral characters are read as themselves rather than as surrogates. Everything else — the
	/// control, format, surrogate, private-use, unassigned, line- and paragraph-separator
	/// categories — fails the test.
	///
	/// A value that fails is not escaped and not truncated; it is replaced wholesale by an ASCII
	/// marker naming the first offending code point. Nothing from the value survives into the log,
	/// so nothing is left sitting in a file waiting for the next cat. The value is composed to
	/// <see cref="NormalizationForm.FormC"/> first, so canonically equivalent spellings are judged
	/// alike; text that will not normalise is itself grounds for suppression. Canonical rather than
	/// compatibility composition, since FormKC would silently rewrite ligatures and full-width
	/// forms a script may have emitted deliberately.
	///
	/// Note what this costs: tab and newline are control characters, so tab-separated or multi-line
	/// output is suppressed in full. That is the intended trade. Callers that must keep such
	/// content should reach for <see cref="EscapeControlCharacters"/>, which preserves everything
	/// at the price of being a blocklist.
	///
	/// This is no defence against a reader that executes what it displays: shell metacharacters are
	/// ordinary punctuation and pass the allowlist, so a logged redirection still reads as one.
	/// Pair the result with a constant message template, never interpolation, so the value stays an
	/// argument rather than becoming the template.
	/// </summary>
	/// <param name="value">Arbitrary text, typically captured from a subprocess.</param>
	/// <returns>
	/// The normalised value when every code point is loggable, an empty string when null or empty,
	/// otherwise an ASCII marker describing why it was withheld.
	/// </returns>
	public static string ToLogLine(this string? value)
	{
		if (string.IsNullOrEmpty(value)) return string.Empty;

		string text;
		try {
			text = value!.IsNormalized(NormalizationForm.FormC) ? value! : value!.Normalize(NormalizationForm.FormC);
		} catch (ArgumentException) {
			// Malformed UTF-16 the allowlist would reject anyway, reported without echoing it
			return string.Format(CultureInfo.InvariantCulture,
				"[log value suppressed: {0} chars, not valid Unicode]", value!.Length);
		}

		int offender = IndexOfNonLoggable(text, out UnicodeCategory category, out int codePoint);
		if (offender < 0) return text;

		return string.Format(CultureInfo.InvariantCulture,
			"[log value suppressed: {0} chars, first disallowed U+{1:X4} ({2}) at index {3}]",
			text.Length, codePoint, category, offender);
	}

	/// <summary>
	/// Reports whether every code point falls in the categories <see cref="ToLogLine"/> admits:
	/// letters, marks, numbers, punctuation, symbols and space separators. Null and empty pass.
	/// </summary>
	/// <param name="value">Text to test.</param>
	/// <returns>True when the text can be logged verbatim.</returns>
	public static bool ContainsOnlyLoggableCharacters(this string? value) =>
		string.IsNullOrEmpty(value) || IndexOfNonLoggable(value!, out _, out _) < 0;

	/// <summary>
	/// Finds the first code point outside the loggable allowlist, reporting its category and value
	/// so a caller can say what was wrong without reproducing the text.
	/// </summary>
	/// <returns>Index of the offending code unit, or -1 when every code point is loggable.</returns>
	private static int IndexOfNonLoggable(string text, out UnicodeCategory category, out int codePoint)
	{
		for (int i = 0; i < text.Length; i++) {
			// GetUnicodeCategory(string, int) folds a valid surrogate pair into its code point and
			// reports a lone surrogate as Surrogate, which the allowlist rejects.
			category = CharUnicodeInfo.GetUnicodeCategory(text, i);
			bool isPair = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
			codePoint = isPair ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];

			if (!IsLoggableCategory(category)) return i;
			if (isPair) i++; // Consume the low surrogate of a pair already accounted for
		}
		category = UnicodeCategory.OtherNotAssigned;
		codePoint = 0;
		return -1;
	}

	/// <summary>
	/// The allowlist itself. Control, Format, Surrogate, PrivateUse, OtherNotAssigned,
	/// LineSeparator and ParagraphSeparator are absent deliberately: each either steers a terminal,
	/// breaks a record in two, or renders unpredictably across readers.
	/// </summary>
	private static bool IsLoggableCategory(UnicodeCategory category) =>
		category switch {
			UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
				UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or
				UnicodeCategory.OtherLetter => true,
			UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
				UnicodeCategory.EnclosingMark => true,
			UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or
				UnicodeCategory.OtherNumber => true,
			UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or
				UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or
				UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or
				UnicodeCategory.OtherPunctuation => true,
			UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or
				UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol => true,
			UnicodeCategory.SpaceSeparator => true,
			_ => false,
		};


	# endregion Text
	
	/// <summary>
	/// Resolves the full path to a file from the application's base directory.
	/// </summary>
	/// <param name="filename">The file name or relative path.</param>
	/// <returns>The full path to the file.</returns>
	public static string GetFullPathFromCurrentDirectory(this string filename)
	{
		if (string.IsNullOrWhiteSpace(filename)) {
			throw new ArgumentException("Filename cannot be null or empty.", nameof(filename));
		}
		string appDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? string.Empty;
		return Path.GetFullPath(Path.Combine(appDirectory, filename));
	}
	
	/// <summary>
	/// Compute SHA1 hash of the string.
	/// </summary>
	public static string ToSha1Hash(this string str)
	{
		using var sha1 = SHA1.Create();
		byte[] inputBytes = Encoding.UTF8.GetBytes(str); // Convert string to byte array
		byte[] hashBytes = sha1.ComputeHash(inputBytes); // Compute hash
		return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant(); // Convert to hex
	}
	
	#region ToSyslogMessage

	/// <summary>
	/// Formats message in syslog suitable format.
	/// Prepends local date timestamp, program name and PID to the message.
	/// A memory-friendly replacement to $"{DateTime.Now:MMM d HH:mm:ss} {programName}[{processId}]: {message}"
	/// </summary>
	/// <param name="message"></param>
	/// <param name="messageType"></param>
	/// <param name="programName"></param>
	/// <param name="processId"></param>
	/// <returns></returns>
	public static string ToSyslogMessage(this string message, int processId = 0, string messageType = "[DBG]", string programName = "bash")
	{
		var now = DateTime.Now;
		var timestamp = $"{now:MMM} {now.Day,2} {now:HH:mm:ss}";
		var pidStr = processId.ToString();
		var spanLength = timestamp.Length + programName.Length + pidStr.Length + messageType.Length + message.Length + 6;
		var logMessage = string.Create(spanLength, (timestamp, programName, pidStr, messageType, message), (span, state) => {
			(string ts, string prog, string pid, string msgType, string msg) = state;
			ts.AsSpan().CopyTo(span);
			var pos = ts.Length;
			span[pos++] = ' ';
			prog.AsSpan().CopyTo(span[pos..]);
			pos += prog.Length;
			span[pos++] = '[';
			pid.AsSpan().CopyTo(span[pos..]);
			pos += pid.Length;
			span[pos++] = ']';
			span[pos++] = ':';
			span[pos++] = ' ';
			msgType.AsSpan().CopyTo(span[pos..]);
			pos += msgType.Length;
			span[pos++] = ' ';
			msg.AsSpan().CopyTo(span[pos..]);
		});
		return logMessage;
	}
	
	#endregion ToSyslogMessage
}

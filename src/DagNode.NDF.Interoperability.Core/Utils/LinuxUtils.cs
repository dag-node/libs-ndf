using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using DagNode.NDF.Interoperability.Bash;
using DagNode.NDF.Interoperability.Model;

namespace DagNode.NDF.Interoperability;

public class LinuxUtils
{
	/// <summary>
	/// Check the UID 0 using a shell command
	/// </summary>
	/// <returns>true if user is root</returns>
	public static async Task<bool> IsUserRootAsync(CancellationTokenSource? cts = null)
	{
		try {
			cts ??= new CancellationTokenSource();
			using var process = new Process().ConfigureAsShellCommand("id", ["-u"]);
			await process.ExecuteShellCommandAsync(cts).ConfigureAwait(false);
			if (!cts.IsCancellationRequested) await process.WaitForExitAsync(cts).ConfigureAwait(false);
			string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
			return int.Parse(output.Trim()) == 0; // UID 0 means root
		} catch (Exception ex) {
			throw new InteroperabilityException(ex, $"Error checking root status");
		}
	}
	
	#region MountTmpfsBsAsync
	
	public static async Task MountTmpfsBsAsync(ILogger logger, CancellationTokenSource cts)
	{
		try {
			using var process = new Process()
				.ConfigureAsShellCommand("/usr/bin/bash", 
					[CheckAndMountTmpfsBsInline]);
			process.Start();
			// Drain stdout before awaiting exit: blocking on exit first stalls the caller's thread
			// and risks wedging the child once it fills the pipe buffer.
			await ReadResultAsync(FunctionWorkDirSettings.FunctionBasePidDir).ConfigureAwait(false);
			await process.WaitForExitAsync(cts).ConfigureAwait(false);
			async Task ReadResultAsync(string path) {
				while (!cts.IsCancellationRequested &&
				       await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } readerLine) {
					switch (readerLine) {
						case TMPFS_CREATE_SUCCESS: {
							logger.LogDebug("Using tmpfs filesystem mounted at {TmpfsBsPath}", path);
							break;
						}
						case TMPFS_CREATE_ERROR:
							logger.LogError("Error mounting tmpfs filesystem at {TmpfsBsPath}", path);
							throw new InteroperabilityException($"Error mounting tmpfs filesystem at {path}");
						default:
							logger.LogError("Unexpected error while trying to mount tmpfs filesystem at {TmpfsBsPath}", path);
							throw new InteroperabilityException($"Unexpected error while trying to mount tmpfs filesystem at {path}");
					}
				}
			}
		} catch (Exception ex) {
			throw new InteroperabilityException(ex, $"Error mounting tmpfs directory at {FunctionWorkDirSettings.FunctionBasePidDir}");
		}
	}
	
	private const string TMPFS_CREATE_SUCCESS = "TMPFS_CREATE_SUCCESS";
	private const string TMPFS_CREATE_ERROR = "TMPFS_CREATE_ERROR";
	private const string FUNCTION_CHECK_AND_MOUNT_TMPFS_BS = "___check__and__mount__tmpfs_bs";
	private static readonly string CheckAndMountTmpfsBsInline = $$$$"""-c '{{{{FUNCTION_CHECK_AND_MOUNT_TMPFS_BS}}}} { local mount_point="/tmp/tmpfsbs"; mount | grep -qE "^tmpfs on /tmp " && { [ -d "${mount_point}" ] || mkdir -p "${mount_point}" || { echo "TMPFS_CREATE_ERROR"; return 1; }; echo "TMPFS_CREATE_SUCCESS"; } || { [ -d "${mount_point}" ] || mkdir -p "${mount_point}" || { echo "TMPFS_CREATE_ERROR"; return 1; }; mount -t tmpfs -o size=64M tmpfs "${mount_point}" && echo "TMPFS_CREATE_SUCCESS" || { echo "TMPFS_CREATE_ERROR"; return 1; }; }; }}}; check_and_mount_tmpfs'""";
	
	#endregion MountTmpfsBsAsync
	
	public static readonly string[] BashShebangs = {
		"#!/bin/bash",
		"#!/usr/bin/bash",
		"#!/usr/local/bin/bash"
	};

	/// <summary>
	/// Use heuristics to check the file at filePath is actually bash script and not a binary file.
	/// </summary>
	/// <param name="filePath"></param>
	/// <returns></returns>
	/// <exception cref="ArgumentException"></exception>
	/// <exception cref="FileNotFoundException"></exception>
	/// <exception cref="InvalidOperationException"></exception>
	public static bool IsBashScript(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
			throw new ArgumentException("File path must not be null or empty", nameof(filePath));

		if (!File.Exists(filePath))
			throw new FileNotFoundException($"The file '{filePath}' does not exist");

		try {
			// Open the file and read the first few bytes
			using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
			// Define a buffer size to read only the necessary characters for the shebang
			int bufferSize = 256;
			byte[] buffer = new byte[bufferSize];
			int bytesRead = fileStream.Read(buffer, 0, bufferSize);

			// Check if the file appears to be binary (heuristic: null bytes in first 256 bytes)
			for (int i = 0; i < bytesRead; i++) {
				if (buffer[i] == 0)
					throw new InvalidOperationException($"The file '{filePath}' appears to be binary");
			}

			// Decode the buffer into a string
			string fileHeader = Encoding.UTF8.GetString(buffer, 0, bytesRead);

			// Check if the file starts with any allowed shebang
			foreach (var shebang in BashShebangs) {
				if (fileHeader.StartsWith(shebang, StringComparison.Ordinal))
					return true;
			}

			return false;
		} catch (UnauthorizedAccessException ex) {
			throw new InvalidOperationException($"Access to the file '{filePath}' is denied", ex);
		} catch (IOException ex) {
			throw new InvalidOperationException($"An error occurred while reading the file '{filePath}'", ex);
		}
	}
	
	#region Deprecated inlining

	/// <summary>
	/// Flattens a bash script to a single escaped line, for reading in a log or pasting into a
	/// <c>bash -c '...'</c> argument. Throws rather than emitting a line that would not behave as the script
	/// does: the escaping targets a <c>echo -e "..."</c> reader, and flattening drops line structure.
	/// </summary>
	/// <param name="script">Bash source restricted to what one line can express, see the exception.</param>
	/// <returns>The script as one line, with <c>"</c>, <c>\</c> and <c>$</c> escaped.</returns>
	/// <exception cref="ArgumentException">
	/// When the script contains a here-doc, a <c>case</c> statement, a string spanning lines, a residual
	/// backslash (which <c>echo -e</c> would reinterpret) or a line ending in an operator that cannot take a
	/// trailing <c>;</c>.
	/// </exception>
	[Obsolete("Sourcing carries function bodies as base64; see GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult. Retained for readable one-liners only.")]
	public static string InlineAndEscapeBashScript(string script)
	{
		var newLined = script.Replace("\r\n", "\n");
		var noComments = StripBashCommentsAndBlankLines(newLined, out char unterminatedQuote);
		if (unterminatedQuote != '\0')
			throw new ArgumentException($"Script has a {unterminatedQuote} string spanning lines, which cannot be inlined", nameof(script));
		var continuations = noComments.ConcatenateBashMultilineContinuations();
		ThrowIfNotInlinable(continuations, nameof(script));

		var escapedScript = new StringBuilder(continuations.Length);
		foreach (char c in continuations) {
			switch (c) {
				case '"':
					escapedScript.Append(@"\"""); // Escape double quotes
					break;
				case '\\':
					escapedScript.Append(@"\\"); // Escape backslashes
					break;
				case '$':
					escapedScript.Append(@"\$"); // Escape dollar signs
					break;
				default:
					escapedScript.Append(c);
					break;
			}
		}

		var compactEscapedInlineScript = escapedScript.ToString()
			.AddInlineSemicolons()
			.ToSingleSpaced();
		var finalInlineScript = compactEscapedInlineScript.Replace("&; ", "& ");
		// A trailing `&` takes no `;`, and the replacement above only reaches the ones followed by a command
		if (finalInlineScript.EndsWith("&;", StringComparison.Ordinal))
			throw new ArgumentException("Script ends in a background `&`, which cannot take a trailing `;`", nameof(script));
		return finalInlineScript;
	}

	/// <summary>
	/// Drops comments and blank lines, tracking quote state so that a <c>#</c> is read as a comment only where
	/// bash reads one - at the start of a word, outside quotes - leaving <c>${v#p}</c>, <c>$#</c> and
	/// <c>"a#b"</c> intact. Reports through <paramref name="unterminatedQuote"/> the first quote left open at
	/// the end of a line, or <c>'\0'</c> when every string closes on the line that opens it.
	/// </summary>
	private static string StripBashCommentsAndBlankLines(string script, out char unterminatedQuote)
	{
		char openQuote = '\0';
		unterminatedQuote = '\0';
		var kept = new List<string>();
		foreach (var line in script.SplitToLines()) {
			var stripped = StripBashComment(line.TrimEnd('\n'), ref openQuote).Trim();
			// A string still open here spans lines, so flattening would move a `;` inside it
			if (openQuote != '\0' && unterminatedQuote == '\0') unterminatedQuote = openQuote;
			if (!string.IsNullOrWhiteSpace(stripped)) kept.Add(stripped);
		}
		return string.Join("\n", kept);
	}

	private static string StripBashComment(string line, ref char openQuote)
	{
		for (int i = 0; i < line.Length; i++) {
			char c = line[i];
			if (openQuote == '\'') { // Single quotes take no escapes: only another quote closes them
				if (c == '\'') openQuote = '\0';
				continue;
			}
			if (c == '\\' && i + 1 < line.Length) { i++; continue; } // Escaped character, never a delimiter
			if (openQuote == '"') {
				if (c == '"') openQuote = '\0';
				continue;
			}
			if (c is '\'' or '"') { openQuote = c; continue; }
			// `#` opens a comment at the start of a word only, so after whitespace or a metacharacter
			if (c == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1]) || line[i - 1] is ';' or '&' or '|' or '(' or ')' or '<' or '>'))
				return line.Substring(0, i);
		}
		return line;
	}

	/// <summary>
	/// Throws when the script uses a construct that survives neither flattening to one line nor the
	/// <c>echo -e</c> reader the escaping targets. Fails closed: unrecognized input is rejected, never
	/// emitted as a line that differs in behaviour from the script it came from.
	/// </summary>
	private static void ThrowIfNotInlinable(string script, string paramName)
	{
		foreach (var line in script.SplitToLines()) {
			var trimmed = line.Trim();
			if (trimmed.Length == 0) continue;
			// Here-doc bodies are newline- and indentation-significant; `<<<` is a herestring and inlines fine
			int heredoc = trimmed.IndexOf("<<", StringComparison.Ordinal);
			if (heredoc >= 0 && !trimmed.Substring(heredoc).StartsWith("<<<", StringComparison.Ordinal))
				throw new ArgumentException($"Script has a here-doc, which cannot be inlined: '{trimmed}'", paramName);
			// `;;` and the `pattern)` arms of a case statement do not survive the semicolon pass
			if (trimmed.Contains(";;") || s_caseStatementRegex.IsMatch(trimmed))
				throw new ArgumentException($"Script has a case statement, which cannot be inlined: '{trimmed}'", paramName);
			// echo -e reinterprets \t, \n, \0nnn and \x in the data, so no backslash can be carried verbatim
			if (trimmed.IndexOf('\\') >= 0)
				throw new ArgumentException($"Script has a backslash escape, which `echo -e` would reinterpret: '{trimmed}'", paramName);
			// The semicolon pass appends `;` to every line, which these cannot take
			if (s_danglingOperatorRegex.IsMatch(trimmed))
				throw new ArgumentException($"Script line continues onto the next and cannot take a trailing `;`: '{trimmed}'", paramName);
		}
	}
	private static readonly Regex s_caseStatementRegex = new(@"(^|\s)(case|esac)(\s|$)", RegexOptions.Compiled);
	private static readonly Regex s_danglingOperatorRegex = new(@"(\|\|?&?|&&|(^|\s)(then|do|else|elif|in))$", RegexOptions.Compiled);

	#endregion Deprecated inlining

	public static string ValidateAndSourceBashScriptWithSourcingResult(AbsolutePath scriptFilePath) =>
		$$"""source "{{scriptFilePath}}" && echo "{{FunctionParser.SOURCING_END_MARKER}} {{scriptFilePath}} {{FunctionParser.SOURCED_SUCCESSFULLY}}" || echo "{{FunctionParser.SOURCING_END_MARKER}} {{scriptFilePath}} {{FunctionParser.SOURCING_FAILED}}" 2> /dev/null""";
}

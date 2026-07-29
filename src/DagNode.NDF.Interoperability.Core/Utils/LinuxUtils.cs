using System.Diagnostics;
using System.Text;
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
	
	public static string InlineAndEscapeBashScript(string script)
	{
		var newLined = script.Replace("\r\n", "\n");
		var noComments = newLined.RemoveBashComments();
		var continuations = noComments.ConcatenateBashMultilineContinuations();
		
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
		return finalInlineScript;
	}
	
	public static string ValidateAndSourceBashScriptWithSourcingResult(AbsolutePath scriptFilePath) =>
		$$"""source "{{scriptFilePath}}" && echo "{{FunctionParser.SOURCING_END_MARKER}} {{scriptFilePath}} {{FunctionParser.SOURCED_SUCCESSFULLY}}" || echo "{{FunctionParser.SOURCING_END_MARKER}} {{scriptFilePath}} {{FunctionParser.SOURCING_FAILED}}" 2> /dev/null""";
}

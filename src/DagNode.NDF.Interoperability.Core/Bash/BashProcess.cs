using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using DagNode.NDF.Interoperability.Model;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability.Bash;

public class BashProcess: IDisposable
{
	public static string BashPath = "/usr/bin/bash";
	public BashProcessSettings BashProcessSettings { get; }
	public Process Process { get => _process; }
	private readonly Process _process;
	private event AsyncTypedEventHandler<Process, ReadOutputStreamEventArgs> EventHandlerReadOutputStreamAsync = null!;
	private event AsyncTypedEventHandler<Process, ReadErrorStreamEventArgs> EventHandlerReadErrorStreamAsync = null!;
	public event AsyncTypedEventHandler<BashProcess, FunctionResultReadyEventArgs> EventHandlerFunctionResultReadyAsync = null!;
	public event AsyncTypedEventHandler<BashProcess, ErrorStreamReceivedEventArgs> EventHandlerErrorStreamReceivedAsync = null!;
	public event AsyncTypedEventHandler<BashProcess, FunctionPidReadyEventArgs> EventHandlerFunctionPidReadyAsync = null!;
	/// <summary>Raised when bash exits without Dispose asking it to, so a subscriber can fail pending calls
	/// instead of leaving their callers waiting on a result that can never arrive.</summary>
	public event AsyncTypedEventHandler<BashProcess, ProcessExitedEventArgs> EventHandlerProcessExitedUnexpectedlyAsync = null!;
	
	#region Interlocked properties
	public bool IsRunning { get => _IsRunning; }
	private bool _IsRunning
	{
		get => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;
		set => Interlocked.Exchange(ref _isRunning, value ? 1 : 0);
	}
	private int _isRunning;
	private readonly SemaphoreSlim _isRunningLock = new(1, 1);

	public bool IsSourcing { get => _IsSourcing; }
	private bool _IsSourcing
	{
		get => Interlocked.CompareExchange(ref _isSourcing, 0, 0) == 1;
		set => Interlocked.Exchange(ref _isSourcing, value ? 1 : 0);
	}
	private int _isSourcing;
	private readonly SemaphoreSlim _isSourcingLock = new(1, 1);
	#endregion Interlocked properties
	
	private readonly ILogger _logger;
	// The stdout/stderr readers outlive any single startup call, so they run on this process-scoped source
	// rather than the transient startup CancellationTokenSource a caller hands to StartBashAsync — that one
	// is a short-lived linked source the sourcing path disposes as soon as it returns, and binding the
	// readers to it races that disposal: the reader touches a disposed token, faults, and the bash is left
	// with no stdout reader, so its markers are never parsed and the first call hangs. Cancelled by Dispose.
	private readonly CancellationTokenSource _readerCts = new();

	public async Task CreateAsync(ILogger? logger, BashProcessSettings bashBashProcessSettings)
	{
		
	}
	public BashProcess(ILogger? logger, BashProcessSettings bashBashProcessSettings)
	{
		_logger = logger ?? LoggingFactory.CreateLogger<BashProcess>();
		BashProcessSettings = bashBashProcessSettings ?? throw new ArgumentException(nameof(bashBashProcessSettings));

		var processStartInfo = new ProcessStartInfo().ConfigureWithSettings(bashBashProcessSettings);
		_process = new Process { StartInfo = processStartInfo };
	}

	/// <summary>
	/// Start bash process and wait for exit.
	/// </summary>
	/// <param name="cts"></param>
	/// <returns>true if the bash process is starting or already started</returns>
	public async Task StartBashAsync(CancellationTokenSource cts)
	{
		if (cts == null) throw new ArgumentNullException(nameof(cts));
		// Allow only single process to start bash
		await _isRunningLock.WaitAsync(cts.Token).ConfigureAwait(false);
		try {
			if (_IsRunning) return;
			_IsRunning = true;
		} finally { _isRunningLock.Release(); }
		_IsRunning = await _process.StartAsync(cts).ConfigureAwait(false);
		if (_IsRunning && !cts.IsCancellationRequested) {
			// Handle exit event automatically when it occurs. The readers run for the whole life of the bash
			// process, so they take _readerCts (cancelled by Dispose), never the caller's transient startup
			// cts which the sourcing path disposes on return.
			var runBashProcess = Task.Run(async () => {
				try {
					EventHandlerReadOutputStreamAsync += ReadOutputStreamAsync; // Long running task
					EventHandlerReadErrorStreamAsync += ReadErrorStreamAsync; // Long running task
					await _process.WaitForExitAsync(_readerCts,
						EventHandlerReadOutputStreamAsync,
						EventHandlerReadErrorStreamAsync)
							.ConfigureAwait(false);
				} catch (Exception ex) {
					// Fire-and-forget: log rather than rethrow into an unobserved task exception (a silent
					// fault here is exactly what once left a dead reader undiagnosable).
					_logger.LogError(ex, "Bash process reader loop terminated");
				} finally {
					await NotifyIfExitedUnexpectedlyAsync().ConfigureAwait(false);
				}
			}).ConfigureAwait(false);
		} else {
			throw new InteroperabilityException("Bash process was not started");
		}
	}
	
	#region SourceBashScriptAsync

	private async Task<bool> EnsureRunningAndNotSourcingLock(CancellationTokenSource cts)
	{
		if (cts == null) throw new ArgumentNullException(nameof(cts));
		// Ensure the bash process is started
		if (!_IsRunning) await StartBashAsync(cts).ConfigureAwait(false);
		if (!_IsRunning) throw new InteroperabilityException("Unable to start bash process");
		// Allow only single process to source the script file 
		await _isSourcingLock.WaitAsync(cts.Token).ConfigureAwait(false);
		try {
			if (_IsSourcing) return false;
			_IsSourcing = true;
		} finally {
			_isSourcingLock.Release();
		}
		return true;
	}
	
	public async Task SourceMainFunctionWrapperAsync(CancellationTokenSource cts)
	{
		if (!await EnsureRunningAndNotSourcingLock(cts).ConfigureAwait(false)) {
			_logger.LogWarning("Main function wrapper is already being sourced");
			return;
		}
		try {
			string sourceCmd;
			// Setup global sub-process cleanup traps to prevent resource leakage when the main bash process is terminated
			sourceCmd = GlobalScripts.SOURCE_FUNCTION___global__on_stop; 
			await _process.StandardInput.WriteLineAsync(sourceCmd).ConfigureAwait(false);
			await _process.StandardInput.FlushAsync().ConfigureAwait(false);
			sourceCmd = GlobalScripts.RUN_SETUP___global__on_stop;
			await _process.StandardInput.WriteLineAsync(sourceCmd).ConfigureAwait(false);
			await _process.StandardInput.FlushAsync().ConfigureAwait(false);
			// We can spawn subprocesses in Bash (using &) to make functions run asynchronously in parallel,
			// source ___run_function__with__stdout_end_marker__async function wrapper to make it available for function calls:
			sourceCmd = GlobalScripts.SOURCE_FUNCTION___run_function__with__stdout_end_marker__async;
			await _process.StandardInput.WriteLineAsync(sourceCmd).ConfigureAwait(false);
			await _process.StandardInput.FlushAsync().ConfigureAwait(false);
			_IsSourcing = false;
		} catch (Exception ex) {
			throw new InteroperabilityException(ex, "Error sourcing function wrapper");
		}
	}
	
	public async Task SourceGlobalFunctionsAsync(IList<AbsolutePath> globalFunctionScripts, CancellationTokenSource cts)
	{
		if (!await EnsureRunningAndNotSourcingLock(cts).ConfigureAwait(false)) {
			_logger.LogWarning("Global functions are already being sourced");
			return;
		}
		foreach (var scriptFilePath in globalFunctionScripts) {
			try {
				// Source global function scripts
				string sourceCmd = LinuxUtils.ValidateAndSourceBashScriptWithSourcingResult(scriptFilePath);
				await _process.StandardInput.WriteLineAsync(sourceCmd).ConfigureAwait(false);
				await _process.StandardInput.FlushAsync().ConfigureAwait(false);
				_IsSourcing = false;
			} catch (Exception ex) {
				_IsSourcing = false;
				throw new InteroperabilityException(ex, $"Error sourcing {scriptFilePath}");
			}
		}
		_IsSourcing = false;
	}
	
	/// <summary>
	/// Source bash script file to make its functions available for further calls.
	/// If the script references functions from another script, it must explicitly
	/// source the referenced script files.
	/// </summary>
	/// <param name="scriptFilePath"></param>
	/// <param name="cts"></param>
	public async Task SourceScriptFileAsync(AbsolutePath scriptFilePath, CancellationTokenSource cts)
	{
		if (!await EnsureRunningAndNotSourcingLock(cts).ConfigureAwait(false)) {
			_logger.LogWarning("Script file is already being sourced");
			return;
		} 
		try {
			// Source user-provided script
			string sourceCmd = LinuxUtils.ValidateAndSourceBashScriptWithSourcingResult(scriptFilePath);
			await _process.StandardInput.WriteLineAsync(sourceCmd).ConfigureAwait(false);
			await _process.StandardInput.FlushAsync().ConfigureAwait(false);
			_IsSourcing = false;
		} catch (Exception ex) {
			_IsSourcing = false;
			throw new InteroperabilityException(ex, "Error sourcing script files");
		}
	}
	
	#endregion SourceBashScriptAsync
	#region Bash process stream handlers
	
	// Reads a redirected stream to EOF. ReadLineAsync returns null only when the far end closes — bash
	// exited — or the stream is disposed on teardown, so the reader ends there instead of polling a closed
	// pipe forever; that quiet exit is also what lets WaitForExitAsync observe an unexpected bash exit and
	// fail pending calls rather than hang. A stream torn down mid-read surfaces as IO/ObjectDisposed and is
	// treated as EOF; parsing/handler faults still propagate to WaitForExitAsync.
	private async Task ReadOutputStreamAsync(Process sender, ReadOutputStreamEventArgs args)
	{
		while (!args.CancellationToken.IsCancellationRequested) {
			string? line;
			try {
				line = await args.StandardOutput.ReadLineAsync().ConfigureAwait(false);
			} catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
				break;
			}
			if (line is null) break; // EOF: bash's stdout closed, or the stream was disposed.
			// Parse source command results
			var sourcingResult = FunctionParser.ReadSourcingResultAsync(line);
			if (sourcingResult != null) {
				_logger.LogDebug("{SourcingResult}", sourcingResult.ToLogLine());
				continue;
			}
			// Parse per-call begin markers carrying the backgrounded call's PID
			if (FunctionParser.TryParseFunctionBegin(line, out string beginMarkerTag, out int beginPid)) {
				if (EventHandlerFunctionPidReadyAsync != null) {
					await EventHandlerFunctionPidReadyAsync.Invoke(this, new FunctionPidReadyEventArgs(beginMarkerTag, beginPid)).ConfigureAwait(false);
				}
				continue;
			}
			// Parse call function results
			if (!FunctionParser.TryParseFunctionResultMetadata(line, out FunctionResultMetadata? metadata) || metadata is null) {
				// Log anything else
				_logger.LogDebug("{ScriptOutputLine}", line.ToLogLine());
				continue;
			}
			// Process function result
			await EventHandlerFunctionResultReadyAsync.Invoke(this, new FunctionResultReadyEventArgs(metadata)).ConfigureAwait(false);
		}
	}

	private async Task ReadErrorStreamAsync(Process sender, ReadErrorStreamEventArgs args)
	{
		while (!args.CancellationToken.IsCancellationRequested) {
			string? line;
			try {
				line = await args.StandardError.ReadLineAsync().ConfigureAwait(false);
			} catch (Exception ex) when (ex is IOException or ObjectDisposedException) {
				break;
			}
			if (line is null) break; // EOF: bash's stderr closed, or the stream was disposed.
			await EventHandlerErrorStreamReceivedAsync.Invoke(this, new ErrorStreamReceivedEventArgs(line)).ConfigureAwait(false);
		}
	}

	// Dispose cancels _readerCts before it stops bash, so a still-live token when the readers end means bash
	// exited on its own (crash, OOM kill, a hit resource limit), not that we asked. Surface that so pending
	// calls fail with a meaningful error instead of waiting on a result that can never arrive.
	private async Task NotifyIfExitedUnexpectedlyAsync()
	{
		if (_readerCts.IsCancellationRequested) return;
		int? exitCode = null;
		try { if (_process.HasExited) exitCode = _process.ExitCode; } catch (InvalidOperationException) { }
		_logger.LogError("Bash process exited unexpectedly (exit code {ExitCode})", exitCode?.ToString() ?? "unknown");
		if (EventHandlerProcessExitedUnexpectedlyAsync != null) {
			await EventHandlerProcessExitedUnexpectedlyAsync.Invoke(this, new ProcessExitedEventArgs(exitCode)).ConfigureAwait(false);
		}
	}

	#endregion Bash process stream handlers

	public void Dispose()
	{
		// Signal the stdout/stderr readers to stop first: their outer loop polls _readerCts, so once bash
		// exits below and its pipes reach EOF the readers fall out of their loops instead of blocking or
		// racing the stream disposal.
		if (!_readerCts.IsCancellationRequested) _readerCts.Cancel();
		// Dispose may run on a process that never started, when CreateAsync cleans up a failed startup:
		// the redirected streams and HasExited are valid only after Start, so gate on _IsRunning and let
		// the unstarted case fall through to disposing the Process object. The try guards the narrow race
		// where the process exits between the HasExited check and the write.
		if (_IsRunning) {
			try {
				// Ask bash to exit; its EXIT trap runs ___global__on_stop, reaping its own process group.
				if (!_process.HasExited) {
					_process.StandardInput.WriteLine("exit");
					_process.StandardInput.Flush();
				}
			} catch (InvalidOperationException) { }
			// Belt: if bash has not wound down within the grace, force-kill the whole tree. Process.Id is
			// the setsid wrapper, so ProcessTree walks setsid -> bash -> descendants (Process.Kill(bool)
			// is not available on netstandard2.1, and would orphan bash anyway).
			try {
				if (!_process.WaitForExit((int)BashProcessSettings.DisposeExitGracePeriod.TotalMilliseconds)) {
					ProcessTree.KillTree(_process.Id);
				}
			} catch (InvalidOperationException) { }
			_process.StandardInput.Dispose();
			_process.StandardOutput.Dispose();
			_process.StandardError.Dispose();
		}
		EventHandlerReadOutputStreamAsync -= ReadOutputStreamAsync;
		EventHandlerReadErrorStreamAsync -= ReadErrorStreamAsync;
		_process.Dispose();
		_readerCts.Dispose();
	}
}

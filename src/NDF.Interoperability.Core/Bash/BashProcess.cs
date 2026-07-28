using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using NDF.Interoperability.Model;
using NDF.Interoperability.Model.Bash;

namespace NDF.Interoperability.Bash;

public class BashProcess: IDisposable
{
	public static string BashPath = "/usr/bin/bash";
	public BashProcessSettings BashProcessSettings { get; }
	public Process Process { get => _process; }
	private readonly Process _process;
	private event AsyncTypedEventHandler<Process, ReadOutputStreamEventArgs> EventHandlerReadOutputStreamAsync;
	private event AsyncTypedEventHandler<Process, ReadErrorStreamEventArgs> EventHandlerReadErrorStreamAsync;
	public event AsyncTypedEventHandler<BashProcess, FunctionResultReadyEventArgs> EventHandlerFunctionResultReadyAsync;
	public event AsyncTypedEventHandler<BashProcess, ErrorStreamReceivedEventArgs> EventHandlerErrorStreamReceivedAsync;
	
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
			// Handle exit event automatically when it occurs
			var runBashProcess = Task.Run(async () => {
				try {
					EventHandlerReadOutputStreamAsync += ReadOutputStreamAsync; // Long running task
					EventHandlerReadErrorStreamAsync += ReadErrorStreamAsync; // Long running task
					await _process.WaitForExitAsync(cts,
						EventHandlerReadOutputStreamAsync,
						EventHandlerReadErrorStreamAsync)
							.ConfigureAwait(false);
				} catch (Exception ex) {
					throw new InteroperabilityException(ex, "Error running bash process");
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
		if (!await EnsureRunningAndNotSourcingLock(cts)) {
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
		if (!await EnsureRunningAndNotSourcingLock(cts)) {
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
		if (!await EnsureRunningAndNotSourcingLock(cts)) {
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
	
	private async Task ReadOutputStreamAsync(Process sender, ReadOutputStreamEventArgs args)
	{
		while (!args.CancellationToken.IsCancellationRequested) {
			while (await args.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line) {
				// Parse source command results
				var sourcingResult = FunctionParser.ReadSourcingResultAsync(line);
				if (sourcingResult != null) {
					_logger.LogDebug(sourcingResult);
					continue;
				}
				// Parse call function results
				if (!FunctionParser.TryParseFunctionResultMetadata(line, out FunctionResultMetadata? metadata) || metadata is null) {
					// Log anything else
					_logger.LogDebug(line);
					continue;
				}
				// Process function result
				await EventHandlerFunctionResultReadyAsync.Invoke(this, new FunctionResultReadyEventArgs(metadata));
			}
			await Task.Delay(TimeSpan.FromMilliseconds(10), args.CancellationToken).ConfigureAwait(false);
		}
	}

	private async Task ReadErrorStreamAsync(Process sender, ReadErrorStreamEventArgs args)
	{
		while (!args.CancellationToken.IsCancellationRequested) {
			while (await args.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line) {
				await EventHandlerErrorStreamReceivedAsync.Invoke(this, new ErrorStreamReceivedEventArgs(line));
			}
			await Task.Delay(TimeSpan.FromMilliseconds(200), args.CancellationToken).ConfigureAwait(false);
		}
	}
	
	#endregion Bash process stream handlers

	public void Dispose()
	{
		// Gracefully exit the bash process by sending "exit"
		if (!_process.HasExited) {
			_process.StandardInput.WriteLine("exit");
			_process.StandardInput.Flush();
		}
		_process.StandardInput.Dispose();
		EventHandlerReadOutputStreamAsync -= ReadOutputStreamAsync;
		EventHandlerReadErrorStreamAsync -= ReadErrorStreamAsync;
		_process.StandardOutput.Dispose();
		_process.StandardError.Dispose();
		_process.Dispose();
	}
}

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using DagNode.NDF.Interoperability.Bash;
using DagNode.NDF.Interoperability.Model;
using DagNode.NDF.Interoperability.Model.Bash;

namespace DagNode.NDF.Interoperability;

public static class ProcessExtensions
{
	public static ProcessStartInfo WithAllRedirectsOn(this ProcessStartInfo startInfo)
	{
		startInfo.CreateNoWindow = true;
		startInfo.UseShellExecute = false;
		startInfo.RedirectStandardInput = true;
		startInfo.RedirectStandardOutput = true; 
		startInfo.RedirectStandardError = true;
		return startInfo;
	}

	public static ProcessStartInfo ConfigureWithSettings(this ProcessStartInfo startInfo, BashProcessSettings processSettings)
	{
		startInfo.FileName = processSettings.BashPath;
		startInfo.Arguments = processSettings.ProcessArgs;
		
		// Set environment variables if provided, default empty
		foreach (var env in processSettings.ProcessEnvironmentVariables) {
			startInfo.EnvironmentVariables[env.Key] = env.Value;
		}
		startInfo.UseShellExecute = false; // Required if you want to redirect StandardOutput, StandardError, or StandardInput.
		startInfo.CreateNoWindow = true; // Prevents creating a new console window. Suitable for background processes or utilities that don't require user interaction.
		startInfo.RedirectStandardInput = true; // Write function name with args, stream redirections and unique end marker to the bash process.
		
		// We are manually redirecting stdout and stderr per every function call as configured in CallOptions.
		// "Redirects" to {prefix}.out and {prefix}.err files by default (generated automatically for every function call).
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardError = true;
		
		return startInfo;
	}

	public static Process ConfigureAsShellCommand(this Process process, string programName, string[]? programArgs)
	{
		process.StartInfo = new ProcessStartInfo {
			FileName = programName,
			Arguments = programArgs != null ? string.Join(' ', programArgs) : string.Empty,
			RedirectStandardOutput = true, // Capture process outputs programatically
			RedirectStandardError = true, // Capture process errors programatically
			UseShellExecute = false, // Required if you want to redirect StandardOutput, StandardError, or StandardInput
			CreateNoWindow = true // Prevents creating a new console window. Suitable for background processes or utilities that don't require user interaction.
		};
		return process;
	}
	
	public static async Task<string> ExecuteShellCommandAsync(this Process process, CancellationTokenSource? cts = null)
	{
		if (process == null) throw new ArgumentNullException(nameof(process));
		cts ??= new CancellationTokenSource();
		await process.StartAsync(cts).ConfigureAwait(false);
		if (!cts.IsCancellationRequested) await process.WaitForExitAsync(cts).ConfigureAwait(false);
		string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
		string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
		if (!string.IsNullOrEmpty(error)) {
			throw new InteroperabilityException($"Process exited with ExitCode: {process.ExitCode}{Environment.NewLine}{error}");
		}
		return output;
	}


	/// <summary>
	/// Waits asynchronously for the process to exit.
	/// Caller is responsible for disposing the Process object.
	/// Cancellation is supported; however, ensure that the CancellationToken
	/// is not already canceled when calling this method.
	/// Caller is responsible for unregistering stream handlers.
	/// </summary>
	/// <param name="process">The process to wait for.</param>
	/// <param name="cts">Cancellation token source.</param>
	/// <param name="eventHandlerReadOutputStreamAsync">Optional handler to process standard output stream.</param>
	/// <param name="eventHandlerReadErrorStreamAsync">Optional handler to process standard error stream.</param>s
	/// s
	public static async Task WaitForExitAsync(this Process process, CancellationTokenSource? cts = null /* infinite */,
		AsyncTypedEventHandler<Process, ReadOutputStreamEventArgs>? eventHandlerReadOutputStreamAsync = null,
		AsyncTypedEventHandler<Process, ReadErrorStreamEventArgs>? eventHandlerReadErrorStreamAsync = null)
	{
		if (process == null) throw new ArgumentNullException(nameof(process));
		cts ??= new CancellationTokenSource();
		
		var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (cts.Token.IsCancellationRequested) {
			tcs.TrySetCanceled();
			return;
		}
		await using CancellationTokenRegistration ctr = cts.Token.Register(() => {
			tcs.TrySetCanceled();
		}, useSynchronizationContext: false);
		
		process.EnableRaisingEvents = true;
		void ProcessExited(object sender, EventArgs e) => tcs.TrySetResult(process.ExitCode);
		process.Exited += ProcessExited;
		
		try {
			if (process.HasExited) {
				// Checking process.HasExited immediately after attaching ProcessExited handler
				ProcessExited(process, EventArgs.Empty);
			}

			ProcessStartInfo startInfo = process.StartInfo;
			Task? readOutputStreamAsync = null;
			Task? readErrorStreamAsync = null;
			
			// https://stackoverflow.com/questions/9533070/how-to-read-to-end-process-output-asynchronously-in-c 
			// https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=net-9.0&redirectedfrom=MSDN#System_Diagnostics_Process_WaitForExit_System_Int32_
			// C# Process RedirectStandardOutput pattern: BeginOutputReadLine + OutputDataReceived is broken
			// https://web.archive.org/web/20170106065853/https://alabaxblog.info/2013/06/redirectstandardoutput-beginoutputreadline-pattern-broken/
			
			// Offload reading of standard output to a separate task
			if (startInfo.RedirectStandardOutput && eventHandlerReadOutputStreamAsync != null) {
				readOutputStreamAsync = Task.Run(async () => {
					try {
						// Intentionally not using:
						// process.OutputDataReceived += outputDataHandler;
						// process.BeginOutputReadLine();
						// Processing standard output stream in custom async handler:
						await eventHandlerReadOutputStreamAsync.Invoke(process,
							new ReadOutputStreamEventArgs(process.StandardOutput, cts.Token))
								.ConfigureAwait(false);
					} catch (OperationCanceledException) {
					} catch (Exception ex) {
						throw new InteroperabilityException(ex, "Error reading standard output");
					}
				}, cts.Token);
				_ = readOutputStreamAsync.ConfigureAwait(false);
			}
			// Offload reading of standard error to a separate task
			if (startInfo.RedirectStandardError && eventHandlerReadErrorStreamAsync != null) {
				readErrorStreamAsync = Task.Run(async () => {
					try {
						// Intentionally not using:
						// process.ErrorDataReceived += errorDataHandler;
						// process.BeginErrorReadLine();
						// Processing standard error stream in custom async handler:
						await eventHandlerReadErrorStreamAsync.Invoke(process,
							new ReadErrorStreamEventArgs(process.StandardError, cts.Token))
								.ConfigureAwait(false);
					} catch (OperationCanceledException) {
					} catch (Exception ex) {
						throw new InteroperabilityException(ex, "Error reading standard error");
					}
				}, cts.Token);
				_ = readErrorStreamAsync.ConfigureAwait(false);
			}
			
			var tasks = new List<Task> { tcs.Task };
			if (readOutputStreamAsync != null) tasks.Add(readOutputStreamAsync);
			if (readErrorStreamAsync != null) tasks.Add(readErrorStreamAsync);
			try {
				// Await the process and both stream reading handlers
				await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(-1, cts.Token)).ConfigureAwait(false);
			} catch (InteroperabilityException) {
				cts.Cancel(); // Terminate all if there is an error from stream reading handlers
				throw; // Keep original exception
			}
		} catch (Exception ex) {
			// Ensure no unobserved exception from TaskCompletionSource
			throw new InvalidOperationException("An error occurred while running the process", ex);
		} finally {
			process.Exited -= ProcessExited;
		}
	}

	public static async Task<bool> StartAsync(this Process process, CancellationTokenSource? cts = null)
	{
		if (process == null) throw new ArgumentNullException(nameof(process));
		cts ??= new CancellationTokenSource();
		
		if (process.StartInfo == null)
			throw new InvalidOperationException("ProcessStartInfo must be set before starting the process");
		var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		void CancellationTokenHandler()
		{
			try {
				if (tcs.TrySetCanceled()) {
					try {
						if (!process.HasExited) process.Kill();
					} catch (InvalidOperationException) { }
				}
			} catch (ObjectDisposedException) { } catch (Exception ex) {
				throw new InteroperabilityException($"Failed to kill process: {ex.Message}");
			}
		}

		cts.Token.ThrowIfCancellationRequested();
		await using var ctr = cts.Token.Register(CancellationTokenHandler);

		// Start the process
		if (!process.Start()) throw new InvalidOperationException("Failed to start the process");
		await Task.Yield(); // Ensure asynchronous context is returned
		return tcs.TrySetResult(true);
	}

	public static async Task StartAsync(this Process process, ILogger logger, TimeSpan timeout)
	{
		using var cts = new CancellationTokenSource(timeout);
		await process.StartAsync(cts).TimeoutAfter(cts, timeout).ConfigureAwait(false);
	}

	public static async Task TimeoutAfter(this Task task, CancellationTokenSource cts,
		TimeSpan timeout)
	{
		if (cts == null) throw new ArgumentException(nameof(cts));
		if (timeout.TotalMilliseconds < 1.0) throw new ArgumentException(nameof(timeout));
		try {
			if (await Task.WhenAny(task, Task.Delay(timeout, cts.Token)).ConfigureAwait(false) == task)
				await task.ConfigureAwait(false);
			throw new TimeoutException($"The operation has timed out after {timeout.TotalMilliseconds}ms");
		} finally {
			cts.Cancel();
			cts.Dispose();
		}
	}

	public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, CancellationTokenSource cts,
		TimeSpan timeout)
	{
		if (cts == null) throw new ArgumentException(nameof(cts));
		if (timeout.TotalMilliseconds < 1.0) throw new ArgumentException(nameof(timeout));
		try {
			if (await Task.WhenAny(task, Task.Delay(timeout, cts.Token)) == task) return await task;
			throw new TimeoutException($"The operation has timed out after {timeout.TotalMilliseconds}ms");
		} finally {
			cts.Cancel();
			cts.Dispose();
		}
	}
}

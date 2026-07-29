using System.Text;
using Microsoft.Extensions.Logging;
using DagNode.NDF.Interoperability.Model;

namespace DagNode.NDF.Interoperability;

public interface IStreamWriter
{
	void WriteLine(string message);
	Task WriteLineAsync(string message);
	void Dispose();
	ValueTask DisposeAsync();
}

/// <summary>
/// Tee-like text writer to write messages to both underlying stream and to configured logger.
/// Logs to Console by default if no logger is provided.
/// </summary>
/// <param name="logger"></param>
/// <param name="streamWriter"></param>
public class TeeWriter(ILogger? logger, StreamWriter streamWriter) : TextWriter, IStreamWriter
{
	private readonly StreamWriter _streamWriter = streamWriter ?? throw new ArgumentNullException(nameof(streamWriter));
	private readonly ILogger _logger = logger ?? LoggingFactory.CreateLogger<TeeWriter>();

	public override Encoding Encoding => _streamWriter.Encoding;

	public override void WriteLine(string message)
	{
		// Forward message to the underlying stream
		_streamWriter.WriteLine(message);
		// Additionally log the message with ILogger
		_logger.LogDebug(message);
	}

	public override async Task WriteLineAsync(string message)
	{
		// Forward message to the underlying stream
		await _streamWriter.WriteLineAsync(message).ConfigureAwait(false);
		// Additionally log the message with ILogger
		_logger.LogDebug(message);
	}

	public override void Flush() => _streamWriter.Flush();
	public override async Task FlushAsync() => await _streamWriter.FlushAsync().ConfigureAwait(false);
	
	protected override void Dispose(bool disposing)
	{
		_streamWriter.Dispose();
		base.Dispose(disposing);
	}

	public override ValueTask DisposeAsync()
	{
		_streamWriter.DisposeAsync().ConfigureAwait(false);
		ValueTask disposeTask = base.DisposeAsync();
		disposeTask.ConfigureAwait(false);
		return disposeTask;
	}
}

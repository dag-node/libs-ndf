using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DagNode.NDF.Interoperability;

/// <summary>
/// Supplies the <see cref="ILogger"/> used by types that are constructed without one.
/// Defaults to <see cref="NullLoggerFactory"/>, so a library consumer that never opts in
/// pays nothing and gets no output; an application enables logging by calling
/// <see cref="Configure(ILoggerFactory)"/> with a factory holding its own providers.
/// </summary>
public static class LoggingFactory
{
	private static volatile ILoggerFactory s_loggerFactory = NullLoggerFactory.Instance;

	/// <summary>
	/// Sets the factory backing every subsequent <see cref="CreateLogger{T}"/> call. The caller
	/// owns the factory's lifetime and must keep it alive for as long as the loggers are used.
	/// </summary>
	/// <param name="loggerFactory">Factory carrying the application's logging providers.</param>
	/// <exception cref="ArgumentNullException">When <paramref name="loggerFactory"/> is null.</exception>
	public static void Configure(ILoggerFactory loggerFactory) =>
		s_loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

	/// <summary>
	/// Creates a logger categorised as <typeparamref name="T"/> from the configured factory.
	/// </summary>
	/// <typeparam name="T">Type whose full name becomes the log category.</typeparam>
	/// <returns>A logger writing to the configured providers, or a no-op logger before <see cref="Configure"/> is called.</returns>
	public static ILogger<T> CreateLogger<T>() => s_loggerFactory.CreateLogger<T>();
}

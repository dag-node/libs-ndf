using Microsoft.Extensions.Logging;

namespace NDF.Interoperability;

/// <summary>
/// This class ensures that if DI isn't used, a default console logger will be provided.
/// </summary>
public static class LoggingFactory
{
	private static ILoggerFactory? s_loggerFactory;

	public static void Configure(Action<ILoggingBuilder> configure)
	{
		var factory = LoggerFactory.Create(configure);
		s_loggerFactory = factory;
	}

	public static ILogger<T> CreateLogger<T>(bool isDebug = false)
	{
		s_loggerFactory ??= Microsoft.Extensions.Logging
			.LoggerFactory.Create(builder => {
				builder.AddConsole(); // Adds the console logger
				if (isDebug) builder.SetMinimumLevel(LogLevel.Debug);
			});
		return s_loggerFactory.CreateLogger<T>();
	}
}

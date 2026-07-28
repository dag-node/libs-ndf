namespace NDF.Interoperability.Model.Bash;

public interface IFunctionResultMetadata
{
	DateTime FunctionStartTimeUtc { get; }
	DateTime FunctionEndTimeUtc { get; }
	TimeSpan Duration { get; }
	string FunctionMarkerTag { get; }
	int ExitCode { get; }
}

public class FunctionResultMetadata(DateTime startTimeUtc, DateTime endTimeUTc, TimeSpan duration, string functionMarkerTag, int exitCode) : IFunctionResultMetadata
{
	public DateTime FunctionStartTimeUtc { get; } = startTimeUtc;
	public DateTime FunctionEndTimeUtc { get; } = endTimeUTc;
	public TimeSpan Duration { get; } = duration;
	public string FunctionMarkerTag { get; } = functionMarkerTag;
	public int ExitCode { get; } = exitCode;

	public override string ToString() => $"{FunctionMarkerTag} {ExitCode} in {Duration.ToReadable()}";
}

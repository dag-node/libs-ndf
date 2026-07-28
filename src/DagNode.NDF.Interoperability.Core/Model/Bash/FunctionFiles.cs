namespace DagNode.NDF.Interoperability.Model.Bash;

public class FunctionFiles
{
	public AbsolutePath ResultOut { get; set; } = null!;
	public AbsolutePath ResultErr { get; set; } = null!;
	public AbsolutePath ResultLog { get; set; } = null!;

	public AbsolutePath InputIn { get; set; } = null!;

	public static FunctionFiles ConfigureFilePaths(AbsolutePath functionWorkDir, CallOptions callOptions, string functionMarkerTag)
	{
		var functionFiles = new FunctionFiles {
			ResultLog = GetResultFilePath(functionWorkDir, functionMarkerTag, "log", callOptions.ResultLogFileLocation),
			ResultOut = GetResultFilePath(functionWorkDir, functionMarkerTag, "out", callOptions.ResultOutFileLocation),
			ResultErr = GetResultFilePath(functionWorkDir, functionMarkerTag,"err", callOptions.ResultErrFileLocation),
			InputIn = GetInputFilePath(functionWorkDir, functionMarkerTag,"in", callOptions.InputFileLocation)
		};
		
		return functionFiles;
	}
	
	private static AbsolutePath GetResultFilePath(AbsolutePath functionWorkDir, string functionMarkerTag, string fileExt,
		FunctionResultLocation functionResultLocation)
	{
		ResultLocationType locationType = functionResultLocation.LocationType;
		string resultFilePath = locationType != ResultLocationType.CustomPath
			? Path.Combine(functionWorkDir, $"{functionMarkerTag}.{fileExt}")
			: functionResultLocation.CustomPath!;
		return AbsolutePath.Create(resultFilePath);
	}
	
	private static AbsolutePath GetInputFilePath(AbsolutePath functionWorkDir, string functionMarkerTag, string fileExt,
		FunctionInputLocation functionInputLocation)
	{
		InputLocationType inputLocationType = functionInputLocation.LocationType;
		string inputFilePath = inputLocationType != InputLocationType.TakeFromCustomPath
			? Path.Combine(functionWorkDir, $"{functionMarkerTag}{fileExt}")
			: functionInputLocation.CustomPath!;
		return AbsolutePath.Create(inputFilePath);
	}
}

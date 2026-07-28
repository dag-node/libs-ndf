namespace NDF.Interoperability.Model;
public enum DirectoryType
{
	Default,
	SystemLogs,
	SystemTemp,
	SystemTempPersistent,
	AppDirectory,
	/// <summary>
	/// Mount /tmp/tmpfsbs as tmpfs,
	/// or create /tmp/tmpfsbs directory if /tmp is tmpfs already.
	/// </summary>
	TmpfsBs,
	Custom
}

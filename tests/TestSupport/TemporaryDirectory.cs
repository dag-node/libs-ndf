namespace DagNode.NDF.Interoperability.Tests;

/// <summary>
/// A directory the test owns for its lifetime, removed on dispose, so a run leaves the host as
/// it found it.
/// </summary>
public sealed class TemporaryDirectory : IDisposable
{
	public string Path { get; }

	public TemporaryDirectory(string? name = null)
	{
		Path = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			$"ndf-tests-{name ?? "t"}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path);
	}

	public string WriteFile(string fileName, string contents)
	{
		string filePath = System.IO.Path.Combine(Path, fileName);
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath)!);
		File.WriteAllText(filePath, contents);
		return filePath;
	}

	public void Dispose()
	{
		try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
		catch (IOException) { /* A directory the OS still holds is left for the temp reaper */ }
	}
}

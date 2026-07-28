using System.Collections.Concurrent;
using DagNode.NDF.Interoperability.Model;

namespace DagNode.NDF.Interoperability;

public class InMemoryFileSystem
{
	private readonly ConcurrentDictionary<AbsolutePath, string> _fileSystem = new();

	public void CreateFile(AbsolutePath path, string content)
	{
		_fileSystem.AddOrUpdate(path, content, (k, v) => content);
	}

	public string? ReadFile(AbsolutePath path)
	{
		_fileSystem.TryGetValue(path, out string value);
		return value;
	}

	public void DeleteFile(AbsolutePath path)
	{
		_fileSystem.TryRemove(path, out _);
	}

	public List<AbsolutePath> ListFiles()
	{
		return _fileSystem.Keys.ToList();
	}
}

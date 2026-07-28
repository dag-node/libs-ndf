namespace NDF.Interoperability.Model;

public sealed class AbsolutePath
{
	private string Value { get; }

	/// <summary>
	/// Creates a new absolute path from the path specified. 
	/// </summary>
	/// <param name="absolutePath"></param>
	/// <returns>Normalized absolute path</returns>
	public static AbsolutePath Create(string absolutePath) => new (absolutePath);

	/// <summary>
	/// Initializes an instance of AbsolutePath after validating the input path.
	/// </summary>
	/// <param name="path">The path to validate and normalize.</param>
	private AbsolutePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) {
			throw new ArgumentException("Path cannot be null or empty.", nameof(path));
		}
		if (!Path.IsPathRooted(path)) {
			throw new ArgumentException("Path must be an absolute path.", nameof(path));
		}
		Value = Path.GetFullPath(path); // Normalize path
	}

	// <summary>
	// Implicit conversion from string to AbsolutePath.
	// Use AbsolutePath.Create factory instead.
	// </summary>
	// <param name="path">The path to convert.</param>
	// public static implicit operator AbsolutePath(string path) => new(path);

	/// <summary>
	/// Implicit conversion from AbsolutePath to string.
	/// </summary>
	/// <param name="absolutePath">The AbsolutePath to convert.</param>
	public static implicit operator string(AbsolutePath absolutePath) => absolutePath.Value;

	public override string? ToString() => Value;
}

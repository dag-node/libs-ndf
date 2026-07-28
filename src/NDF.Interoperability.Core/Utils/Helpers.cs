using System.Security.Cryptography;
using NDF.Interoperability.Model;

namespace NDF.Interoperability;

public class Helpers
{
	private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
	private const string Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
	private const int MaxRandomValue = 248; //byte.MaxValue - (byte.MaxValue % Chars.Length);
	public static string GenerateRandomString(int length)
	{
		char[] result = new char[length];
		byte[] buffer = new byte[1]; // Small buffer for memory efficiency
		for (int i = 0; i < length; i++) {
			byte randomByte;
			// Rejection sampling to avoid modulo bias
			do {
				_rng.GetBytes(buffer);
				randomByte = buffer[0];
			}
			while (randomByte >= MaxRandomValue);
			result[i] = Chars[randomByte % Chars.Length];
		}
		return new string(result);
	}
	
	public static Dictionary<string, string>? GetSystemEnvironmentVariables(List<string>? variableNames)
	{
		if (variableNames == null || variableNames.Count == 0) return null;
		var r = new Dictionary<string, string>();
		foreach (var name in variableNames) {
			r.Add(name, Environment.GetEnvironmentVariable(name));
		}

		return r;
	}
	
	/// <summary>
	/// Compute SHA1 hash of contents of the file at filePath.
	/// </summary>
	public static string ComputeFileContentsHash(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
			throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

		if (!File.Exists(filePath))
			throw new FileNotFoundException("File does not exist.", filePath);

		using var sha1 = SHA1.Create();
		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		byte[] hash = sha1.ComputeHash(stream);
		return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
	}
	
	public static async Task<string?> ReadFileContentsAsync(AbsolutePath customPath)
	{
		try {
			if (!File.Exists(customPath)) return null;
			using var sr = new StreamReader(customPath);
			var contents = await sr.ReadToEndAsync().ConfigureAwait(false);
			return contents?.TrimEnd('\n'); // Remove trailing new line
		} catch (Exception ex) {
			throw new InteroperabilityException(ex, $"Error reading results from '{customPath}");
		}
	}
}

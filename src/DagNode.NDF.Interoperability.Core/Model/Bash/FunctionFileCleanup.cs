namespace DagNode.NDF.Interoperability.Model.Bash;

/// <summary>
/// When the library deletes the <c>{prefix}.out/.err/.log/.in</c> files a call's stream redirection
/// wrote. The contents are captured onto <see cref="FunctionResult"/> before any deletion, so cleanup
/// never loses data a caller reads from the result. A caller-supplied
/// <see cref="ResultLocationType.CustomPath"/> file is never deleted — the caller owns it. A streaming
/// call keeps the file it streams and applies the same policy when its enumerator disposes.
/// </summary>
public enum FunctionFileCleanup
{
	/// <summary>Delete an instance's <c>{prefix}</c> files when it is disposed (default), leaving them on
	/// disk and inspectable for the life of the session.</summary>
	OnDispose,

	/// <summary>Delete a call's <c>{prefix}</c> files as soon as its result is captured; a streaming call
	/// defers this until its enumerator disposes, since it reads the file after the call returns.</summary>
	AfterCall,

	/// <summary>Never delete; <c>{prefix}</c> files accumulate until the working directory is cleared
	/// externally.</summary>
	Never
}

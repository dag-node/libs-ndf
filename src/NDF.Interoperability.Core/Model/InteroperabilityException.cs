using System.Runtime.Serialization;

namespace NDF.Interoperability.Model;

/// <summary>
/// Represents a library specific error.
/// </summary>
[Serializable]
public class InteroperabilityException : Exception
{
	/// <summary>
	/// Initializes a new instance of the <see cref="InteroperabilityException"/> class.
	/// </summary>
	public InteroperabilityException()
		: base("An error occurred")
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="InteroperabilityException"/> class with a specified error message.
	/// </summary>
	/// <param name="message">The error message that explains the reason for the exception.</param>
	public InteroperabilityException(string message)
		: base(message)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="InteroperabilityException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
	/// </summary>
	/// <param name="message">The error message that explains the reason for the exception.</param>
	/// <param name="innerException">The exception that is the cause of the current exception.</param>
	public InteroperabilityException(Exception innerException, string message)
		: base(message, innerException)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="InteroperabilityException"/> class with serialized data.
	/// </summary>
	/// <param name="info">The SerializationInfo that holds the serialized object data.</param>
	/// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
	protected InteroperabilityException(SerializationInfo info, StreamingContext context)
		: base(info, context)
	{
	}
}

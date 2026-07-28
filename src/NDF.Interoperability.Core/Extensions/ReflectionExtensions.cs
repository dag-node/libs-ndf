using System.Reflection;

namespace NDF.Interoperability;

public static class ReflectionExtensions
{
	/// <summary>
	/// Gets the value of a private property by name from a given object.
	/// </summary>
	/// <typeparam name="T">The expected type of the property value.</typeparam>
	/// <param name="obj">The object instance containing the private property.</param>
	/// <param name="propertyName">The name of the private property.</param>
	/// <returns>The value of the private property, or default(T) if not found or inaccessible.</returns>
	public static T? GetPrivateProperty<T>(this object obj, string propertyName)
	{
		Type type = obj.GetType();
		PropertyInfo propertyInfo = type.GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Instance);
		if (propertyInfo == null)
			throw new ArgumentException($"Property '{propertyName}' not found on type '{type.FullName}'.");	
		object value = propertyInfo.GetValue(obj);
		return value is T result ? result : default;
	}
	
	/// <summary>
	/// Gets the value of a private field by name from a given object.
	/// </summary>
	/// <typeparam name="T">The expected type of the field value.</typeparam>
	/// <param name="obj">The object instance containing the private field.</param>
	/// <param name="fieldName">The name of the private field.</param>
	/// <returns>The value of the private field, or default(T) if not found or inaccessible.</returns>
	public static T? GetPrivateField<T>(this object obj, string fieldName)
	{
		Type type = obj.GetType();
		FieldInfo fieldInfo = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
		if (fieldInfo == null)
			throw new ArgumentException($"Field '{fieldName}' not found on type '{type.FullName}'.");
		object value = fieldInfo.GetValue(obj);
		return value is T result ? result : default;
	}
	
	public static void SetPublicProperty<T>(this object obj, string propertyName, T value)
	{
		if (obj == null)
			throw new ArgumentNullException(nameof(obj), "The target object cannot be null.");

		if (string.IsNullOrWhiteSpace(propertyName))
			throw new ArgumentException("Property name cannot be null or empty.", nameof(propertyName));

		Type type = obj.GetType();
		PropertyInfo propertyInfo = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
		if (propertyInfo == null)
			throw new ArgumentException($"Public property '{propertyName}' not found on type '{type.FullName}'.");
        
		if (!propertyInfo.CanWrite)
			throw new InvalidOperationException($"Property '{propertyName}' is read-only and cannot be set.");

		if (propertyInfo.PropertyType != typeof(T))
			throw new ArgumentException($"The value type '{typeof(T)}' does not match the property type '{propertyInfo.PropertyType}'.");

		propertyInfo.SetValue(obj, value);
	}

	public static void SetPrivateField<T>(this object obj, string fieldName, T value)
	{
		if (obj == null)
			throw new ArgumentNullException(nameof(obj), "The target object cannot be null.");

		if (string.IsNullOrWhiteSpace(fieldName))
			throw new ArgumentException("Field name cannot be null or empty.", nameof(fieldName));

		Type type = obj.GetType();
		FieldInfo fieldInfo = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
		if (fieldInfo == null)
			throw new ArgumentException($"Private field '{fieldName}' not found on type '{type.FullName}'.");

		if (fieldInfo.FieldType != typeof(T))
			throw new ArgumentException($"The value type '{typeof(T)}' does not match the field type '{fieldInfo.FieldType}'.");

		fieldInfo.SetValue(obj, value);
	}
}

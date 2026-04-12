namespace BunSharp;

/// <summary>
/// Marks a class, method, or property for JavaScript export.
/// Public members of an exported class are included by default unless they are explicitly disabled.
/// </summary>
/// <remarks>
/// The generator rejects unsupported static export shapes at compile time.
/// Static JS reference properties and static method return values that use JSObjectRef, JSFunctionRef,
/// JSArrayRef, JSArrayBufferRef, JSTypedArrayRef, or JSBufferRef report BSG010.
/// Static delegate properties and static delegate method return values report BSG011.
/// </remarks>
[
  AttributeUsage(AttributeTargets.Class |
  AttributeTargets.Method |
  AttributeTargets.Property,
  AllowMultiple = false,
  Inherited = false)
]
public sealed class JSExportAttribute : Attribute
{

  /// <summary>
  /// Creates an enabled export attribute with default naming.
  /// </summary>

  public JSExportAttribute()
  {
    Enabled = true;
  }

  /// <summary>
  /// Creates an export attribute that explicitly enables or disables export for the target member.
  /// </summary>
  public JSExportAttribute(bool enabled)
  {
    Enabled = enabled;
  }

  /// <summary>
  /// Creates an enabled export attribute and overrides the JavaScript member name.
  /// </summary>
  public JSExportAttribute(string name)
  {
    ArgumentException.ThrowIfNullOrEmpty(name);
    Enabled = true;
    Name = name;
  }

  /// <summary>
  /// Gets whether export is enabled for the annotated target.
  /// </summary>
  public bool Enabled { get; }

  /// <summary>
  /// Gets the optional JavaScript name override.
  /// </summary>
  public string? Name { get; }

  /// <summary>
  /// Requests stable JavaScript identity for supported exported properties and method return values.
  /// </summary>
  /// <remarks>
  /// Stable currently applies to exported byte[] and T[] properties and method return values.
  /// Delegate exports already use stable function-reference semantics by default.
  /// This option does not make unsupported static JS reference or static delegate export shapes valid.
  /// </remarks>
  public bool Stable { get; set; }
}
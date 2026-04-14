using System;

namespace BunSharp;

/// <summary>
/// Marks a class, constructor, method, or property for JavaScript export.
/// Public members of an exported class are included by default unless they are explicitly disabled.
/// </summary>
/// <remarks>
/// Exported <c>byte[]</c> values use dedicated binary marshalling rather than the general <c>T[]</c>
/// array mapper. JavaScript inputs must be <c>Uint8Array</c> or <c>ArrayBuffer</c>, and managed
/// <c>byte[]</c> values are surfaced back to JavaScript as <c>Uint8Array</c> rather than ordinary JS arrays.
/// The generator rejects unsupported static export shapes at compile time.
/// Static JS reference properties and static method return values that use JSObjectRef, JSFunctionRef,
/// JSArrayRef, JSArrayBufferRef, JSTypedArrayRef, or JSBufferRef report BSG010.
/// Static delegate properties and static delegate method return values report BSG011.
/// </remarks>
[
  AttributeUsage(AttributeTargets.Class |
  AttributeTargets.Constructor |
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
  /// Stable currently applies to exported <c>string</c>, <c>byte[]</c>, and <c>T[]</c> properties
  /// and method return values.
  /// It guarantees stable JavaScript object identity for the same managed reference: as long as the
  /// property or method returns the same .NET object reference, the same JavaScript value is reused
  /// without re-encoding or re-allocating.
  /// For <c>string</c>, stable identity is determined by .NET reference equality, so interned or
  /// constant strings benefit most. Mutating a string field to a new value (even with equal content)
  /// clears the cache and causes a new JS string to be created on the next read.
  /// Stable does not turn exports into a live synchronized view over mutable managed storage.
  /// If the same managed array instance is mutated in place, existing JavaScript Array or Uint8Array
  /// values are not guaranteed to observe the new contents immediately.
  /// Use JSBufferRef, JSTypedArrayRef, or JSArrayBufferRef when the API needs explicit shared backing
  /// storage or live reference semantics rather than stable identity plus snapshot reuse.
  /// Delegate exports already use stable function-reference semantics by default.
  /// This option does not make unsupported static JS reference or static delegate export shapes valid.
  /// </remarks>
  public bool Stable { get; set; }
}
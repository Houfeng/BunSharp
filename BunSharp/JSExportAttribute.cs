namespace BunSharp;

[
  AttributeUsage(AttributeTargets.Class |
  AttributeTargets.Method |
  AttributeTargets.Property,
  AllowMultiple = false,
  Inherited = false)
]
public sealed class JSExportAttribute : Attribute
{
  public JSExportAttribute()
  {
    Enabled = true;
  }

  public JSExportAttribute(bool enabled)
  {
    Enabled = enabled;
  }

  public JSExportAttribute(string name)
  {
    ArgumentException.ThrowIfNullOrEmpty(name);
    Enabled = true;
    Name = name;
  }

  public bool Enabled { get; }

  public string? Name { get; }
}
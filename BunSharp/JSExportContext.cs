namespace BunSharp;

public class JSExportContext
{
  private readonly BunContext Context;

  internal JSExportContext(BunContext context)
  {
    ArgumentNullException.ThrowIfNull(context);
    Context = context;
  }

  public bool TryGetExportedValue<T>(T instance, out BunValue value) where T : class
  {
    ArgumentNullException.ThrowIfNull(instance);
    return Context.TryGetExportedValue(instance, out value);
  }

  public bool TryUnwrapExported<T>(BunValue value, out T? result) where T : class
  {
    return Context.TryUnwrapExported(value, out result);
  }

  public void Protect(BunValue value)
  {
    Context.Protect(value);
  }

  public void Unprotect(BunValue value)
  {
    Context.Unprotect(value);
  }

  public bool Protect<T>(T instance) where T : class
  {
    if (!TryGetExportedValue(instance, out var value))
    {
      return false;
    }
    Protect(value);
    return true;
  }

  public bool Unprotect<T>(T instance) where T : class
  {
    if (!TryGetExportedValue(instance, out var value))
    {
      return false;
    }
    Unprotect(value);
    return true;
  }

  public bool IsArray(BunValue value)
  {
    return Context.IsArray(value);
  }

  public bool IsObject(BunValue value)
  {
    return Context.IsObject(value);
  }

  public bool IsCallable(BunValue value)
  {
    return Context.IsCallable(value);
  }

}
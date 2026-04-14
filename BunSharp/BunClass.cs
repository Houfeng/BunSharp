using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using BunSharp.Interop;

namespace BunSharp;

public sealed class BunClass : IDisposable
{
    private readonly BunRuntime _runtime;
    private readonly PreparedClassDefinition _preparedDescriptor;
    private bool _disposed;

    internal BunClass(BunRuntime runtime, nint handle, PreparedClassDefinition preparedDescriptor)
    {
        _runtime = runtime;
        Handle = handle;
        _preparedDescriptor = preparedDescriptor;
    }

    public nint Handle { get; }

    public BunValue Prototype
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _runtime.VerifyThread();
            return BunNative.ClassPrototype(_runtime.Context.Handle, Handle);
        }
    }

    public BunValue Constructor
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _runtime.VerifyThread();
            return BunNative.ClassConstructor(_runtime.Context.Handle, Handle);
        }
    }

    public BunValue CreateInstance(nint nativePtr, BunManagedClassFinalizer? finalizer = null, nint userdata = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.VerifyThread();

        BunCallbackHandle? callbackHandle = null;
        try
        {
            nint finalizerPointer = 0;
            var finalizerUserData = userdata;

            if (finalizer is not null)
            {
                callbackHandle = BunManagedCallbackRegistry.CreateClassFinalizer(finalizer, _runtime, userdata);
                finalizerPointer = BunManagedCallbackRegistry.ClassFinalizerPointer;
                var disposerHandle = GCHandle.Alloc(callbackHandle);
                var disposerPtr = GCHandle.ToIntPtr(disposerHandle);
                callbackHandle.SetDisposerHandle(disposerPtr);
                finalizerUserData = disposerPtr;
            }

            var value = BunNative.ClassNew(_runtime.Context.Handle, Handle, nativePtr, finalizerPointer, finalizerUserData);
            if (callbackHandle is not null)
            {
                _runtime.RetainWithAutoRelease(callbackHandle);
                callbackHandle = null;
            }

            return value;
        }
        finally
        {
            callbackHandle?.Dispose();
        }
    }

    public BunValue CreateInstance(nint nativePtr, nint finalizer, nint userdata = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.VerifyThread();
        return BunNative.ClassNew(_runtime.Context.Handle, Handle, nativePtr, finalizer, userdata);
    }

    public BunClassPersistentFinalizer CreatePersistentFinalizer(BunManagedClassFinalizer finalizer, nint userdata = 0)
    {
        ArgumentNullException.ThrowIfNull(finalizer);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.VerifyThread();
        return new BunClassPersistentFinalizer(BunManagedCallbackRegistry.CreatePersistentClassFinalizer(finalizer, _runtime, userdata));
    }

    public BunValue CreateInstance(nint nativePtr, BunClassPersistentFinalizer finalizer)
    {
        ArgumentNullException.ThrowIfNull(finalizer);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.VerifyThread();
        return BunNative.ClassNew(_runtime.Context.Handle, Handle, nativePtr, BunManagedCallbackRegistry.PersistentClassFinalizerPointer, finalizer.UserData);
    }

    public nint Unwrap(BunValue value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.VerifyThread();
        return BunNative.ClassUnwrap(_runtime.Context.Handle, value, Handle);
    }

    public bool IsInstance(BunValue value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.VerifyThread();
        return BunNative.InstanceOfClass(_runtime.Context.Handle, value, Handle) != 0;
    }

    public bool DisposeInstance(BunValue value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.VerifyThread();
        return BunNative.ClassDispose(_runtime.Context.Handle, value) != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _preparedDescriptor.Dispose();
        _disposed = true;
    }
}

public sealed class BunClassPersistentFinalizer : IDisposable
{
    private BunCallbackHandle? _callbackHandle;

    internal BunClassPersistentFinalizer(BunCallbackHandle callbackHandle)
    {
        _callbackHandle = callbackHandle;
    }

    internal nint UserData => _callbackHandle?.Pointer ?? 0;

    public void Dispose()
    {
        Interlocked.Exchange(ref _callbackHandle, null)?.Dispose();
    }
}

public sealed class BunClassDefinition
{
    public BunClassDefinition(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    public string Name { get; }

    public IList<BunClassMethodDefinition> Methods { get; } = new List<BunClassMethodDefinition>();

    public IList<BunClassPropertyDefinition> Properties { get; } = new List<BunClassPropertyDefinition>();

    public BunManagedClassConstructor? Constructor { get; set; }

    public int ConstructorArgCount { get; set; }

    public nint ConstructorUserData { get; set; }

    public IList<BunClassStaticMethodDefinition> StaticMethods { get; } = new List<BunClassStaticMethodDefinition>();

    public IList<BunClassStaticPropertyDefinition> StaticProperties { get; } = new List<BunClassStaticPropertyDefinition>();
}

public sealed class BunClassMethodDefinition
{
    public BunClassMethodDefinition(string name, BunManagedClassMethod callback, int argCount = 0, bool dontEnum = false, bool dontDelete = false, nint userdata = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(callback);
        Name = name;
        Callback = callback;
        ArgCount = argCount;
        DontEnum = dontEnum;
        DontDelete = dontDelete;
        UserData = userdata;
    }

    public string Name { get; }

    public BunManagedClassMethod Callback { get; }

    public int ArgCount { get; }

    public bool DontEnum { get; }

    public bool DontDelete { get; }

    public nint UserData { get; }
}

public sealed class BunClassPropertyDefinition
{
    public BunClassPropertyDefinition(string name, BunManagedClassGetter? getter = null, BunManagedClassSetter? setter = null, bool readOnly = false, bool dontEnum = false, bool dontDelete = false, nint userdata = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Getter = getter;
        Setter = setter;
        ReadOnly = readOnly;
        DontEnum = dontEnum;
        DontDelete = dontDelete;
        UserData = userdata;
    }

    public string Name { get; }

    public BunManagedClassGetter? Getter { get; }

    public BunManagedClassSetter? Setter { get; }

    public bool ReadOnly { get; }

    public bool DontEnum { get; }

    public bool DontDelete { get; }

    public nint UserData { get; }
}

public sealed class BunClassStaticMethodDefinition
{
    public BunClassStaticMethodDefinition(string name, BunManagedClassStaticMethod callback, int argCount = 0, bool dontEnum = false, bool dontDelete = false, nint userdata = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(callback);
        Name = name;
        Callback = callback;
        ArgCount = argCount;
        DontEnum = dontEnum;
        DontDelete = dontDelete;
        UserData = userdata;
    }

    public string Name { get; }

    public BunManagedClassStaticMethod Callback { get; }

    public int ArgCount { get; }

    public bool DontEnum { get; }

    public bool DontDelete { get; }

    public nint UserData { get; }
}

public sealed class BunClassStaticPropertyDefinition
{
    public BunClassStaticPropertyDefinition(string name, BunManagedClassStaticGetter? getter = null, BunManagedClassStaticSetter? setter = null, bool readOnly = false, bool dontEnum = false, bool dontDelete = false, nint userdata = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Getter = getter;
        Setter = setter;
        ReadOnly = readOnly;
        DontEnum = dontEnum;
        DontDelete = dontDelete;
        UserData = userdata;
    }

    public string Name { get; }

    public BunManagedClassStaticGetter? Getter { get; }

    public BunManagedClassStaticSetter? Setter { get; }

    public bool ReadOnly { get; }

    public bool DontEnum { get; }

    public bool DontDelete { get; }

    public nint UserData { get; }
}

internal unsafe sealed class PreparedClassDefinition : IDisposable
{
    private readonly List<Utf8NativeString> _nameBuffers = [];
    private readonly List<IDisposable> _callbackHandles = [];
    private BunClassMethodDescriptor* _methods;
    private BunClassPropertyDescriptor* _properties;
    private BunClassStaticMethodDescriptor* _staticMethods;
    private BunClassStaticPropertyDescriptor* _staticProperties;
    private bool _disposed;

    private PreparedClassDefinition()
    {
    }

    public BunClassDescriptor Descriptor { get; private set; }

    public static PreparedClassDefinition Create(BunClassDefinition definition)
    {
        var prepared = new PreparedClassDefinition();
        prepared.Initialize(definition);
        return prepared;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (var index = _callbackHandles.Count - 1; index >= 0; index--)
        {
            _callbackHandles[index].Dispose();
        }

        if (_methods != null)
        {
            NativeMemory.Free(_methods);
            _methods = null;
        }

        if (_properties != null)
        {
            NativeMemory.Free(_properties);
            _properties = null;
        }

        if (_staticMethods != null)
        {
            NativeMemory.Free(_staticMethods);
            _staticMethods = null;
        }

        if (_staticProperties != null)
        {
            NativeMemory.Free(_staticProperties);
            _staticProperties = null;
        }

        foreach (var nameBuffer in _nameBuffers)
        {
            nameBuffer.Dispose();
        }

        _nameBuffers.Clear();
        _callbackHandles.Clear();
        _disposed = true;
    }

    private void Initialize(BunClassDefinition definition)
    {
        var className = AllocateName(definition.Name);
        _methods = definition.Methods.Count == 0 ? null : (BunClassMethodDescriptor*)NativeMemory.Alloc((nuint)definition.Methods.Count, (nuint)sizeof(BunClassMethodDescriptor));
        _properties = definition.Properties.Count == 0 ? null : (BunClassPropertyDescriptor*)NativeMemory.Alloc((nuint)definition.Properties.Count, (nuint)sizeof(BunClassPropertyDescriptor));
        _staticMethods = definition.StaticMethods.Count == 0 ? null : (BunClassStaticMethodDescriptor*)NativeMemory.Alloc((nuint)definition.StaticMethods.Count, (nuint)sizeof(BunClassStaticMethodDescriptor));
        _staticProperties = definition.StaticProperties.Count == 0 ? null : (BunClassStaticPropertyDescriptor*)NativeMemory.Alloc((nuint)definition.StaticProperties.Count, (nuint)sizeof(BunClassStaticPropertyDescriptor));
        BunCallbackHandle? constructorHandle = null;

        if (definition.Constructor is not null)
        {
            constructorHandle = BunManagedCallbackRegistry.CreateClassConstructor(definition.Constructor, definition.ConstructorUserData);
            _callbackHandles.Add(constructorHandle);
        }

        for (var index = 0; index < definition.Methods.Count; index++)
        {
            var method = definition.Methods[index];
            var methodName = AllocateName(method.Name);
            var callbackHandle = BunManagedCallbackRegistry.CreateClassMethod(method.Callback, method.UserData);
            _callbackHandles.Add(callbackHandle);

            _methods[index] = new BunClassMethodDescriptor
            {
                Name = methodName.Pointer,
                NameLength = methodName.ByteLength,
                Callback = BunManagedCallbackRegistry.ClassMethodPointer,
                UserData = callbackHandle.Pointer,
                ArgCount = method.ArgCount,
                DontEnum = method.DontEnum ? 1 : 0,
                DontDelete = method.DontDelete ? 1 : 0,
            };
        }

        for (var index = 0; index < definition.Properties.Count; index++)
        {
            var property = definition.Properties[index];
            var propertyName = AllocateName(property.Name);
            BunCallbackHandle? propertyHandle = null;

            if (property.Getter is not null || property.Setter is not null)
            {
                propertyHandle = BunManagedCallbackRegistry.CreateClassProperty(property.Getter, property.Setter, property.UserData);
                _callbackHandles.Add(propertyHandle);
            }

            _properties[index] = new BunClassPropertyDescriptor
            {
                Name = propertyName.Pointer,
                NameLength = propertyName.ByteLength,
                Getter = property.Getter is null ? 0 : BunManagedCallbackRegistry.ClassGetterPointer,
                Setter = property.Setter is null ? 0 : BunManagedCallbackRegistry.ClassSetterPointer,
                UserData = propertyHandle?.Pointer ?? 0,
                ReadOnly = property.ReadOnly ? 1 : 0,
                DontEnum = property.DontEnum ? 1 : 0,
                DontDelete = property.DontDelete ? 1 : 0,
            };
        }

        for (var index = 0; index < definition.StaticMethods.Count; index++)
        {
            var method = definition.StaticMethods[index];
            var methodName = AllocateName(method.Name);
            var callbackHandle = BunManagedCallbackRegistry.CreateClassStaticMethod(method.Callback, method.UserData);
            _callbackHandles.Add(callbackHandle);

            _staticMethods[index] = new BunClassStaticMethodDescriptor
            {
                Name = methodName.Pointer,
                NameLength = methodName.ByteLength,
                Callback = BunManagedCallbackRegistry.ClassStaticMethodPointer,
                UserData = callbackHandle.Pointer,
                ArgCount = method.ArgCount,
                DontEnum = method.DontEnum ? 1 : 0,
                DontDelete = method.DontDelete ? 1 : 0,
            };
        }

        for (var index = 0; index < definition.StaticProperties.Count; index++)
        {
            var property = definition.StaticProperties[index];
            var propertyName = AllocateName(property.Name);
            BunCallbackHandle? propertyHandle = null;

            if (property.Getter is not null || property.Setter is not null)
            {
                propertyHandle = BunManagedCallbackRegistry.CreateClassStaticProperty(property.Getter, property.Setter, property.UserData);
                _callbackHandles.Add(propertyHandle);
            }

            _staticProperties[index] = new BunClassStaticPropertyDescriptor
            {
                Name = propertyName.Pointer,
                NameLength = propertyName.ByteLength,
                Getter = property.Getter is null ? 0 : BunManagedCallbackRegistry.ClassStaticGetterPointer,
                Setter = property.Setter is null ? 0 : BunManagedCallbackRegistry.ClassStaticSetterPointer,
                UserData = propertyHandle?.Pointer ?? 0,
                ReadOnly = property.ReadOnly ? 1 : 0,
                DontEnum = property.DontEnum ? 1 : 0,
                DontDelete = property.DontDelete ? 1 : 0,
            };
        }

        Descriptor = new BunClassDescriptor
        {
            Name = className.Pointer,
            NameLength = className.ByteLength,
            Properties = _properties,
            PropertyCount = checked((nuint)definition.Properties.Count),
            Methods = _methods,
            MethodCount = checked((nuint)definition.Methods.Count),
            Constructor = constructorHandle is null ? 0 : BunManagedCallbackRegistry.ClassConstructorPointer,
            ConstructorUserData = constructorHandle?.Pointer ?? 0,
            ConstructorArgCount = definition.ConstructorArgCount,
            StaticProperties = _staticProperties,
            StaticPropertyCount = checked((nuint)definition.StaticProperties.Count),
            StaticMethods = _staticMethods,
            StaticMethodCount = checked((nuint)definition.StaticMethods.Count),
        };
    }

    private Utf8NativeString AllocateName(string value)
    {
        var name = Utf8NativeString.Allocate(value);
        _nameBuffers.Add(name);
        return name;
    }
}

internal readonly unsafe struct Utf8NativeString : IDisposable
{
    private Utf8NativeString(nint pointer, nuint byteLength)
    {
        Pointer = pointer;
        ByteLength = byteLength;
    }

    public nint Pointer { get; }

    public nuint ByteLength { get; }

    public static Utf8NativeString Allocate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var byteCount = checked((nuint)System.Text.Encoding.UTF8.GetByteCount(value));
        var buffer = (byte*)NativeMemory.Alloc(byteCount + 1);
        System.Text.Encoding.UTF8.GetBytes(value, new Span<byte>(buffer, checked((int)byteCount)));
        buffer[byteCount] = 0;
        return new Utf8NativeString((nint)buffer, byteCount);
    }

    public void Dispose()
    {
        if (Pointer != 0)
        {
            NativeMemory.Free((void*)Pointer);
        }
    }
}
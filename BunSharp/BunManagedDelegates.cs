using System;

namespace BunSharp;

public delegate BunValue BunManagedHostCallback(BunContext context, ReadOnlySpan<BunValue> args, nint userdata);

public delegate void BunManagedEventCallback(BunRuntime runtime, nint userdata);

public delegate BunValue BunManagedGetter(BunContext context, BunValue thisValue);

public delegate void BunManagedSetter(BunContext context, BunValue thisValue, BunValue value);

public delegate void BunManagedFinalizer(nint userdata);

public delegate BunValue BunManagedClassMethod(BunContext context, BunValue thisValue, nint nativePtr, ReadOnlySpan<BunValue> args, nint userdata);

public delegate BunValue BunManagedClassGetter(BunContext context, BunValue thisValue, nint nativePtr, nint userdata);

public delegate void BunManagedClassSetter(BunContext context, BunValue thisValue, nint nativePtr, BunValue value, nint userdata);

public delegate BunValue BunManagedClassConstructor(BunContext context, nint classHandle, ReadOnlySpan<BunValue> args, nint userdata);

public delegate BunValue BunManagedClassStaticMethod(BunContext context, BunValue thisValue, ReadOnlySpan<BunValue> args, nint userdata);

public delegate BunValue BunManagedClassStaticGetter(BunContext context, BunValue thisValue, nint userdata);

public delegate void BunManagedClassStaticSetter(BunContext context, BunValue thisValue, BunValue value, nint userdata);

public delegate void BunManagedClassFinalizer(nint nativePtr, nint userdata);
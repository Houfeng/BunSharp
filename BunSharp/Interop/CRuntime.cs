using System.Runtime.InteropServices;

namespace BunSharp.Interop;

internal static partial class CRuntime
{
    public static void Free(nint pointer)
    {
        if (pointer == 0)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            FreeWindows(pointer);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            FreeMac(pointer);
            return;
        }

        FreeUnix(pointer);
    }

    [LibraryImport("ucrtbase", EntryPoint = "free")]
    private static partial void FreeWindows(nint pointer);

    [LibraryImport("libSystem.B.dylib", EntryPoint = "free")]
    private static partial void FreeMac(nint pointer);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void FreeUnix(nint pointer);
}
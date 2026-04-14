using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace BunSharp.Interop;

internal static class BunNativeLibraryResolver
{
    public const string LibraryName = "libbun";

    private static readonly Lock SyncRoot = new();
    private static bool s_initialized;
    private static nint s_libraryHandle;
    private static string[]? s_lastSearchPaths;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (s_initialized)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(BunNativeLibraryResolver).Assembly, (libraryName, _, _) =>
            {
                if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
                {
                    return 0;
                }

                lock (SyncRoot)
                {
                    if (s_libraryHandle != 0)
                    {
                        return s_libraryHandle;
                    }

                    if (TryLoadLibrary(out s_libraryHandle, out s_lastSearchPaths))
                    {
                        return s_libraryHandle;
                    }

                    return 0;
                }
            });
            s_initialized = true;
        }
    }

    public static void EnsureLoaded()
    {
        Initialize();

        lock (SyncRoot)
        {
            if (s_libraryHandle != 0)
            {
                return;
            }

            if (!TryLoadLibrary(out s_libraryHandle, out s_lastSearchPaths))
            {
                throw new DllNotFoundException($"Unable to load native Bun library '{LibraryName}'. Searched: {string.Join(", ", s_lastSearchPaths ?? Array.Empty<string>())}");
            }
        }
    }

    private static bool TryLoadLibrary(out nint handle, out string[] searchedPaths)
    {
        var searchPaths = new List<string>();

        foreach (var candidate in EnumerateCandidates())
        {
            searchPaths.Add(candidate);
            if (NativeLibrary.TryLoad(candidate, out handle))
            {
                searchedPaths = searchPaths.ToArray();
                return true;
            }
        }

        handle = 0;
        searchedPaths = searchPaths.ToArray();
        return false;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var rid = GetRuntimeIdentifier();

        foreach (var fileName in GetLibraryFileNames())
        {
            yield return fileName;
            yield return Path.Combine(baseDirectory, fileName);
            yield return Path.Combine(baseDirectory, "runtimes", rid, "native", fileName);
            yield return Path.Combine(baseDirectory, "runtimes", rid, fileName);
        }
    }

    private static string GetRuntimeIdentifier()
    {
        var os = OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsWindows() ? "win"
            : throw new PlatformNotSupportedException("The current OS is not supported by libbun.");

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException($"The current architecture '{RuntimeInformation.ProcessArchitecture}' is not supported by libbun.")
        };

        return $"{os}-{architecture}";
    }

    private static IReadOnlyList<string> GetLibraryFileNames()
    {
        if (OperatingSystem.IsWindows())
        {
            return ["libbun.dll", "bun.dll"];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["libbun.dylib", "bun.dylib", "libbun"];
        }

        return ["libbun.so", "bun.so", "libbun"];
    }
}
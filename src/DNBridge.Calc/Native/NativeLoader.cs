using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DNBridge.Calc.Native;

/// <summary>
/// OS-specific resolution of the native calculation engine, registered as a
/// <see cref="NativeLibrary"/> import resolver. This keeps every <c>[DllImport(An3f4w.Dll)]</c>
/// call site OS-agnostic — the same managed code runs on Windows today and on a future Linux
/// engine build with no source changes.
///
/// <para>The resolver is wired up by a <see cref="ModuleInitializerAttribute"/>, so it is in
/// place before the first P/Invoke regardless of which type is touched first.</para>
/// </summary>
internal static class NativeLoader
{
    // CA2255: a module initializer is deliberate here — it guarantees the native resolver is
    // registered before the first P/Invoke, so no engine entry point can forget to wire it up.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register()
        => NativeLibrary.SetDllImportResolver(typeof(An3f4w).Assembly, Resolve);

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != An3f4w.Dll)
            return IntPtr.Zero; // not ours — let the default resolver handle it

        string baseDir = AppContext.BaseDirectory;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // borlndmm.dll (Delphi shared-memory manager) MUST load before the engine — the
            // engine allocates the strings it returns from this heap. Deterministic order.
            NativeLibrary.Load(Path.Combine(baseDir, "borlndmm.dll"));
            return NativeLibrary.Load(Path.Combine(baseDir, "an3f4w.dll"));
        }

        // Future Linux engine build. borlndmm is a Windows/Delphi-only artifact and is not
        // part of the Linux build, so it is intentionally not pre-loaded here.
        return NativeLibrary.Load(Path.Combine(baseDir, "liban3f4w.so"));
    }
}

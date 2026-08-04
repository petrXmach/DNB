using System.Runtime.InteropServices;
using DNBridge.Calc.Native;

namespace DNBridge.Calc;

/// <summary>Outcome of an <see cref="An3f4wSmokeTest"/> probe.</summary>
public readonly record struct An3f4wProbeResult(bool Ok, string? Version, string Message);

/// <summary>
/// Minimal "is the calculation engine loadable and alive?" probe. Pre-loads the engine's
/// Borland memory-manager dependency, brings the engine to an initialized state
/// (<see cref="An3f4wEngine.Reset"/> — "test connection"), then reads <c>anDLLVersion</c>
/// ("get info").
///
/// <para>This is a smoke test only — it does not run any analysis, and it deliberately leaves the
/// engine <b>initialized</b> (the C++ resting state between calcs); it does not call
/// <c>anDoneLibrary</c>. The engine is single-instance and not thread-safe, so callers must invoke
/// <see cref="Run"/> off the UI thread and never concurrently (the WPF host wraps it in a single
/// <c>Task.Run</c>). All engine lifecycle/serialization lives in <see cref="An3f4wEngine"/>.</para>
/// </summary>
public static class An3f4wSmokeTest
{
    /// <summary>
    /// Probe the engine. Returns version on success, or the engine's error text / the load
    /// exception message on failure. Never throws for an expected engine failure.
    /// Native library loading is handled per-OS by <see cref="Native.NativeLoader"/>.
    /// </summary>
    public static An3f4wProbeResult Run()
    {
        lock (An3f4wEngine.Gate)
        {
            int initResult;
            try
            {
                // First P/Invoke triggers NativeLoader's resolver (load order, OS-specific file).
                // Reset() balances init/done state with the calc tests so a probe never desyncs it.
                initResult = An3f4wEngine.Reset(_ => { }); // Delphi LongBool: 0 = failure
            }
            catch (Exception ex)
            {
                return new An3f4wProbeResult(false, null, $"Failed to load native engine: {ex.Message}");
            }

            if (initResult == 0)
                return new An3f4wProbeResult(false, null, $"anInitLibrary failed: {ReadError()}");

            string? version = Marshal.PtrToStringUni(An3f4w.anDLLVersion());
            return string.IsNullOrEmpty(version)
                ? new An3f4wProbeResult(false, null, "anDLLVersion returned empty string")
                : new An3f4wProbeResult(true, version, $"Engine OK — version {version}");
        }
    }

    private static string ReadError()
    {
        string? msg = Marshal.PtrToStringUni(An3f4w.anGetErrorMsg());
        return string.IsNullOrEmpty(msg) ? "(no error text)" : msg;
    }
}

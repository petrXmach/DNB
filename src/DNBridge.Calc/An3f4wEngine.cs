using DNBridge.Calc.Native;

namespace DNBridge.Calc;

/// <summary>
/// Shared lifecycle for the single, stateful, non-thread-safe an3f4w engine instance. Every engine
/// user (the smoke probe, the calc tests, future runners) serializes on <see cref="Gate"/> and
/// brings the engine to a clean state with <see cref="Reset"/>.
///
/// <para><b>Why a shared reset (the init/done ordering bug):</b> the proven C++ host
/// (<c>../DNC/Doc/dll/dll_call_sequence_orpf_repro.md</c> §1–2) calls <c>anInitLibrary</c> <b>once</b>
/// at <c>cl_AN3_Lib::Open</c>, then does <c>anDoneLibrary → anInitLibrary</c> before every
/// calculation. Over the process lifetime that is <c>Init | Done Init | Done Init | …</c> — it
/// always <b>starts with Init</b>, and every <c>anDoneLibrary</c> is balanced by a preceding
/// <c>anInitLibrary</c>. The engine relies on that: a <c>anDoneLibrary</c> on a never-initialized
/// engine leaves it in a state where the <b>next</b> <c>anInitLibrary</c> returns 0 (observed: the
/// second calc run failed with "anInitLibrary failed"). So we track init state and call
/// <c>anDoneLibrary</c> only when already initialized — the first engine call in the process is then
/// a bare <c>anInitLibrary</c>, never a bare <c>anDoneLibrary</c>.</para>
/// </summary>
internal static class An3f4wEngine
{
    /// <summary>
    /// Serializes all engine access — there is exactly one stateful native instance and it is not
    /// thread-safe. Every entry point (smoke probe, calc tests) locks on this.
    /// </summary>
    internal static readonly object Gate = new();

    // True while an anInitLibrary is in effect (not yet balanced by anDoneLibrary). Guarded by Gate.
    private static bool _initialized;

    /// <summary>
    /// Bring the engine to a fresh, initialized state before a run, mirroring the C++ lifecycle:
    /// <c>anDoneLibrary</c> first (but <b>only</b> if already initialized) then <c>anInitLibrary</c>.
    /// Each native call is reported through <paramref name="log"/>. The caller <b>must</b> hold
    /// <see cref="Gate"/>. Returns the <c>anInitLibrary</c> result (0 = failure).
    /// </summary>
    internal static int Reset(Action<string> log)
    {
        if (_initialized)
        {
            int done = An3f4w.anDoneLibrary();
            log($"anDoneLibrary() -> {done}");
            _initialized = false;
        }

        int init = An3f4w.anInitLibrary();
        log($"anInitLibrary() -> {init}");
        _initialized = init != 0;
        return init;
    }
}

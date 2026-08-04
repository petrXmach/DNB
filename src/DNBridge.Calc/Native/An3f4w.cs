using System.Runtime.InteropServices;

namespace DNBridge.Calc.Native;

/// <summary>
/// Raw P/Invoke surface for the an3f4w calculation engine (<c>an3f4w.dll</c>, the x64
/// Delphi power-system library renamed from <c>wxbase_supp64.dll</c>).
///
/// <para>Contract (see <c>../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dll_lifecycle_pinvoke.md</c>):</para>
/// <list type="bullet">
///   <item>Calling convention is <c>__stdcall</c> (a no-op on x64 — a single convention —
///   but declared for correctness and a future 32-bit build).</item>
///   <item>Delphi <c>LongBool</c> returns are 4-byte ints: <c>0 = false</c>, non-zero = true.
///   Marshalled as <see cref="int"/>; never as a 1-byte bool.</item>
///   <item>Returned strings are <c>PChar = wchar_t*</c> (UTF-16LE), owned by the engine's
///   Borland heap. Read with <see cref="Marshal.PtrToStringUni(nint)"/>; never free them.</item>
///   <item>The engine is a single stateful instance and is NOT thread-safe — all calls must
///   be serialized onto one thread (the dedicated calc thread). It also has an implicit
///   dependency on <c>borlndmm.dll</c>, which must sit alongside <c>an3f4w.dll</c>.</item>
/// </list>
/// </summary>
internal static class An3f4w
{
    /// <summary>
    /// Logical (OS-agnostic) library name. <see cref="NativeLoader"/> maps it to the real
    /// file: <c>an3f4w.dll</c> on Windows, <c>liban3f4w.so</c> on a future Linux engine build.
    /// Keeping the <c>[DllImport]</c> name extension-less is what makes the call sites portable.
    /// </summary>
    internal const string Dll = "an3f4w";
    private const CallingConvention Cc = CallingConvention.StdCall;

    /// <summary>Initialize the engine's internal structures. Call before every run. Returns LongBool.</summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anInitLibrary();

    /// <summary>Free engine resources (does not unload the DLL). Returns LongBool.</summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anDoneLibrary();

    /// <summary>Version string of the engine, e.g. "8.2.1130.64bit". Returns wchar_t* (do not free).</summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern nint anDLLVersion();

    /// <summary>Last error message. Returns wchar_t* (do not free).</summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern nint anGetErrorMsg();

    // ── Input / run / status (the dvChod load-flow hot path) ───────────────────────────────

    /// <summary>
    /// Parse the engine-input text. <paramref name="utf16Text"/> must point at a pinned
    /// UTF-16LE byte buffer (with a trailing NUL); never use string auto-marshalling for the
    /// large blob. Returns LongBool; on 0 read <see cref="anGetErrorMsg"/>.
    /// </summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anReadInpData(nint utf16Text);

    /// <summary>
    /// Run a solve. <paramref name="calcKind"/> is <c>Calc3_Kind_T</c> (5 = dvChod non-linear
    /// load-flow); <paramref name="createOutFiles"/> writes human-readable dumps when non-zero.
    /// Returns LongBool; on 0 read <see cref="anGetErrorMsg"/>.
    /// </summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anRunAnalysis(uint calcKind, int createOutFiles);

    /// <summary>Second success gate after <see cref="anRunAnalysis"/> — solver converged? Returns LongBool.</summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anCheckFinishStatus();

    /// <summary>
    /// Output dump base path (UTF-16LE pointer). Required before <see cref="anRunAnalysis"/> with
    /// <c>createOutFiles = 1</c>; the engine writes <c>.bin3</c>/<c>.err</c> next to it. Returns LongBool.
    /// </summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anSetFileName(nint utf16Path);

    /// <summary>Finish / status text. Returns wchar_t* (do not free).</summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern nint anGetFinishMessage();

    /// <summary>Solved system frequency (4-wire only).</summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern double anGetFinishFrequency();

    // ── Result counts & summary getters ────────────────────────────────────────────────────

    /// <summary>Number of node result records.</summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anGetNodeCount();

    /// <summary>Number of branch result records.</summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anGetBranchCount();

    /// <summary>
    /// Total system losses as a complex P+jQ, returned <b>by value</b> as a 16-byte
    /// <see cref="AnCplx"/>. Returning a >8-byte struct by value over __stdcall is ABI-sensitive;
    /// verified on x64.
    /// </summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern AnCplx anGetSystemLosses1f();

    /// <summary>
    /// Write node result record <paramref name="idx"/> (0..count-1) into <paramref name="buf"/>
    /// (the shared ≥64 KB scratch buffer). <paramref name="harm"/> = 0 for the fundamental.
    /// Engine fills the buffer with an <c>AN_NODE_DATA_4_T</c>. Returns LongBool (0 = error).
    /// </summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anGetNodeData(uint idx, uint harm, nint buf);

    /// <summary>
    /// Write branch result record <paramref name="idx"/> (0..count-1) into <paramref name="buf"/>.
    /// Engine fills the buffer with an <c>AN_BRANCH_DATA_4_T</c>. Returns LongBool (0 = error).
    /// </summary>
    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern int anGetBranchData(uint idx, uint harm, nint buf);
}

/// <summary>
/// Engine complex scalar (<c>AN_CPLX_T</c>): cartesian <c>re + j·im</c> in base SI units
/// (W/var for power). Byte-packed, 16 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct AnCplx
{
    internal double Re;
    internal double Im;
}

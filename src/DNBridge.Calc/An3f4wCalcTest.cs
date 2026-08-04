using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using DNBridge.Calc.Native;

namespace DNBridge.Calc;

/// <summary>Outcome of an <see cref="An3f4wCalcTest"/> run.</summary>
public readonly record struct An3f4wCalcResult(bool Ok, string Message);

/// <summary>
/// A named calculation test: a friendly name, the engine analysis kind passed to
/// <c>anRunAnalysis</c> (<see cref="CalcKind"/> — 5 = dvChod, 21 = dvOrpf), and the default
/// engine-input dump file under <c>data/</c>. The catalog is <see cref="An3f4wCalcTest.Catalog"/>.
/// </summary>
public sealed record An3f4wCalcCase(string Name, string Description, uint CalcKind, string FileName);

/// <summary>
/// Runs one engine analysis against an engine-input text dump and reports a lifecycle + summary
/// trace: every native call with its params and return value, the finish message, node/branch
/// counts, system losses (P+jQ), the solved frequency and a sample struct decode of the first few
/// node/branch records.
///
/// <para>The same engine entry point — <c>anRunAnalysis(kind, createOutFiles)</c> — drives every
/// kind of calculation; only the <c>kind</c> and the objective embedded in the input text differ
/// (see <c>../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dll_orpf_scada.md</c> §2). In the C++ original
/// the OPER/dvChod stage uses kind 5 while the QMIN/QMAX/LOSS/OPTIMIZE stages all use kind 21
/// (dvOrpf), distinguished only by their stage objective. Each dump's header records its kind, e.g.
/// <c>;engine input dump  calc_kind=21 (5=dvChod,21=dvOrpf)</c>; <see cref="ParseCalcKind"/> reads it.</para>
///
/// <para>The canonical per-run sequence (<c>L2_dll_lifecycle_pinvoke.md</c> §4; mirrors C++
/// <c>cl_Calculation::Do_Calculate3</c> which resets the engine before <b>every</b> calculation) is:
/// <c>(anDoneLibrary if already initialized) → anInitLibrary → anReadInpData →
/// anRunAnalysis(kind,outFlag) → anCheckFinishStatus</c>, then pull counts/losses. The reset is
/// done by <see cref="An3f4wEngine.Reset"/>, which calls <c>anDoneLibrary</c> only when the engine
/// is already initialized — so the first run starts with a bare <c>anInitLibrary</c> and tests can
/// be run back-to-back with no special handling. Input is UTF-16LE with a trailing NUL, pinned and
/// passed as a pointer. Two success gates: <c>anRunAnalysis ≠ 0</c> AND <c>anCheckFinishStatus ≠ 0</c>.</para>
///
/// <para>The engine is single-instance and not thread-safe, so all access serializes on
/// <see cref="An3f4wEngine.Gate"/> and must be invoked off the UI thread.</para>
/// </summary>
public static class An3f4wCalcTest
{
    /// <summary>
    /// The built-in calculation tests, in display order. Files are the dumps captured under
    /// <c>data/</c> for scheme <c>ct_2026.egc3</c>. dvChod/Oper are plain load flows (kind 5);
    /// QMin/QMax/Loss/Optim are U/Q OPF stages (kind 21) differing only by their objective.
    /// </summary>
    public static readonly IReadOnlyList<An3f4wCalcCase> Catalog = new[]
    {
        new An3f4wCalcCase("dvChod", "Steady-state load flow, non-linear (Newton)", 5,  "dvChod.txt"),
        new An3f4wCalcCase("Oper",   "Operational load flow",                       5,  "Oper.txt"),
        new An3f4wCalcCase("QMin",   "Min reactive power probe (U/Q OPF)",          21, "QMin.txt"),
        new An3f4wCalcCase("QMax",   "Max reactive power probe (U/Q OPF)",          21, "QMax.txt"),
        new An3f4wCalcCase("Loss",   "Loss-minimising regulation (U/Q OPF)",        21, "Loss.txt"),
        new An3f4wCalcCase("Optim",  "Voltage-regulation optimize (U/Q OPF)",       21, "Optim.txt"),
    };

    private static readonly Regex CalcKindHeader =
        new(@"calc_kind\s*=\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Read the <c>calc_kind=N</c> marker from a dump's header comment. Returns
    /// <paramref name="fallback"/> when the marker is absent.
    /// </summary>
    public static uint ParseCalcKind(string inputText, uint fallback)
    {
        ArgumentNullException.ThrowIfNull(inputText);
        Match m = CalcKindHeader.Match(inputText);
        return m.Success && uint.TryParse(m.Groups[1].Value, out uint k) ? k : fallback;
    }

    /// <summary>
    /// Run analysis <paramref name="calcKind"/> on <paramref name="inputText"/>. <paramref name="name"/>
    /// is a label for the trace header. Each native call is reported through <paramref name="log"/>
    /// (params + return). Never throws for an expected engine failure — failures are logged and
    /// returned as <see cref="An3f4wCalcResult.Ok"/> = false.
    /// </summary>
    public static An3f4wCalcResult Run(
        string name, uint calcKind, string inputText, Action<string> log, bool createOutFiles = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(inputText);
        ArgumentNullException.ThrowIfNull(log);

        // Serialize against the smoke test and any other engine use — one stateful instance.
        lock (An3f4wEngine.Gate)
        {
            try
            {
                return RunLocked(name, calcKind, inputText, log, createOutFiles);
            }
            catch (Exception ex)
            {
                log($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                return new An3f4wCalcResult(false, $"{name}: failed — {ex.Message}");
            }
        }
    }

    private static An3f4wCalcResult RunLocked(
        string name, uint calcKind, string inputText, Action<string> log, bool createOutFiles)
    {
        log($"===== TEST: {name}  (calc_kind={calcKind} {KindName(calcKind)}) =====");

        // 1. Reset — bring the engine to a fresh initialized state. Mirrors C++
        //    cl_Calculation::Do_Calculate3 (anDoneLibrary then anInitLibrary per calc), except the
        //    Done is skipped on the very first run so the process never starts with a bare
        //    anDoneLibrary — that corrupts the engine and makes the *next* anInitLibrary fail. This
        //    is exactly what makes running several tests one after another safe (see An3f4wEngine).
        int init = An3f4wEngine.Reset(log);
        if (init == 0)
            return Fail(log, name, $"anInitLibrary failed: {ReadError()}");

        log($"anDLLVersion() -> \"{ReadStr(An3f4w.anDLLVersion())}\"");

        // 2. Parse input. UTF-16LE bytes + trailing NUL, pinned; pass the address (not auto-marshalled).
        byte[] buf = Encoding.Unicode.GetBytes(inputText + "\0");
        GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
        int read;
        try
        {
            read = An3f4w.anReadInpData(pin.AddrOfPinnedObject());
        }
        finally
        {
            pin.Free();
        }
        log($"anReadInpData({buf.Length} bytes UTF-16LE, {inputText.Length} chars) -> {read}");
        if (read == 0)
            return Fail(log, name, $"anReadInpData failed: {ReadError()}");

        // 3. Run. createOutFiles controls whether the engine materializes its output structures /
        //    .bin3 files (the per-index Get*Data getters read from them). With the flag on, the
        //    engine demands an output path first (anSetFileName).
        if (createOutFiles)
        {
            string outPath = Path.Combine(Path.GetTempPath(), $"dnbridge_{name}_out.dat");
            byte[] pathBuf = Encoding.Unicode.GetBytes(outPath + "\0");
            GCHandle pathPin = GCHandle.Alloc(pathBuf, GCHandleType.Pinned);
            int sfn;
            try { sfn = An3f4w.anSetFileName(pathPin.AddrOfPinnedObject()); }
            finally { pathPin.Free(); }
            log($"anSetFileName(\"{outPath}\") -> {sfn}");
        }

        int outFlag = createOutFiles ? 1 : 0;
        var sw = Stopwatch.StartNew();
        int run = An3f4w.anRunAnalysis(calcKind, outFlag);
        sw.Stop();
        log($"anRunAnalysis(kind={calcKind} {KindName(calcKind)}, createOutFiles={outFlag}) -> {run}   [{sw.ElapsedMilliseconds} ms]");
        if (run == 0)
            return Fail(log, name, $"anRunAnalysis failed: {ReadError()}");

        // 4. Second success gate.
        int fin = An3f4w.anCheckFinishStatus();
        log($"anCheckFinishStatus() -> {fin}");
        if (fin == 0)
            return Fail(log, name, $"Solver did not converge: {ReadError()} / {ReadStr(An3f4w.anGetFinishMessage())}");

        log($"anGetFinishMessage() -> \"{ReadStr(An3f4w.anGetFinishMessage())}\"");

        // 5. Summary results (no struct decode).
        int nodes = An3f4w.anGetNodeCount();
        log($"anGetNodeCount() -> {nodes}");

        int branches = An3f4w.anGetBranchCount();
        log($"anGetBranchCount() -> {branches}");

        AnCplx losses = An3f4w.anGetSystemLosses1f();
        log($"anGetSystemLosses1f() -> P={F(losses.Re)} W, Q={F(losses.Im)} var  ({F(losses.Re / 1e6)} MW + j{F(losses.Im / 1e6)} MVAr)");

        double freq = An3f4w.anGetFinishFrequency();
        log($"anGetFinishFrequency() -> {F(freq)} Hz");

        // 6. Sample the first few node/branch records (struct decode by explicit offsets).
        DumpRecords(log, "NODE", "anGetNodeData", nodes, NodeRecordSize, An3f4w.anGetNodeData, DecodeNode);
        DumpRecords(log, "BRANCH", "anGetBranchData", branches, BranchRecordSize, An3f4w.anGetBranchData, DecodeBranch);

        return new An3f4wCalcResult(
            true,
            $"{name} OK — {nodes} nodes, {branches} branches, losses {F(losses.Re / 1e6)} MW.");
    }

    private static string KindName(uint kind) => kind switch
    {
        5 => "dvChod",
        21 => "dvOrpf",
        _ => "?",
    };

    private const int SampleCount = 5;

    // One pull-based getter: fills the shared buffer with one record per call.
    private delegate int RecordGetter(uint idx, uint harm, nint buf);

    // Decode one record (ReadOnlySpan can't be an Action<> generic arg — it's a ref struct).
    private delegate void RecordDecoder(Action<string> log, int idx, ReadOnlySpan<byte> record);

    private static void DumpRecords(
        Action<string> log, string label, string funcName, int count, int recordSize,
        RecordGetter getter, RecordDecoder decode)
    {
        int take = Math.Min(SampleCount, count);
        log($"--- {label} records: {count} total, showing {take} ---");
        if (take == 0)
            return;

        // One pinned scratch buffer (mirrors the C++ host's 64 KB m_uBuffer).
        byte[] buf = new byte[65536];
        GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            nint ptr = pin.AddrOfPinnedObject();
            // Log every getter call uniformly ("func(params) -> ret") and never stop early, so the
            // resulting trace is a clean, repeating block to hand to the vendor for a bug report.
            for (int i = 0; i < take; i++)
            {
                Array.Clear(buf, 0, recordSize);
                int ok = getter((uint)i, 0, ptr);
                if (ok == 0)
                {
                    log($"{funcName}(idx={i}, harm=0) -> 0   ({ReadError()})");
                    continue;
                }

                log($"{funcName}(idx={i}, harm=0) -> {ok}");
                decode(log, i, buf.AsSpan(0, recordSize));

                // m_nCheck is the engine's natural record ordinal (1-based in observed builds).
                // A value far from the index we asked for means the struct layout / DLL version
                // is out of sync — the single most useful tripwire when swapping DLLs.
                uint check = BitConverter.ToUInt32(buf, 0);
                if (check != (uint)i && check != (uint)(i + 1))
                    log($"  [!! m_nCheck={check} not near idx {i} — layout/version mismatch?]");
            }
        }
        finally
        {
            pin.Free();
        }
    }

    // ── AN_NODE_DATA_4_T decode (Pack=1; offsets verified against AN3_Iface.h, total 564 B) ──
    private const int NodeRecordSize = 564;
    private const int OffNodeId = 4;       // AN_ID_STR m_szID (wchar_t[80]) after uint32 m_nCheck
    private const int OffNodeUn = 204;     // AN_DBL_T m_fUn (4 + 200; after m_ID = 4 + 200)
    private const int OffNodeVolt = 220;   // T4_PHASE_T m_Node_Volt (Va,Vb,Vc,Vn cartesian, volts)
    private const int OffNodeSabc = 548;   // AN_CPLX_T m_Sabc (total injected power)

    private static void DecodeNode(Action<string> log, int i, ReadOnlySpan<byte> r)
    {
        uint rec = BitConverter.ToUInt32(r);
        string id = ReadId(r, OffNodeId);
        double un = BitConverter.ToDouble(r[OffNodeUn..]);
        double ua = Mag(r, OffNodeVolt + 0);
        double ub = Mag(r, OffNodeVolt + 16);
        double uc = Mag(r, OffNodeVolt + 32);
        double p = BitConverter.ToDouble(r[OffNodeSabc..]);
        double q = BitConverter.ToDouble(r[(OffNodeSabc + 8)..]);
        log($"  node[{i}] rec={rec} id={id} Un={F(un / 1000)}kV |U|abc={F(ua / 1000)}/{F(ub / 1000)}/{F(uc / 1000)}kV  Sinj={F(p / 1000)}+j{F(q / 1000)} kVA");
    }

    // ── AN_BRANCH_DATA_4_T decode (Pack=1; offsets verified against AN3_Iface.h, total 1248 B) ──
    private const int BranchRecordSize = 1248;
    private const int OffBrId = 4;          // AN_ID_STR m_szID after uint32 m_nCheck
    private const int OffBrCode = 168;      // uint32 m_nCode (4 + 160 + 4)
    private const int OffBrFrom = 172;      // uint32 m_nFrom_No
    private const int OffBrTo = 176;        // uint32 m_nTo_No
    private const int OffBrPort0Sabc = 416; // m_Port[0].m_Sabc (4 + 220 ID + 192 within port)
    private const int OffBrDeltaSabc = 1224;// AN_CPLX_T m_DeltaSabc (total branch losses)

    private static void DecodeBranch(Action<string> log, int i, ReadOnlySpan<byte> r)
    {
        uint rec = BitConverter.ToUInt32(r);
        string id = ReadId(r, OffBrId);
        uint code = BitConverter.ToUInt32(r[OffBrCode..]);
        uint from = BitConverter.ToUInt32(r[OffBrFrom..]);
        uint to = BitConverter.ToUInt32(r[OffBrTo..]);
        double p = BitConverter.ToDouble(r[OffBrPort0Sabc..]);
        double q = BitConverter.ToDouble(r[(OffBrPort0Sabc + 8)..]);
        double dp = BitConverter.ToDouble(r[OffBrDeltaSabc..]);
        double dq = BitConverter.ToDouble(r[(OffBrDeltaSabc + 8)..]);
        log($"  branch[{i}] rec={rec} code={code} {from}-{to} id={id}  S_port0={F(p / 1000)}+j{F(q / 1000)} kVA  losses={F(dp / 1000)}+j{F(dq / 1000)} kVA");
    }

    // |re + j·im| of the AN_CPLX_T at the given offset.
    private static double Mag(ReadOnlySpan<byte> r, int off)
    {
        double re = BitConverter.ToDouble(r[off..]);
        double im = BitConverter.ToDouble(r[(off + 8)..]);
        return Math.Sqrt(re * re + im * im);
    }

    // AN_ID_STR = wchar_t[80], NUL-terminated UTF-16LE. Returns e.g. "<00000004>".
    private static string ReadId(ReadOnlySpan<byte> r, int off)
    {
        ReadOnlySpan<byte> field = r.Slice(off, 160);
        string s = Encoding.Unicode.GetString(field);
        int nul = s.IndexOf('\0');
        return (nul >= 0 ? s[..nul] : s).Trim();
    }


    private static An3f4wCalcResult Fail(Action<string> log, string name, string message)
    {
        log($"RESULT: FAIL — {message}");
        return new An3f4wCalcResult(false, $"{name}: {message}");
    }

    private static string ReadError()
    {
        string msg = ReadStr(An3f4w.anGetErrorMsg());
        return string.IsNullOrEmpty(msg) ? "(no error text)" : msg;
    }

    private static string ReadStr(nint ptr) => Marshal.PtrToStringUni(ptr) ?? string.Empty;

    private static string F(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
}

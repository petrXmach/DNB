# DNBridge — SCADA Client Wrapper Implementation Guide

See [lib60870.NET/AI_GUIDE.md](C:/_EGC/lib60870.NET/AI_GUIDE.md) for the IEC104 library quick start,
and [lib60870.NET/AI_GUIDE_API.md](C:/_EGC/lib60870.NET/AI_GUIDE_API.md) for full API details.

---

## Architecture

```
C++ DNC Client
    │ TLV protocol over TCP:1550
    ▼
DncTcpServer          src/DNBridge/DncServer/DncTcpServer.cs
    │
DncClientHandler      src/DNBridge/DncServer/DncClientHandler.cs
    │  FrameReceived event (deserialized DnbCommand)
    ▼
DnbEngine             src/DNBridge/Core/DnbEngine.cs        ← integration point
    │  DncCommandReceived event
    │  calls ScadaClient methods
    ▼
ScadaClient           src/DNBridge/ScadaClient/ScadaClient.cs  ← TO BUILD
    │  lib60870.CS104.Connection
    ▼
IEC 60870-5-104 RTU / SCADA server (TCP:2404)
```

---

## Key Files

### DNBridge (existing)

| File | Purpose |
|------|---------|
| `src/DNBridge/Core/DnbEngine.cs` | Coordinator — start/stop, events, _elements cache |
| `src/DNBridge/Core/IDnbEngine.cs` | Interface: IsScadaConnected, ScadaConnectionChanged, ScadaDataReceived |
| `src/DNBridge/Elements/Element104.cs` | Cache entry for one IEC104 data point |
| `src/DNBridge/Commands/RegisterElementsCommand.cs` | List of ElementStub (elements to subscribe) |
| `src/DNBridge/Commands/ElementStub.cs` | Element descriptor: Address104, Iec104Type, IsSetPoint |
| `src/DNBridge/Commands/GetDataCommand.cs` | Request values newer than timestamp; Start=true for first page |
| `src/DNBridge/Commands/DataAnswer.cs` | Response: list of ElementValue, IsFinal flag, max 30/page |
| `src/DNBridge/Commands/ElementValue.cs` | One value: Address, Value (double), Quality (uint), DateTime |
| `src/DNBridge/Commands/PokeCommand.cs` | Write request: ElementId + list of PokeValue |
| `src/DNBridge/Commands/FloatPokeValue.cs` | Float write: Value (double), COT (byte, default=3) |
| `src/DNBridge/Commands/BoolPokeValue.cs` | Bool write: Value (bool), COT (byte, default=3) |
| `src/DNBridge/Commands/FourStatePokeValue.cs` | 4-state write (DoubleCommand) |
| `src/DNBridge/Commands/RegisterElementsAnswer.cs` | Reply to RegisterElementsCommand: IsOk bool |
| `src/DNBridge/Commands/AckAnswer.cs` | Generic ack for Poke |
| `src/DNBridge/Tlv/TlvWriter.cs` | Serialize commands to TLV bytes |
| `src/DNBridge/Tlv/TlvTags.cs` | All tag constants |
| `src/DNBridge/DncServer/IDncClientHandler.cs` | SendAsync(byte[]) for replies |

### lib60870.NET (external)

| File | Purpose |
|------|---------|
| `lib60870/CS104/Connection.cs` | IEC104 client (see AI_GUIDE_API.md) |
| `lib60870/CS101/ASDU.cs` | Received message container |
| `lib60870/CS101/TypeID.cs` | TypeID enum values |

---

## Address Encoding

`Element104.Address` and `ElementStub.Address104` are both `ulong` with the same layout:

```
bits [39:24] = CA  (Common Address / RTU address, uint16)
bits [23:0]  = IOA (Information Object Address, uint24)
```

```csharp
// Decode (from Element104 properties):
ushort ca  = (ushort)((address >> 24) & 0xFFFF);
uint   ioa = (uint)(address & 0xFFFFFF);

// Encode:
ulong address = ((ulong)ca << 24) | (ioa & 0xFFFFFF);

// Visual format "CCC.CCC.III.III.III" — use ElementStub.AddrToStr(address)
```

---

## IEC104 Type Mapping

`ElementStub.Iec104Type` is the raw IEC104 TypeID byte. Map to lib60870 types:

### Monitoring Direction (data from RTU → DNBridge)

| Iec104Type | TypeID name | lib60870 cast class | Value member | Map to double |
|-----------|-------------|---------------------|--------------|---------------|
| 1  | M_SP_NA_1 | `SinglePointInformation` | `.Value` bool | `val ? 1.0 : 0.0` |
| 3  | M_DP_NA_1 | `DoublePointInformation` | `.Value` DoublePointValue | `(double)val.Value` (0-3) |
| 5  | M_ST_NA_1 | `StepPositionInformation` | `.Value` int | `(double)val` |
| 9  | M_ME_NA_1 | `MeasuredValueNormalized` | `.NormalizedValue` float | direct |
| 11 | M_ME_NB_1 | `MeasuredValueScaled` | `.ScaledValue` int | direct |
| 13 | M_ME_NC_1 | `MeasuredValueShort` | `.Value` float | direct |
| 21 | M_ME_ND_1 | `MeasuredValueNormalizedWithoutQuality` | `.NormalizedValue` float | direct |
| 30 | M_SP_TB_1 | `SinglePointWithCP56Time2a` | `.Value` bool | `val ? 1.0 : 0.0` |
| 31 | M_DP_TB_1 | `DoublePointWithCP56Time2a` | `.Value` DoublePointValue | `(double)val.Value` |
| 34 | M_ME_TD_1 | `MeasuredValueNormalizedWithCP56Time2a` | `.NormalizedValue` float | direct |
| 35 | M_ME_TE_1 | `MeasuredValueScaledWithCP56Time2a` | `.ScaledValue` int | direct |
| 36 | M_ME_TF_1 | `MeasuredValueShortWithCP56Time2a` | `.Value` float | direct |

### Quality → uint32

`QualityDescriptor.GetEncoded()` gives the raw byte; store in `Element104.Quality`.
Alternatively pack flags manually: bit 7=Invalid, bit 6=NonTopical, bit 5=Substituted, bit 4=Blocked.

### Control Direction (DNBridge → RTU via PokeCommand)

| PokeValue type | lib60870 command class | TypeID |
|---------------|------------------------|--------|
| `BoolPokeValue` | `SingleCommand(ioa, value, select:false, qoc:0)` | C_SC_NA_1 = 45 |
| `FloatPokeValue` | `SetpointCommandShort(ioa, (float)value, select:false, ql:0)` | C_SE_NC_1 = 50 |
| `FourStatePokeValue` | `DoubleCommand(ioa, (DoubleCommandValue)value, select:false, qoc:0)` | C_DC_NA_1 = 46 |

Use `CauseOfTransmission.ACTIVATION` for all commands sent to RTU.

---

## Command Handling in DnbEngine

Wire up in `DnbEngine.StartAsync` after creating `_scadaClient`:

```csharp
_dncServer.FrameReceived += async (s, e) => {
    DncCommandReceived?.Invoke(this, e);
    await HandleDncCommandAsync(e);
};
```

### RegisterElementsCommand

```csharp
// Input: cmd.Elements = List<ElementStub>
// Action: build _elements cache, subscribe to RTU data
// Reply: RegisterElementsAnswer { IsOk = true }

foreach (var stub in cmd.Elements)
{
    var elem = new Element104(stub.Address104, stub.IsSetPoint);
    elem.Iec104Type = stub.Iec104Type;
    _elements[stub.Address104] = elem;
}
// _scadaClient.UpdateSubscriptions(_elements);
var answer = new RegisterElementsAnswer { IsOk = true, SessionId = cmd.SessionId };
await _dncClientHandler.SendAsync(TlvWriter.Serialize(answer), ct);
```

### GetDataCommand

```csharp
// Input: cmd.NewerThan (DateTime), cmd.Start (bool, pagination)
// Action: collect Element104 where LastDataTime > NewerThan, page 30 at a time
// Reply: DataAnswer { Values=..., IsFinal=bool }

var newer = _elements.Values
    .Where(e => e.LastDataTime > cmd.NewerThan)
    .OrderBy(e => e.LastDataTime)
    .ToList();

// paginate
var page = newer.Take(DataAnswer.MaxRecordsPerAnswer).ToList();
var answer = new DataAnswer {
    SessionId = cmd.SessionId,
    IsFinal = page.Count == newer.Count
};
foreach (var e in page)
    answer.Values.Add(new ElementValue {
        Address  = e.Address,
        Value    = e.Value,
        Quality  = e.Quality,
        DateTime = e.LastDataTime
    });

await _dncClientHandler.SendAsync(TlvWriter.Serialize(answer), ct);
```

### PokeCommand

```csharp
// Input: cmd.ElementId (matches ElementStub.Id), cmd.Values (list of PokeValue)
// Action: find element, send IEC104 control command, reply AckAnswer

var elem = _elements.Values.FirstOrDefault(e => /* match by Id */);
if (elem == null) { /* send AckAnswer IsOk=false */ return; }

foreach (var poke in cmd.Values)
{
    if (poke is BoolPokeValue b)
        _con.SendControlCommand(CauseOfTransmission.ACTIVATION, (int)elem.CA,
            new SingleCommand((int)elem.IOA, b.Value, false, 0));
    else if (poke is FloatPokeValue f)
        _con.SendControlCommand(CauseOfTransmission.ACTIVATION, (int)elem.CA,
            new SetpointCommandShort((int)elem.IOA, (float)f.Value, false, 0));
}
var ack = new AckAnswer { SessionId = cmd.SessionId, IsOk = true };
await _dncClientHandler.SendAsync(TlvWriter.Serialize(ack), ct);
```

---

## ASDU Receive Handler

Update the `_elements` cache whenever RTU data arrives:

```csharp
private bool OnAsduReceived(object param, ASDU asdu)
{
    for (int i = 0; i < asdu.NumberOfElements; i++)
    {
        var io = asdu.GetElement(i);
        ulong addr = EncodeAddress((ushort)asdu.Ca, (uint)io.ObjectAddress);

        if (!_elements.TryGetValue(addr, out var elem))
            continue;  // ignore unregistered IOAs

        (elem.Value, elem.Quality) = ExtractValue(asdu.TypeId, io);
        elem.LastDataTime = /* use asdu timestamp if CP56, else DateTime.UtcNow */;
    }
    ScadaDataReceived?.Invoke(this, new ScadaDataEventArgs(...));
    return true;
}

private static (double value, uint quality) ExtractValue(TypeID typeId, InformationObject io)
{
    return typeId switch {
        TypeID.M_SP_NA_1 => { var v=(SinglePointInformation)io; return (v.Value?1:0, v.Quality.GetEncoded()); },
        TypeID.M_ME_NC_1 => { var v=(MeasuredValueShort)io;     return (v.Value, v.Quality.GetEncoded()); },
        TypeID.M_ME_TF_1 => { var v=(MeasuredValueShortWithCP56Time2a)io; return (v.Value, v.Quality.GetEncoded()); },
        TypeID.M_ME_NB_1 => { var v=(MeasuredValueScaled)io;    return (v.ScaledValue, v.Quality.GetEncoded()); },
        // ... extend for all needed types
        _ => (0, 0x80)  // unknown = invalid quality
    };
}
```

---

## Connection Lifecycle

```csharp
public class ScadaClient : IAsyncDisposable
{
    private Connection? _con;
    private CancellationTokenSource? _cts;

    public event EventHandler<bool>? ConnectionChanged;

    public async Task StartAsync(string host, int port, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _con = new Connection(host, port);
        _con.DebugOutput = false;
        _con.SetASDUReceivedHandler(OnAsduReceived, null);
        _con.SetConnectionHandler(OnConnectionEvent, null);

        // Run reconnect loop in background
        _ = ReconnectLoopAsync(_cts.Token);
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try {
                _con!.Connect();
                _con.SendInterrogationCommand(CauseOfTransmission.ACTIVATION, _ca, 20);
                // Wait until disconnected
                await WaitForDisconnectAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) {
                ConnectionChanged?.Invoke(this, false);
                await Task.Delay(5000, ct);  // retry delay
            }
        }
    }

    private void OnConnectionEvent(object param, ConnectionEvent ev)
    {
        if (ev == ConnectionEvent.OPENED)
            ConnectionChanged?.Invoke(this, true);
        else if (ev == ConnectionEvent.CLOSED)
            ConnectionChanged?.Invoke(this, false);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _con?.Close();
    }
}
```

Wire into `DnbEngine`:
```csharp
_scadaClient.ConnectionChanged += (s, connected) => {
    IsScadaConnected = connected;
    ScadaConnectionChanged?.Invoke(this, new ScadaConnectionEventArgs(connected));
};
```

---

## Where to Add the New Class

**Suggested file:** `src/DNBridge/ScadaClient/ScadaClient.cs`

Instantiate and wire in `DnbEngine.StartAsync()`, dispose in `DnbEngine.StopAsync()`.

The `_elements` `ConcurrentDictionary<ulong, Element104>` lives in `DnbEngine` — pass it to `ScadaClient` or let the engine update it from `ScadaClient` events.

---

## Sending TLV Replies

`TlvWriter` serializes commands to bytes for `IDncClientHandler.SendAsync`:

```csharp
// Build the answer
var answer = new RegisterElementsAnswer { IsOk = true, SessionId = cmd.SessionId };

// Serialize via TlvWriter
var writer = new TlvWriter();
writer.SerializeObject(answer);
byte[] payload = writer.ToEnvelope();   // wraps in outer tag=0 envelope

await _dncClientHandler.SendAsync(payload, ct);
```

Check `DncClientHandler.cs` line ~199 for the existing temporary test code that already does this for `InitAnswer` — follow the same pattern.

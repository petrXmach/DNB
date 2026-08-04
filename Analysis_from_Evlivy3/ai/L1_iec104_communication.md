# IEC 104 / TCP Communication — Module Overview (L1)

## Architecture
```
EVlivy3/DNCoRS ←─TLV/TCP─→ dncors_iec104 ←─IEC 60870-5-104─→ SCADA RTUs
```
- Only active when compiled with `_VOLTAGE_CTRL_` (DNCoRS mode)
- dncors_iec104 is a separate service (../dncors_iec104/) — EVlivy3 is a TCP client
- Protocol: custom TLV-over-TCP (uses same cl_Serializer as file I/O)

## TCP Connection (cl_104_Connector.cpp)
- Blocking TCP socket with `select()` timeout (100ms)
- Dedicated receive thread (`cl_104_Rx::Run()`)
- Auto-reconnect with 200ms retry interval
- Receive buffer: 128KB (`RX_BUFF_LEN = 128 * 1024`)
- Events posted to main thread via wxWidgets event system

## Communication Sequence
1. **Connect**: TCP connect to dncors_iec104 server (IP:port from config)
2. **Init**: Send `cl_Init_Cmd(linkID)` → receive `cl_Init_Answer(OK, mode)`
   - Modes: `Mode_IEC104` (live) or `Mode_Replay` (historical)
3. **Register elements**: Batch register scheme elements with IEC 104 addresses
   - Send `cl_Reg_Elems_Cmd` with `cl_Elem_Stub` entries: {ID, 104_addr, isCommand, isMain}
   - Batched in chunks of `ELEM_REG_RECORDS_MAX`
4. **Data exchange** (cyclic):
   - Send `cl_Get_Data_Cmd(timestamp)` → receive `cl_Data_Answer` with `cl_Elem_104_Value` list
   - Each value: {address (40-bit), float value, timestamp, quality}
5. **Send results**: After calculation, `SendData_to_DRS()` sends regulation outputs

## IEC 104 Database (cl_104_DB — DB_104.h/.cpp)
SQLite-backed mapping between scheme elements and IEC 104 addresses:
- **cl_104_item**: measurement/control point — ASDU address, IOA, multiplier, quality, timestamp, last value
- **cl_dncalc_item**: element link — scheme element ID, 104 type code, command flag, main item flag
- **Translation table**: maps element IDs ↔ IEC 104 addresses
- CRUD via `cl_SQLite_Set` / `cl_SQLite_Object` base classes

## SendData_to_DRS() (DNCoRS_Data.cpp)
Sends calculation results to dncors_iec104:
- Packs into TLV: `TAG_CLASS_SNDDATA_to_DRS` container
- Contents: calculation ID, timestamp, per-element values (U, P, Q, I), switch states, tap positions
- Each value sent via `Poke(address, value)` → `cl_Poke_Command` → serialized → TCP send

## Data Reception
- `cl_Data_Answer` contains list of `cl_Elem_104_Value`
- Each value matched to `cl_104_item` by 40-bit address
- Multiplier applied, value stored in item
- Applied to scheme elements via `UpdateValues()`
- Can trigger automatic recalculation

## DNCoRS Regulation (DNCoRS_Data.h/.cpp)
Automated voltage control system with:
- **Modes**: Auto, Manual, ReadOnly, Voltage control, cosφ, Q control
- **States**: Idle, Running, Paused
- **Stages**: Check → Calculate → Optimize → Control
- **Calculation sequence**: `DO_CALC_SAVE → SPLIT → OPER → QMIN → QMAX → LOSS → OPTIMIZE → CONTROLL`
- Timer-driven cycle: period = `m_InStep_mSec`

## Data Quality Filter (DNCoRS_Filter.cpp)
Validates measurement quality before calculation:
- Critical measurements (busbar voltage, generator power) → calculation fails if invalid
- Lower-priority measurements → tolerated as invalid
- Quality based on IEC 104 quality descriptor

## Configuration
- **dncors_iec104 connection**: IP, port, link ID stored in application config
- **Regulation params**: stored per-scheme in `/Config/` section (BrnchReg, Unetmin/max, Qvvn, Qtol)
- **DNCoRS.ini**: application-level settings (window, display preferences)

## Source Files
| File | Purpose |
|------|---------|
| `DB_104.h / .cpp` | IEC 104 database, item classes, SQLite layer |
| `cl_104_Connector.cpp` | TCP connection, send/receive, threading |
| `DNCoRS_Data.h / .cpp` | Regulation logic, SendData_to_DRS(), calculation sequence |
| `DNCoRS_Filter.cpp` | Data quality validation |
| `cl_DNCoRS_Pnl.cpp` | DNCoRS control panel (triggers regulation cycle) |
| `cl_104_Ctrl_Dlg.cpp` | IEC 104 control dialog |
| `cl_104_Rec_Dlg.cpp` | Recording dialog |
| `cl_104_Replay_Pnl.cpp` | Replay panel |

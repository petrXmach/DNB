# EVlivy3 / DNCalc — Architecture Summary

## 1. What Is This Application?

EVlivy3 (branded as **DNCalc** in standard mode, **DNCoRS** in SCADA mode) is a C++ desktop application for designing, editing, and computing electrical power distribution networks. Think of it as a domain-specific diagram editor (similar to JointJS) combined with a power system calculation engine.

**Core capabilities:**
- Visual diagram editor for electrical networks (nodes = buses, edges = lines/transformers/switches)
- Power flow calculation via external DLL (wxbase_supp4.dll / wxbase_supp64.dll)
- Short-circuit analysis, harmonics, flicker, reliability, contingency analysis
- IEC 104 SCADA integration for real-time monitoring and voltage control (DNCoRS mode)
- Import/export in TLV (native), CSV, XML, JSON, EGC, and Bodor 3-phase formats

**Tech stack:** C++, wxWidgets 3.3.1, SQLite, OpenSSL, libcurl, libbz2, CodeBlocks build system (GCC/MSVC), 32/64-bit Windows.

**Size:** ~160 source files, ~22,000 LOC.

---

## 2. System Architecture

The application is organized into distinct layers:

```
┌─────────────────────────────────────────────────────────────┐
│                     GUI Layer (wxWidgets)                     │
│  EVlivy3Main.h/cpp, *_Dlg.cpp, *_Pnl.cpp, Cust_Property     │
├─────────────────────────────────────────────────────────────┤
│                    Application Logic                         │
│  ┌──────────┐  ┌──────────┐  ┌───────────┐  ┌───────────┐  │
│  │  Scheme   │  │ Actions  │  │ Topology  │  │Protection │  │
│  │ Container │  │Undo/Redo │  │ Analysis  │  │ System    │  │
│  └─────┬─────┘  └──────────┘  └───────────┘  └───────────┘  │
├────────┼────────────────────────────────────────────────────┤
│        │              Data Model Layer                        │
│  ┌─────┴─────┐                                               │
│  │ cl_Scheme  │──contains──▶ ElemSet_T (sorted by Z-order)   │
│  └───────────┘                    │                          │
│                    ┌──────────────┼──────────────┐           │
│              cl_Node        cl_Term_Element (base)           │
│                          ┌───┬───┬───┬───┬───┐              │
│                        Line Xfmr Switch Load Power Sync ... │
│                        (18 concrete element types)           │
├─────────────────────────────────────────────────────────────┤
│                   Infrastructure Layer                        │
│  ┌───────────┐  ┌──────────┐  ┌──────────┐  ┌───────────┐  │
│  │    TLV     │  │  IEC 104 │  │    AN    │  │  Config   │  │
│  │Serializer  │  │TCP/SCADA │  │DLL Iface │  │ INI/SQLite│  │
│  └───────────┘  └──────────┘  └──────────┘  └───────────┘  │
├─────────────────────────────────────────────────────────────┤
│                   External Dependencies                      │
│  wxWidgets │ SQLite │ OpenSSL │ libcurl │ libbz2 │ AN DLLs  │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Module Interactions & Data Flow

### 3.1 Primary Data Flows

**A. User creates/edits diagram → saves to disk:**
```
User interaction → cl_Scheme (element collection)
  → each element's Serialize() → cl_Serializer (TLV packer)
  → bzip2 compression → .tlv file on disk
```

**B. Load diagram from disk:**
```
.tlv file → bzip2 decompress → cl_Serializer (TLV unpacker)
  → CreateObjectByTag() factory → element Deserialize()
  → cl_Scheme rebuilds element collection
  → Deserialize_Done() resolves ID references → pointers
```

**C. Power flow calculation:**
```
cl_Scheme elements → cl_Calculation.PrepareData()
  → cl_OperCalc assembles AN input data
  → cl_AN_Lib / cl_AN3_Lib → DLL call (via .nap temp files)
  → DLL processes and writes output files
  → cl_OperCalc reads results
  → cl_Oper_Result / cl_Elem_Op_Result stored per element
```

**D. Real-time SCADA (DNCoRS mode):**
```
SCADA RTUs ←IEC 104→ dncors_iec104 service ←TLV/TCP→ EVlivy3
  ↕                                                      ↕
Measurements arrive → applied to scheme elements → recalculate
  → results sent back via SendData_to_DRS() → dncors_iec104
  → forwarded as IEC 104 commands to SCADA
```

### 3.2 Module Dependency Graph

```
cl_Scheme ──────▶ cl_Serializer (save/load)
    │            ▲
    │            │ (all elements implement cl_SerializableObject)
    ├──▶ cl_Topology (connectivity analysis)
    │
    ├──▶ cl_Calculation ──▶ cl_AN_Lib / cl_AN3_Lib (DLL)
    │         │
    │         └──▶ cl_OperCalc (result extraction)
    │
    ├──▶ cl_DNCoRS_Data ──▶ cl_104_Connector (TCP)
    │         │                    │
    │         └──▶ cl_104_DB ──▶ SQLite
    │
    └──▶ Export modules (CSV, XML, JSON, Bodor)
```

---

## 4. Detailed Module Descriptions

### 4.1 TLV Serialization System
*Files: `Serializable.h/.cpp`, `tlv_tag.h`, `tlv_tag.h.types`*
*Full analysis: [tlv_serialization.md](tlv_serialization.md)*

The binary serialization backbone of the entire application. Every element, every scheme, and every TCP message uses TLV format.

**TLV binary layout:**
- Each record: `[4-byte tag][4-byte length][N-byte value]` (little-endian)
- Nesting: class tags (bit 31 set = `0x80000000`) contain child TLV records
- Supports: u32, u64, double, bool, string (UTF8/UTF16), complex numbers, datetime, color, rectangles
- Files compressed with bzip2 (typically 10:1 ratio)

**Key pattern:** Every serializable element implements `cl_SerializableObject` with:
- `GetClassTag()` — returns unique TLV class tag (e.g., `TAG_CLASS_LINE = 0x80001200`)
- `Serialize(cl_Serializer&)` — writes properties as TLV attributes
- `Deserialize(cl_Serializer&, uint32_t tag, uint32_t len)` — reads TLV attributes
- `Deserialize_Done(cl_Scheme*)` — resolves ID references to pointers after all elements loaded

**Factory:** `CreateObjectByTag(tag)` — switch on tag value, returns new element instance.

**400+ tags** defined in `tlv_tag.h` covering basic types, positions, physical parameters (Un, P, Q, I, R, X, etc.), and class markers.

### 4.2 DLL Calculation Interface (AN Library)
*Files: `AN_Iface.h/.cpp`, `AN3_Iface.h/.cpp`, `cl_Calculation.h/.cpp`, `cl_OperCalc.h/.cpp`*
*Full analysis: [dll_calculation_interface.md](dll_calculation_interface.md)*

The calculation engine lives in external DLLs loaded at runtime via `wxDynamicLibrary`:
- **wxbase_supp4.dll** — single-phase calculations (cl_AN_Lib)
- **wxbase_supp64.dll** — three-phase calculations (cl_AN3_Lib)

**Communication mode: File-based** (via temporary .nap files, not memory buffers):
1. `cl_Calculation` prepares element data and writes input file
2. DLL function `ReadInpData(filename)` reads the file
3. `RunAnalysis()` performs the calculation
4. Results read back via `GetNodeData()`, `GetBranchData()`, `GetSCFaultData()`

**DLL API (resolved at runtime via function pointers):**
- `InitLibrary()` — initialize
- `ReadInpData(filename)` — read .nap input file
- `RunAnalysis()` — execute calculation
- `GetStatusDescr()` — get convergence status
- `GetNodeData(index)` / `GetBranchData(index)` — extract results

**Data preparation pipeline:**
```
cl_Scheme elements
  → cl_Calculation.PrepareData() (iterates elements, fills node/branch arrays)
  → cl_OperCalc (assembles calculation-specific input)
  → Write .nap file
  → DLL processes
  → Read results into cl_Oper_Result
     ├── cl_Node_Op_Result (voltage per node)
     └── cl_Elem_Op_Result (P, Q, I, losses per element)
```

**Calculation types:** Power flow (Newton/Gauss-Seidel/Nodal voltage), short-circuit (various fault types), harmonics, HDO, flicker, frequency analysis, reliability, economy, contingency (N-1).

**Modifiers:** Up to 16 calculation modifiers (cl_Elem_Modifier) for time slices, contingencies, regulation states.

**3-phase export (Bodor format):** `cl_Bodor_Element` hierarchy (15+ derived classes) serializes elements for 3-phase calculations with specific naming: VK (power), TR (transformer), SI (sync), PQ (load), etc.

### 4.3 IEC 104 / TCP Communication
*Files: `DB_104.h/.cpp`, `cl_104_Connector.cpp`, `DNCoRS_Data.h/.cpp`, `DNCoRS_Filter.cpp`*
*Full analysis: [iec104_tcp_communication.md](iec104_tcp_communication.md)*

When compiled with `_VOLTAGE_CTRL_` defined, the application operates as **DNCoRS** — a real-time voltage control system communicating with SCADA via the `dncors_iec104` intermediary service.

**Architecture:**
```
EVlivy3/DNCoRS ←──TLV/TCP──→ dncors_iec104 ←──IEC 60870-5-104──→ SCADA RTUs
```

**Protocol:** Custom TLV-over-TCP (not standard IEC 104 frames). Uses the same `cl_Serializer` as file I/O.

**TCP Connection (cl_104_Connector):**
- Blocking socket with `select()` timeout (100ms)
- Dedicated receive thread
- Auto-reconnect with 200ms retry
- Receive buffer: 128KB

**Communication sequence:**
1. **Connect** → TCP to dncors_iec104
2. **Init** → Exchange init commands (link ID, mode: live/replay)
3. **Register elements** → Batch register scheme elements with IEC 104 addresses (ASDU, IOA)
4. **Data exchange** → Periodic measurement polling + result pushing
5. **Calculation loop** → Receive measurements → apply to scheme → recalculate → send results

**IEC 104 Database (cl_104_DB):**
- SQLite-backed mapping between scheme element IDs and IEC 104 addresses
- `cl_104_item` — measurement/control point (address, multiplier, quality, timestamp)
- `cl_dncalc_item` — scheme element link (element ID, 104 type, command flag)

**Key method: `SendData_to_DRS()`:**
Packs calculation results (voltages, powers, switch states, tap positions) into TLV and sends to dncors_iec104 for forwarding to SCADA.

**DNCoRS regulation system:** Automated voltage control with modes (auto, manual, RO, voltage control, cosφ, Q control) operating on regulation stages (check, calculate, optimize, control).

**Data quality filter (DNCoRS_Filter):** Validates measurement quality based on element type and priority — critical measurements (busbar voltage, generator power) cause calculation failure if invalid; lower-priority measurements are tolerated.

### 4.4 Element Data Model
*Files: `cl_Scheme_Element.h/.cpp`, `TermElement.h/.cpp`, `cl_Node.h/.cpp`, all `cl_*_Element.h/.cpp`*
*Full analysis: [element_data_model.md](element_data_model.md)*

The heart of the application — 18 concrete element types representing all electrical network components.

**Inheritance hierarchy:**
```
cl_SerializableObject (TLV interface)
└── cl_Scheme_Element (base: position, name, ID, Z-order, orientation)
    ├── cl_Node (bus — manages terminal connections, nominal voltage, grounding)
    └── cl_Term_Element (base for elements with terminals)
        ├── cl_Deviation_Element (adds uncertainty parameters)
        │   ├── cl_Power_Element (grid infeed — Ik, ψk, Sn)
        │   ├── cl_Load_Element (consumption — P, Q, cosφ, flicker, asymmetry)
        │   └── cl_PhotoVolt_Element (PV inverter — regulation, harmonics)
        ├── cl_CircleTerm_Element (rotating machines)
        │   ├── cl_Sync_Element (sync generator/motor — Xd, X'd, X''d, Pmin/Pmax)
        │   └── cl_Async_Element (induction machine — motor/generator/wind)
        ├── cl_MultiTerm_Element (2+ connection points)
        │   ├── cl_Line_Element (cable/overhead — R, X, B per km, length)
        │   ├── cl_Transformer_Element (2-winding — U1/U2, Sn, Uk, tap regulation)
        │   ├── cl_Transformer3_Element (3-winding — extends Transformer)
        │   ├── cl_Switch_Element (breaker — open/closed, reliability)
        │   ├── cl_Reactor_Element (series impedance)
        │   └── cl_FuseRack_Element (up to 6 terminals)
        └── Single-terminal elements:
            ├── cl_Gate_Element (capacitor bank — Q, detuning)
            ├── cl_Choke_Element (Petersen coil — Q, R/X ratio)
            ├── cl_CurrSrc_Element (thyristor converter — harmonics)
            ├── cl_HDO_Src_Element (audio frequency source)
            ├── cl_Accumulation_Element (battery storage)
            ├── cl_MicroCoGen_Element (micro-cogeneration mixin)
            └── cl_Text_Element (annotation — 0 terminals)
```

**Connection model:**
- Elements connect to `cl_Node` instances via indexed terminals (0, 1, 2...)
- `cl_Node` maintains `TermElemMap_T` — maps terminal indices to connected elements
- Connections serialized as `cl_Term_Conn_Hlp` (terminal index + node ID)
- During deserialization, ID references resolved to pointers in `Deserialize_Done()`

**Common element properties (cl_Scheme_Element):**
- ID (uint32), Name (string), Position (X, Y, Z), Orientation (0°/90°/180°/270°)
- Visibility, selection state, value display mode
- Virtual methods: `AddData()` (for calculation), `GetValue()`/`SetValue()` (generic property access)

**Regulation interfaces:**
- `I_Regulation_Interface` — implemented by transformers (tap control) and inverter-based elements (Q(U), cosφ)
- `cl_Inverter_Regulation` — regulation for PV, sync, async machines

### 4.5 Configuration & Database
*Files: `Configuration.h/.cpp`, `DNCalc.ini`, `DB_Objects.h/.cpp`*
*Full analysis: [element_data_model.md](element_data_model.md) (sections 8-9)*

**Configuration classes:** Hierarchy of `cl_*_Config` classes reading/writing INI files via `wxFileConfig`:
- `cl_Applic_Config` — application paths, defaults, feature toggles
- `cl_ShortCircuit_Config` — short-circuit calculation parameters
- `cl_OperResult_Config` — result display options
- `cl_PhasorGr_Config` — phasor graph preferences
- `cl_Topo_Config` — topology display settings

**Database objects:** SQLite-backed component library:
- `cl_DB_Line_Element`, `cl_DB_Xformer_Element`, etc. — pre-defined component types
- Used to fill scheme elements from standardized catalogs
- Each wraps a SQLite table row with `LoadObj()`, `GetTableName()`, `Edit()` methods

### 4.6 Scheme Container
*Files: `cl_Scheme.h/.cpp`*

`cl_Scheme` is the top-level container:
- `ElemSet_T` — sorted set of all elements (sorted by Z-axis for rendering order)
- Calculation settings: method (Newton/Gauss-Seidel/Nodal), accuracy, epsilon, max iterations
- Feature flags: reliability, OPF, protections, time slices, 3-phase
- ID series management for automatic element naming
- Canvas/grid settings
- Undo/redo support via `cl_Scheme_Action` hierarchy

### 4.7 Topology Analysis
*Files: `cl_Topology.h/.cpp`*

Determines network connectivity:
- Identifies electrically connected islands
- Assigns power domains (voltage-level color coding)
- Validates scheme structure before calculation
- Detects isolated subsystems

### 4.8 Export/Import
*Files: `cl_Scheme_CSV_Export.cpp`, `cl_Scheme_XML_Export.cpp`, `cl_Scheme_JSON_Export.cpp`, `cl_Scheme_EGC_Import.cpp`, `Elem_3ph_Export.h/.cpp`*

Multiple export formats:
- **TLV** — native binary format (primary)
- **CSV** — tabular export of calculation results
- **XML** — structured export
- **JSON** — modern structured export
- **EGC** — legacy format import
- **Bodor** — 3-phase calculation format (15+ element type classes)

---

## 5. Cross-Cutting Concerns

### 5.1 Conditional Compilation
The application has two main personalities controlled by `_VOLTAGE_CTRL_`:
- **DNCalc** (undefined) — standard offline calculation tool
- **DNCoRS** (defined) — real-time SCADA voltage control system

Additional feature flags:
- `LIM_PROTECTION` — overcurrent protection simulation
- `_BATT_STORAGE_` — battery/accumulation elements
- `LIM_MicroCoGen` — micro-cogeneration elements

### 5.2 Error Handling
- `cl_BaseException` extends `std::exception` with file/line/function info in debug mode
- Macro `BASE_EXCEPTION(message)` for consistent exception creation
- Calculation errors reported via `StatusDescr_T` from DLL
- IEC 104 data quality filtering prevents calculations with invalid measurements

### 5.3 Serialization as Integration Point
TLV serialization is the universal data format:
- File persistence (scheme save/load with bzip2 compression)
- TCP communication (IEC 104 data exchange)
- Clipboard operations (copy/paste elements)
- All 18 element types + scheme container implement `cl_SerializableObject`

### 5.4 Undo/Redo
`Action.h/.cpp` provides a comprehensive undo/redo framework with 16+ action types (insert, delete, move, reconnect, rotate, property change, group operations).

### 5.5 Localization
gettext-based i18n with translations for Czech (cs), English (en), and Bulgarian (bg).

---

## 6. External Dependencies Summary

| Dependency | Purpose | Integration |
|---|---|---|
| wxWidgets 3.3.1 | GUI framework | System/linked |
| wxbase_supp4.dll / wxbase_supp64.dll | Power flow calculation engine | Runtime DLL loading |
| SQLite | Component database, IEC 104 data | Via wxLib/sqlite/ |
| OpenSSL | Encryption (SCADA communication) | Static linking |
| libcurl | HTTP communication | Static linking |
| libbz2 | TLV file compression | Static linking |

---

## 7. Key Design Patterns

| Pattern | Where Used |
|---|---|
| Factory | `CreateObjectByTag()` — instantiates elements by TLV tag during deserialization |
| Visitor-like | `AddData(Calc_Type_T)` — elements export data for different calculation contexts |
| Command | `cl_Scheme_Action` hierarchy — undo/redo with 16+ action types |
| Observer | wxWidgets event system — scheme changes propagate to UI |
| Strategy | `I_Regulation_Interface` — different regulation algorithms per element |
| Template Method | `cl_Calculation.PrepareData()` — base preparation with element-specific overrides |
| Bridge | `cl_AN_Lib` / `cl_AN3_Lib` — runtime-loaded DLL with function pointers |

---

## 8. Notable Architectural Decisions

1. **File-based DLL communication**: The AN library communicates via temporary .nap files rather than in-memory buffers. This simplifies the interface but adds I/O overhead.

2. **TLV as universal format**: Using the same binary format for both file persistence and TCP communication reduces code duplication but couples the two concerns.

3. **Flat source structure**: All ~100 source files in the project root, with only headers split between root/ and include/. No src/ subdirectory hierarchy.

4. **Dual-mode application**: The same codebase produces both DNCalc (offline calculator) and DNCoRS (real-time SCADA controller) via conditional compilation, sharing the element model and calculation engine.

5. **Element-centric architecture**: Each element type encapsulates its own serialization, calculation data preparation, property editing, and rendering — highly cohesive but large classes.

6. **ID-based references**: Elements reference each other by uint32 IDs (not pointers), enabling clean serialization/deserialization with a post-load resolution pass.

---

## 9. Sub-Projects

| Project | Location | Purpose |
|---|---|---|
| **DB_Edit** | `DB_Edit/` | Standalone database editor for power system components (lines, nodes, transformers) |
| **EGC_Convert** | `EGC_Convert/` | EGC format converter tool |
| **dncors_iec104** | `../dncors_iec104/` | IEC 104 SCADA gateway service (separate project, communicates via TCP) |

---

## 10. Files Excluded from Analysis

The following were identified but excluded from deep analysis:
- **GUI/visual layer**: All `*_Dlg.cpp`, `*_Pnl.cpp`, `EVlivy3Main.cpp`, Paint/Draw methods
- **SMU Interface**: `SMU_Interface.h/.cpp`, `SMU.ini` — standalone Modbus TCP server, separate from IEC 104/SCADA flow
- **Sub-projects**: `DB_Edit/`, `EGC_Convert/` — separate applications
- **Third-party libraries**: `OpenSSL/`, `curl/`, `libbz2/`, `wxLib/sqlite/`

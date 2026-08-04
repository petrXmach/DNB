# EVlivy3/DNCalc — AI System Context (L0)
# Include this document in EVERY prompt when working with this project.

## Project Identity
- **Name**: EVlivy3 (branded DNCalc / DNCoRS)
- **Language**: C++ (CodeBlocks, GCC/MSVC, Windows 32/64-bit)
- **GUI**: wxWidgets 3.3.1
- **Purpose**: Electrical power network diagram editor + calculation engine
- **LOC**: ~22,000 lines, ~160 source files
- **Dual mode**: DNCalc (offline calculator) vs DNCoRS (real-time SCADA, `_VOLTAGE_CTRL_` defined)

## Module Map
| Module | Role | Key Classes |
|--------|------|-------------|
| Element Model | 18 electrical element types (nodes + edges) | `cl_Scheme_Element`, `cl_Term_Element`, `cl_Node`, `cl_Line_Element`, `cl_Transformer_Element`, etc. |
| Scheme Container | Holds all elements, calc settings, canvas | `cl_Scheme`, `ElemSet_T` |
| TLV Serialization | Binary format for file I/O and TCP | `cl_Serializer`, `cl_SerializableObject`, tags in `tlv_tag.h` |
| DLL Calculation | Power flow via external DLL (.nap files) | `cl_AN_Lib`, `cl_AN3_Lib`, `cl_Calculation`, `cl_OperCalc` |
| IEC 104 / TCP | SCADA communication (DNCoRS mode only) | `cl_104_DB`, `cl_DNCoRS_Data`, `cl_104_Connector` |
| Configuration | INI files + SQLite component DB | `cl_EVlivy3_Config`, `cl_DB_Objects` |
| Export/Import | CSV, XML, JSON, EGC, Bodor formats | `cl_Scheme_*_Export`, `cl_Bodor_Element` |
| Topology | Network connectivity, power domains | `cl_Topology` |

## Element Types (18 total)
Node, Line, Transformer (2W), Transformer (3W), Switch, Load, Power (grid infeed), Sync machine, Async machine, PhotoVolt, Gate (capacitor bank), Reactor, Choke (Petersen coil), Current source, HDO source, Text, Accumulation (battery), FuseRack, MicroCoGen (mixin).

## Data Flow (5 key paths)
1. **Save/Load**: Elements ↔ TLV serialization ↔ bzip2 ↔ .tlv file
2. **Calculation**: Elements → cl_Calculation → .nap file → DLL → results → cl_OperCalc
3. **SCADA in**: dncors_iec104 → TLV/TCP → measurements applied to elements → recalculate
4. **SCADA out**: Calculation results → SendData_to_DRS() → TLV/TCP → dncors_iec104
5. **Export**: Elements → CSV/XML/JSON/Bodor format

## Key Patterns
- All elements inherit `cl_SerializableObject` (TLV serialize/deserialize)
- Elements connect to `cl_Node` via indexed terminals (ID-based references, resolved post-load)
- DLL communication is file-based (.nap temp files), not memory-based
- Factory pattern: `CreateObjectByTag(uint32_t)` instantiates elements by TLV class tag
- Each element implements `AddData(Calc_Type_T)` to export itself for specific calculation types

## Terminology
| Term | Meaning |
|------|---------|
| TLV | Tag-Length-Value binary format (4B tag + 4B length + NB value) |
| AN library | ActiveNEST — the external DLL calculation engine |
| .nap file | Temporary file for DLL input/output data |
| Bodor | 3-phase element export format (named element codes: VK, TR, SI, PQ...) |
| DNCoRS | Voltage control mode with IEC 104 SCADA integration |
| Power domain | Group of connected elements at same voltage level |
| ASDU/IOA | IEC 104 addressing (Application Service Data Unit / Information Object Address) |
| ElemSet_T | Sorted set of scheme elements (by Z-order) |
| Scheme | The complete network diagram (cl_Scheme) |
| Terminal | Indexed connection point on an element (0, 1, 2...) |

## Source File Layout
- Root: all .h and .cpp (flat structure, no src/ hierarchy)
- `include/`: core data model headers (elements, scheme, serialization, topology)
- `wxLib/`: shared wxWidgets utilities + SQLite bindings
- `AN_dll/`, `AN_dll64/`: calculation DLLs
- `analysis/`: project analysis documents (this file)

## Excluded from Analysis
- GUI/visual layer (*_Dlg.cpp, *_Pnl.cpp, Paint/Draw methods)
- SMU Interface (standalone Modbus TCP, separate from SCADA — files: `SMU_Interface.h/.cpp`, `SMU.ini`)
- Sub-projects: DB_Edit/, EGC_Convert/

## When to Include Additional Context
Include L1 documents when working on specific modules:

### Serialization / File I/O
→ Include `L1_tlv_serialization.md` when: implementing save/load, defining new serializable types, working with binary format
→ Include `L2_tlv_tags.md` when: adding new TLV tags, debugging serialization, mapping tag values

### Calculation / DLL
→ Include `L1_dll_calculation.md` when: preparing calculation data, reading results, interfacing with DLL
→ Include `L2_data_structures.md` when: working with specific struct layouts, DLL function signatures

### SCADA / IEC 104
→ Include `L1_iec104_communication.md` when: implementing TCP communication, working with measurements, SCADA integration

### Element Model
→ Include `L1_element_model.md` when: adding/modifying element types, working with connections, properties
→ Include `L2_element_properties.md` when: implementing specific element's properties, serialization tags, enums

### Configuration
→ Include `L1_configuration.md` when: working with settings, INI files, database component catalog

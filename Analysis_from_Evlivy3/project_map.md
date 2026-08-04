# EVlivy3 — Project Structure Map

## Overview

**Project**: EVlivy3 (also branded as DNCalc)
**Type**: C++ desktop application (CodeBlocks project, GCC/MSVC)
**GUI**: wxWidgets
**Purpose**: Electrical network diagram editor with power flow calculations, short-circuit analysis, IEC 104 SCADA communication, and protection simulation
**Estimated LOC**: ~22,000 lines (headers + sources)
**Scheme file version**: 0x00010005
**Build targets**: 32-bit and 64-bit (Debug/Release, GCC/MSVC)

---

## Directory Tree

```
EVlivy3/
├── *.h, *.cpp              # Main application source (root)
├── include/                 # Core data model headers (elements, scheme, serialization)
├── wxLib/                   # Shared wxWidgets utilities (BaseObjects, validators, graph, list ctrl)
│   └── sqlite/              # SQLite wx bindings
├── AN_dll/                  # 32-bit AN calculation DLLs (wxbase_supp4.dll)
├── AN_dll64/                # 64-bit AN calculation DLLs (wxbase_supp64.dll)
├── DB_Edit/                 # Sub-project: database editor for power elements
├── EGC_Convert/             # Sub-project: EGC format converter
├── OpenSSL/                 # OpenSSL headers + libraries
│   ├── include/openssl/     # 125+ OpenSSL headers
│   └── lib/                 # Pre-built libraries
├── curl/                    # libcurl headers + libraries
│   ├── inc/                 # Headers
│   └── lib/                 # GCC and MSVC builds
├── libbz2/                  # bzip2 compression library
│   ├── 32/                  # 32-bit build
│   └── 64/                  # 64-bit build
├── bin/                     # Build output
│   ├── Debug-GCC-820-64/    # GCC debug (DNCalc.exe)
│   ├── Debug-VC-820-64/     # MSVC debug (DNCoRS.exe)
│   ├── Release-GCC-820-64/
│   └── Release-VC-820-64/
├── obj/                     # Intermediate object files
├── PQ_Diagram/              # PQ diagram template files (*.tlv)
├── Doc/                     # Documentation (xlsx, pdf, docx, odt)
├── TDD/                     # Test data (xlsx, csv, db3)
├── NSIS/                    # Installer scripts
├── doxygen/                 # Doxygen config
├── ico/                     # Application icons
├── bg/, cs/, en/            # Localization (.mo files)
├── Geo_cache/               # Map tile cache
├── gettext/                 # Translation infrastructure
├── wxsmith/                 # wxSmith GUI designer files
├── VC-Specific/             # MSVC-specific build files
├── Test_Back/               # Test backup data
└── analysis/                # ← Analysis output (this document)
```

---

## Logical Module Grouping

### 1. ELEMENT MODEL (Data Structures)
Core electrical element classes — each element represents a node or edge in the network diagram.

| File (include/) | Class | Element Type |
|---|---|---|
| cl_Scheme_Element.h | `cl_Scheme_Element` | Abstract base for all elements |
| TermElement.h | `cl_Term_Element`, `cl_MultiTerm_Element`, `cl_CircleTerm_Element`, `cl_Deviation_Element`, `cl_Shadow_Element` | Terminal/connection base classes |
| cl_Node.h | `cl_Node` | Network bus/node |
| cl_Line_Element.h | `cl_Line_Element` | Transmission/distribution line |
| cl_Transformer_Element.h | `cl_Transformer_Element` | 2-winding transformer |
| cl_Transformer3_Element.h | `cl_Transformer3_Element` | 3-winding transformer |
| cl_Switch_Element.h | `cl_Switch_Element` | Switch/breaker |
| cl_Load_Element.h | `cl_Load_Element` | Load/consumption |
| cl_Power_Element.h | `cl_Power_Element` | Power source (grid infeed) |
| cl_Sync_Element.h | `cl_Sync_Element` | Synchronous machine |
| cl_Async_Element.h | `cl_Async_Element` | Asynchronous machine |
| cl_PhotoVolt_Element.h | `cl_PhotoVolt_Element` | Photovoltaic source |
| cl_Gate_Element.h | `cl_Gate_Element` | Capacitor bank / compensation |
| cl_Reactor_Element.h | `cl_Reactor_Element` | Series reactor |
| cl_Choke_Element.h | `cl_Choke_Element` | Earthing reactor / Petersen coil |
| cl_CurrSrc_Element.h | `cl_CurrSrc_Element` | Current source (thyristor converter) |
| cl_HDO_Src_Element.h | `cl_HDO_Src_Element` | HDO signal source |
| cl_Text_Element.h | `cl_Text_Element` | Text annotation (no terminals) |

**Corresponding .cpp files**: `cl_*_Element.cpp`, `TermElement.cpp`, `cl_Node.cpp`, `cl_Node_GUI.cpp` in root

### 2. SCHEME CONTAINER
The diagram/schema as a whole — collection of elements, calculation settings, canvas.

| File | Class | Role |
|---|---|---|
| include/cl_Scheme.h | `cl_Scheme` | Main scheme container, element collection, calc settings |
| cl_Scheme.cpp | | Implementation |
| cl_Scheme_Pnl.cpp | | Scheme panel (visual — canvas rendering) |
| cl_Scheme_Config_Dlg.cpp | | Scheme configuration dialog |
| cl_SchemeInfo_Dlg.cpp | | Scheme info dialog |

### 3. TLV SERIALIZATION
Tag-Length-Value format for saving/loading schemes and data transfer.

| File | Class | Role |
|---|---|---|
| include/Serializable.h | `cl_Serializer`, `cl_SerializableObject` | Core TLV serializer — pack/unpack all types, bzip2 compression, file I/O |
| include/tlv_tag.h | — (constants) | Tag definitions: basic types, positions, physical params, class markers |
| include/tlv_tag.h.types | — | Additional type definitions |
| Serializable.cpp | | Implementation |
| include/common.h | — | Scheme file version, common includes |

### 4. IEC 104 / TCP COMMUNICATION (DNCoRS)
Communication with dncors_iec104 SCADA service.

| File | Class | Role |
|---|---|---|
| DB_104.h | `cl_104_DB`, `cl_104_item`, `cl_dncalc_item`, `cl_104_Grid` | IEC104 database, item mapping, SQLite storage |
| DB_104.cpp | | Implementation |
| DNCoRS_Data.h | `cl_DNCoRS_Data` | Data manager for voltage control / regulation |
| DNCoRS_Data.cpp | | Implementation, `SendData_to_DRS()` |
| cl_104_Connector.cpp | | TCP connection to dncors_iec104 |
| cl_104_Ctrl_Dlg.cpp | | IEC104 control dialog |
| cl_104_Interface_item_Dlg.cpp | | Interface item editing |
| cl_104_Rec_Dlg.cpp | | Recording dialog |
| cl_104_Replay_Pnl.cpp | | Replay panel |
| cl_104_Sel_Dlg.cpp | | Selection dialog |
| cl_DNCoRS_Pnl.cpp | | DNCoRS control panel |
| DNCoRS.ini | | DNCoRS connection settings |
| SMU_Interface.h / .cpp | `cl_SMU_Interface` | SMU (Station Management Unit) interface |
| SMU.ini | | SMU settings |

### 5. DLL CALCULATION INTERFACE
Interface to wxbase_supp4.dll / wxbase_supp64.dll (AN = ActiveNEST library).

| File | Class | Role |
|---|---|---|
| AN_Iface.h | `cl_AN_Lib` | Single-phase AN library interface: InitLibrary, ReadInpData, RunAnalysis |
| AN_Iface.cpp | | Implementation |
| AN3_Iface.h | `cl_AN3_Lib` | 3-phase AN library interface: node/branch/SC data |
| AN3_Iface.cpp | | Implementation |
| include/cl_Calculation.h | `cl_Calculation` | Data preparation framework for AN library |
| cl_Calculation.cpp | | Preparing element data → AN input |
| include/cl_OperCalc.h | `cl_OperCalc`, `cl_DeltaOperCalc` | Operational calculation, result extraction |
| cl_OperCalc.cpp | | Implementation |

### 6. SPECIALIZED CALCULATIONS
Various electrical engineering calculations built on top of the AN interface.

| File | Role |
|---|---|
| cl_ShortCircCalc.cpp | Short-circuit calculation |
| cl_ShortImpCalc.cpp | Short-circuit impedance |
| cl_InnerImpCalc.cpp | Inner impedance calculation |
| cl_HarmCalc.cpp, cl_HarmPolCalc.cpp | Harmonics analysis |
| cl_HDO_Calc.cpp, cl_HDO_Shrt_Calc.cpp | HDO (audio frequency) calculations |
| cl_Flikr_Calc.cpp | Flicker calculation |
| cl_FreqCalc.cpp | Frequency calculation |
| cl_EconomyCalc.cpp | Economy / loss evaluation |
| cl_LossCalc.cpp | Loss calculation |
| cl_LoadConnCalc.cpp | Load connection calculation |
| cl_ReliabilityCalc.cpp | Reliability analysis |
| cl_Contingency_Calc.cpp | N-1 contingency analysis |
| cl_Power_DivisionCalc.cpp | Power division |
| cl_Time_SliceCalc.cpp | Time-slice simulation |
| cl_MaxP_Solver.cpp | Maximum power solver |
| cl_NAP_Solver.cpp | NAP (network access point) solver |
| cl_AsymCalc.cpp | Asymmetric calculation |
| cl_Protect1_Calc.cpp | Protection calculation |
| cl_ControllQ.cpp | Reactive power control |
| cl_PQ_Split.cpp, cl_PQ_Diag.cpp | PQ diagram analysis |
| cl_Calc_Test.cpp | Calculation testing |

### 7. EXPORT / IMPORT
Scheme export/import in various formats.

| File | Role |
|---|---|
| cl_Scheme_CSV_Export.cpp | CSV export |
| cl_Scheme_XML_Export.cpp | XML export |
| cl_Scheme_JSON_Export.cpp | JSON export |
| cl_Scheme_EGC_Import.cpp | EGC format import |
| Elem_3ph_Export.h / .cpp | 3-phase element export (Bodor format) |
| cl_Clipboard.cpp | Clipboard operations (copy/paste elements) |

### 8. DATABASE LAYER
SQLite-based component databases.

| File | Class | Role |
|---|---|---|
| DB_Objects.h / .cpp | `cl_DB_Line_Element`, `cl_DB_Xformer_Element`, etc. | Power component DB objects |
| cl_LoadFromDB_Dlg.cpp | | Load element from database dialog |
| wxLib/sqlite/ | | SQLite wx bindings |

### 9. CONFIGURATION
Application and calculation settings.

| File | Class | Role |
|---|---|---|
| Configuration.h / .cpp | `cl_EVlivy3_Config`, `cl_Applic_Config`, etc. | INI file config management |
| DNCalc.ini | | Main application settings |
| DNCoRS.ini | | DNCoRS connection settings |
| SMU.ini | | SMU settings |
| cl_Settings_Dlg.cpp | | Settings dialog (UI) |
| cl_StartUp.cpp | | Application startup / initialization |

### 10. PROTECTION SYSTEM
Overcurrent and other protection simulation.

| File | Role |
|---|---|
| Protection.h / .cpp | Protection base classes |
| OvrCurrProtection.h / .cpp | Overcurrent protection |
| cl_NewProtection.cpp | New protection implementation |
| cl_CurrProt_Flash_Dlg.cpp | Flash protection dialog |
| cl_CurrProt_Time_Dep_Dlg.cpp | Time-dependent protection |
| cl_CurrProt_Time_Ind_Dlg.cpp | Time-independent protection |

### 11. TOPOLOGY
Network topology analysis (connectivity, islands, power domains).

| File | Class | Role |
|---|---|---|
| include/cl_Topology.h | `cl_Topology` | Topology analysis |
| cl_Topology.cpp | | Implementation |

### 12. UNDO / REDO
Action history for scheme editing.

| File | Class | Role |
|---|---|---|
| Action.h / .cpp | `cl_Scheme_Action`, `cl_Group_Action`, 16+ action types | Undo/redo framework |

### 13. APPLICATION FRAMEWORK (UI — not in scope for deep analysis)

| File | Role |
|---|---|
| EVlivy3App.h / .cpp | wxApp — application entry point |
| EVlivy3Main.h / .cpp | Main frame, menus, toolbars, panels |
| Cust_Property.h / .cpp | Custom property grid editors |
| CalcErrElems.h / .cpp | Calculation error display |
| Misconf_Elements.h / .cpp | Misconfigured element display |
| Other_Stuff.h / .cpp | Miscellaneous utilities |
| cl_Log.cpp | Logging |
| Various *_Dlg.cpp, *_Pnl.cpp | Dialogs and panels |

### 14. SHARED LIBRARY (wxLib/)

| File | Class | Role |
|---|---|---|
| BaseObjects.h / .cpp | `cl_BaseObject`, `cl_OpData` | Base object framework |
| wxValidators.h / .cpp | `wxIntValidator`, `wxU32Validator`, etc. | Input validators |
| cl_Graph.h / .cpp | `cl_Graph` | Graph/plot rendering |
| MyCtrlCommon.h | | Common control definitions |
| MyListCtrl.h / .cpp | | Custom list control |

---

## Sub-Projects

| Project | Location | Purpose |
|---|---|---|
| DB_Edit | `DB_Edit/` | Standalone database editor for power system components (lines, nodes, transformers) |
| EGC_Convert | `EGC_Convert/` | EGC format converter tool |

---

## Key External Dependencies

| Library | Location | Used For |
|---|---|---|
| wxWidgets 3.3.1 | System / linked | GUI framework |
| wxbase_supp4.dll / wxbase_supp64.dll | AN_dll/ AN_dll64/ | Power flow calculation engine (AN library) |
| SQLite | wxLib/sqlite/ | Component database, IEC104 data |
| OpenSSL | OpenSSL/ | Encryption (likely for TCP/SCADA communication) |
| libcurl | curl/ | HTTP communication |
| libbz2 | libbz2/ | TLV file compression |

---

## Conditional Compilation

The macro `_VOLTAGE_CTRL_` switches between two application modes:
- **Not defined**: DNCalc mode — standard network calculation tool
- **Defined**: DNCoRS mode — voltage control / SCADA integration mode (additional IEC104 tags, regulation features)

---

## Data Flow Summary (High Level)

```
[Scheme Elements] --serialize/deserialize--> [TLV file on disk]
[Scheme Elements] --prepare data--> [cl_Calculation] --DLL call--> [AN Library] --results--> [cl_OperCalc]
[Scheme Elements] --IEC104 mapping--> [cl_104_DB] --TCP/TLV--> [dncors_iec104 service] <--> [SCADA]
[Scheme Elements] --export--> [CSV / XML / JSON / Bodor 3-phase format]
[DB components]   --SQLite-->  [cl_DB_Objects] --fill--> [Scheme Elements]
```

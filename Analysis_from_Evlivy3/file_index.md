# EVlivy3 — Header File Index

Every `.h` file in the project root, `include/`, and `wxLib/` directories. For each: filename, main class/struct names, and a brief description of responsibilities.

---

## Root Directory Headers

### AN3_Iface.h
**Classes**: `cl_AN3_Lib`, structs `StatusDescr3_T`, `T3_PHASE_T`, `T4_PHASE_T`
Interface to the 3-phase AN calculation library (wxbase_supp64.dll). Defines data structures for 3-phase node data, branch data, short-circuit fault data, and impedance results. Provides methods like `GetNodeData`, `GetBranchData`, `GetSCFaultData`, `ReadNodeData`, `ReadBranchData` for exchanging data with the DLL.

### AN_Iface.h
**Classes**: `cl_AN_Lib`, struct `StatusDescr_T`
Interface to the single-phase AN calculation library (wxbase_supp4.dll). Simpler than AN3_Iface — provides `InitLibrary`, `ReadInpData`, `RunAnalysis`, `GetStatusDescr` for basic power flow and short-circuit calculations.

### Action.h
**Classes**: `cl_Scheme_Action` (base), `cl_Group_Action`, `cl_Insert_Action`, `cl_Add_Action`, `cl_Delete_Action`, `cl_Move_Action`, `cl_Reconnect_Action`, and 10+ more specific action classes
Undo/redo framework for scheme editing operations. Each action class implements `Undo()` and `Redo()` with optional `PreUndo`/`PostUndo`/`PreRedo`/`PostRedo` hooks. Covers element insertion, deletion, movement, reconnection, rotation, property changes, and group operations.

### CalcErrElems.h
**Classes**: `cl_CalcErrElems_Rept`, `cl_CalcErr_Elem_Glue`
Custom list control panel for displaying elements that have calculation errors. Extends `MyListCtrl` with columns for element name and error description. Supports double-click navigation to the offending element.

### Configuration.h
**Classes**: `cl_EVlivy3_Config`, `cl_ShortCircuit_Config`, `cl_OperResult_Config`, `cl_PhasorGr_Config`, `cl_Topo_Config`, `cl_Applic_Config`
Application and calculation configuration management. Reads/writes INI files via `wxFileConfig`. Stores short-circuit calculation parameters, operational result display options, phasor graph preferences, topology display settings, and general application settings (paths, defaults, feature toggles).

### Cust_Property.h
**Classes**: `cl_EnumProperty`, `cl_CheckProperty`, `cl_IntProperty`, `cl_FloatProperty`, `cl_ColourProperty`
Custom wxPropertyGrid properties with callback support. Each property triggers an `OnSetValue()` callback when modified, enabling live updates to element parameters in the scheme editor.

### DB_104.h
**Classes**: `cl_SQLite_Set`, `cl_SQLite_Object`, `cl_104_item`, `cl_dncalc_item`, `cl_104_DB`, `cl_104_Grid`
IEC 104 protocol database layer. Manages the mapping between IEC 104 data points and scheme elements via SQLite. `cl_104_item` represents a single IEC 104 measurement/control point. `cl_104_DB` handles loading/saving sets of mappings. `cl_104_Grid` provides a grid view for editing mappings.

### DB_Objects.h
**Classes**: `cl_DB_Voltage`, `cl_DB_Line_Type`, `cl_DB_Company`, `cl_DB_Producer`, `cl_DB_Winding`, `cl_DB_Element`, `cl_DB_Line_Element`, `cl_DB_Xformer_Element`, and more
Power system component database objects. Each class wraps a SQLite table row for a specific component type (voltage level, line type, transformer winding, etc.). Provides `LoadObj()`, `GetTableName()`, `GetColumns()`, `Edit()`, `FillNew()` for CRUD operations.

### DNCoRS_Data.h
**Classes**: `cl_DNCoRS_Data`
Data manager for DNCoRS (voltage control) mode. Handles regulation modes, states, and stages. Key method `SendData_to_DRS()` sends calculation data to the dncors_iec104 service. Contains enumerations for regulation parameters (`RegMode`, `RegState`, `RegStage`) and methods `Init()`, `SetParameter()`, `SetElement()`, `FillVirtPQ()`.

### Elem_3ph_Export.h
**Classes**: `cl_Bodor_Element` (base), plus 15+ derived: `cl_Bodor_VK`, `cl_Bodor_TR`, `cl_Bodor_T3`, `cl_Bodor_SI`, `cl_Bodor_PQ`, `cl_Bodor_ZQ`, `cl_Bodor_FV`, `cl_Bodor_M3`, `cl_Bodor_SG`, `cl_Bodor_VY`, `cl_Bodor_RE`, `cl_Bodor_L3`, `cl_Bodor_KB`, `cl_Bodor_QK`, `cl_Bodor_ZN`
Export framework for converting scheme elements to the Bodor 3-phase calculation input format. Each derived class implements `DoExport()` to serialize one element type's parameters for the 3-phase AN library.

### EVlivy3App.h
**Classes**: `EVlivy3App` (wxApp)
Application entry point. Handles initialization (`OnInit`), locale setup, data directory creation, and command-line path validation. Entry point for both DNCalc and DNCoRS modes.

### EVlivy3Main.h
**Classes**: `EVlivy3Frame` (wxFrame), `cl_Timer`, `cl_PerspectiveRec`, `cl_MyLogBuffer`, `cl_ToggleBitmap`, `EVlivy3_StatusBar`, `cl_SchemeBook`, `cl_ToolBook`
Main application frame — the largest header (~1127 lines). Defines 100+ menu IDs and event handlers. Manages toolbooks, property grids, notebooks, scheme tabs, status bar, and the overall application workflow. Contains `Do_Calculation()`, `UpdateMenu()`, `UpdateScheme()`. Not in scope for deep analysis (UI layer).

### Link_Header.h
Minimal include forwarding header (4 lines). Just includes another header.

### Misconf_Elements.h
**Classes**: `cl_MisConf_Rept`, `cl_MisConf_Elem_Glue`
List control for misconfigured scheme elements. Similar to `CalcErrElems.h` — provides a panel showing elements with configuration issues, with double-click navigation and search functionality.

### Other_Stuff.h
Miscellaneous utility declarations. Contains helper functions and small classes that don't fit into other modules.

### OvrCurrProtection.h
Overcurrent protection classes. Defines protection device models for overcurrent relay simulation and coordination.

### Protection.h
Base protection framework classes. Provides abstract interfaces for protection device modeling used by `OvrCurrProtection` and other protection types.

### Regulation.h
Voltage regulation classes. Defines regulation interfaces for transformer tap changers, inverter regulation (Q(U), cos(φ) control), used by `cl_Transformer_Element`, `cl_PhotoVolt_Element`, `cl_Sync_Element`, etc.

### SMU_Interface.h
**Classes**: `cl_SMU_Interface`
Station Management Unit interface. Handles communication with SMU devices, likely via TCP. Configuration stored in `SMU.ini`.

### version.h
Application version constants. Defines version number, build date, and version string for the application.

### wx_pch.h
Precompiled header for wxWidgets. Standard includes for wxWidgets compilation acceleration.

---

## include/ Directory Headers

### common.h
Project-wide common definitions and includes. Pulls in `tlv_tag.h`, wxWidgets headers, memory utilities. Defines application name (`DNCalc` or `DNCoRS` based on `_VOLTAGE_CTRL_`), scheme file version (0x00010005), physical constants (PI, NET_FREQ=50, SQRT3), voltage limit constants, grid/canvas defaults, and internationalization macros.

### tlv_tag.h
TLV (Tag-Length-Value) serialization tag constants. Defines tags for all serializable data types: basic types (u32, u64, dbl, bool, string, datetime, color), position tags (X, Y, Z), rectangle tags, physical parameter tags (Un, U1, U2, Qk, S, P, Q, I, etc.). Key constant `TLV_CLASS = 0x80000000` marks class-type tags. Extended tags for DNCoRS mode when `_VOLTAGE_CTRL_` is defined.

### Serializable.h
**Classes**: `cl_Serializer`, `cl_SerializableObject`
Core TLV serialization framework. `cl_Serializer` handles binary packing/unpacking of all basic types, complex numbers, dates, and wxStrings (UTF8/UTF16). Supports bzip2 compression for file storage. Provides file I/O methods. `cl_SerializableObject` is the pure virtual interface all serializable elements implement. Global factory function `CreateObjectByTag(uint32_t nTag)` instantiates elements by their TLV class tag.

### Exceptions.h
**Classes**: `cl_BaseException`
Exception class extending `std::exception`. In debug mode captures file, line, and function info. Macro `BASE_EXCEPTION(message)` for consistent exception creation across the codebase.

### TermElement.h
**Classes**: `cl_Term_Element`, `cl_Deviation_Element`, `cl_CircleTerm_Element`, `cl_MultiTerm_Element`, `cl_Shadow_Element`, helpers `cl_Term_Conn_Hlp`, `cl_Point_Conn_Hlp`
Base classes for elements with electrical terminals/connections. `cl_Term_Element` adds terminal management, 3-phase connection support, reliability parameters, and time-slice parameters to `cl_Scheme_Element`. `cl_MultiTerm_Element` supports elements with multiple connection points (lines, reactors). `cl_CircleTerm_Element` is for machines (circular symbol). `cl_Deviation_Element` adds deviation/uncertainty parameters. Enums: `Conn_3_Ph_Type` (D, Y, YN, NA), `AsymType`, `LoadKind`.

### cl_Scheme_Element.h
**Classes**: `cl_Scheme_Element`, `cl_Element_Attrib`, `cl_Multi_Chng`, `cl_Multi_Chng_Rec`
Abstract base class for ALL scheme elements (both nodes and edges). Inherits from `cl_SerializableObject`. Defines common properties: name, position (X/Y/Z), orientation, visibility, selection state, and value display. Provides interfaces for property grid editing, hit testing, and serialization. `cl_Multi_Chng` supports batch parameter modifications across multiple elements.

### cl_Node.h
**Classes**: `cl_Node`, `cl_Node_Conn_Hlp`
Network bus/node element. Manages connections to terminal elements via `TermElemMap_T`. Parameters: nominal voltage, grounding flag, 4-wire connection, neutral impedance (Rn/Xn). Supports voltage error detection masks (over/under voltage, asymmetry). Topology methods for 3-phase power domain assignment.

### cl_Line_Element.h
**Classes**: `cl_Line_Element`, `cl_LineType_Attrib`, `cl_LineLen_Attrib`, `cl_Line_Reliability`
Power line element (overhead or cable). Parameters: nominal voltage, max current, length, specific impedance/admittance per km (R, X, B, G and their zero-sequence counterparts). Line kinds: `lt_Outdoor`, `lt_Cable`. Supports parallel line count, forest/normal terrain reliability parameters. DB-loadable from component database.

### cl_Transformer_Element.h
**Classes**: `cl_Transformer_Element`, struct `XFORMER_Points`
Two-winding transformer. Parameters: primary/secondary voltages (U1, U2), nominal power (Sn), losses (Pk, P0), short-circuit voltage (Uk), impedance ratios. Winding types: D, Y, YN, ZN. Supports branch regulation with configurable step count and range. Neutral impedance, block transformer mode, manufacturer/type info. DB-loadable.

### cl_Transformer3_Element.h
**Classes**: `cl_Transformer3_Element`
Three-winding transformer (extends `cl_Transformer_Element`). Adds third winding parameters (U3, In3) and pairwise impedances between all three windings (Sn12/13/23, Pk12/13/23, Uk12/13/23). G0/G1 and B0/B1 ratios. Three terminal connections.

### cl_Switch_Element.h
**Classes**: `cl_Switch_Element`
Switch/breaker element. Types: arbitrary, remote controlled, recloser. State: open/closed. Supports time manipulation for time-slice calculations, reliability parameters, economy parameters. Optional protection set (when `LIM_PROTECTION` is enabled).

### cl_Load_Element.h
**Classes**: `cl_Load_Element`
Load/consumption element. Input types: USPhi, UPQ, UIP, UIPhi, UPPhi (different ways to specify load). Supports flicker calculation (model-based or measured), asymmetric load (inter-phase, two-phase, one-phase), constant impedance mode. Multi-change support for batch operations.

### cl_Power_Element.h
**Classes**: `cl_Power_Element`
Power supply / grid infeed element. Dual representation for single-phase and 3-phase calculations. Parameters: nominal voltage, short-circuit current (Ik), impedance angle (Psi_k). Supports frequency-dependent impedance. DB-loadable with power domain color support.

### cl_Sync_Element.h
**Classes**: `cl_Sync_Element`
Synchronous machine (motor, generator, wind turbine). Extensive reactance support: Xd, X'd, X''d. Power limits: Pmin, Pmax, Qmin, Qmax. Power plant block support. Time constant (Tm) for dynamic analysis. Short-circuit contribution with category support. Inverter regulation interface.

### cl_Async_Element.h
**Classes**: `cl_Async_Element`
Asynchronous machine (motor, generator, wind turbine). Types: `async_type_Motor`, `async_type_Generator`, `async_type_Wind`. Stator types: Y, D, Yn. Parameters: nominal voltage, power, power factor, harmonic parameters. Startup simulation flags. Inverter regulation support.

### cl_PhotoVolt_Element.h
**Classes**: `cl_PhotoVolt_Element`
Photovoltaic power source. Parameters: nominal voltage, power, power factor, category (regulation type). Supports harmonic analysis, regulation (cos(φ), Q(U)), 3-phase connection (Y, YN). Multi-change support for batch modifications.

### cl_Gate_Element.h
**Classes**: `cl_Gate_Element`
Compensation / reactive power control element (capacitor bank). Parameters: reactive power (Q), detuning factor, power loss. Economy parameters for cost analysis. KB (capacitor bank) mode flag.

### cl_Reactor_Element.h
**Classes**: `cl_Reactor_Element`
Series reactor / impedance element. Two-terminal element. Parameters: nominal voltage/current, short-circuit voltage (Uk), R/X ratios for positive and zero sequence.

### cl_Choke_Element.h
**Classes**: `cl_Choke_Element`
Earthing reactor / Petersen coil. Parameters: nominal voltage, reactive power, R/X ratio, R0/R1, X0/X1 ratios. Petersen coil mode with configurable compensation parameters. Optional additional resistor.

### cl_CurrSrc_Element.h
**Classes**: `cl_CurrSrc_Element`
Current source element (thyristor converters, rectifiers). Types: `currsrc_type_TyristorCnv`, `currsrc_type_CapRectifier`, `currsrc_type_CoilRectifier`. Supports harmonic parameter specification.

### cl_HDO_Src_Element.h
**Classes**: `cl_HDO_Src_Element`
HDO (audio-frequency ripple control) signal source. Simple element with a single parameter: voltage (m_fU). Used for HDO injection point in the network.

### cl_Text_Element.h
**Classes**: `cl_Text_Element`
Text annotation element. No terminals (max terminals = 0). Purely visual element for adding labels/notes to the diagram.

### cl_Calculation.h
**Classes**: `cl_Calculation`, `cl_Elem_Modifier`, `cl_Shadow_Result`
General calculation framework. Prepares scheme element data for the AN library. Handles content types: loads, PQ sources, ZQ (impedance), FVE (photovoltaic), async/sync machines. Supports up to 16 calculation modifiers (`MAX_Modifiers`). Methods for preparing node data, line data, transformer data, switch data for the AN input format.

### cl_OperCalc.h
**Classes**: `cl_OperCalc`, `cl_DeltaOperCalc`, `cl_Oper_Result`, `cl_Delta_Oper_Result`, `cl_Node_Op_Result`, `cl_Node_Op_Result3`, `cl_Elem_Op_Result`, `cl_Elem_Op_Result3`, `cl_Inverter_Regulation_Result`, `cl_Transf_Regulation_Result`
Operational calculation and result extraction. Supports Newton, Gauss-Seidel, and nodal voltage calculation methods. Result classes store per-node voltages and per-element power flows, currents, losses. Separate 3-phase result classes for asymmetric calculations. Regulation result classes for inverter and transformer tap control.

### cl_Topology.h
**Classes**: `cl_Topology`
Network topology analysis. Determines connectivity (islands), power domains, and electrical separation. Used to validate scheme structure before calculation and to identify isolated subsystems.

---

## wxLib/ Directory Headers

### BaseObjects.h
**Classes**: `cl_BaseObject`, `cl_OpData`, list type `T_BaseObj_List`
Base object framework for wxWidgets data objects. Defines common interfaces for tree/list display, menu IDs, and insertion modes. `cl_OpData` wraps operation-specific data. Auto-delete list container.

### wxValidators.h
**Classes**: `wxIntValidator`, `wxU32Validator`, `wxU64Validator`, `wxByteValidator`
Custom wxWidgets text validators for numeric input. Each extends `wxTextValidator` with typed data transfer (int, uint32, uint64, byte). Used in dialogs for parameter input validation.

### cl_Graph.h
**Classes**: `cl_Graph`, `cl_Graph_Axis`, `cl_Graph_Data`
Graph/plot visualization framework. Data series types: `Value_Serie_T` (double), `CplxValue_Serie_T` (complex). Interactive mouse support for zooming/panning. Auto-scaling with min/max computation. Pen customization for multiple series.

### MyCtrlCommon.h
Common definitions for custom wxWidgets controls. Defines keyboard event flags (INSERT, EDIT, DELETE, etc.), custom event types, helper classes `cl_ListCtrlColumn`, `cl_TreeData`. Shared between `MyListCtrl` and tree controls.

### MyListCtrl.h
Custom virtual list control implementation. Extended wxListCtrl with sorting, column management, and custom data binding.

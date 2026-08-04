# Element Data Model — Module Overview (L1)

## Class Hierarchy
```
cl_SerializableObject (TLV interface)
└── cl_Scheme_Element (base: ID, name, position, Z-order, orientation)
    ├── cl_Node (bus — connection hub, nominal voltage, grounding)
    └── cl_Term_Element (base for elements with terminals)
        ├── cl_Deviation_Element (adds uncertainty params)
        │   ├── cl_Power_Element (grid infeed)
        │   ├── cl_Load_Element (consumption)
        │   └── cl_PhotoVolt_Element (PV inverter)
        ├── cl_CircleTerm_Element (rotating machines, circular symbol)
        │   ├── cl_Sync_Element (synchronous generator/motor)
        │   └── cl_Async_Element (induction machine)
        ├── cl_MultiTerm_Element (2+ connection points)
        │   ├── cl_Line_Element (cable/overhead line)
        │   ├── cl_Transformer_Element (2-winding)
        │   ├── cl_Transformer3_Element (3-winding, extends Transformer)
        │   ├── cl_Switch_Element (breaker/disconnector)
        │   ├── cl_Reactor_Element (series impedance)
        │   └── cl_FuseRack_Element (up to 6 terminals)
        └── Single-terminal:
            ├── cl_Gate_Element (capacitor bank)
            ├── cl_Choke_Element (Petersen coil)
            ├── cl_CurrSrc_Element (thyristor converter)
            ├── cl_HDO_Src_Element (audio frequency source)
            ├── cl_Accumulation_Element (battery storage)
            ├── cl_MicroCoGen_Element (micro-cogeneration mixin)
            └── cl_Text_Element (annotation, 0 terminals)
```

## Base Class: cl_Scheme_Element
Common to all elements:
- `m_nID` (uint32) — unique element ID
- `m_szName` (wxString) — display name
- `m_nX`, `m_nY` (int) — position on canvas
- `m_nZ` (int) — Z-order for rendering
- `m_nOrientation` — 0°/90°/180°/270°
- `m_bVisible`, `m_bSelected` — display state

**Virtual methods every element implements:**
- `GetClassTag()` → TLV class tag
- `Serialize()` / `Deserialize()` — TLV persistence
- `AddData(Calc_Type_T)` — export data for calculation
- `GetValue(int)` / `SetValue(int, double)` — generic property access
- `ValueOK(int)` — validate property value

## Connection Model
- **cl_Node**: electrical bus, manages `TermElemMap_T` (terminal→element mapping)
- **Terminal**: indexed connection point (0, 1, 2...) on an element
- **cl_Term_Conn_Hlp**: serializes connection as (terminal_index, node_ID) pair
- **Resolution**: during deserialization, uint32 IDs → pointer resolution in `Deserialize_Done()`
- Elements reference nodes by ID, not direct pointers — enables clean serialization

## Terminal Counts by Element Type
| Type | Terminals | Notes |
|------|-----------|-------|
| Node | 0 (manages connections) | Hub, not an edge |
| Text | 0 | Annotation only |
| Line, Switch, Reactor, Transformer (2W) | 2 | Two-terminal edge |
| Transformer (3W) | 3 | Three-terminal |
| FuseRack | Up to 6 | Multi-terminal distribution |
| All others (Load, Power, Sync, etc.) | 1 | Single connection to node |

## Scheme Container (cl_Scheme)
- `ElemSet_T` — sorted set of elements (by Z-axis)
- Calculation settings: method, accuracy, epsilon, max iterations
- Feature flags: reliability, OPF, protections, time slices, 3-phase
- ID series for auto-naming (separate counters per element type)
- Canvas/grid settings, version tracking

## Regulation Interfaces
- `I_Regulation_Interface` — abstract: `GetRegulationType()`, `GetTargetVoltage()`
- Implemented by: `cl_Transformer_Element` (tap control), `cl_PhotoVolt_Element`, `cl_Sync_Element`, `cl_Async_Element` (inverter regulation: Q(U), cosφ)
- `cl_Inverter_Regulation` — mixin for inverter-based regulation

## Conditional Element Types
- `cl_Accumulation_Element` — requires `_BATT_STORAGE_`
- `cl_MicroCoGen_Element` — requires `LIM_MicroCoGen`
- Protection features — requires `LIM_PROTECTION`

## Source Files
| File | Purpose |
|------|---------|
| `include/cl_Scheme_Element.h`, `cl_Scheme_Element.cpp` | Base class |
| `include/TermElement.h`, `TermElement.cpp` | Terminal element hierarchy |
| `include/cl_Node.h`, `cl_Node.cpp` | Node/bus element |
| `include/cl_Scheme.h`, `cl_Scheme.cpp` | Scheme container |
| `include/cl_Topology.h`, `cl_Topology.cpp` | Connectivity analysis |
| `include/cl_*.h`, `cl_*_Element.cpp` | Per-element type files |
| `Regulation.h` | Regulation interfaces |

→ For full property lists per element see `L2_element_properties.md`

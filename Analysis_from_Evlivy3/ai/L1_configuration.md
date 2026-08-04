# Configuration & Database — Module Overview (L1)

## Configuration System
INI files read/written via `wxFileConfig`. Hierarchy of config classes:

| Class | INI Section | Purpose |
|-------|-------------|---------|
| `cl_Applic_Config` | `[Application]` | Paths, defaults, feature toggles, database path |
| `cl_ShortCircuit_Config` | `[ShortCircuit]` | SC calculation params (fault type, Ik", Sk") |
| `cl_OperResult_Config` | `[OperResult]` | Result display options (which values to show) |
| `cl_PhasorGr_Config` | `[PhasorGraph]` | Phasor diagram preferences |
| `cl_Topo_Config` | `[Topology]` | Topology display, power domain colors |
| `cl_EVlivy3_Config` | (container) | Groups all above configs |

**Config files:**
- `DNCalc.ini` — main application settings (DNCalc mode)
- `DNCoRS.ini` — application settings (DNCoRS/SCADA mode)

**Per-scheme config (inside scheme TLV):**
- Calculation method (Newton/Gauss-Seidel/Nodal)
- Accuracy, epsilon, max iterations
- Feature enables (reliability, OPF, protections, time slices, 3-phase)
- Regulation parameters (when DNCoRS): `/Config/BrnchReg`, `/Config/Unetmin`, `/Config/Unetmax`

## Database Objects (DB_Objects.h/.cpp)
SQLite-backed component catalog for pre-defined equipment types:

| Class | Table | Represents |
|-------|-------|-----------|
| `cl_DB_Voltage` | Voltage | Voltage level definitions |
| `cl_DB_Line_Type` | LineType | Cable/overhead line catalog |
| `cl_DB_Company` | Company | Equipment manufacturers |
| `cl_DB_Producer` | Producer | Power producers |
| `cl_DB_Winding` | Winding | Transformer winding types |
| `cl_DB_Element` | Element | Base equipment record |
| `cl_DB_Line_Element` | LineElement | Line parameters from catalog |
| `cl_DB_Xformer_Element` | XformerElement | Transformer parameters from catalog |

**Pattern:** Each DB object extends `cl_BaseObject` with `LoadObj()`, `GetTableName()`, `GetColumns()`, `Edit()`, `FillNew()` methods. Elements can be loaded from DB to fill scheme element properties.

**SQLite bindings:** `wxLib/sqlite/` provides wxWidgets-compatible SQLite wrapper classes.

## Source Files
| File | Purpose |
|------|---------|
| `Configuration.h / .cpp` | Config class hierarchy, INI read/write |
| `DNCalc.ini` | Main application settings |
| `DNCoRS.ini` | DNCoRS mode settings |
| `DB_Objects.h / .cpp` | Database object classes |
| `cl_LoadFromDB_Dlg.cpp` | Dialog for loading from DB (UI) |
| `wxLib/sqlite/` | SQLite bindings |

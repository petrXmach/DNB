# DLL Calculation Interface — Module Overview (L1)

## Architecture
```
cl_Scheme elements → cl_Calculation → .nap temp file → DLL → results → cl_OperCalc
```
- Calculation engine in external DLLs: **wxbase_supp4.dll** (1-phase), **wxbase_supp64.dll** (3-phase)
- DLLs loaded at runtime via `wxDynamicLibrary` (function pointers resolved dynamically)
- Communication is **file-based** via temporary .nap files (not memory buffers)

## DLL API (cl_AN_Lib — AN_Iface.h/.cpp)
Single-phase interface. Functions resolved from DLL at load time:
| Function | Signature | Purpose |
|----------|-----------|---------|
| `InitLibrary` | `void(int logLevel)` | Initialize DLL |
| `ReadInpData` | `bool(const char* filename)` | Read .nap input file |
| `RunAnalysis` | `void()` | Execute calculation |
| `GetStatusDescr` | `StatusDescr_T*()` | Get convergence status |
| `GetNodeCount` / `GetBranchCount` | `int()` | Result counts |
| `GetNodeData(idx)` | `NodeData_T*(int)` | Per-node voltage results |
| `GetBranchData(idx)` | `BranchData_T*(int)` | Per-branch P/Q/I results |
| `GetSCFaultData(idx)` | `SCFault_T*(int)` | Short-circuit fault data |

**StatusDescr_T**: convergence info (iterations, accuracy, error messages).

## 3-Phase DLL API (cl_AN3_Lib — AN3_Iface.h/.cpp)
Extended interface with packed structs for 3-phase values:
- `T3_PHASE_T`: 3 complex values (phases A, B, C)
- `T4_PHASE_T`: 4 complex values (A, B, C, N)
- `StatusDescr3_T`: 3-phase convergence status with wchar_t error messages

## Data Preparation (cl_Calculation — cl_Calculation.h/.cpp)
Converts scheme elements to DLL input format:
1. Iterates all elements in scheme
2. For each element type, calls element's `AddData(Calc_Type_T)` method
3. Element fills node/branch data arrays with electrical parameters
4. Data written to .nap temporary file
5. DLL reads the file and runs analysis

**Calc_Type_T** determines what data to export:
- Power flow (normal, with regulation, delta)
- Short-circuit (3ph, 2ph, 1ph, various fault types)
- Harmonics, HDO, flicker, frequency
- Reliability, economy, contingency

## Calculation Modifiers (cl_Elem_Modifier)
Up to 16 modifiers (`MAX_Modifiers = 16`) can alter element data before calculation:
- Time slice modifications (different load profiles per time period)
- Contingency modifications (N-1 element removal)
- Regulation state changes (tap position, reactive power setpoint)

## Result Extraction (cl_OperCalc — cl_OperCalc.h/.cpp)
After DLL completes:
- `cl_Oper_Result` / `cl_Delta_Oper_Result` — top-level result container
- `cl_Node_Op_Result` — per-node: voltage magnitude, angle, deviation
- `cl_Node_Op_Result3` — per-node 3-phase: Va, Vb, Vc, Vn
- `cl_Elem_Op_Result` — per-element: P, Q, I, losses (both ends)
- `cl_Elem_Op_Result3` — per-element 3-phase: per-phase P, Q, I
- `cl_Inverter_Regulation_Result` — regulation output (Q setpoint, voltage)
- `cl_Transf_Regulation_Result` — transformer tap position result

## Calculation Methods
| Constant | Method |
|----------|--------|
| `CALC_METH_NODAL_VOLTAGE` | Nodal voltage (linear) |
| `CALC_METH_NEWTON` | Newton-Raphson (nonlinear, default) |
| `CALC_METH_GAUSS_SEIDEL` | Gauss-Seidel (iterative) |

## 3-Phase Export — Bodor Format (Elem_3ph_Export.h/.cpp)
Parallel hierarchy for 3-phase calculations:
- `cl_Bodor_Element` (base) → 15+ derived classes
- Element codes: VK (power), TR (transformer 2W), T3 (3W), SI (sync), PQ (load), ZQ (impedance load), FV (PV), M3 (async), SG (generator), VY (switch), RE (reactor), L3 (line), KB (capacitor), QK (choke), ZN (current source)
- Each implements `DoExport()` to serialize parameters

## Source Files
| File | Purpose |
|------|---------|
| `AN_Iface.h / .cpp` | Single-phase DLL interface, function pointer resolution |
| `AN3_Iface.h / .cpp` | 3-phase DLL interface, packed data structures |
| `include/cl_Calculation.h`, `cl_Calculation.cpp` | Data preparation, modifier system |
| `include/cl_OperCalc.h`, `cl_OperCalc.cpp` | Result extraction, calculation orchestration |
| `Elem_3ph_Export.h / .cpp` | Bodor 3-phase element export |
| `cl_Calc_Test.cpp` | Calculation testing framework |

→ For struct layouts and constants see `L2_data_structures.md`

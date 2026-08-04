# Data Structures & Constants — Detailed Reference (L2)

## DLL Interface Structures

### StatusDescr_T (AN_Iface.h — single-phase)
Convergence status from DLL after `RunAnalysis()`:
```
struct StatusDescr_T {
    int    nIterations;     // Number of iterations performed
    double fAccuracy;       // Achieved accuracy
    bool   bConverged;      // Converged successfully
    char*  szErrorMsg;      // Error message (null if OK)
};
```

### StatusDescr3_T (AN3_Iface.h — three-phase)
```
struct StatusDescr3_T {
    int      nIterations;
    double   fAccuracy;
    bool     bConverged;
    wchar_t* szErrorMsg;    // UTF-16LE error message
};
```

### T3_PHASE_T (AN3_Iface.h)
Three complex values (phases A, B, C):
```
struct T3_PHASE_T {
    std::complex<double> a;
    std::complex<double> b;
    std::complex<double> c;
};
```

### T4_PHASE_T (AN3_Iface.h)
Four complex values (A, B, C, N):
```
struct T4_PHASE_T {
    std::complex<double> a;
    std::complex<double> b;
    std::complex<double> c;
    std::complex<double> n;
};
```

## Calculation Constants

### Calculation Methods (cl_Scheme.h)
```
CALC_METH_NODAL_VOLTAGE  = 0   // Linear nodal voltage
CALC_METH_NEWTON         = 1   // Newton-Raphson (default)
CALC_METH_GAUSS_SEIDEL   = 2   // Gauss-Seidel iterative
```

### Calculation Request Flags (cl_OperCalc.h)
```
RES_CALC_RUN            = 0x0001  // Run calculation
RES_CALC_REGULATION     = 0x0002  // Include regulation
RES_CALC_DELTA          = 0x0004  // Delta calculation
RES_CALC_3PH            = 0x0008  // 3-phase mode
```

### MAX_Modifiers = 16
Maximum calculation modifiers (time slices, contingencies).

## Result Value Indices (cl_OperCalc.h)

### Per-Node Results (cl_Node_Op_Result)
```
VALUE_U     = 0   // Voltage magnitude (kV)
VALUE_U_ANG = 1   // Voltage angle (deg)
VALUE_dU    = 2   // Voltage deviation (%)
```

### Per-Element Results (cl_Elem_Op_Result)
```
VALUE_P1    = 0   // Active power, end 1 (W)
VALUE_Q1    = 1   // Reactive power, end 1 (VAr)
VALUE_I1    = 2   // Current, end 1 (A)
VALUE_P2    = 3   // Active power, end 2 (W)
VALUE_Q2    = 4   // Reactive power, end 2 (VAr)
VALUE_I2    = 5   // Current, end 2 (A)
VALUE_dP    = 6   // Active losses (W)
VALUE_dQ    = 7   // Reactive losses (VAr)
```

## Physical Constants (common.h)
```
PI        = 3.14159265358979
NET_FREQ  = 50.0              // Network frequency (Hz)
SQRT3     = 1.7320508075688772
RAD_DEG   = 57.29577951308232 // Radians to degrees
```

## Scheme File Version
```
SCHEME_FILE_VERSION = 0x00010005
```

## Voltage Level Limits (common.h)
```
VOLT_LIM_NN   = 1.0     // kV boundary: low voltage
VOLT_LIM_VN   = 52.0    // kV boundary: medium → high voltage
VOLT_LIM_VVN  = 300.0   // kV boundary: high → extra-high voltage
```

## 3-Phase Connection Types (TermElement.h)
```
enum Conn_3_Ph_Type {
    c3_D   = 0,   // Delta
    c3_Y   = 1,   // Star
    c3_YN  = 2,   // Star with neutral
    c3_NA  = 3    // Not applicable
};
```

## Async Machine Types (cl_Async_Element.h)
```
enum AsyncType {
    async_type_Motor     = 0,
    async_type_Generator = 1,
    async_type_Wind      = 2
};
enum AsyncStator {
    stat_type_Y  = 0,
    stat_type_D  = 1,
    stat_type_Yn = 2
};
```

## Sync Machine Types (cl_Sync_Element.h)
```
enum SyncType {
    sync_type_Motor     = 0,
    sync_type_Generator = 1,
    sync_type_Wind      = 2
};
```

## Load Input Types (cl_Load_Element.h)
```
enum LoadInpType {
    USPhi  = 0,   // U, S, cosφ
    UPQ    = 1,   // U, P, Q
    UIP    = 2,   // U, I, P
    UIPhi  = 3,   // U, I, cosφ
    UPPhi  = 4    // U, P, cosφ
};
```

## Switch Types (cl_Switch_Element.h)
```
enum SwitchType_T {
    sw_Arbitrary   = 0,
    sw_RemoteCtrl  = 1,
    sw_Recloser    = 2
};
```

## Line Kinds (cl_Line_Element.h)
```
enum LineKind_T {
    lt_Outdoor = 0,
    lt_Cable   = 1,
    lt_NA      = 2
};
```

## Transformer Winding Types
```
D   = 0   // Delta
Y   = 1   // Star
YN  = 2   // Star with neutral
ZN  = 3   // Zigzag with neutral
```

## Bodor Element Codes (Elem_3ph_Export.h)
3-phase export naming convention:
```
VK = Power source       TR = Transformer 2W    T3 = Transformer 3W
SI = Sync machine       PQ = Load              ZQ = Impedance load
FV = Photovoltaic       M3 = Async machine     SG = Sync generator
VY = Switch             RE = Reactor           L3 = Line
KB = Capacitor bank     QK = Choke             ZN = Current source
```

## IEC 104 Key Constants (DB_104.h)
```
RX_BUFF_LEN         = 128 * 1024  // 131072 bytes receive buffer
MAIN_104_Flag        = high bit on ID  // Distinguishes main items
tlv_head_t size      = 8 bytes   // 4B tag + 4B length
Socket select timeout = 100 ms
Connect retry delay   = 200 ms
```

## DNCoRS Regulation Sequence
```
DO_CALC_SAVE → DO_CALC_SPLIT → DO_CALC_OPER → DO_CALC_QMIN
→ DO_CALC_QMAX → DO_CALC_LOSS → DO_CALC_OPTIMIZE → DO_CALC_CONTROLL
```

## Canvas/Grid Constants (common.h)
```
DEFAULT_GRID_SIZE   = 10      // Grid spacing (pixels)
DEFAULT_CANVAS_X    = 2000    // Default canvas width
DEFAULT_CANVAS_Y    = 2000    // Default canvas height
```

## Element Symbol Radii
```
ASYNC_CIRCLE_RAD   = 18   // Async machine circle symbol
SYNC_CIRCLE_RAD    = 18   // Sync machine circle symbol
REACTOR_CIRCLE_RAD = 18   // Reactor circle symbol
```

## Harmonic Constants
```
Converter_HARM_Cnt = 50   // Max harmonic orders for current sources
```

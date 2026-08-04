# TLV Serialization System - DNCalc/EVlivy3

## Overview

The EVlivy3 application uses a custom Tag-Length-Value (TLV) binary serialization system to persist electrical network scheme data to disk and to transfer data over TCP (IEC 104 protocol interface). The system is implemented in `cl_Serializer` (defined in `include/Serializable.h`, implemented in `Serializable.cpp`) and relies on `cl_SerializableObject` as the polymorphic interface that all serializable objects implement.

**Key source files:**

| File | Purpose |
|------|---------|
| `include/Serializable.h` | `cl_Serializer` class and `cl_SerializableObject` interface |
| `Serializable.cpp` | Full implementation of serializer |
| `include/tlv_tag.h` | Tag constant definitions (>400 tags) |
| `include/tlv_tag.h.types` | Tag-to-type hint file (3 entries) |
| `include/common.h` | `SCHEME_FILE_VERSION` and shared constants |
| `cl_Scheme.cpp` | Scheme serialization, `CreateObjectByTag()` factory |
| `cl_Scheme_Pnl.cpp` | `Do_Save()` -- top-level save flow |
| `EVlivy3Main.cpp` | `Do_OpenScheme()` -- top-level load flow |

---

## 1. TLV Binary Format Structure

### 1.1 TLV Record Header (`tlv_head_t`)

Every TLV record (attribute or class) shares an 8-byte packed header:

```
Offset  Size   Field      Description
------  -----  ---------  -----------
0x00    4      nTag       uint32_t tag identifier
0x04    4      nLength    uint32_t byte count of the Value portion
0x08    ...    [value]    Raw bytes (nLength bytes)
```

The struct is defined with `#pragma pack(push,1)` (MSVC) or `__attribute__((packed))` (GCC) to ensure no padding:

```cpp
typedef struct __attribute__((packed))
{
    uint32_t nTag;
    uint32_t nLength;
} tlv_head_t;
```

**Byte order:** Native (little-endian on x86/x64 Windows and Linux). There is no byte-swapping; the format is platform-native. All multi-byte values (uint16, uint32, uint64, double) are written as raw memory (`memcpy`).

### 1.2 Distinguishing Attributes from Classes

Bit 31 of the tag (`0x80000000`) is the **TLV_CLASS marker**:

```cpp
#define TLV_CLASS  0x80000000
#define CLASS(tag) (tag | TLV_CLASS)
```

- If `(nTag & TLV_CLASS) == TLV_CLASS`: the record is a **class** (nested object). Its value portion contains further TLV records (attributes and sub-classes).
- Otherwise: the record is a **leaf attribute**. Its value portion is raw data of the specified type.

### 1.3 Data Type Encoding

Each typed `AddTLV_*` method writes a specific binary encoding:

| Method | Tag type prefix | nLength | Value encoding |
|--------|----------------|---------|----------------|
| `AddTLV_Bool` | `TAG_b_*` | 1 | `uint8_t`: 0 or 1 |
| `AddTLV_U8` | `TAG_u8_*` | 1 | Raw `uint8_t` |
| `AddTLV_U16` | `TAG_u16_*` | 2 | Raw `uint16_t` (LE) |
| `AddTLV_I16` | `TAG_i16_*` | 2 | Raw `int16_t` (LE) |
| `AddTLV_U32` | `TAG_u32_*` | 4 | Raw `uint32_t` (LE) |
| `AddTLV_I32` | `TAG_i32_*` | 4 | Raw `int32_t` (LE) |
| `AddTLV_U64` | `TAG_u64_*` | 8 | Raw `uint64_t` (LE) |
| `AddTLV_Dbl` | `TAG_dbl_*` | 8 | Raw IEEE 754 `double` (LE) |
| `AddTLV_CD` | `TAG_cd_*` | 16 | Two consecutive `double` values: `[real, imag]` |
| `AddTLV_Date` | `TAG_dt_*` / `TAG_dtm_*` | 8 | `uint64_t` from `wxDateTime::GetValue()` (ms since epoch) |
| `AddTLV_Colour` | `TAG_Colour*` | 4 | `uint32_t` from `wxColour::GetRGBA()` |
| `AddTLV_UTF8` | `TAG_sz8_*` | strlen+1 | Null-terminated UTF-8 byte string |
| `AddTLV_UTF16` | n/a | (len+1)*2 | UTF-16LE encoded string, null-terminated |
| `AddTLV` (generic) | various | varies | Raw `memcpy` of `nLength` bytes |

**Notes:**
- `AddTLV_Bool` internally calls `AddTLV_U8` with value 0 or 1.
- `AddTLV_Colour` internally calls `AddTLV_U32`.
- The generic `AddTLV` is used for enum types, raw structs, and doubles (many elements use `AddTLV(tag, sizeof(double), &value)` rather than `AddTLV_Dbl`).
- UTF-8 strings include the null terminator in `nLength`. On read, this is validated: `((const char*)pValue)[nLength - 1] != 0` throws.

### 1.4 Type Hints File (`tlv_tag.h.types`)

The file `include/tlv_tag.h.types` contains 3 lines mapping specific tags to display sizes (likely for external tooling/debugging):

```
00000002 0010    (TAG_sz8_Name -> type 0x0010)
00000104 0030    (TAG_dbl_Scale -> type 0x0030)
00003120 0038    (TAG_cd_RES_Ua -> type 0x0038)
```

---

## 2. `cl_Serializer` API

### 2.1 Construction and Buffer Management

```cpp
cl_Serializer(uint32_t nSize = 65536);
~cl_Serializer();
```

- Allocates an initial buffer of `nSize` bytes (default 64KB).
- `m_pData`: heap-allocated `uint8_t*` buffer.
- `m_nOffset`: current write/read position in the buffer.
- `m_nSize`: current allocated buffer size.

**Dynamic growth** via `EnsureSpace(uint32_t nSize)`:
```cpp
void EnsureSpace(uint32_t nSize)
{
    if (m_nSize < (m_nOffset + nSize))
    {
        uint32_t new_size = (m_nOffset + nSize + 65536) & ~4095; // +64KB, page-aligned
        uint8_t *pData = new uint8_t[new_size];
        memcpy(pData, m_pData, m_nSize);
        delete[] m_pData;
        m_pData = pData;
        m_nSize = new_size;
    }
}
```

Buffer grows by at least 64KB, rounded up to 4KB page alignment.

### 2.2 Packing (Serialization) Methods

**Core writer:**
```cpp
void AddTLV(uint32_t nTag, uint32_t nLength, void *pValue);
```
Writes header + value to the buffer at `m_nOffset`, advances offset.

**Typed writers** (see Section 1.3 table):
```cpp
void AddTLV_UTF8(uint32_t nTag, wxString& szValue);
void AddTLV_UTF8_C(uint32_t nTag, wxString szValue);  // copy variant
void AddTLV_UTF16(uint32_t nTag, wxString& szValue);
void AddTLV_Bool(uint32_t nTag, bool bValue);
void AddTLV_U8(uint32_t nTag, uint8_t nValue);
void AddTLV_U16(uint32_t nTag, uint16_t nValue);
void AddTLV_I16(uint32_t nTag, int16_t nValue);
void AddTLV_U32(uint32_t nTag, uint32_t nValue);
void AddTLV_I32(uint32_t nTag, int32_t nValue);
void AddTLV_U64(uint32_t nTag, uint64_t nValue);
void AddTLV_Dbl(uint32_t nTag, double nValue);
void AddTLV_CD(uint32_t nTag, std::complex<double> cfValue);
void AddTLV_Date(uint32_t nTag, wxDateTime &dtValue);
void AddTLV_Colour(uint32_t nTag, wxColour &Colour);  // NO_GUI guarded
```

**Object serialization:**
```cpp
void Serialize(cl_SerializableObject *pObj);
```
1. Records current offset (`nOffset`).
2. Writes a TLV header with `nTag = pObj->GetObjectType()` and `nLength = 0`.
3. Calls `pObj->Serialize(this)` -- the object writes all its attributes as TLV records.
4. Patches `nLength` in the header at step 2 to `m_nOffset - nOffset - sizeof(tlv_head_t)`.

This creates a **nested TLV**: the class header's value section contains all its attribute TLVs and sub-class TLVs.

### 2.3 Unpacking (Deserialization) Methods

**Core reader:**
```cpp
bool GetTLV(uint32_t &nTag, uint32_t &nLength, void *&pValue);
```
Reads the header at current offset, returns tag/length/pointer-to-value. Does NOT advance offset (caller uses `SkipTLV()` or `SkipTLVHead()`).

**Static typed readers** (all validate length and throw on mismatch):
```cpp
static void GetTLV(uint32_t nLength, void *pValue, void *pResult);       // raw memcpy
static void GetTLV_CD(uint32_t nLength, void *pValue, std::complex<double> *out);
static void GetTLV_Colour(uint32_t nLength, void *pValue, wxColour *out);
static void GetTLV_Bool(uint32_t nLength, void *pValue, bool &out);
static void GetTLV_U8(uint32_t nLength, void *pValue, uint8_t &out);
static void GetTLV_U16(uint32_t nLength, void *pValue, uint16_t &out);
static void GetTLV_I16(uint32_t nLength, void *pValue, int16_t &out);
static void GetTLV_U32(uint32_t nLength, void *pValue, uint32_t &out);
static void GetTLV_I32(uint32_t nLength, void *pValue, int32_t &out);
static void GetTLV_U64(uint32_t nLength, void *pValue, uint64_t &out);
static void GetTLV_Dbl(uint32_t nLength, void *pValue, double &out);
static void GetTLV_Date(uint32_t nLength, void *pValue, wxDateTime &out);
static void GetTLV_UTF8(uint32_t nLength, void *pValue, wxString &out);
static void GetTLV_UTF16(uint32_t nLength, void *pValue, wxString &out);
```

Each typed `Get` method validates that `nLength` matches the expected size (e.g., `sizeof(uint32_t)` for U32). If not, it throws a `BASE_EXCEPTION` with a Czech-language error message (e.g., "neplatna delka dat" = "invalid data length").

**Object deserialization:**
```cpp
cl_SerializableObject *Deserialize();
```
1. Resets `m_nOffset = 0`.
2. Reads the root TLV header.
3. Calls `CreateObjectByTag(nTag)` to instantiate the root object.
4. Calls `DeserializeClassRecursive(pRoot, nLength)`.
5. Calls `pRoot->Deserialize_Done()`.
6. Returns the root object.

**Recursive deserializer:**
```cpp
void DeserializeClassRecursive(cl_SerializableObject *pObj, uint32_t nBaseLength);
```
Iterates through TLV records within `nBaseLength` bytes:
- If tag has `TLV_CLASS` bit: calls `pObj->CreateObjectByTag(nTag)` to create a sub-object, recursively deserializes it, then calls `pObj->ProcessNewSubObject(pNew)`. If the parent does not claim the sub-object (returns `false`), it is `delete`d.
- Otherwise: calls `pObj->Deserialize(nTag, nLength, pValue)` for attribute processing, then `SkipTLV()`.

Unknown classes are silently skipped via `SkipTLV()`.

### 2.4 Navigation

```cpp
void SkipTLV();      // Skip entire record (header + value)
void SkipTLVHead();  // Skip only the 8-byte header
uint32_t GetSize() const { return m_nOffset; }
```

### 2.5 Compression and File I/O

```cpp
uint8_t *CompressBuffer(uint32_t *pnSize, int nCompressionLevel);
bool WriteTLVArchive(wxFile &OFile, int nCompressionLevel = 6);
uint32_t WriteTLVArchive(uint8_t **pBuffer, int nCompressionLevel = 6);
void ReadTLV(wxFile *IFile, uint8_t *pBuffer = NULL, uint32_t nBuffLen = 0);
```

See Section 5 for details.

---

## 3. `cl_SerializableObject` Interface

```cpp
class cl_SerializableObject
{
public:
    virtual uint32_t GetObjectType() = 0;
    virtual void Serialize(cl_Serializer *pSerializer) = 0;
    virtual bool Deserialize(uint32_t nTag, uint32_t nLength, void *pValue) = 0;
    virtual bool ProcessNewSubObject(cl_SerializableObject *pObj) { return false; }
    virtual void Deserialize_Done() { ; }
    virtual cl_SerializableObject *CreateObjectByTag(uint32_t nTag)
        { return ::CreateObjectByTag(nTag); }
};
```

| Method | Purpose |
|--------|---------|
| `GetObjectType()` | Returns the tag constant for this class (e.g., `TAG_CLASS_LINE` = `CLASS(0x00001200)` = `0x80001200`). Written as the TLV class header tag. |
| `Serialize(pSerializer)` | Writes all attributes of this object using `pSerializer->AddTLV_*()` calls. Must also serialize child objects via `pSerializer->Serialize(pChild)`. |
| `Deserialize(nTag, nLength, pValue)` | Called for each non-class TLV within this object's scope. Returns `true` if the tag was recognized and handled. Pattern: big `switch(nTag)` statement calling `cl_Serializer::GetTLV_*()` static methods. Falls through to parent class `Deserialize()` if not found. |
| `ProcessNewSubObject(pObj)` | Called when a sub-class TLV is deserialized. The object should `dynamic_cast` to the expected type, store it, and return `true`. Returning `false` causes the sub-object to be deleted. |
| `Deserialize_Done()` | Post-deserialization hook. Used to resolve ID references to actual object pointers (e.g., node connections stored as IDs during serialization). |
| `CreateObjectByTag(nTag)` | Virtual factory override. Default delegates to the global `::CreateObjectByTag()`. |

---

## 4. Tag System

### 4.1 Organization in `tlv_tag.h`

Tags are organized by element type using hex ranges:

| Range | Purpose |
|-------|---------|
| `0x00000001 - 0x000000FF` | Common attributes (ID, Name, Version, Position, Orientation, electrical parameters like Un, Imax, R, X, etc.) |
| `0x00000100 - 0x0000015F` | Scheme-level attributes (grid size, canvas size, counters, calculation settings) |
| `0x00000160 - 0x000001FF` | Contingency analysis, CORS voltage control settings |
| `0x00001000 - 0x000010FF` | Node-specific attributes |
| `0x00001100 - 0x000011FF` | Power source attributes |
| `0x00001200 - 0x000012FF` | Line element attributes |
| `0x00001300 - 0x000013FF` | Transformer (2-winding) attributes |
| `0x00001400 - 0x000014FF` | Switch attributes |
| `0x00001500 - 0x000015FF` | Load element attributes |
| `0x00001600 - 0x000016FF` | Async machine attributes |
| `0x00001700 - 0x000017FF` | Sync machine attributes |
| `0x00001800 - 0x000018FF` | Current source attributes |
| `0x00001900 - 0x000019FF` | Photovoltaic element attributes |
| `0x00001A00 - 0x00001AFF` | Gate element attributes |
| `0x00001B00 - 0x00001BFF` | Choke element attributes |
| `0x00001C00 - 0x00001CFF` | Reactor element attributes |
| `0x00001D00 - 0x00001DFF` | 3-winding transformer attributes |
| `0x00001E00` | Text element |
| `0x00001F00 - 0x00001FFF` | HDO source attributes |
| `0x00002000 - 0x00002FFF` | Clipboard, fuse rack, accumulation, micro-cogeneration, config |
| `0x00003000 - 0x00006FFF` | Calculation results (operational, short-circuit, frequency, harmonics, HDO, flicker, etc.) |
| `0x00007000 - 0x00007FFF` | PQ diagram attributes |
| `0x00008000 - 0x00008FFF` | Protection attributes |

### 4.2 Naming Convention

Tags follow a naming pattern indicating their data type:

| Prefix | Type |
|--------|------|
| `TAG_u32_*` | `uint32_t` |
| `TAG_u64_*` | `uint64_t` |
| `TAG_u16_*` | `uint16_t` |
| `TAG_i16_*` | `int16_t` |
| `TAG_i32_*` | `int32_t` |
| `TAG_u8_*` | `uint8_t` |
| `TAG_dbl_*` | `double` |
| `TAG_cd_*` | `std::complex<double>` |
| `TAG_sz8_*` | UTF-8 string |
| `TAG_b_*` | boolean |
| `TAG_dt_*` / `TAG_dtm_*` | `wxDateTime` |
| `TAG_Colour*` | `wxColour` (RGBA as uint32) |
| `TAG_CLASS_*` | Class marker (has `0x80000000` bit set) |

Tags without a type prefix (e.g., `TAG_Calc_Method`, `TAG_PrimWindg`, `TAG_LINE_Kind`) are typically raw-memcpy'd enums or small structs using the generic `AddTLV`.

### 4.3 Class Tags

All class tags use the `CLASS()` macro:

```cpp
#define CLASS(tag) (tag | TLV_CLASS)   // TLV_CLASS = 0x80000000
```

Key class tags:

| Constant | Value | Class |
|----------|-------|-------|
| `TAG_CLASS_SCHEME` | `0x80000100` | `cl_Scheme` |
| `TAG_CLASS_NODE` | `0x80001000` | `cl_Node` |
| `TAG_CLASS_POWER` | `0x80001100` | `cl_Power_Element` |
| `TAG_CLASS_LINE` | `0x80001200` | `cl_Line_Element` |
| `TAG_CLASS_XFORMER` | `0x80001300` | `cl_Transformer_Element` |
| `TAG_CLASS_SWITCH` | `0x80001400` | `cl_Switch_Element` |
| `TAG_CLASS_LOAD` | `0x80001500` | `cl_Load_Element` |
| `TAG_CLASS_ASYNC` | `0x80001600` | `cl_Async_Element` |
| `TAG_CLASS_SYNC` | `0x80001700` | `cl_Sync_Element` |
| `TAG_CLASS_CURR_SRC` | `0x80001800` | `cl_CurrSrc_Element` |
| `TAG_CLASS_PHOTOVOLT` | `0x80001900` | `cl_PhotoVolt_Element` |
| `TAG_CLASS_GATE` | `0x80001A00` | `cl_Gate_Element` |
| `TAG_CLASS_CHOKE` | `0x80001B00` | `cl_Choke_Element` |
| `TAG_CLASS_REACTOR` | `0x80001C00` | `cl_Reactor_Element` |
| `TAG_CLASS_XFORMER3` | `0x80001D00` | `cl_Transformer3_Element` |
| `TAG_CLASS_TEXT` | `0x80001E00` | `cl_Text_Element` |
| `TAG_CLASS_HDO_SRC` | `0x80001F00` | `cl_HDO_Src_Element` |
| `TAG_CLASS_CLIPBOARD` | `0x80002000` | `cl_Clipboard` |
| `TAG_CLASS_FUSE_RACK` | `0x80002100` | `cl_FuseRack_Element` |
| `TAG_CLASS_ACCU` | `0x80002200` | `cl_Accumulation_Element` |
| `TAG_CLASS_CONFIG` | `0x80002F00` | `cl_Applic_Config` |
| `TAG_CLASS_TERM_CONN_HLP` | `0x80000280` | `cl_Term_Conn_Hlp` (connection helper) |
| `TAG_CLASS_POINT_CONN_HLP` | `0x80000290` | `cl_Point_Conn_Hlp` (line point helper) |
| `TAG_CLASS_NODE_CONN_HLP` | `0x80001080` | `cl_Node_Conn_Hlp` |
| `TAG_CLASS_NAME_ATTRIB` | `0x80000010` | `cl_Name_Attrib` (label) |
| `TAG_CLASS_LINE_TYPE_ATTRIB` | `0x80000011` | `cl_LineType_Attrib` |
| `TAG_CLASS_VALUE_ATTRIB` | `0x80000012` | `cl_ResultValue_Attrib` |
| `TAG_CLASS_MEAS_ATTRIB` | `0x80000013` | `cl_Measurement_Attrib` |
| `TAG_CLASS_OSM_POINT` | `0x800002A0` | `cl_OSMPosition` (GIS waypoint) |
| `TAG_CLASS_PQ_Diagram` | `0x80007000` | `cl_PQ_Diagram` |
| `TAG_CLASS_PROT_OVRCURR_*` | `0x80008000+` | Protection elements |
| Result classes | `0x80003000+` | Various calculation result containers |

---

## 5. File I/O

### 5.1 File Format

Scheme files use the `.egc3` extension. The file format is:

**Compressed format (default):**
```
Offset  Size   Content
------  -----  -------
0x00    4      Magic: 0x564C5458 ('VLTX' -- COMPRESSED_ARCHIVE_MAGIC)
0x04    4      uint32_t: compressed data length (N)
0x08    N      bzip2 compressed TLV stream
```

**Uncompressed format (compression level 0):**
```
The raw TLV stream is written directly (first 8 bytes are the root class TLV header).
```

### 5.2 Compression: bzip2

The system uses **libbz2** for compression/decompression:
- Default compression level: **6** (out of 1-9).
- Enabled unless the `_SER_NO_BZ2_` preprocessor macro is defined.
- The compressed data is produced by `BZ2_bzCompress()` with `BZ_FINISH` action.
- During decompression, the buffer is expanded dynamically (4KB increments) as needed.

### 5.3 Scheme File Version

Defined in `include/common.h`:
```cpp
#define SCHEME_FILE_VERSION  0x00010005
```
This is version `1.5` (major.minor as `0001.0005`). Written as the first attribute in `cl_Scheme::Serialize()`:
```cpp
pSerializer->AddTLV_U32(TAG_u32_Version, SCHEME_FILE_VERSION);
```

On load, `cl_Scheme::Deserialize_Done()` checks `m_nVersion` and applies migration logic:
- Version <= `0x00010003`: converts old calculation method settings to new format.
- Adjusts island/3-phase calculation method flags for backward compatibility.

### 5.4 Write Flow (`WriteTLVArchive` to file)

```cpp
bool cl_Serializer::WriteTLVArchive(wxFile &OFile, int nCompressionLevel)
```

1. If `nCompressionLevel > 0`:
   - Calls `CompressBuffer()` to bzip2-compress the internal TLV buffer.
   - Writes an 8-byte header: `{ COMPRESSED_ARCHIVE_MAGIC, compressed_size }`.
   - Writes the compressed data.
2. If `nCompressionLevel == 0`:
   - Writes the raw TLV buffer directly.

### 5.5 Write Flow (to memory buffer)

```cpp
uint32_t cl_Serializer::WriteTLVArchive(uint8_t **pBuffer, int nCompressionLevel)
```

Returns a `malloc()`-allocated buffer containing the header + data. Caller must `free()` it. Used for clipboard operations and IEC 104 TCP transfer.

### 5.6 Read Flow (`ReadTLV`)

```cpp
void cl_Serializer::ReadTLV(wxFile *IFile, uint8_t *pBuffer, uint32_t nBuffLen)
```

1. Reads 8-byte header from file or memory buffer.
2. If `head.nTag == COMPRESSED_ARCHIVE_MAGIC`:
   - Reads `head.nLength` bytes of compressed data.
   - Decompresses using `BZ2_bzDecompress()` into the internal buffer.
   - Dynamically grows the output buffer as needed during decompression.
   - Sets `m_nOffset` to the decompressed size on success.
3. Otherwise (plain TLV):
   - Copies the header + data into the internal buffer.
   - Sets `m_nOffset = head.nLength + sizeof(head)`.

The method accepts either a `wxFile*` (for file I/O) or a raw `uint8_t*` buffer (for memory-based I/O such as clipboard paste or network receive).

---

## 6. Factory Pattern: `CreateObjectByTag()`

The global function `CreateObjectByTag()` in `cl_Scheme.cpp` (line ~1661) is a large switch statement that maps class tags to `new` expressions:

```cpp
cl_SerializableObject *CreateObjectByTag(uint32_t nTag)
{
    switch (nTag)
    {
        case TAG_CLASS_SCHEME:        return new cl_Scheme;
        case TAG_CLASS_NODE:          return new cl_Node;
        case TAG_CLASS_POWER:         return new cl_Power_Element;
        case TAG_CLASS_LINE:          return new cl_Line_Element;
        case TAG_CLASS_XFORMER:       return new cl_Transformer_Element;
        case TAG_CLASS_XFORMER3:      return new cl_Transformer3_Element;
        case TAG_CLASS_SWITCH:        return new cl_Switch_Element;
        case TAG_CLASS_LOAD:          return new cl_Load_Element;
        case TAG_CLASS_ASYNC:         return new cl_Async_Element;
        case TAG_CLASS_SYNC:          return new cl_Sync_Element;
        case TAG_CLASS_PHOTOVOLT:     return new cl_PhotoVolt_Element;
        case TAG_CLASS_CURR_SRC:      return new cl_CurrSrc_Element;
        case TAG_CLASS_GATE:          return new cl_Gate_Element;
        case TAG_CLASS_CHOKE:         return new cl_Choke_Element;
        case TAG_CLASS_REACTOR:       return new cl_Reactor_Element;
        case TAG_CLASS_ACCU:          return new cl_Accumulation_Element;
        case TAG_CLASS_PHOTO_MICOGE:  return new cl_MicroCoGen_Photo_Element;
        case TAG_CLASS_ASYN_MICOGE:   return new cl_MicroCoGen_Async_Element;
        case TAG_CLASS_SYNC_MICOGE:   return new cl_MicroCoGen_Sync_Element;
        case TAG_CLASS_PHOTO1_MICOGE: return new cl_MicroCoGen_Photo1_Element;
        case TAG_CLASS_HDO_SRC:       return new cl_HDO_Src_Element;
        case TAG_CLASS_TEXT:          return new cl_Text_Element;
        case TAG_CLASS_CLIPBOARD:     return new cl_Clipboard;
        case TAG_CLASS_NODE_CONN_HLP: return new cl_Node_Conn_Hlp;
        case TAG_CLASS_TERM_CONN_HLP: return new cl_Term_Conn_Hlp;
        case TAG_CLASS_POINT_CONN_HLP:return new cl_Point_Conn_Hlp;
        case TAG_CLASS_NAME_ATTRIB:   return new cl_Name_Attrib;
        case TAG_CLASS_NAME_COLOUR_ATTRIB: return new cl_ClrName_Attrib;
        case TAG_CLASS_VALUE_ATTRIB:  return new cl_ResultValue_Attrib;
        case TAG_CLASS_LINE_TYPE_ATTRIB: return new cl_LineType_Attrib;
        case TAG_CLASS_LINE_LEN_ATTRIB:  return new cl_LineLen_Attrib;
        case TAG_CLASS_MEAS_ATTRIB:   return new cl_Measurement_Attrib;
        case TAG_CLASS_OSM_POINT:     return new cl_OSMPosition;
        case TAG_CLASS_FUSE_RACK:     return new cl_FuseRack_Element;
        case TAG_CLASS_CONFIG:        return new cl_Applic_Config(NULL);
        case TAG_CLASS_CALC_TEST:     return new cl_Calc_Test;
        case TAG_CLASS_PROT_OVRCURR_Tim_Ind: return new cl_OvrCurrProtection_Tim_Ind;
        case TAG_CLASS_PROT_OVRCURR_Tim_Dep: return new cl_OvrCurrProtection_Tim_Dep;
        case TAG_CLASS_PROT_OVRCURR_Flash:   return new cl_OvrCurrProtection_Flash;
        case TAG_CLASS_PQ_Diagram:           return new cl_PQ_Diagram;
        case TAG_CLASS_PQ_Diag_Serie:        return new cl_PQ_Diagram_Serie;
        case TAG_CLASS_PQ_Diag_Serie_Point:  return new cl_PQ_Diagram_Serie_Point;
    }

    // Extension points for conditional builds:
    #if defined _CLIENT_
        // Auth_CreateObjectByTag(nTag) -- licensing objects
    #endif
    #if defined _VOLTAGE_CTRL_
        // IEC104_CreateObjectByTag(nTag) -- voltage control objects
    #endif

    return nullptr;  // Unknown tag logged, silently skipped
}
```

The `cl_SerializableObject` base class has a virtual `CreateObjectByTag()` that delegates to this global function by default. Specific objects can override it for context-specific sub-object creation (e.g., `cl_FreqCalc_Serializer` in `cl_FreqCalc.h` overrides it to create frequency calculation result objects).

---

## 7. Element Serialization Examples

### 7.1 `cl_Line_Element` (Power Line)

**Serialize** (`cl_Line_Element.cpp` line 177):

```cpp
void cl_Line_Element::Serialize(cl_Serializer *pSerializer)
{
    // First: serialize base class attributes (ID, name, position, connections...)
    cl_Term_Element::Serialize(pSerializer);

    // Line-specific attributes
    pSerializer->AddTLV(TAG_dbl_Un, sizeof(double), &m_fUn);         // Nominal voltage
    pSerializer->AddTLV(TAG_dbl_Imax, sizeof(double), &m_fImax);     // Max current
    pSerializer->AddTLV(TAG_dbl_Imaxn, sizeof(double), &m_fImaxn);   // Max neutral current
    pSerializer->AddTLV(TAG_dbl_Length, sizeof(double), &m_fLength);  // Length in km
    pSerializer->AddTLV(TAG_dbl_CrossSection, sizeof(double), &m_fCrossSect);

    pSerializer->AddTLV_UTF8(TAG_sz8_Type, m_szType);                // Line type string
    pSerializer->AddTLV(TAG_LINE_Kind, sizeof(m_LineKind), &m_LineKind); // Enum: outdoor/cable/NA

    pSerializer->AddTLV(TAG_dbl_SpecR, sizeof(double), &m_fSpecR);   // Specific resistance
    pSerializer->AddTLV(TAG_dbl_SpecX, sizeof(double), &m_fSpecX);   // Specific reactance
    pSerializer->AddTLV(TAG_dbl_SpecB, sizeof(double), &m_fSpecB);   // Specific susceptance

    pSerializer->AddTLV_Bool(TAG_b_EnterL, m_bEnterL);
    pSerializer->AddTLV_Bool(TAG_b_EnterC, m_bEnterC);
    pSerializer->AddTLV(TAG_dbl_SpecL, sizeof(double), &m_fSpecL);
    pSerializer->AddTLV(TAG_dbl_SpecC, sizeof(double), &m_fSpecC);

    pSerializer->AddTLV(TAG_dbl_R0_R1, sizeof(double), &m_fR0_R1);  // Zero-seq ratios
    pSerializer->AddTLV(TAG_dbl_X0_X1, sizeof(double), &m_fX0_X1);

    m_RelParams.Serialize(pSerializer);   // Reliability parameters (inline)
    m_EcoParams.Serialize(pSerializer);   // Economy parameters (inline)

    Serialize_3Ph(pSerializer, CFG_3PH_Line_Elem);  // 3-phase specific params

    // Serialize GIS topo points as sub-objects
    for (auto iter = m_lstTopoPoint.begin(); iter != m_lstTopoPoint.end(); ++iter)
        pSerializer->Serialize(*iter);  // Each is a TAG_CLASS_OSM_POINT sub-object
}
```

**Deserialize** (`cl_Line_Element.cpp` line 214):

```cpp
bool cl_Line_Element::Deserialize(uint32_t nTag, uint32_t nLength, void *pValue)
{
    bool bReturn = true;
    switch(nTag)
    {
        case TAG_dbl_Un:           GetTLV(nLength, pValue, &m_fUn); break;
        case TAG_dbl_Imax:         GetTLV(nLength, pValue, &m_fImax); break;
        case TAG_dbl_Length:       GetTLV(nLength, pValue, &m_fLength); break;
        case TAG_dbl_CrossSection: GetTLV(nLength, pValue, &m_fCrossSect); break;
        case TAG_sz8_Type:         GetTLV_UTF8(nLength, pValue, m_szType); break;
        case TAG_LINE_Kind:        GetTLV(nLength, pValue, &m_LineKind); break;
        case TAG_dbl_SpecR:        GetTLV(nLength, pValue, &m_fSpecR); break;
        // ... more attributes ...
        default: bReturn = false; break;
    }

    // Chain to parent class and composition objects
    if (!bReturn) bReturn = cl_Term_Element::Deserialize(nTag, nLength, pValue);
    if (!bReturn) bReturn = m_RelParams.Deserialize(nTag, nLength, pValue);
    if (!bReturn) bReturn = m_EcoParams.Deserialize(nTag, nLength, pValue);
    return bReturn;
}
```

**ProcessNewSubObject** -- handles GIS points:
```cpp
bool cl_Line_Element::ProcessNewSubObject(cl_SerializableObject *pObj)
{
    cl_OSMPosition *pOSM_Pos = dynamic_cast<cl_OSMPosition*>(pObj);
    if (pOSM_Pos != NULL)
    {
        m_lstTopoPoint.push_back(pOSM_Pos);
        return true;  // Claimed ownership
    }
    return cl_MultiTerm_Element::ProcessNewSubObject(pObj);
}
```

### 7.2 `cl_Transformer_Element` (2-Winding Transformer)

The pattern is identical. Key differences:

- Returns `TAG_CLASS_XFORMER` (`0x80001300`) from `GetObjectType()`.
- Serializes transformer-specific parameters: U1, U2, St, Pk, uk, branch regulation, winding types, clock angle, impedance ratios, auto-regulation parameters.
- Uses `AddTLV_Bool` for booleans, `AddTLV_U16`/`AddTLV_I16` for branch counts, `AddTLV_UTF8` for manufacturer and type strings.
- Also serializes via composition: `cl_Power_Colour_Element::Serialize()` (adds power domain colour), `m_RelParams`, `m_EcoParams`.
- Deserialization chains: own switch -> `m_RelParams` -> `m_EcoParams` -> `cl_MultiTerm_Element` -> `cl_Power_Colour_Element`.

### 7.3 `cl_Term_Element` (Base Terminal Element)

Serializes shared terminal element attributes:
- Calls `cl_Scheme_Element::Serialize()` for base attributes (ID, name, position, etc.).
- Writes hand-over node/line IDs (`TAG_u32_HandOvr_Node`, `TAG_u32_HandOvr_Line`).
- For each connected node terminal, serializes a `cl_Term_Conn_Hlp` sub-object containing the terminal index and connected node ID.
- Optionally serializes 3-phase parameters (`TAG_u8_Phase_Conn`, `TAG_3Ph_Connection`, etc.).

### 7.4 Inheritance Chain Pattern

The serialization follows the C++ class hierarchy:

```
cl_SerializableObject
  -> cl_Scheme_Element          (ID, name, position, visible, orientation, note, attributes)
     -> cl_Term_Element         (connections, hand-over refs, 3-phase params)
        -> cl_MultiTerm_Element (multi-terminal specifics)
           -> cl_Line_Element   (line-specific: Un, length, R, X, B, ...)
           -> cl_Transformer_Element (U1, U2, St, Pk, uk, windings, ...)
```

Each level calls `Parent::Serialize(pSerializer)` first, then writes its own attributes. On deserialization, unrecognized tags cascade up through `Parent::Deserialize()`.

---

## 8. Data Flow

### 8.1 Save: User -> File

1. **User action**: Menu "Ulozit" (Ctrl+S) -> `EVlivy3Frame::OnMenuSchemeSave()`.
2. **Dispatch**: Calls `pScheme_Pnl->Do_Save()` (`cl_Scheme_Pnl.cpp` line 2380).
3. **File name resolution**: If new scheme, prompts for `.egc3` filename via `wxFileDialog`.
4. **Modification info**: `m_pScheme->SetModifiedInfo()` updates modified-by/timestamp.
5. **Serialization**:
   ```cpp
   cl_Serializer TLV;
   TLV.Serialize(m_pScheme);
   ```
   This writes:
   - 8-byte class header: `{ TAG_CLASS_SCHEME | TLV_CLASS, 0 }` (length patched later)
   - Scheme attributes: version, grid size, canvas, scale, all ID counters, calculation settings, etc.
   - For each element in `m_setElement` (Z-order sorted): `pSerializer->Serialize(*iter)` -- writes each element as a nested class TLV.
   - Header length is patched to reflect actual size.
6. **File write**:
   ```cpp
   wxFile OutFile(szFileName, wxFile::write);
   bOK = TLV.WriteTLVArchive(OutFile);  // default compression level 6
   ```
   - Compresses the TLV buffer with bzip2.
   - Writes 8-byte compressed header (`VLTX` + size) followed by compressed data.
7. **Status update**: Sets `m_dtLastSave`, marks scheme as unchanged.

### 8.2 Load: File -> Scheme

1. **User action**: Menu "Otevrit" (Ctrl+O) -> `EVlivy3Frame::Do_OpenScheme(szFileName)` (`EVlivy3Main.cpp` line ~2107).
2. **Header validation**:
   ```cpp
   wxFile InFile(szFileName, wxFile::read);
   uint32_t nHead;
   InFile.Read(&nHead, sizeof(nHead));
   if (nHead != cl_Serializer::COMPRESSED_ARCHIVE_MAGIC)
       throw ...;  // "not a supported format"
   ```
3. **Read and decompress**:
   ```cpp
   InFile.Seek(0, wxFromStart);
   cl_Serializer TLV;
   TLV.ReadTLV(&InFile);
   ```
   - Reads the 8-byte header.
   - Detects `VLTX` magic -> reads compressed data -> bzip2 decompresses into internal buffer.
4. **Deserialize**:
   ```cpp
   pScheme = dynamic_cast<cl_Scheme*>(TLV.Deserialize());
   ```
   - Reads root TLV header -> `CreateObjectByTag(TAG_CLASS_SCHEME)` -> `new cl_Scheme`.
   - `DeserializeClassRecursive()` processes all TLV records within the scheme's data:
     - **Attributes** (non-class tags): dispatched to `cl_Scheme::Deserialize()` switch statement.
     - **Sub-objects** (class tags): `CreateObjectByTag()` creates the element, recursively deserializes it, then `cl_Scheme::ProcessNewSubObject()` adds it to `m_setElement`.
   - Each element's sub-objects (connection helpers, label attributes, GIS points) are recursively handled the same way.
5. **Post-processing**: `cl_Scheme::Deserialize_Done()`:
   - Calls `Load_Done()` on each element (resolves ID -> pointer references for connections).
   - Recalculates `m_nZAxis_Series` and `m_nID_Series`.
   - Applies version migration logic.
   - Sets impedance limits.
6. **GUI setup**: Assigns the scheme to a new `cl_Scheme_Pnl`, applies configuration, populates UI.

---

## 9. TLV for IEC 104 (TCP Data Transfer)

Evidence in `cl_104_Connector.cpp` confirms TLV is used for TCP communication with the `dncors_iec104` module:

### 9.1 Sending Commands

```cpp
// cl_104_Connector.cpp line ~498
cl_Serializer TLV;
TLV.Serialize(pCommand);
uint8_t *pTx_Buff;
uint32_t Cmd_len = TLV.WriteTLVArchive(&pTx_Buff, 0);  // NO compression
int nRes = send(m_104_Rx.m_Sockfd, (const char*)pTx_Buff, Cmd_len, 0);
free(pTx_Buff);
```

Key observations:
- Uses **compression level 0** (no bzip2) for TCP transfer -- the 8-byte header is still written but with `nTag = 0` (not the VLTX magic).
- The buffer-based `WriteTLVArchive` variant is used, producing a `malloc()`-allocated buffer that is sent via `send()` and then `free()`d.

### 9.2 Receiving Commands

```cpp
// cl_104_Connector.cpp line ~2013
cl_Serializer TLV_Rx;
TLV_Rx.ReadTLV(nullptr, m_pParent->m_pRxBuffer, RX_BUFF_LEN);
pRxCmd = dynamic_cast<cl_Command*>(TLV_Rx.Deserialize());
```

Uses the memory-buffer variant of `ReadTLV()` (first param `nullptr` means no file, use the provided buffer).

### 9.3 IEC 104 Tags

When `_VOLTAGE_CTRL_` is defined:
- `tlv_tag.h` includes `DN_Cors_tag.h` which defines additional IEC 104 / voltage control command tags.
- The factory includes `IEC104_CreateObjectByTag()` for creating command/response objects specific to the voltage control protocol.
- Scheme-level CORS attributes are serialized: `TAG_u32_CORS_REG_MODE`, `TAG_b_CORS_REG_BRANCH`, `TAG_dbl_CORS_UNET0/1`, `TAG_dbl_CORS_QVVN`, `TAG_dbl_CORS_QTOL`, `TAG_sz8_104_LINK`.

### 9.4 Clipboard Transfer

The clipboard also uses TLV with bzip2 compression (default level 6):
```cpp
// cl_Clipboard.cpp
cl_Serializer TLV;
TLV.Serialize(this);  // this = cl_Clipboard object
uint8_t *pBuff;
uint32_t nSize = TLV.WriteTLVArchive(&pBuff);  // compressed to buffer
// ... copy to wxClipboard ...
```

On paste, it reverses via `ReadTLV(NULL, data, size)` -> `Deserialize()`.

---

## 10. Summary of Binary File Layout

A typical `.egc3` file contains:

```
[VLTX Header: 8 bytes]
  Tag:    0x564C5458 (VLTX magic)
  Length: N (compressed size)

[bzip2 compressed payload: N bytes]
  Decompresses to:

  [CLASS: TAG_CLASS_SCHEME (0x80000100)]
    Length: M
    [ATTR: TAG_u32_Version = 0x00010005]
    [ATTR: TAG_u32_GridSize = 20]
    [ATTR: TAG_u32_Canvas_X = 2000]
    [ATTR: TAG_u32_Canvas_Y = 1000]
    [ATTR: TAG_dbl_Scale = 1.0]
    [ATTR: TAG_u32_Elem_Counter = ...]
    ... (dozens of scheme-level attributes) ...

    [CLASS: TAG_CLASS_NODE (0x80001000)]
      Length: ...
      [ATTR: TAG_u32_ID = 1]
      [ATTR: TAG_sz8_Name = "N1"]
      [ATTR: TAG_u32_Position_X = 100]
      [ATTR: TAG_u32_Position_Y = 200]
      ... (node attributes) ...
      [CLASS: TAG_CLASS_NAME_ATTRIB (0x80000010)]
        ... (label position, font) ...
      [CLASS: TAG_CLASS_NODE_CONN_HLP (0x80001080)]
        ... (connected element IDs) ...

    [CLASS: TAG_CLASS_LINE (0x80001200)]
      Length: ...
      [ATTR: TAG_u32_ID = 2]
      [ATTR: TAG_sz8_Name = "V1"]
      [ATTR: TAG_dbl_Un = 22.0]
      [ATTR: TAG_dbl_Length = 5.3]
      ... (line attributes) ...
      [CLASS: TAG_CLASS_TERM_CONN_HLP (0x80000280)]
        [ATTR: TAG_u32_Terminal = 0]
        [ATTR: TAG_u32_ID = 1]  (connected to node N1)
      [CLASS: TAG_CLASS_TERM_CONN_HLP (0x80000280)]
        [ATTR: TAG_u32_Terminal = 1]
        [ATTR: TAG_u32_ID = 3]  (connected to node N2)

    [CLASS: TAG_CLASS_XFORMER (0x80001300)]
      ... (transformer attributes and sub-objects) ...

    ... (more elements) ...
```

Each element is a complete, self-contained nested TLV structure. The tree mirrors the object graph in memory. Connections between elements are stored as **ID references** (uint32 IDs), resolved to pointers during `Deserialize_Done()`.

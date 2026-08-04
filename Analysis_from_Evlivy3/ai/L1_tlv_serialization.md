# TLV Serialization — Module Overview (L1)

## Binary Format
Each TLV record: `[uint32 tag][uint32 length][N bytes value]` — little-endian.
- **Tag bit 31** (`0x80000000` = `TLV_CLASS`): marks nested class containers (contain child TLV records)
- **Simple tags**: encode primitive values (uint32, double, string, etc.)
- Length = byte count of value only (excludes tag+length header = 8 bytes)

## Data Type Encoding
| Type | Method | Encoding |
|------|--------|----------|
| uint32 | `Add_uint32(tag, val)` | 4 bytes LE |
| uint64 | `Add_uint64(tag, val)` | 8 bytes LE |
| double | `Add_double(tag, val)` | 8 bytes IEEE 754 |
| bool | `Add_bool(tag, val)` | 4 bytes (0 or 1) |
| string UTF8 | `Add_szUTF8(tag, wxString)` | UTF-8 bytes, no null terminator |
| string UTF16 | `Add_szUTF16(tag, wxString)` | UTF-16LE bytes |
| complex | `Add_complex(tag, std::complex<double>)` | 16 bytes (real + imag) |
| datetime | `Add_DateTime(tag, wxDateTime)` | 8 bytes (IsValid flag + GetTicks) |
| color | `Add_Colour(tag, wxColour)` | 4 bytes (R,G,B,A) |
| rect | Separate X,Y,W,H tags | 4 doubles via sub-tags |

## Key Classes
### cl_Serializer (Serializable.h, Serializable.cpp)
- **Buffer management**: internal byte buffer, grows as needed
- **Packing**: `Add_*(tag, value)` methods append to buffer
- **Unpacking**: `Get_*(tag, &value)` methods read from buffer
- **Nesting**: `OpenObject(classTag)` / `CloseObject()` for class containers
- **File I/O**: `WriteTLVArchive(filename, compressionLevel)` — bzip2 compressed
- **File read**: `ReadTLVArchive(filename)` — auto-detects bzip2 or raw

### cl_SerializableObject (interface)
Every serializable class must implement:
- `GetClassTag()` → uint32 (e.g., `TAG_CLASS_LINE = 0x80001200`)
- `Serialize(cl_Serializer&)` — write all properties as TLV attributes
- `Deserialize(cl_Serializer&, uint32_t tag, uint32_t len)` — read one attribute by tag
- `Deserialize_Done(cl_Scheme*)` — post-load: resolve ID refs → pointers

### Factory: CreateObjectByTag(uint32_t tag)
Switch on tag value → returns `new cl_*_Element()`. Used during file/TCP deserialization.

## Serialization Pattern (per element)
```cpp
void cl_Line_Element::Serialize(cl_Serializer &rS) {
    cl_MultiTerm_Element::Serialize(rS);  // base class first
    rS.Add_double(TAG_dbl_Un, m_fUn);
    rS.Add_double(TAG_dbl_Length, m_fLength);
    rS.Add_double(TAG_dbl_R, m_fR);
    // ... more properties
}
void cl_Line_Element::Deserialize(cl_Serializer &rS, uint32_t tag, uint32_t len) {
    switch(tag) {
        case TAG_dbl_Un: rS.Get_double(tag, &m_fUn); break;
        case TAG_dbl_Length: rS.Get_double(tag, &m_fLength); break;
        // ... or pass to base class
        default: cl_MultiTerm_Element::Deserialize(rS, tag, len);
    }
}
```

## File Format
```
[bzip2 header (if compressed)]
[TLV_CLASS_SCHEME container]
  [TAG_u32_Version = 0x00010005]
  [TAG_u32_CalcMethod = ...]
  [... scheme attributes ...]
  [TLV_CLASS_NODE container]
    [TAG_u32_ID = 1]
    [TAG_sz8_Name = "N1"]
    [... node attributes ...]
  [TLV_CLASS_LINE container]
    [TAG_u32_ID = 2]
    [... line attributes ...]
    [TLV_CLASS_TERM_CONN_HLP]
      [TAG_u32_Terminal = 0]
      [TAG_u32_ID = 1]  // connected node ID
    [TLV_CLASS_TERM_CONN_HLP]
      [TAG_u32_Terminal = 1]
      [TAG_u32_ID = 3]
  [... more elements ...]
```

## TLV Also Used For
- TCP communication with dncors_iec104 (same cl_Serializer, different top-level tags)
- Clipboard operations (copy/paste elements)
- PQ diagram templates (files in PQ_Diagram/*.tlv)

## Source Files
| File | Purpose |
|------|---------|
| `include/Serializable.h` | cl_Serializer class + cl_SerializableObject interface |
| `Serializable.cpp` | Implementation |
| `include/tlv_tag.h` | 400+ tag constant definitions |
| `include/common.h` | SCHEME_FILE_VERSION (0x00010005) |

→ For complete tag list see `L2_tlv_tags.md`

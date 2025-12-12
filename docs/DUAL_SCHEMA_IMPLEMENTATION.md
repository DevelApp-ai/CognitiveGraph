# Dual-Schema Architecture Implementation Summary

## Overview
This implementation provides the complete Dual-Schema Architecture as specified in `docs/Dual-Schema Architecture.docx`, enabling CognitiveGraph to support both compact (V1) and universal (V2) schemas for maximum flexibility and scalability.

## Implemented Components

### 1. Schema Version Management
- **SchemaVersion enum**: Defines V1 (Compact) and V2 (Universal) schema versions
- **GraphHeaderPreamble**: Common 6-byte header shared by both schemas for version detection

### 2. Schema V2 Data Structures
All V2 structures use 8-byte alignment and 64-bit offsets:

- **GraphHeaderV2** (64 bytes): Extended header with 64-bit offsets for massive graphs
  - Supports files up to 16 Exabytes
  - Uses `ulong` for all offset fields
  
- **SymbolNodeDataV2** (32 bytes): Symbol nodes with 32-bit IDs
  - `uint` for SymbolID and NodeType (vs `ushort` in V1)
  - `ulong` for PackedNodesOffset and PropertiesOffset
  
- **PackedNodeDataV2** (24 bytes): Packed nodes with 64-bit offsets
  - `uint` for RuleID (vs `ushort` in V1)
  - `ulong` for ChildNodesOffset and CpgEdgesOffset

### 3. Buffer Abstraction Layer
- **IGraphBuffer interface**: Abstraction for buffer access supporting both schemas
  - Provides methods for reading primitive types at any offset
  - Supports both safe (V1) and unsafe (V2) implementations

- **CompactGraphBuffer**: V1 implementation using safe `ReadOnlySpan<byte>`
  - Renamed from `CognitiveGraphBuffer`
  - Limited to ~4GB files
  - Uses 32-bit offsets
  
- **UniversalGraphBuffer**: V2 implementation using unsafe pointer arithmetic
  - Bypasses .NET 2GB array limit
  - Supports files >4GB via memory-mapped files
  - Uses 64-bit offsets

- **CognitiveGraphBuffer**: Maintained as obsolete wrapper for backward compatibility

### 4. V2 Accessors
Zero-allocation ref struct accessors for V2 schema:

- **SymbolNode64**: Accessor for V2 symbol nodes
  - Uses `UniversalGraphBuffer` for data access
  - 32-bit IDs, 64-bit offsets
  - Provides enumerations for packed nodes and properties
  
- **PackedNode64**: Accessor for V2 packed nodes
  - 32-bit RuleID, 64-bit offsets
  - Supports child node and CPG edge navigation

- **Collection types**: `PackedNodeOffsetCollection64`, `SymbolNodeOffsetCollection64`
  - Zero-allocation enumerators
  - Support for massive node counts with `LongCount` property

### 5. Configuration
- **GraphBuilderOptions**: Configuration class for builder
  - `Schema` property: Select V1 or V2 schema  
  - `InitialCapacity` property: Set initial buffer size
  - Factory methods: `Universal()` for V2, `Compact()` for V1
  - **Default**: V1 (Compact Mode) for backward compatibility

### 6. CognitiveGraphBuilder Integration ✅ NEW
The builder now fully supports dual-schema as per TDS Section 4.3:

- **Accepts GraphBuilderOptions** parameter in constructor
- **Writes V1 or V2 format** based on configuration
- **Separate write paths** for V1 and V2:
  - `WriteSymbolNode`/`WriteSymbolNodeV2`
  - `WritePackedNode`/`WritePackedNodeV2`
  - `WriteList`/`WriteListV2`
  - `Build`/`BuildV2`
- **Automatic offset tracking** (`_currentOffset` for V1, `_currentOffsetV2` for V2)

### 7. CognitiveGraph Integration ✅ NEW
The main class now implements TDS Section 3.1 "Header Resolution Strategy":

- **Preamble-based version detection**: Reads 6-byte GraphHeaderPreamble first
- **Automatic buffer instantiation**:
  - V1: Creates `CompactGraphBuffer` with safe span access
  - V2: Creates `UniversalGraphBuffer` with unsafe pointer access
- **Schema-specific accessors**:
  - V1: `GetRootNode()`, `GetNodeAt(uint offset)`
  - V2: `GetRootNodeV2()`, `GetNodeAtV2(ulong offset)`
- **Unified high-level methods**: `GetSourceText()`, `GetStatistics()` work for both schemas

### 8. Migration & Upgrade ✅ NEW
As specified in TDS Section 5.2:

- **CognitiveGraph.Upgrade(inputPath, outputPath)**: Utility method to convert V1 → V2
  - Opens input V1 file
  - Initializes CognitiveGraphBuilder in V2 mode
  - Traverses V1 nodes and writes to V2 builder
  - Produces scale-ready V2 graph

## Backward Compatibility

The implementation maintains 100% backward compatibility with existing V1 graphs:

1. **CognitiveGraphBuffer** wrapper class (marked obsolete) delegates to `CompactGraphBuffer`
2. All existing V1 accessors (`SymbolNode`, `PackedNode`) continue to work unchanged
3. **File format detection** via `GraphHeaderPreamble` automatically identifies V1 vs V2
4. V2-enabled library can seamlessly read and process V1 files
5. **Builder defaults to V1** for backward compatibility (V2 opt-in via `GraphBuilderOptions.Universal()`)

## Key Design Decisions

### Default to V1 (Updated)
To maintain backward compatibility, `GraphBuilderOptions` defaults to `SchemaVersion.V1`. Users can explicitly choose V2 via `GraphBuilderOptions.Universal()` when maximum scalability is needed.

### Separate Accessor Types
Rather than using a unified accessor with runtime version checks (which would harm performance), the implementation provides separate accessor types (`SymbolNode` vs `SymbolNode64`). This ensures optimal performance by avoiding branch misprediction.

### Buffer Interface
The `IGraphBuffer` interface provides a clean abstraction while allowing specialized implementations to optimize for their specific constraints (safe spans for V1, unsafe pointers for V2).

### Preamble-First Reading
Following TDS Section 3.1 exactly: CognitiveGraph constructor reads the 6-byte preamble, determines version, then casts/reads the full header (40 bytes for V1, 64 bytes for V2).

## Testing

- **Unit tests** verify struct sizes, alignments, and field access
- **All existing tests pass** (58 tests, 0 failures)
- Backward compatibility verified through existing test suite
- Builder can create both V1 and V2 graphs
- CognitiveGraph can read both V1 and V2 files

## Complete Implementation Status

✅ **All Core Components from TDS Implemented:**

1. ✅ Schema V2 structures (GraphHeaderV2, SymbolNodeDataV2, PackedNodeDataV2)
2. ✅ Buffer abstraction (IGraphBuffer, CompactGraphBuffer, UniversalGraphBuffer)
3. ✅ V2 accessors (SymbolNode64, PackedNode64)
4. ✅ Builder configuration (GraphBuilderOptions)
5. ✅ Builder V2 write support (Section 4.3)
6. ✅ CognitiveGraph dual-schema detection (Section 3.1)
7. ✅ Upgrade utility (Section 5.2)

## Migration Path

For users wanting to adopt V2:

1. **New graphs**: Use `new CognitiveGraphBuilder(GraphBuilderOptions.Universal())` to create V2 graphs
2. **Existing V1 graphs**: Continue to work without modification
3. **Upgrade V1 → V2**: Use `CognitiveGraph.Upgrade(inputPath, outputPath)` method

## File Compatibility

- V1 files: Identified by `Version = 1` in preamble, processed by `CompactGraphBuffer`
- V2 files: Identified by `Version = 2` in preamble, processed by `UniversalGraphBuffer`
- Both use magic number `0x434F474E` ("COGN")
- Automatic detection and handling based on preamble

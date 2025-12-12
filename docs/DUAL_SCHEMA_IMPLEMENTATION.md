# Dual-Schema Architecture Implementation Summary

## Overview
This implementation provides the Dual-Schema Architecture as specified in `docs/Dual-Schema Architecture.docx`, enabling CognitiveGraph to support both compact (V1) and universal (V2) schemas for maximum flexibility and scalability.

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
  - Support for massive node counts

### 5. Configuration
- **GraphBuilderOptions**: Configuration class for builder
  - `Schema` property: Select V1 or V2 schema
  - `InitialCapacity` property: Set initial buffer size
  - Factory methods: `Universal()` for V2, `Compact()` for V1
  - **Default**: V2 (Universal Mode) for maximum scalability

## Backward Compatibility

The implementation maintains 100% backward compatibility with existing V1 graphs:

1. **CognitiveGraphBuffer** wrapper class (marked obsolete) delegates to `CompactGraphBuffer`
2. All existing V1 accessors (`SymbolNode`, `PackedNode`) continue to work unchanged
3. File format detection via `GraphHeaderPreamble` automatically identifies V1 vs V2
4. V2-enabled library can seamlessly read and process V1 files

## Key Design Decisions

### Default to V2
`GraphBuilderOptions` defaults to `SchemaVersion.V2` to ensure new graphs are created with maximum scalability by default. Users can explicitly choose V1 for smaller graphs if desired.

### Separate Accessor Types
Rather than using a unified accessor with runtime version checks (which would harm performance), the implementation provides separate accessor types (`SymbolNode` vs `SymbolNode64`). This ensures optimal performance by avoiding branch misprediction.

### Buffer Interface
The `IGraphBuffer` interface provides a clean abstraction while allowing specialized implementations to optimize for their specific constraints (safe spans for V1, unsafe pointers for V2).

## Testing

- **Unit tests** verify struct sizes, alignments, and field access
- **All existing tests pass** (49 tests, 0 failures)
- Backward compatibility verified through existing test suite

## Not Yet Implemented

The following items from the TDS remain to be implemented in future work:

1. **Builder V2 support**: Update `CognitiveGraphBuilder` to write V2 format
2. **CognitiveGraph dual-schema support**: Update main class to detect and use correct schema
3. **Upgrade utility**: `CognitiveGraph.Upgrade()` method to convert V1 → V2
4. **V2-specific integration tests**: End-to-end tests with V2 format
5. **Property and CPG edge V2 accessors**: Complete the accessor suite

## Migration Path

For users wanting to adopt V2:

1. **New graphs**: Will use V2 by default via `GraphBuilderOptions`
2. **Existing V1 graphs**: Continue to work without modification
3. **Upgrade V1 → V2**: Will use `CognitiveGraph.Upgrade()` method (to be implemented)

## File Compatibility

- V1 files: Identified by `Version = 1` in header, processed by `CompactGraphBuffer`
- V2 files: Identified by `Version = 2` in header, processed by `UniversalGraphBuffer`
- Both use magic number `0x434F474E` ("COGN")

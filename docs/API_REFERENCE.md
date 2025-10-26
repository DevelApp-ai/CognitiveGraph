# API Reference

## Overview

The CognitiveGraph library provides a high-performance, zero-copy graph structure for advanced code analysis. The API is designed around three core concepts:

1. **Building Graphs** - Creating graph structures with `CognitiveGraphBuilder`
2. **Reading Graphs** - Accessing graph data with zero-copy `CognitiveGraph`
3. **Navigation** - Traversing nodes and edges through accessor objects

## Core Classes

### CognitiveGraph

The main entry point for reading and navigating cognitive graphs.

```csharp
public sealed class CognitiveGraph : IDisposable
```

#### Key Methods

- **`GetRootNode()`** - Returns the root `SymbolNode` of the graph
- **`GetNode(uint offset)`** - Retrieves a specific node by offset
- **`Query(string queryExpression)`** - Executes a graph query
- **`GetSourceText()`** - Returns the original source code text

#### Usage Example

```csharp
using var graph = new CognitiveGraph(buffer);
var rootNode = graph.GetRootNode();
var sourceText = graph.GetSourceText();
```

### CognitiveGraphBuilder

Builder class for creating cognitive graphs with zero-copy architecture.

```csharp
public sealed class CognitiveGraphBuilder : IDisposable
```

#### Key Methods

- **`WriteSymbolNode(...)`** - Creates a symbol node with properties
- **`WritePackedNode(...)`** - Creates a packed node for ambiguous parsing
- **`WriteCpgEdge(...)`** - Creates control/data flow edges
- **`Build(...)`** - Finalizes the graph and returns buffer

#### Usage Example

```csharp
using var builder = new CognitiveGraphBuilder();
var nodeOffset = builder.WriteSymbolNode(
    symbolId: 1,
    nodeType: 200,
    sourceStart: 0,
    sourceLength: 13,
    properties: properties
);
var buffer = builder.Build(nodeOffset, sourceCode);
```

## Accessor Classes

### SymbolNode

Represents a symbol in the Abstract Syntax Tree with zero-copy access.

```csharp
public readonly ref struct SymbolNode
```

#### Properties

- **`SymbolID`** - Unique identifier for the symbol
- **`NodeType`** - Type identifier for the node
- **`SourceStart`** - Starting position in source code
- **`SourceLength`** - Length of source span
- **`IsAmbiguous`** - Whether this node has multiple parse interpretations

#### Key Methods

- **`TryGetProperty(string key, out Property value)`** - Type-safe property access
- **`GetPackedNodes()`** - Returns all parse interpretations for ambiguous nodes
- **`GetCpgEdges()`** - Returns control/data flow edges
- **`GetChildNodes()`** - Returns child symbol nodes

#### Usage Example

```csharp
if (rootNode.TryGetProperty("Operator", out var op))
{
    Console.WriteLine($"Operator: {op.AsString()}");
}

if (rootNode.IsAmbiguous)
{
    var interpretations = rootNode.GetPackedNodes();
    foreach (var interpretation in interpretations)
    {
        // Process each possible parse tree
    }
}
```

### PackedNode

Represents a specific parse interpretation for ambiguous syntax.

```csharp
public readonly ref struct PackedNode
```

#### Properties

- **`RuleID`** - Grammar rule identifier
- **`NodeType`** - Type of the parsed construct
- **`ChildCount`** - Number of child nodes

#### Key Methods

- **`GetChildNodes()`** - Returns child nodes for this interpretation
- **`GetSourceSpan()`** - Returns source code span covered by this node

### Property

Type-safe property value accessor supporting multiple data types.

```csharp
public readonly ref struct Property
```

#### Properties

- **`Type`** - Property value type (`PropertyValueType` enum)
- **`Key`** - Property name/key

#### Key Methods

- **`AsString()`** - Returns string value (throws if wrong type)
- **`AsInt32()`** - Returns 32-bit integer value
- **`AsInt64()`** - Returns 64-bit integer value
- **`AsDouble()`** - Returns double-precision floating point value
- **`AsBoolean()`** - Returns boolean value
- **`TryAsString(out string value)`** - Safe string conversion
- **`TryAsInt32(out int value)`** - Safe integer conversion

#### Usage Example

```csharp
if (node.TryGetProperty("LineNumber", out var lineProp))
{
    if (lineProp.TryAsInt32(out int lineNum))
    {
        Console.WriteLine($"Line: {lineNum}");
    }
}
```

### CpgEdge

Represents control flow and data flow relationships between nodes.

```csharp
public readonly ref struct CpgEdge
```

#### Properties

- **`EdgeType`** - Type of relationship (`CpgEdgeType` enum)
- **`SourceOffset`** - Source node offset
- **`TargetOffset`** - Target node offset
- **`Label`** - Optional edge label

#### Edge Types

- **`ControlFlow`** - Sequential execution flow
- **`DataFlow`** - Variable/value dependencies
- **`TypeHierarchy`** - Type inheritance relationships
- **`CallGraph`** - Function call relationships

## Enumerations

### PropertyValueType

Supported property value types:

- **`String`** - UTF-8 encoded string
- **`Int32`** - 32-bit signed integer
- **`Int64`** - 64-bit signed integer
- **`Double`** - Double-precision floating point
- **`Boolean`** - Boolean true/false

### CpgEdgeType

Types of relationships in the Code Property Graph:

- **`ControlFlow`** - Program execution flow
- **`DataFlow`** - Data dependencies
- **`TypeHierarchy`** - Type relationships
- **`CallGraph`** - Function calls

## Memory Management

### Zero-Copy Architecture

The library uses `ReadOnlySpan<T>` and `ref struct` types to provide direct memory access without allocations:

- All accessor objects are stack-only (`ref struct`)
- No heap allocations during graph navigation
- Direct access to underlying byte buffer
- Compatible with memory-mapped files for large datasets

### Disposal Pattern

```csharp
// Builder disposal
using var builder = new CognitiveGraphBuilder();
// Automatically disposed when out of scope

// Graph disposal
using var graph = new CognitiveGraph(buffer);
// Automatically disposed when out of scope

// Memory-mapped file usage
using var mmf = MemoryMappedFile.CreateFromFile("graph.bin");
using var accessor = mmf.CreateViewAccessor();
// Resources automatically cleaned up
```

## Error Handling

### Common Exceptions

- **`ArgumentException`** - Invalid parameters passed to methods
- **`InvalidOperationException`** - Graph is in invalid state
- **`ObjectDisposedException`** - Attempting to use disposed objects
- **`InvalidCastException`** - Type mismatch in property access

### Best Practices

```csharp
// Always use try-get methods for optional data
if (node.TryGetProperty("OptionalProp", out var prop))
{
    // Property exists, safe to use
}

// Check type before casting
if (prop.Type == PropertyValueType.String)
{
    string value = prop.AsString();
}

// Use using statements for proper disposal
using var graph = new CognitiveGraph(buffer);
// Graph automatically disposed at end of scope
```

## Threading and Concurrency

### Thread Safety

- **Read operations**: Fully thread-safe, multiple threads can read simultaneously
- **Write operations**: Single-threaded during graph building
- **Concurrent readers**: No synchronization required for reading
- **Builder isolation**: Each `CognitiveGraphBuilder` instance is single-threaded

### Example: Concurrent Reading

```csharp
var graph = new CognitiveGraph(buffer);

// Multiple threads can safely read from the same graph
Parallel.ForEach(nodeOffsets, offset =>
{
    var node = graph.GetNode(offset);
    // Process node safely in parallel
});
```

## Performance Characteristics

### Time Complexity

- **Node access**: O(1) - Direct offset-based lookup
- **Property access**: O(1) - Direct memory access
- **Graph traversal**: O(V + E) where V = vertices, E = edges
- **Memory allocation**: O(0) - Zero-allocation accessor pattern

### Space Complexity

- **Memory usage**: O(n) where n = source code size
- **Graph structure**: Compact binary format with minimal overhead
- **Property storage**: Efficient variant-typed storage system

## Integration Examples

### Basic Usage Pattern

```csharp
// 1. Build a graph
using var builder = new CognitiveGraphBuilder();
var rootOffset = builder.WriteSymbolNode(/* parameters */);
var buffer = builder.Build(rootOffset, sourceCode);

// 2. Read and navigate
using var graph = new CognitiveGraph(buffer);
var root = graph.GetRootNode();

// 3. Extract information
if (root.TryGetProperty("NodeType", out var nodeType))
{
    ProcessNodeType(nodeType.AsString());
}

// 4. Navigate structure
foreach (var child in root.GetChildNodes())
{
    ProcessChild(child);
}
```

### Advanced: Memory-Mapped Files

```csharp
// For very large graphs that don't fit in memory
using var mmf = MemoryMappedFile.CreateFromFile("huge-graph.bin");
using var accessor = mmf.CreateViewAccessor();

unsafe
{
    byte* ptr = (byte*)accessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
    var span = new ReadOnlySpan<byte>(ptr, (int)accessor.Capacity);
    var buffer = new CognitiveGraphBuffer(span);
    
    using var graph = new CognitiveGraph(buffer);
    // Process without loading entire file into memory
}
```

## Version Compatibility

- **Target Framework**: .NET 8.0+
- **Language Version**: C# 12.0
- **Binary Format**: Stable across library versions
- **API Stability**: Semantic versioning for breaking changes

For more examples and advanced usage patterns, see the [Examples](../CognitiveGraph/Examples/) directory in the source code.
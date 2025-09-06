# Cognitive Graph - Zero-Copy Code Analysis Library

A high-performance C# library implementing the Zero-Copy Cognitive Graph architecture for advanced code analysis. This library unifies syntactic ambiguity handling through Shared Packed Parse Forests (SPPF) with semantic analysis via Code Property Graphs (CPG) in a memory-efficient, zero-copy data structure.

## Features

- **Zero-Copy Architecture**: Uses contiguous memory buffers with offset-based navigation for maximum performance
- **Syntactic Ambiguity Support**: Full SPPF implementation to handle all possible parse interpretations
- **Semantic Analysis**: Rich CPG overlay with control flow, data flow, and call relationship modeling
- **Memory Efficient**: Uses `readonly ref struct` and `Span<T>` for allocation-free access patterns
- **Type Safe**: Strongly typed accessors with compile-time safety guarantees
- **Extensible**: Property system for custom metadata and analysis results

## Quick Start

### Installation

```bash
dotnet add package CognitiveGraph.Core
```

### Basic Usage

```csharp
using CognitiveGraph.Core;
using CognitiveGraph.Core.Builder;
using CognitiveGraph.Core.Schema;

// Create a graph for source code
using var builder = new CognitiveGraphBuilder();

// Add properties for semantic information
var properties = new List<(string key, PropertyValueType type, object value)>
{
    ("NodeType", PropertyValueType.String, "BinaryExpression"),
    ("Operator", PropertyValueType.String, "+")
};

// Create a node representing "hello + world"
var rootNodeOffset = builder.WriteSymbolNode(
    symbolId: 1,      // Expression
    nodeType: 200,    // BinaryExpression
    sourceStart: 0,
    sourceLength: 13,
    properties: properties
);

// Build the final graph
var buffer = builder.Build(rootNodeOffset, "hello + world");

// Use the graph for analysis
using var graph = new CognitiveGraph(buffer);

Console.WriteLine($"Source: {graph.GetSourceText()}");
Console.WriteLine($"Parsed successfully: {graph.IsFullyParsed}");

var rootNode = graph.GetRootNode();
Console.WriteLine($"Node type: {rootNode.NodeType}");

// Access properties
if (rootNode.TryGetProperty("Operator", out var op))
{
    Console.WriteLine($"Operator: {op.AsString()}");
}
```

## Architecture Overview

The Cognitive Graph consists of several key components:

### Binary Schema
- **GraphHeader**: File format metadata and root node references
- **SymbolNode**: Represents grammar symbols with source spans
- **PackedNode**: Specific derivations/interpretations of symbol nodes
- **CpgEdge**: Semantic relationships (control flow, data flow, calls)
- **Property**: Key-value metadata for nodes and edges

### Zero-Copy Buffer Management
- Contiguous memory layout using offset-based references
- No object allocations during graph traversal
- Support for memory-mapped files for large codebases
- Direct serialization/deserialization

### Accessor Pattern
- `readonly ref struct` types for stack-only allocation
- `ReadOnlySpan<T>` for safe memory access
- Fluent API for graph navigation
- Compile-time memory safety guarantees

## Performance Characteristics

- **Memory**: Single contiguous buffer, minimal GC pressure
- **Speed**: Direct memory access without allocations
- **Scalability**: O(n³) space complexity for SPPF, suitable for large files
- **Concurrency**: Immutable structure enables safe concurrent access

## Advanced Features

### Syntactic Ambiguity
```csharp
// Check if a node has multiple interpretations
if (node.IsAmbiguous)
{
    var interpretations = node.GetPackedNodes();
    foreach (var interpretation in interpretations)
    {
        // Analyze each possible parse
        var edges = interpretation.GetCpgEdges();
        // ...
    }
}
```

### Semantic Analysis
```csharp
// Traverse semantic relationships
foreach (var edge in node.GetCpgEdges().OfType(EdgeType.DATA_FLOW))
{
    var target = edge.GetTargetNode();
    // Analyze data flow relationships
}
```

### Property Access
```csharp
// Type-safe property access
if (node.TryGetProperty("LineNumber", out var lineNum))
{
    int line = lineNum.AsInt32();
}
```

## License

This project is licensed under the MIT License.

## Contributing

Contributions are welcome! Please see the project repository for guidelines.

## Documentation

For detailed documentation on the architecture and implementation, see the PDF specification in the `docs/` folder of the repository.
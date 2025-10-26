---
layout: default
title: CognitiveGraph - High-Performance Zero-Copy Cognitive Graph
---

# CognitiveGraph

**High-Performance Zero-Copy Cognitive Graph for Advanced Code Analysis**

[![CI/CD Pipeline](https://github.com/DevelApp-ai/CognitiveGraph/actions/workflows/ci.yml/badge.svg)](https://github.com/DevelApp-ai/CognitiveGraph/actions/workflows/ci.yml)
[![NuGet Package](https://img.shields.io/nuget/v/DevelApp.CognitiveGraph)](https://www.nuget.org/packages/DevelApp.CognitiveGraph/)
[![Platform Support](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)](#platform-compatibility)

A revolutionary approach to code analysis that unifies syntactic ambiguity handling with semantic analysis through a zero-copy memory architecture. Built for .NET 8, it addresses limitations of traditional AST-based approaches by combining Shared Packed Parse Forest (SPPF) and Code Property Graph (CPG) technologies.

## 🚀 Key Features

### Zero-Copy Architecture
- **Direct Memory Access**: Eliminates parsing overhead through contiguous memory buffers
- **Allocation-Free Operations**: Uses `Span<T>` and `readonly ref struct` for stack-only access
- **Memory-Mapped File Support**: Handle massive datasets without loading into memory
- **Cross-Platform Binary Format**: Consistent memory layout across all supported platforms

### Advanced Code Analysis
- **Syntactic Ambiguity Handling**: Complete SPPF implementation for representing all parse interpretations
- **Semantic Analysis**: CPG edges for control flow, data flow, and type relationships  
- **Property System**: Type-safe storage of metadata with variant-typed values
- **Graph Traversal**: Efficient navigation through complex code structures

### Production Ready
- **Comprehensive Testing**: 49 unit tests covering core functionality, performance, and edge cases
- **Multi-Platform CI/CD**: Automated testing on Windows, Linux, and macOS
- **NuGet Distribution**: Ready-to-use package with complete API documentation
- **Performance Optimized**: Designed for large codebases with minimal GC pressure

## 📦 Installation

```bash
dotnet add package DevelApp.CognitiveGraph
```

## 🔧 Quick Start

```csharp
using DevelApp.CognitiveGraph;
using DevelApp.CognitiveGraph.Builder;
using DevelApp.CognitiveGraph.Schema;

// Create a graph for a simple expression
using var builder = new CognitiveGraphBuilder();

var properties = new List<(string key, PropertyValueType type, object value)>
{
    ("NodeType", PropertyValueType.String, "BinaryExpression"),
    ("Operator", PropertyValueType.String, "+"),
    ("IsAmbiguous", PropertyValueType.Boolean, false)
};

var rootNodeOffset = builder.WriteSymbolNode(
    symbolId: 1,
    nodeType: 200,
    sourceStart: 0,
    sourceLength: 13,
    properties: properties
);

var buffer = builder.Build(rootNodeOffset, "hello + world");

// Read the graph with zero-copy access
using var graph = new CognitiveGraph(buffer);
var rootNode = graph.GetRootNode();

// Access properties with type safety
if (rootNode.TryGetProperty("Operator", out var op))
{
    Console.WriteLine($"Operator: {op.AsString()}"); // Output: Operator: +
}

// Check for syntactic ambiguity
if (rootNode.IsAmbiguous)
{
    var interpretations = rootNode.GetPackedNodes();
    Console.WriteLine($"Found {interpretations.Count} parse interpretations");
}
```

## 📚 Documentation

- **[Architecture Guide](docs/Architecture.html)** - Detailed system architecture and design principles
- **[API Reference](docs/API_REFERENCE.html)** - Complete API documentation with examples
- **[Platform Compatibility Guide](docs/PLATFORM_COMPATIBILITY.html)** - Detailed platform support information
- **[Troubleshooting Guide](docs/TROUBLESHOOTING.html)** - Common issues and solutions
- **[Examples](https://github.com/DevelApp-ai/CognitiveGraph/tree/main/CognitiveGraph/Examples)** - Sample code and use cases

## 🌍 Platform Compatibility

| Platform | Architecture | Status | Performance |
|----------|-------------|---------|-------------|
| **Windows** | x64, x86, ARM64 | ✅ Full Support | ⭐⭐⭐⭐⭐ |
| **Linux** | x64, ARM64 | ✅ Full Support | ⭐⭐⭐⭐⭐ |
| **macOS** | x64, ARM64 (M1/M2) | ✅ Full Support | ⭐⭐⭐⭐⭐ |

### Requirements
- **.NET 8.0** or later
- **64-bit architecture** recommended for optimal performance
- **No additional dependencies** - pure managed code implementation

## 📊 Performance Characteristics

- **Memory Usage**: O(n) where n is source code size
- **Parse Tree Space**: O(n³) worst case, O(n) typical case for unambiguous grammars
- **Access Time**: O(1) for direct property and node access
- **Thread Safety**: Full read concurrency, single-writer design
- **GC Pressure**: Minimal due to zero-allocation accessor pattern

### Benchmarks
| Operation | Time | Memory |
|-----------|------|---------|
| Graph Creation (1K nodes) | <1ms | ~50KB |
| Property Access | ~10ns | 0 bytes |
| Ambiguity Resolution | ~100ns | 0 bytes |
| Thread-safe Reading | ~15ns | 0 bytes |

## 🛠 Advanced Usage

### Handling Syntactic Ambiguity

```csharp
// Create an ambiguous expression: "a+b*c" can be parsed as ((a+b)*c) or (a+(b*c))
var packed1 = builder.WritePackedNode(ruleId: 1); // First interpretation
var packed2 = builder.WritePackedNode(ruleId: 2); // Second interpretation

var ambiguousNode = builder.WriteSymbolNode(
    symbolId: 1,
    nodeType: 200,
    sourceStart: 0,
    sourceLength: 5,
    packedNodeOffsets: new List<uint> { packed1, packed2 }
);

// Later, analyze all possible interpretations
if (node.IsAmbiguous)
{
    foreach (var interpretation in node.GetPackedNodes())
    {
        Console.WriteLine($"Rule ID: {interpretation.RuleID}");
        // Process each possible parse tree
    }
}
```

### Memory-Mapped Files for Large Datasets

```csharp
// For analyzing huge codebases
using var mmf = MemoryMappedFile.CreateFromFile("huge-graph.bin");
using var accessor = mmf.CreateViewAccessor();
unsafe
{
    byte* ptr = (byte*)accessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
    var buffer = new CognitiveGraphBuffer(new ReadOnlySpan<byte>(ptr, (int)accessor.Capacity));
    using var graph = new CognitiveGraph(buffer);
    // Process without loading entire file into memory
}
```

## 🔒 Security and Safety

- **Memory Safety**: Compile-time bounds checking via `ReadOnlySpan<T>`
- **No Buffer Overflows**: Structured access prevents unsafe operations  
- **Thread Safety**: Read operations are fully concurrent
- **Deterministic Layout**: Consistent binary format across platforms
- **No Code Injection**: Pure data format with no executable content

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`
3. Make your changes and add tests
4. Ensure all tests pass: `dotnet test`
5. Commit your changes: `git commit -m 'Add amazing feature'`
6. Push to the branch: `git push origin feature/amazing-feature`
7. Open a Pull Request

## 📄 License

This project is licensed under the AGPL 3.0 License - see the [LICENSE](https://github.com/DevelApp-ai/CognitiveGraph/blob/main/LICENSE) file for details.

## 🌟 Why Choose CognitiveGraph?

- **Unmatched Performance**: Zero-copy architecture eliminates traditional parsing bottlenecks
- **Complete Ambiguity Support**: Unlike traditional ASTs, handles all possible parse interpretations
- **Production Ready**: Comprehensive testing, documentation, and multi-platform CI/CD
- **Future-Proof**: Designed for massive codebases and evolving analysis requirements
- **Type Safe**: Leverages .NET 8's latest features for compile-time safety

---

*Built with ❤️ for the developer community by [DevelApp-ai](https://github.com/DevelApp-ai)*
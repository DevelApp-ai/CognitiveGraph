using System.Runtime.InteropServices;

namespace CognitiveGraph.Core.Schema;

/// <summary>
/// Binary layout for a Symbol Node in the SPPF.
/// Represents an instance of a grammar symbol spanning a source region.
/// Total size: 20 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SymbolNodeData
{
    /// <summary>
    /// Identifier for the grammar symbol (terminal/non-terminal)
    /// </summary>
    public readonly ushort SymbolID;
    
    /// <summary>
    /// High-level semantic type (e.g., FunctionDeclaration)
    /// </summary>
    public readonly ushort NodeType;
    
    /// <summary>
    /// Start character index in the source text
    /// </summary>
    public readonly uint SourceStart;
    
    /// <summary>
    /// Length of the source text span for this node
    /// </summary>
    public readonly uint SourceLength;
    
    /// <summary>
    /// Offset to the list of child Packed Nodes
    /// </summary>
    public readonly uint PackedNodesOffset;
    
    /// <summary>
    /// Offset to the list of key-value properties for this node
    /// </summary>
    public readonly uint PropertiesOffset;

    public SymbolNodeData(ushort symbolId, ushort nodeType, uint sourceStart, uint sourceLength,
        uint packedNodesOffset, uint propertiesOffset)
    {
        SymbolID = symbolId;
        NodeType = nodeType;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
        PackedNodesOffset = packedNodesOffset;
        PropertiesOffset = propertiesOffset;
    }

    /// <summary>
    /// Size of the SymbolNode data in bytes
    /// </summary>
    public const int SIZE = 20;
}
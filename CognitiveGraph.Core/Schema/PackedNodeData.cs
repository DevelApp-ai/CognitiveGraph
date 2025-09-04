using System.Runtime.InteropServices;

namespace CognitiveGraph.Core.Schema;

/// <summary>
/// Binary layout for a Packed Node in the SPPF.
/// Represents a specific derivation for a parent Symbol Node.
/// Total size: 12 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct PackedNodeData
{
    /// <summary>
    /// Identifier for the grammar rule applied (e.g., Expression ::= Expression '+' Term)
    /// </summary>
    public readonly ushort RuleID;
    
    /// <summary>
    /// Reserved for alignment (could be used for pivot offset in future)
    /// </summary>
    public readonly ushort Reserved;
    
    /// <summary>
    /// Offset to the list of child Symbol/Intermediate Nodes
    /// </summary>
    public readonly uint ChildNodesOffset;
    
    /// <summary>
    /// Offset to the list of CPG edges for this specific derivation
    /// </summary>
    public readonly uint CpgEdgesOffset;

    public PackedNodeData(ushort ruleId, uint childNodesOffset, uint cpgEdgesOffset, ushort reserved = 0)
    {
        RuleID = ruleId;
        Reserved = reserved;
        ChildNodesOffset = childNodesOffset;
        CpgEdgesOffset = cpgEdgesOffset;
    }

    /// <summary>
    /// Size of the PackedNode data in bytes
    /// </summary>
    public const int SIZE = 12;
}
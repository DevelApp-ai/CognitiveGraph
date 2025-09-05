using System.Runtime.InteropServices;

namespace CognitiveGraph.Schema;

/// <summary>
/// Binary layout for a CPG edge representing semantic relationships.
/// Total size: 12 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CpgEdgeData
{
    /// <summary>
    /// Type of edge (AST_CHILD, CONTROL_FLOW, DATA_FLOW, CALLS, etc.)
    /// </summary>
    public readonly ushort EdgeType;
    
    /// <summary>
    /// Reserved for alignment and future use
    /// </summary>
    public readonly ushort Reserved;
    
    /// <summary>
    /// Offset to the target Symbol Node of the edge
    /// </summary>
    public readonly uint TargetNodeOffset;
    
    /// <summary>
    /// Offset to the list of properties for this edge (e.g., CFG condition)
    /// </summary>
    public readonly uint PropertiesOffset;

    public CpgEdgeData(ushort edgeType, uint targetNodeOffset, uint propertiesOffset, ushort reserved = 0)
    {
        EdgeType = edgeType;
        Reserved = reserved;
        TargetNodeOffset = targetNodeOffset;
        PropertiesOffset = propertiesOffset;
    }

    /// <summary>
    /// Size of the CpgEdge data in bytes
    /// </summary>
    public const int SIZE = 12;
}

/// <summary>
/// Enumeration of CPG edge types
/// </summary>
public enum EdgeType : ushort
{
    /// <summary>
    /// Syntactic parent-child relationship (conditional on specific parse)
    /// </summary>
    AST_CHILD = 1,
    
    /// <summary>
    /// Control flow edge connecting statements in execution order
    /// </summary>
    CONTROL_FLOW = 2,
    
    /// <summary>
    /// Data flow edge tracking variable definitions and uses
    /// </summary>
    DATA_FLOW = 3,
    
    /// <summary>
    /// Call relationship from CallExpression to FunctionDeclaration
    /// </summary>
    CALLS = 4,
    
    /// <summary>
    /// Type relationship for type annotations and inferred types
    /// </summary>
    TYPE = 5
}
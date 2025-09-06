using System.Runtime.InteropServices;

namespace CognitiveGraph.Schema;

/// <summary>
/// Binary layout for the graph header at the start of the buffer.
/// Total size: 32 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct GraphHeader
{
    /// <summary>
    /// File format identifier (e.g., 0x434F474E for "COGN")
    /// </summary>
    public readonly uint MagicNumber;
    
    /// <summary>
    /// Schema version number for compatibility checking
    /// </summary>
    public readonly ushort Version;
    
    /// <summary>
    /// Bit flags for graph properties (e.g., is_fully_parsed)
    /// </summary>
    public readonly ushort Flags;
    
    /// <summary>
    /// Offset to the root Symbol Node of the parse
    /// </summary>
    public readonly uint RootNodeOffset;
    
    /// <summary>
    /// Total number of Symbol/Packed/Intermediate nodes
    /// </summary>
    public readonly uint NodeCount;
    
    /// <summary>
    /// Total number of CPG edges
    /// </summary>
    public readonly uint EdgeCount;
    
    /// <summary>
    /// Length of the original source text
    /// </summary>
    public readonly uint SourceTextLength;
    
    /// <summary>
    /// Offset to the start of the source text copy in the buffer
    /// </summary>
    public readonly uint SourceTextOffset;

    public GraphHeader(uint magicNumber, ushort version, ushort flags, uint rootNodeOffset,
        uint nodeCount, uint edgeCount, uint sourceTextLength, uint sourceTextOffset)
    {
        MagicNumber = magicNumber;
        Version = version;
        Flags = flags;
        RootNodeOffset = rootNodeOffset;
        NodeCount = nodeCount;
        EdgeCount = edgeCount;
        SourceTextLength = sourceTextLength;
        SourceTextOffset = sourceTextOffset;
    }

    /// <summary>
    /// Standard magic number for Cognitive Graph files
    /// </summary>
    public const uint MAGIC_NUMBER = 0x434F474E; // "COGN"
    
    /// <summary>
    /// Current schema version
    /// </summary>
    public const ushort CURRENT_VERSION = 1;
    
    /// <summary>
    /// Size of the header in bytes
    /// </summary>
    public const int SIZE = 32;
}
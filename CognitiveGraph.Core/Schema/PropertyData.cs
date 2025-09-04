using System.Runtime.InteropServices;

namespace CognitiveGraph.Core.Schema;

/// <summary>
/// Binary layout for a key-value property.
/// Total size: 8 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct PropertyData
{
    /// <summary>
    /// Offset to a null-terminated string for the property key
    /// </summary>
    public readonly uint KeyOffset;
    
    /// <summary>
    /// Offset to a variant-typed value (string, int, float, etc.)
    /// </summary>
    public readonly uint ValueOffset;

    public PropertyData(uint keyOffset, uint valueOffset)
    {
        KeyOffset = keyOffset;
        ValueOffset = valueOffset;
    }

    /// <summary>
    /// Size of the Property data in bytes
    /// </summary>
    public const int SIZE = 8;
}

/// <summary>
/// Header for variant-typed property values
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct PropertyValueHeader
{
    /// <summary>
    /// Type of the value
    /// </summary>
    public readonly PropertyValueType Type;
    
    /// <summary>
    /// Reserved for alignment
    /// </summary>
    public readonly ushort Reserved;
    
    /// <summary>
    /// Length of the value data in bytes
    /// </summary>
    public readonly uint Length;

    public PropertyValueHeader(PropertyValueType type, uint length, ushort reserved = 0)
    {
        Type = type;
        Reserved = reserved;
        Length = length;
    }

    /// <summary>
    /// Size of the value header in bytes
    /// </summary>
    public const int SIZE = 8;
}

/// <summary>
/// Types of property values
/// </summary>
public enum PropertyValueType : ushort
{
    String = 1,
    Int32 = 2,
    UInt32 = 3,
    Int64 = 4,
    UInt64 = 5,
    Float = 6,
    Double = 7,
    Boolean = 8,
    Binary = 9
}
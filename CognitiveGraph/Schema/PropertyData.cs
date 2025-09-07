/*
 * CognitiveGraph - Zero-Copy Cognitive Graph for Advanced Code Analysis
 * Copyright (C) 2024 DevelApp-ai
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */


using System.Runtime.InteropServices;

namespace CognitiveGraph.Schema;

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
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
/// Binary layout for a key-value property in Schema V2 (Universal Mode).
/// Uses 64-bit offsets so property keys and values can live anywhere in
/// graphs larger than 4 GB (the V1 PropertyData truncates offsets to uint).
/// Total size: 16 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public readonly struct PropertyDataV2
{
    /// <summary>
    /// Offset to a null-terminated string for the property key
    /// </summary>
    public readonly ulong KeyOffset;
    
    /// <summary>
    /// Offset to a variant-typed value (string, int, float, etc.)
    /// </summary>
    public readonly ulong ValueOffset;

    public PropertyDataV2(ulong keyOffset, ulong valueOffset)
    {
        KeyOffset = keyOffset;
        ValueOffset = valueOffset;
    }

    /// <summary>
    /// Size of the Property data in bytes
    /// </summary>
    public const int SIZE = 16;
}

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
/// Common preamble shared by all schema versions for version detection.
/// Both V1 and V2 headers share the first 6 bytes identically.
/// Total size: 6 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct GraphHeaderPreamble
{
    /// <summary>
    /// File format identifier: 0x434F474E ("COGN")
    /// </summary>
    public readonly uint MagicNumber;
    
    /// <summary>
    /// Schema version number (1 for V1, 2 for V2)
    /// </summary>
    public readonly ushort Version;

    public GraphHeaderPreamble(uint magicNumber, ushort version)
    {
        MagicNumber = magicNumber;
        Version = version;
    }

    /// <summary>
    /// Standard magic number for Cognitive Graph files
    /// </summary>
    public const uint MAGIC_NUMBER = 0x434F474E; // "COGN"
    
    /// <summary>
    /// Size of the preamble in bytes
    /// </summary>
    public const int SIZE = 6;
}

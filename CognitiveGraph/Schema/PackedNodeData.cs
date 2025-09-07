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
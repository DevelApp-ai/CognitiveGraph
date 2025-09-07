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
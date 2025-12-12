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


using CognitiveGraph.Schema;

namespace CognitiveGraph.Builder;

/// <summary>
/// Configuration options for the CognitiveGraphBuilder
/// </summary>
public class GraphBuilderOptions
{
    /// <summary>
    /// Schema version to use for the graph.
    /// Default is V2 (Universal Mode) for maximum scalability.
    /// </summary>
    public SchemaVersion Schema { get; set; } = SchemaVersion.V2;

    /// <summary>
    /// Initial capacity for the buffer in bytes.
    /// Default is 64KB.
    /// For V2 schemas supporting >4GB, this will be expanded dynamically.
    /// </summary>
    public int InitialCapacity { get; set; } = 64 * 1024;

    /// <summary>
    /// Creates default options for V2 (Universal Mode)
    /// </summary>
    public static GraphBuilderOptions Universal() => new GraphBuilderOptions
    {
        Schema = SchemaVersion.V2
    };

    /// <summary>
    /// Creates options for V1 (Compact Mode)
    /// </summary>
    public static GraphBuilderOptions Compact() => new GraphBuilderOptions
    {
        Schema = SchemaVersion.V1
    };
}

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


namespace CognitiveGraph.Schema;

/// <summary>
/// Schema version for the Cognitive Graph binary format
/// </summary>
public enum SchemaVersion : ushort
{
    /// <summary>
    /// Schema V1 (Compact Mode): Legacy layout optimized for memory efficiency.
    /// Uses 32-bit offsets and 16-bit IDs. Max file size ~4GB.
    /// </summary>
    V1 = 1,
    
    /// <summary>
    /// Schema V2 (Universal Mode): High-scale layout for massive graphs.
    /// Uses 64-bit offsets and 32-bit IDs. Max file size 16 Exabytes.
    /// </summary>
    V2 = 2
}

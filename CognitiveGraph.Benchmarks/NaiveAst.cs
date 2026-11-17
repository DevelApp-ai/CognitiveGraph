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

namespace CognitiveGraph.Benchmarks;

/// <summary>
/// The naive baseline the article promises to compare against: one heap
/// object per AST node, mutable, children in a <see cref="List{T}"/>,
/// properties in a <see cref="Dictionary{TKey,TValue}"/>. This is the shape
/// a conventional object-based AST (e.g. Roslyn-style green/red trees in
/// their simplest form) would take.
/// </summary>
public sealed class NaiveObjectNode
{
    public ushort SymbolId;
    public ushort NodeType;
    public uint SourceStart;
    public uint SourceLength;
    public List<NaiveObjectNode> Children = new();
    public Dictionary<string, object> Properties = new(StringComparer.Ordinal);
}

/// <summary>
/// Persistent (immutable) tree baseline: editing rebuilds only the path from
/// the changed node to the root, sharing everything else (Okasaki-style
/// structural sharing). The child array is cloned per rebuilt node; the
/// properties dictionary is treated as immutable and shared.
/// </summary>
public sealed class PersistentNode
{
    public readonly ushort SymbolId;
    public readonly ushort NodeType;
    public readonly uint SourceStart;
    public readonly uint SourceLength;
    public readonly PersistentNode[] Children;
    public readonly Dictionary<string, object> Properties;

    public PersistentNode(ushort symbolId, ushort nodeType, uint sourceStart, uint sourceLength,
        PersistentNode[] children, Dictionary<string, object> properties)
    {
        SymbolId = symbolId;
        NodeType = nodeType;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
        Children = children;
        Properties = properties;
    }

    /// <summary>
    /// Returns a new tree in which the node at <paramref name="targetIndex"/>
    /// (level-order/BFS indexing, matching <see cref="GraphFactory"/>) is
    /// replaced by <paramref name="replacement"/>. Rebuilds exactly the path
    /// root -> target; every other node is shared.
    /// </summary>
    public PersistentNode ReplaceAt(int targetIndex, PersistentNode replacement)
    {
        var path = new List<int>();
        int cur = targetIndex;
        while (cur > 0)
        {
            int parent = (cur - 1) / 2;
            path.Add(cur - parent * 2 - 1); // 0 = left child, 1 = right child
            cur = parent;
        }
        path.Reverse();
        return ReplaceAtPath(this, path, 0, replacement);
    }

    private static PersistentNode ReplaceAtPath(PersistentNode node, List<int> path, int depth, PersistentNode replacement)
    {
        if (depth == path.Count)
            return replacement;

        var children = (PersistentNode[])node.Children.Clone();
        children[path[depth]] = ReplaceAtPath(children[path[depth]], path, depth + 1, replacement);
        return new PersistentNode(node.SymbolId, node.NodeType, node.SourceStart, node.SourceLength,
            children, node.Properties);
    }
}

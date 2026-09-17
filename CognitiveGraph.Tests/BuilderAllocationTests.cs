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

using System;
using System.Collections.Generic;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;
using Xunit;

namespace CognitiveGraph.Tests;

/// <summary>
/// Allocation regression tests for the CognitiveGraphBuilder hot path (issue #40).
/// Parsers call WriteSymbolNode for every shifted token and every reduction across
/// all concurrent GLR paths, so per-call allocations are multiplied by token count
/// and path count. On DevelApp.CognitiveGraph 1.1.0 this repro measured ~1.1 KB of
/// internal allocation per call; the builder must stay far below that.
/// </summary>
public class BuilderAllocationTests
{
    private const int WarmupCalls = 500;
    private const int MeasuredCalls = 2000;

    [Fact]
    public void WriteSymbolNode_WithPreSizedBuffer_AllocatesAlmostNothingInternally()
    {
        // Pre-size the buffer so the measurement excludes amortized buffer growth:
        // what remains must be close to pure bookkeeping.
        var options = new GraphBuilderOptions { Schema = SchemaVersion.V1, InitialCapacity = 4 * 1024 * 1024 };
        var internalBytesPerCall = MeasureInternalAllocationPerCall(options);

        // Regression guard: was ~1144 bytes/call on 1.1.0, ~43 after the fix
        // (residual: interval-tree node growth, which is data-proportional).
        Assert.True(internalBytesPerCall < 128,
            $"WriteSymbolNode allocated {internalBytesPerCall:F0} bytes/call internally (expected < 128)");
    }

    [Fact]
    public void WriteSymbolNode_WithDefaultOptions_AllocationIsDominatedByBufferGrowth()
    {
        // Default options (64 KB initial capacity): the measured batch outgrows the
        // buffer several times, so amortized doubling dominates — but total internal
        // allocation must still stay in the same ballpark as the bytes appended
        // (~100 bytes/call), not the ~1.1 KB/call of the 1.1.0 package.
        var options = new GraphBuilderOptions { Schema = SchemaVersion.V1 };
        var internalBytesPerCall = MeasureInternalAllocationPerCall(options);

        Assert.True(internalBytesPerCall < 400,
            $"WriteSymbolNode allocated {internalBytesPerCall:F0} bytes/call internally (expected < 400)");
    }

    /// <summary>
    /// Reproduces the measurement from issue #40 (2000 calls, 4-entry property list)
    /// and returns the per-call allocation attributable to the builder itself,
    /// with the caller-side allocations (property list + boxed values) subtracted.
    /// </summary>
    private static double MeasureInternalAllocationPerCall(GraphBuilderOptions options)
    {
        using var builder = new CognitiveGraphBuilder(options);
        uint offset = 0;

        // Warm-up: JIT, string-table population, initial buffer sizing
        for (int i = 0; i < WarmupCalls; i++)
        {
            offset = builder.WriteSymbolNode(symbolId: (ushort)i, nodeType: 100,
                sourceStart: (uint)i, sourceLength: 5u, properties: BuildProperties(i));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < MeasuredCalls; i++)
        {
            offset = builder.WriteSymbolNode(symbolId: (ushort)i, nodeType: 100,
                sourceStart: (uint)i, sourceLength: 5u, properties: BuildProperties(i));
        }
        var after = GC.GetAllocatedBytesForCurrentThread();
        var totalPerCall = (after - before) / (double)MeasuredCalls;

        // Caller-side reference: the identical property-list construction without the builder
        before = GC.GetAllocatedBytesForCurrentThread();
        uint sink = 0;
        for (int i = 0; i < MeasuredCalls; i++)
        {
            var properties = BuildProperties(i);
            sink += (uint)properties.Count;
        }
        after = GC.GetAllocatedBytesForCurrentThread();
        var callerPerCall = (after - before) / (double)MeasuredCalls;

        Assert.True(offset > 0 && sink > 0); // keep the loops observable
        return totalPerCall - callerPerCall;
    }

    private static List<(string key, PropertyValueType type, object value)> BuildProperties(int i) => new()
    {
        ("TokenType", PropertyValueType.String, "NUMBER"),
        ("TokenValue", PropertyValueType.String, (i % 97).ToString()),
        ("Context", PropertyValueType.String, ""),
        ("IsTerminal", PropertyValueType.Boolean, true)
    };
}

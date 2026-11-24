using System.Reflection;
using DevelApp.CognitiveGraph.DefectMining;
using Xunit;

namespace CognitiveGraph.Tests;

/// <summary>
/// CI drift gate (issue #55): the committed ground-truth corpus must stay
/// internally consistent with its manifest on every build. Validation is
/// offline (hash + schema re-checks, no network).
/// </summary>
public class CorpusIntegrityTests
{
    private static string? FindCorpusDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "measurements", "issue-55", "corpus");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>The uploaded corpus is missing or corrupt.</summary>
    [Fact]
    public void Committed_Corpus_Passes_Offline_Validation()
    {
        var corpus = FindCorpusDirectory();
        Assert.False(corpus is null, "docs/measurements/issue-55/corpus not found (repo root walk from the test bin directory)");

        var report = Pipeline.Validate(corpus!);
        Assert.True(report.Ok, $"corpus validation failed: {report.Detail}");
        Assert.True(report.Entries >= 15, $"expected at least 15 corpus entries, found {report.Entries}");
    }

    /// <summary>Manifest must be readable and pinned to the right issue.</summary>
    [Fact]
    public void Manifest_Targets_Issue_55()
    {
        var corpus = FindCorpusDirectory();
        if (corpus is null)
        {
            Assert.Fail("corpus directory missing");
        }

        var manifestPath = Path.Combine(corpus!, "manifest.json");
        Assert.True(File.Exists(manifestPath), "manifest.json missing");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;
        Assert.Equal("DevelApp-ai/CognitiveGraph#55", root.GetProperty("issue").GetString());
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.True(root.GetProperty("pins").GetProperty("projects").GetArrayLength() >= 15,
            "pins must cover at least 15 projects");
    }
}

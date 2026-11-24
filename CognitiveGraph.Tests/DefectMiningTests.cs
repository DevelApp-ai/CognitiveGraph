using System.Text.Json;
using DevelApp.CognitiveGraph.DefectMining;
using Xunit;

namespace CognitiveGraph.Tests;

/// <summary>
/// Unit tests for the issue #55 ground-truth mining classifier and pipeline
/// transforms. All fixtures are synthetic; no network access, no GitHub state.
/// </summary>
public class DefectMiningClassifierTests
{
    [Theory]
    [InlineData("Fix NullReferenceException when settings are missing", BugCategory.Nullability)]
    [InlineData("fix crash: index out of range on empty input", BugCategory.ArrayBounds)]
    [InlineData("Bugfix: InvalidCastException while deserializing pets", BugCategory.TypeSafety)]
    [InlineData("Resolved divide by zero in the rate calculator", BugCategory.DivisionByZero)]
    public void MessagePass_Accepts_FixFamily_Plus_Category(string message, BugCategory expected)
    {
        Assert.Equal(expected, Classifier.CategoryFromMessage(message));
    }

    [Theory]
    [InlineData("Add new feature flag")]
    [InlineData("Update README installation steps")]
    [InlineData("null refactor across the builder API")] // no fix-family word
    [InlineData("Merge pull request #45 from dev/fix-null-handling")] // merge commits never selected
    [InlineData("Fixed crash")] // fix-family but no category keyword
    [InlineData("Fix null ref and out of range parsing bug")] // two categories claimed: ambiguous
    public void MessagePass_Rejects_Out_Of_Scope_Messages(string message)
    {
        Assert.Null(Classifier.CategoryFromMessage(message));
    }

    [Theory]
    [InlineData("Fixed invalid cast exception in the internal API", BugCategory.TypeSafety)]
    [InlineData("Resolve invalid-cast mismatch on interface import", BugCategory.TypeSafety)]
    [InlineData("Fix null ref when the parser buffer is empty", BugCategory.Nullability)]
    public void MessagePass_v3_Accepts_Spaced_And_Punctuated_Keywords(string message, BugCategory expected)
    {
        Assert.Equal(expected, Classifier.CategoryFromMessage(message));
    }

    [Theory]
    [InlineData("Fix nullable warnings in the serialization layer")] // compiler-annotation sweep
    [InlineData("Fix up nullability warnings across the codebase")]
    [InlineData("Fixed NRT warnings after the SDK upgrade")] // NRT sweeps are annotations, not defects
    [InlineData("Update nullable annotations to match the public API")]
    public void MessagePass_v3_Rejects_Annotation_Sweep_Subjects(string message)
    {
        Assert.Null(Classifier.CategoryFromMessage(message));
    }

    [Fact]
    public void PatchPass_Accepts_Strong_Unique_Category_Matching_The_Message_Hint()
    {
        var patch = string.Join("\n",
            "diff --git a/app.cs b/app.cs",
            "--- a/app.cs",
            "+++ b/app.cs",
            "@@ -42,7 +42,9 @@",
            " context line",
            "+    if (value == null)",
            "+    {",
            "+        return Array.Empty<string>();",
            " context line");

        var scores = Classifier.ScorePatch(patch.Split('\n').Where(l => l.StartsWith('+')));
        var result = Classifier.CategoryFromPatch(scores, BugCategory.Nullability);

        Assert.NotNull(result);
        Assert.Equal(BugCategory.Nullability, result!.Value.Category);
        Assert.True(result.Value.Signals.Values.Sum() >= Classifier.MinPatchScore);
    }

    [Fact]
    public void PatchPass_Rejects_Tied_Categories()
    {
        var added = new[]
        {
            "+    if (list.Count == 0)",
            "+    {",
            "+        throw new InvalidCastException();",
        };
        var scores = Classifier.ScorePatch(added);
        // 1 count-guard + 1 invalid-cast: tie.
        Assert.Null(Classifier.CategoryFromPatch(scores, null));
    }

    [Fact]
    public void PatchPass_Rejects_Weak_Signals_Below_Threshold()
    {
        var added = new[] { "+    var x = 1;" };
        var scores = Classifier.ScorePatch(added);
        Assert.Null(Classifier.CategoryFromPatch(scores, BugCategory.Nullability));
    }

    [Fact]
    public void PatchPass_Rejects_Disagreement_With_Message_Hint()
    {
        var added = new[]
        {
            "+    if (i < items.Count)",
            "+    {",
            "+        sum += items[i].Length;",
        };
        var scores = Classifier.ScorePatch(added);
        Assert.Null(Classifier.CategoryFromPatch(scores, BugCategory.Nullability)); // patch says ArrayBounds
        Assert.NotNull(Classifier.CategoryFromPatch(scores, BugCategory.ArrayBounds));
    }

    [Theory]
    [InlineData("src/app/core/Parser.cs", true)]
    [InlineData("Src/App/Parser.v2.cs", true)]
    [InlineData("tests/ParserTests.cs", false)]
    [InlineData("src/App/ParserTests.cs", false)]
    [InlineData("spec/flows.md", false)]
    [InlineData("src/generated/obj/skip.cs", false)]
    [InlineData("Src/AngleSharp.Core.Tests/Css/CssSelector.cs", false)] // *.Tests project segment
    [InlineData("src/UnitTests/Helper.cs", false)]
    [InlineData("src/Snippets/Docs/DependencyInjection.cs", true)] // snippets are not test surfaces
    public void SourcePath_Rule_Filters_Corpus_Units(string path, bool admissible)
    {
        Assert.Equal(admissible, Classifier.IsAdmissibleSourcePath(path));
    }
}

/// <summary>Tests for the deterministic pipeline transforms and hashing helpers.</summary>
public class DefectMiningPipelineTests
{
    [Fact]
    public void HunkRanges_Map_Old_And_New_Coordinates()
    {
        var (old, now) = Pipeline.ParseHunkRanges("@@ -10,3 +10,5 @@\n@@ -0,0 +1,4 @@");
        Assert.Equal(10, old[0][0]);
        Assert.Equal(12, old[0][1]);
        Assert.Single(old); // -0,0 yields no pre-fix range
        Assert.Equal(2, now.Count);
        Assert.Equal(14, now[0][1]);
        Assert.Equal(1, now[1][0]);
    }

    [Fact]
    public void GitBlobSha1_Matches_The_Reference_Computation()
    {
        // git hash-object of "hello\n" = ce013625030ba8dba906f756967f9e9ca394464a
        // (independently verified with: python -c "import hashlib; print(hashlib.sha1(b'blob 6\0hello\n').hexdigest())")
        Assert.Equal("ce013625030ba8dba906f756967f9e9ca394464a", GitHashing.BlobSha1("hello\n"));
    }

    [Fact]
    public void EntryId_Is_Stable()
    {
        var a = GitHashing.EntryId("nunit/nunit", "abc", "src/a.cs");
        var b = GitHashing.EntryId("nunit/nunit", "abc", "src/a.cs");
        var c = GitHashing.EntryId("nunit/nunit", "abd", "src/a.cs");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void PlanCandidates_Uses_Linear_Parent_And_Writes_Candidates()
    {
        var raw = Directory.CreateTempSubdirectory("cgm").FullName;
        try
        {
            File.WriteAllText(Path.Combine(raw, "pins.json"), """
                {"schema":1,"projects":[{"project":"a/b","owner":"a","repo":"b","license":"MIT","licenseSource":"LICENSE"}]}
                """);
            Directory.CreateDirectory(Path.Combine(raw, "a_b"));
            File.WriteAllText(Path.Combine(raw, "a_b", "commits.json"), """
                {"project":"a/b","since":null,"headSha":"c3","commits":[
                  {"sha":"c3","date":"2026-01-03","message":"Fix null reference on load"},
                  {"sha":"c2","date":"2026-01-02","message":"refactor internals"},
                  {"sha":"c1","date":"2026-01-01","message":"Fixed index out of range"}]}
                """);

            Pipeline.PlanCandidates(raw, "a/b");
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(raw, "a_b", "candidates.json")));
            var json = doc.RootElement;

            var candidates = json.GetProperty("candidates");
            // The oldest scanned commit (c1) has no known parent inside the
            // window and is excluded; c2 has no fix message.
            Assert.Equal(1, candidates.GetArrayLength());
            Assert.Equal("c3", candidates[0].GetProperty("sha").GetString());
            Assert.Equal("c2", candidates[0].GetProperty("preFixSha").GetString());
            Assert.Equal("Nullability", candidates[0].GetProperty("messageCategory").GetString());
        }
        finally
        {
            TryCleanup(raw);
        }
    }

    private static void TryCleanup(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // best effort only
        }
    }
}

using System.Text.RegularExpressions;

namespace DevelApp.CognitiveGraph.DefectMining;

/// <summary>
/// Deterministic classifier turning GitHub commit metadata and unified diffs
/// into taxonomy decisions for the issue #55 ground-truth corpus.
/// </summary>
/// <remarks>
/// <para>
/// The classifier runs in two passes. The message pass (used by the
/// <c>plan-candidates</c> phase) keeps a commit only when its message contains
/// a fix-family word AND a category keyword, so the expensive per-commit diff
/// fetches stay bounded. The patch pass (used by <c>plan-blobs</c>) scores the
/// ADDED lines of each unified diff (the lines the fix introduces, typically
/// the guard or safe expression) against per-category signal regexes and
/// accepts a commit only when the unique best patch category scores at least
/// <see cref="MinPatchScore"/> AND agrees with the message category.
/// </para>
/// <para>
/// Every rule is a plain regex over the message or the diff text, so given the
/// same inputs the classifier always returns the same decisions; the corpus
/// manifest stores the signals that fired so each decision is auditable.
/// </para>
/// </remarks>
public static class Classifier
{
    /// <summary>Minimum score (fired signals) required to accept a patch classification.</summary>
    public const int MinPatchScore = 2;

    /// <summary>Messages of merge commits never become corpus candidates.</summary>
    private static readonly Regex MergeMessage = new(@"^\s*merge\s+(pull request|branch)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Compiler-annotation sweeps ("fix nullable warnings", "NRT makeover") are
    /// not defect fixes: they annotate signatures project-wide. Such subjects
    /// are excluded even when they match the fix-family and category filters.
    /// </summary>
    private static readonly Regex AnnotationSweep = new(@"nullable\s+warnings?|nullability\s+warnings?|\bNRT\b|nullable\s+annotations?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Fix-family words that must appear in a candidate message.</summary>
    private static readonly Regex FixFamily = new(@"\b(fix(ed|es)?|bug(fix)?|regression|crash(ed|es)?|resolv(e|ed)|correct(ed|ion))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly (BugCategory Category, Regex Keyword)[] MessageKeywords =
    {
        (BugCategory.Nullability, new Regex(@"null\s*reference|nre\b|argumentnullexception|nullability|nullable|\bnull\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (BugCategory.ArrayBounds, new Regex(@"out\s*[- ]of\s*[- ]?range|indexoutofrange|bounds|\bindex(es)?\b|array\s*access", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (BugCategory.TypeSafety, new Regex(@"invalid\s*[- ]?cast|cast\s*(exception|mismatch|error)|type\s*(mismatch|safety)|unsafe\s*cast|bad\s*cast", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        (BugCategory.DivisionByZero, new Regex(@"divide|division|by\s+zero|divisor|denominator|modulo.*zero", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    };

    /// <summary>Per-category signal regexes applied to the ADDED lines of a fix diff.</summary>
    private static readonly (BugCategory Category, (string Signal, Regex Rx)[] Signals)[] PatchSignals =
    {
        (BugCategory.Nullability, new[]
        {
            ("null-check", new Regex(@"[!=]=\s*null", RegexOptions.Compiled)),
            ("is-null", new Regex(@"\bis\s+(not\s+)?null\b", RegexOptions.Compiled)),
            ("null-coalescing", new Regex(@"\?\?", RegexOptions.Compiled)),
            ("null-conditional", new Regex(@"\?\.", RegexOptions.Compiled)),
            ("throw-if-null", new Regex(@"ThrowIfNull", RegexOptions.Compiled)),
            ("is-null-or-empty", new Regex(@"IsNullOr(Empty|WhiteSpace)", RegexOptions.Compiled)),
            ("reference-equals-null", new Regex(@"ReferenceEquals\s*\([^)]*,\s*null", RegexOptions.Compiled)),
            ("default-null", new Regex(@"==\s*(null|default)|\?\s*default", RegexOptions.Compiled)),
        }),
        (BugCategory.ArrayBounds, new[]
        {
            ("length-guard", new Regex(@"\.Length\b", RegexOptions.Compiled)),
            ("count-guard", new Regex(@"\.Count\b", RegexOptions.Compiled)),
            ("arg-out-of-range", new Regex(@"ArgumentOutOfRangeException", RegexOptions.Compiled)),
            ("index-check", new Regex(@"\b(index|idx|i|start|end|offset|position)\s*(<|>|<=|>=)", RegexOptions.Compiled)),
            ("bounds-word", new Regex(@"\b(bounds|offset)\b", RegexOptions.Compiled)),
            ("clamp", new Regex(@"Math\.(Min|Max|Clamp)", RegexOptions.Compiled)),
        }),
        (BugCategory.TypeSafety, new[]
        {
            ("as-cast-checked", new Regex(@"\bas\s+[A-Z]\w*\s", RegexOptions.Compiled)),
            ("is-pattern", new Regex(@"\bis\s+[A-Z]\w*(\s+\w+)?\s", RegexOptions.Compiled)),
            ("invalid-cast", new Regex(@"InvalidCastException", RegexOptions.Compiled)),
            ("get-type-check", new Regex(@"\.GetType\s*\(\)", RegexOptions.Compiled)),
            ("typeof-compare", new Regex(@"[!=]=\s*typeof\s*\(", RegexOptions.Compiled)),
        }),
        (BugCategory.DivisionByZero, new[]
        {
            ("divisor-guard", new Regex(@"\b(denominator|divisor)\b", RegexOptions.Compiled)),
            ("by-zero-word", new Regex(@"by\s+zero|zero\s+(denominator|divisor)", RegexOptions.Compiled)),
            ("epsilon-guard", new Regex(@"(epsilon|double\.Epsilon|float\.Epsilon|Tolerance)", RegexOptions.Compiled)),
        }),
    };

    /// <summary>
    /// Message pass: decides whether a commit message makes the commit a
    /// candidate for diff inspection, and with which hint category.
    /// </summary>
    /// <param name="message">Commit message (subject line or full text).</param>
    /// <returns>The hinted category, or <c>null</c> when the message does not
    /// pass the fix-family/category filters or belongs to a merge commit.</returns>
    public static BugCategory? CategoryFromMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || MergeMessage.IsMatch(message))
        {
            return null;
        }

        if (AnnotationSweep.IsMatch(message))
        {
            return null;
        }

        if (!FixFamily.IsMatch(message))
        {
            return null;
        }

        BugCategory? hint = null;
        foreach (var (category, keyword) in MessageKeywords)
        {
            if (keyword.IsMatch(message))
            {
                if (hint is not null && hint != category)
                {
                    return null; // ambiguous: several categories claimed in the message
                }

                hint = category;
            }
        }

        return hint;
    }

    /// <summary>Scores the added lines of a unified diff against the signal table.</summary>
    /// <param name="addedLines">The lines starting with '+' (excluding the '+++' file header).</param>
    /// <returns>Signal counts per category.</returns>
    public static Dictionary<BugCategory, Dictionary<string, int>> ScorePatch(IEnumerable<string> addedLines)
    {
        var result = new Dictionary<BugCategory, Dictionary<string, int>>();
        foreach (var (category, _) in PatchSignals)
        {
            result[category] = new Dictionary<string, int>();
        }

        foreach (var line in addedLines)
        {
            if (line.StartsWith("+++", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (category, signals) in PatchSignals)
            {
                foreach (var (signal, rx) in signals)
                {
                    var count = rx.Matches(line).Count;
                    if (count > 0)
                    {
                        result[category].TryAdd(signal, 0);
                        result[category][signal] += count;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Patch pass: derives the final category from the patch signal scores and
    /// the message hint.
    /// </summary>
    /// <param name="scores">Signal counts produced by <see cref="ScorePatch"/>.</param>
    /// <param name="messageHint">Category hinted by the message pass.</param>
    /// <returns>The accepted category together with its fired signals, or
    /// <c>null</c> when the patch is ambiguous, unscored or disagrees with the
    /// message hint.</returns>
    public static (BugCategory Category, IReadOnlyDictionary<string, int> Signals)? CategoryFromPatch(
        IReadOnlyDictionary<BugCategory, Dictionary<string, int>> scores,
        BugCategory? messageHint)
    {
        var total = new Dictionary<BugCategory, int>();
        foreach (var (category, signals) in scores)
        {
            total[category] = signals.Values.Sum();
        }

        var best = total.OrderByDescending(kv => kv.Value).First();
        var second = total.OrderByDescending(kv => kv.Value).Skip(1).First();

        if (best.Value < MinPatchScore)
        {
            return null; // too weak: no auditable classification
        }

        if (best.Value == second.Value)
        {
            return null; // tie between categories: ambiguous, not corpus-grade
        }

        if (messageHint is not null && messageHint != best.Key)
        {
            return null; // message and patch disagree: drop for precision
        }

        return (best.Key, scores[best.Key]);
    }

    /// <summary>
    /// Decides whether a changed file path is an admissible corpus unit: a C#
    /// source file outside test projects and frameworks.
    /// </summary>
    /// <param name="path">Repository-relative path of the changed file.</param>
    /// <returns><c>true</c> when the file may enter the corpus.</returns>
    public static bool IsAdmissibleSourcePath(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(normalized);
        if (fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Test surfaces live in path segments: bare tests/specs directories
        // and project segments such as "AngleSharp.Core.Tests" or "UnitTests".
        // Over-exclusion of production names ending in the same letters
        // (e.g. "docs/latest/") is accepted: corpus purity beats recall.
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            var lower = segment.ToLowerInvariant();
            if (lower.EndsWith("tests") || lower.EndsWith("test") ||
                lower.EndsWith("specs") || lower.EndsWith("spec"))
            {
                return false;
            }
        }

        return true;
    }
}

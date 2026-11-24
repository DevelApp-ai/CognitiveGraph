using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevelApp.CognitiveGraph.DefectMining;

/// <summary>A pinned corpus source project.</summary>
public sealed class ProjectPin
{
    /// <summary>Project slug used in raw dumps and the manifest (owner/repo).</summary>
    [JsonPropertyName("project")] public string Project { get; set; } = string.Empty;

    /// <summary>GitHub owner (used by the REST-mode fetch).</summary>
    [JsonPropertyName("owner")] public string Owner { get; set; } = string.Empty;

    /// <summary>GitHub repository name (used by the REST-mode fetch).</summary>
    [JsonPropertyName("repo")] public string Repo { get; set; } = string.Empty;

    /// <summary>SPDX identifier of the project license.</summary>
    [JsonPropertyName("license")] public string License { get; set; } = string.Empty;

    /// <summary>Where the license claim was verified (URL or file in the project).</summary>
    [JsonPropertyName("licenseSource")] public string LicenseSource { get; set; } = string.Empty;

    /// <summary>Optional ISO date: only commits after it are scanned.</summary>
    [JsonPropertyName("since")] public string? Since { get; set; }

    /// <summary>Optional cap on the number of scanned commits (most recent first).</summary>
    [JsonPropertyName("maxCommits")] public int? MaxCommits { get; set; }
}

/// <summary>The committed pins file listing every corpus source.</summary>
public sealed class PinsFile
{
    /// <summary>Schema version of the pins format.</summary>
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;

    /// <summary>The pinned projects.</summary>
    [JsonPropertyName("projects")] public List<ProjectPin> Projects { get; set; } = new();
}

/// <summary>Commit metadata as recorded in the commits.json dump.</summary>
public sealed class CommitMeta
{
    /// <summary>Commit SHA.</summary>
    [JsonPropertyName("sha")] public string Sha { get; set; } = string.Empty;

    /// <summary>Committed date (ISO 8601).</summary>
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;

    /// <summary>Full commit message.</summary>
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
}

/// <summary>The commits.json dump for one project.</summary>
public sealed class CommitListDump
{
    /// <summary>The project slug.</summary>
    [JsonPropertyName("project")] public string Project { get; set; } = string.Empty;

    /// <summary>The date cutoff actually used by the scan.</summary>
    [JsonPropertyName("since")] public string? Since { get; set; }

    /// <summary>The HEAD SHA at mining time (the tip the scan started from).</summary>
    [JsonPropertyName("headSha")] public string HeadSha { get; set; } = string.Empty;

    /// <summary>The scanned commits, newest first.</summary>
    [JsonPropertyName("commits")] public List<CommitMeta> Commits { get; set; } = new();
}

/// <summary>One changed file inside a commit-detail dump.</summary>
public sealed class FileChangeDump
{
    /// <summary>Repository-relative path.</summary>
    [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;

    /// <summary>GitHub file status (added/modified/removed/...).</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;

    /// <summary>Unified diff text, when GitHub returned one.</summary>
    [JsonPropertyName("patch")] public string? Patch { get; set; }
}

/// <summary>The details/&lt;sha&gt;.json dump for a candidate commit.</summary>
public sealed class CommitDetailDump
{
    /// <summary>Commit SHA.</summary>
    [JsonPropertyName("sha")] public string Sha { get; set; } = string.Empty;

    /// <summary>Commit message.</summary>
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;

    /// <summary>The SHA of the file snapshot to extract (the fix commit's parent on the default branch).</summary>
    [JsonPropertyName("preFixSha")] public string PreFixSha { get; set; } = string.Empty;

    /// <summary>True when the commit is a merge commit (candidates must be false).</summary>
    [JsonPropertyName("merge")] public bool Merge { get; set; }

    /// <summary>Committed date.</summary>
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;

    /// <summary>The changed files with their unified diffs.</summary>
    [JsonPropertyName("files")] public List<FileChangeDump> Files { get; set; } = new();
}

/// <summary>A planned bug extraction request produced by the plan-blobs phase.</summary>
public sealed class WantedBlob
{
    /// <summary>Corpus entry id (stable hash).</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    /// <summary>Project slug.</summary>
    [JsonPropertyName("project")] public string Project { get; set; } = string.Empty;

    /// <summary>The bug-fixing commit SHA.</summary>
    [JsonPropertyName("fixSha")] public string FixSha { get; set; } = string.Empty;

    /// <summary>The parent (pre-fix) commit the snapshot is taken from.</summary>
    [JsonPropertyName("preFixSha")] public string PreFixSha { get; set; } = string.Empty;

    /// <summary>Repository-relative path of the file that contained the defect.</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;

    /// <summary>Accepted bug category.</summary>
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;

    /// <summary>Signals that fired in the fix diff (signal name to count).</summary>
    [JsonPropertyName("signals")] public Dictionary<string, int> Signals { get; set; } = new();

    /// <summary>Defect vicinity line ranges in the pre-fix file (start,end pairs).</summary>
    [JsonPropertyName("preFixRanges")] public List<int[]> PreFixRanges { get; set; } = new();

    /// <summary>Same ranges expressed in post-fix coordinates (the fix hunks).</summary>
    [JsonPropertyName("postFixRanges")] public List<int[]> PostFixRanges { get; set; } = new();

    /// <summary>Commit message, trimmed to 300 characters.</summary>
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;

    /// <summary>Committed date.</summary>
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;

    /// <summary>Unified diff of the file, for triage localization.</summary>
    [JsonPropertyName("patch")] public string? Patch { get; set; }
}

/// <summary>The wanted-blobs.json plan for one project.</summary>
public sealed class WantedBlobsPlan
{
    /// <summary>Project slug.</summary>
    [JsonPropertyName("project")] public string Project { get; set; } = string.Empty;

    /// <summary>Planned corpus entries.</summary>
    [JsonPropertyName("entries")] public List<WantedBlob> Entries { get; set; } = new();

    /// <summary>Counters of rejected/skipped candidates per reason, for honesty accounting.</summary>
    [JsonPropertyName("rejected")] public Dictionary<string, int> Rejected { get; set; } = new();
}

/// <summary>A fetched file snapshot (blobs/&lt;id&gt;.json).</summary>
public sealed class BlobDump
{
    /// <summary>Corpus entry id.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    /// <summary>The pre-fix file content (exact bytes as text).</summary>
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;

    /// <summary>Git blob SHA-1 recorded by the fetching client, when known.</summary>
    [JsonPropertyName("gitBlobSha")] public string? GitBlobSha { get; set; }
}

/// <summary>One corpus entry in the committed manifest.</summary>
public sealed class CorpusEntry
{
    /// <summary>Corpus entry id.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    /// <summary>Project slug.</summary>
    [JsonPropertyName("project")] public string Project { get; set; } = string.Empty;

    /// <summary>SPDX license of the source project.</summary>
    [JsonPropertyName("license")] public string License { get; set; } = string.Empty;

    /// <summary>The bug-fixing commit SHA.</summary>
    [JsonPropertyName("fixSha")] public string FixSha { get; set; } = string.Empty;

    /// <summary>The pre-fix (parent) commit SHA the snapshot was taken from.</summary>
    [JsonPropertyName("preFixSha")] public string PreFixSha { get; set; } = string.Empty;

    /// <summary>Repository-relative path of the defect file.</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;

    /// <summary>Category of the defect.</summary>
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;

    /// <summary>Signals that fired in the fix diff.</summary>
    [JsonPropertyName("signals")] public Dictionary<string, int> Signals { get; set; } = new();

    /// <summary>Defect vicinity ranges in the pre-fix file.</summary>
    [JsonPropertyName("preFixRanges")] public List<int[]> PreFixRanges { get; set; } = new();

    /// <summary>Fix hunk ranges in post-fix coordinates.</summary>
    [JsonPropertyName("postFixRanges")] public List<int[]> PostFixRanges { get; set; } = new();

    /// <summary>Commit message trimmed to 300 characters.</summary>
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;

    /// <summary>Committed date of the fix.</summary>
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;

    /// <summary>Path of the snapshot relative to the corpus root.</summary>
    [JsonPropertyName("snapshot")] public string Snapshot { get; set; } = string.Empty;

    /// <summary>Snapshot size in bytes (UTF-8).</summary>
    [JsonPropertyName("bytes")] public int Bytes { get; set; }

    /// <summary>Git blob SHA-1 of the snapshot, recomputed at emit/validate time.</summary>
    [JsonPropertyName("gitBlobSha")] public string GitBlobSha { get; set; } = string.Empty;
}

/// <summary>The committed corpus manifest.</summary>
public sealed class CorpusManifest
{
    /// <summary>Schema version of the corpus format.</summary>
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;

    /// <summary>Issue this corpus supports.</summary>
    [JsonPropertyName("issue")] public string Issue { get; set; } = "DevelApp-ai/CognitiveGraph#55";

    /// <summary>Classifier version (bumped whenever a rule changes corpus decisions).</summary>
    /// <remarks>2 = subject-line-only message pass + segment-aware test-path exclusion.</remarks>
    [JsonPropertyName("classifierVersion")] public string ClassifierVersion { get; set; } = "3";

    /// <summary>Timeout-safe snapshot byte cap that was applied.</summary>
    [JsonPropertyName("maxSnapshotBytes")] public int MaxSnapshotBytes { get; set; }

    /// <summary>Corpus entries, deterministically ordered by project then fix SHA then path.</summary>
    [JsonPropertyName("entries")] public List<CorpusEntry> Entries { get; set; } = new();

    /// <summary>The pins used at mining time.</summary>
    [JsonPropertyName("pins")] public PinsFile Pins { get; set; } = new();

    /// <summary>Honesty counters per project (rejected/skipped decision reasons).</summary>
    [JsonPropertyName("rejected")] public Dictionary<string, Dictionary<string, int>> Rejected { get; set; } = new();

    /// <summary>Date the corpus was mined.</summary>
    [JsonPropertyName("minedAtUtc")] public string MinedAtUtc { get; set; } = string.Empty;
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevelApp.CognitiveGraph.DefectMining;

/// <summary>
/// The staged mining pipeline. Every phase is a deterministic pure transform
/// over JSON dumps under a raw working directory, so the sandbox path (dumps
/// fetched through the GitHub connector) and the REST path (the
/// <c>mine</c> command with a token) produce identical corpus artifacts.
/// </summary>
public static class Pipeline
{
    /// <summary>Snapshot byte cap (256 KiB) applied at emit time.</summary>
    public const int MaxSnapshotBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Regex HunkHeader = new(@"^@@ \-(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.Compiled);

    /// <summary>File-system key for a project slug.</summary>
    /// <param name="project">The owner/repo slug.</param>
    /// <returns>A path-safe directory name.</returns>
    public static string ProjectKey(string project) => project.Replace('/', '_');

    private static T LoadJson<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidOperationException($"unreadable dump: {path}");
    }

    private static void SaveJson(string path, object value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    /// <summary>Reads the pins file from a raw working directory.</summary>
    /// <param name="rawDir">Raw working directory.</param>
    /// <returns>The pins.</returns>
    public static PinsFile LoadPins(string rawDir) => LoadJson<PinsFile>(Path.Combine(rawDir, "pins.json"));

    /// <summary>
    /// Phase A: filters the recorded commit list of one project through the
    /// classifier message pass and records each candidate's pre-fix (parent)
    /// commit from the reverse-chronological default-branch ordering.
    /// </summary>
    /// <param name="rawDir">Raw working directory containing pins.json and &lt;key&gt;/commits.json.</param>
    /// <param name="project">Project slug to plan.</param>
    /// <exception cref="InvalidOperationException">The project is not pinned or has no commit dump.</exception>
    public static void PlanCandidates(string rawDir, string project)
    {
        var pins = LoadPins(rawDir);
        if (!pins.Projects.Any(p => p.Project == project))
        {
            throw new InvalidOperationException($"project not pinned: {project}");
        }

        var commitsPath = Path.Combine(rawDir, ProjectKey(project), "commits.json");
        if (!File.Exists(commitsPath))
        {
            throw new InvalidOperationException($"missing commit dump for {project}: {commitsPath}");
        }

        var dump = LoadJson<CommitListDump>(commitsPath);

        var candidates = new List<Dictionary<string, string?>>();
        for (var i = 0; i < dump.Commits.Count; i++)
        {
            var meta = dump.Commits[i];
            // Precision rule: only the commit SUBJECT (first line) drives the
            // message pass. Bilingual bodies of bulk refactors ("NRT
            // makeover") and feature commits routinely mention "fix" and a
            // category word without being defect fixes; a corpus-grade
            // candidate claims the fix in its subject line.
            var subject = meta.Message.Split('\n')[0];
            var hint = Classifier.CategoryFromMessage(subject);
            if (hint is null)
            {
                continue;
            }

            // The pre-fix parent is the next-older commit on the default branch
            // (merge commits are excluded by the message pass; pinned projects
            // have linear squash/rebase histories by construction).
            var parent = i + 1 < dump.Commits.Count ? dump.Commits[i + 1].Sha : null;
            if (parent is null)
            {
                continue; // oldest scanned commit: parent unknown within the window
            }

            candidates.Add(new Dictionary<string, string?>
            {
                ["sha"] = meta.Sha,
                ["preFixSha"] = parent,
                ["date"] = meta.Date,
                ["message"] = meta.Message,
                ["messageCategory"] = hint.Value.ToString(),
            });
        }

        SaveJson(Path.Combine(rawDir, ProjectKey(project), "candidates.json"), new Dictionary<string, object?>
        {
            ["project"] = project,
            ["headSha"] = dump.HeadSha,
            ["since"] = dump.Since,
            ["candidates"] = candidates,
        });
    }

    /// <summary>
    /// Phase B: classifies each candidate commit from its diff dump, selects
    /// the admissible defect files and writes the wanted-blobs plan.
    /// </summary>
    /// <param name="rawDir">Raw working directory.</param>
    /// <param name="project">Project slug to plan.</param>
    /// <remarks>Rejects are counted per documented reason for honesty accounting.</remarks>
    public static void PlanBlobs(string rawDir, string project)
    {
        var candidatesPath = Path.Combine(rawDir, ProjectKey(project), "candidates.json");
        if (!File.Exists(candidatesPath))
        {
            throw new InvalidOperationException($"run plan-candidates first for {project}");
        }

        var candidateDoc = LoadJson<JsonElement>(candidatesPath);
        var key = ProjectKey(project);
        var rejected = new Dictionary<string, int>();
        void Reject(string reason) => rejected[reason] = rejected.GetValueOrDefault(reason) + 1;

        var plan = new WantedBlobsPlan { Project = project };

        foreach (var candidate in candidateDoc.GetProperty("candidates").EnumerateArray())
        {
            var sha = candidate.GetProperty("sha").GetString()!;
            var messageSha = candidate.GetProperty("message").GetString() ?? string.Empty;
            var dateSha = candidate.GetProperty("date").GetString() ?? string.Empty;
            var preFixSha = candidate.GetProperty("preFixSha").GetString() ?? string.Empty;
            var messageHint = (BugCategory?)Enum.Parse(typeof(BugCategory), candidate.GetProperty("messageCategory").GetString()!);

            var detailPath = Path.Combine(rawDir, key, "details", $"{sha}.json");
            if (!File.Exists(detailPath))
            {
                Reject("missing-detail");
                continue;
            }

            var detail = LoadJson<CommitDetailDump>(detailPath);
            if (detail.Merge)
            {
                Reject("merge");
                continue;
            }

            List<string> addedLines(string? patch) => (patch ?? string.Empty)
                .Split('\n')
                .Where(l => l.StartsWith('+'))
                .ToList();

            var anyAccepted = false;
            foreach (var file in detail.Files)
            {
                if (file.Status != "modified")
                {
                    // Whole-file adds/removes/renames do not exhibit a
                    // historical defect in an analyzable pre-fix snapshot.
                    Reject($"status:{file.Status}");
                    continue;
                }

                if (!Classifier.IsAdmissibleSourcePath(file.Filename))
                {
                    Reject("not-source");
                    continue;
                }

                if (string.IsNullOrEmpty(file.Patch))
                {
                    Reject("no-patch");
                    continue;
                }

                var scores = Classifier.ScorePatch(addedLines(file.Patch));
                var accepted = Classifier.CategoryFromPatch(scores, messageHint);
                if (accepted is null)
                {
                    Reject("unclassified-patch");
                    continue;
                }

                var (category, signals) = accepted.Value;
                var ranges = ParseHunkRanges(file.Patch);
                plan.Entries.Add(new WantedBlob
                {
                    Id = GitHashing.EntryId(project, sha, file.Filename),
                    Project = project,
                    FixSha = sha,
                    PreFixSha = string.IsNullOrEmpty(detail.PreFixSha) ? preFixSha : detail.PreFixSha,
                    Path = file.Filename,
                    Category = category.ToString(),
                    Signals = signals.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value),
                    PreFixRanges = ranges.Old,
                    PostFixRanges = ranges.New,
                    Message = messageSha.Length > 300 ? messageSha[..300] : messageSha,
                    Date = dateSha,
                    Patch = file.Patch,
                });
                anyAccepted = true;
            }

            if (!anyAccepted)
            {
                Reject("no-accepted-file");
            }
        }

        plan.Rejected = rejected;
        SaveJson(Path.Combine(rawDir, key, "wanted-blobs.json"), plan);
    }

    /// <summary>Phase C: writes the corpus files and manifest from the blob dumps.</summary>
    /// <param name="rawDir">Raw working directory (all projects with plans and blobs).</param>
    /// <param name="corpusDir">Output corpus directory.</param>
    public static void Emit(string rawDir, string corpusDir)
    {
        var pins = LoadPins(rawDir);
        var manifest = new CorpusManifest
        {
            MaxSnapshotBytes = MaxSnapshotBytes,
            Pins = pins,
            MinedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // collision ledger: keeps renames stable across reruns

        foreach (var pin in pins.Projects)
        {
            var key = ProjectKey(pin.Project);
            var planPath = Path.Combine(rawDir, key, "wanted-blobs.json");
            if (!File.Exists(planPath))
            {
                continue;
            }

            var plan = LoadJson<WantedBlobsPlan>(planPath);
            manifest.Rejected[pin.Project] = plan.Rejected;

            foreach (var wanted in plan.Entries.OrderBy(e => e.FixSha, StringComparer.Ordinal).ThenBy(e => e.Path, StringComparer.Ordinal))
            {
                var blobPath = Path.Combine(rawDir, key, "blobs", $"{wanted.Id}.json");
                if (!File.Exists(blobPath))
                {
                    var rejKey = "missing-blob";
                    manifest.Rejected[pin.Project][rejKey] = manifest.Rejected[pin.Project].GetValueOrDefault(rejKey) + 1;
                    continue;
                }

                var blob = LoadJson<BlobDump>(blobPath);
                var byteCount = System.Text.Encoding.UTF8.GetByteCount(blob.Content);
                if (byteCount > MaxSnapshotBytes)
                {
                    var rejKey = "oversize";
                    manifest.Rejected[pin.Project][rejKey] = manifest.Rejected[pin.Project].GetValueOrDefault(rejKey) + 1;
                    continue;
                }

                var computedSha = GitHashing.BlobSha1(blob.Content);
                if (!string.IsNullOrEmpty(blob.GitBlobSha) &&
                    !string.Equals(blob.GitBlobSha, computedSha, StringComparison.OrdinalIgnoreCase))
                {
                    // The fetching client reported a different blob hash than
                    // the downloaded bytes hash to: the dump is not
                    // byte-faithful (transcoding/normalization). Never enter
                    // such a snapshot into the corpus.
                    var rejKey = "fetch-bytes-mismatch";
                    manifest.Rejected[pin.Project][rejKey] = manifest.Rejected[pin.Project].GetValueOrDefault(rejKey) + 1;
                    continue;
                }

                var snapshotRel = SnapshotPath(wanted, used);
                var snapshotAbs = Path.Combine(corpusDir, snapshotRel);
                Directory.CreateDirectory(Path.GetDirectoryName(snapshotAbs)!);
                File.WriteAllText(snapshotAbs, blob.Content);

                manifest.Entries.Add(new CorpusEntry
                {
                    Id = wanted.Id,
                    Project = wanted.Project,
                    License = pin.License,
                    FixSha = wanted.FixSha,
                    PreFixSha = wanted.PreFixSha,
                    Path = wanted.Path,
                    Category = wanted.Category,
                    Signals = wanted.Signals,
                    PreFixRanges = wanted.PreFixRanges,
                    PostFixRanges = wanted.PostFixRanges,
                    Message = wanted.Message,
                    Date = wanted.Date,
                    Snapshot = snapshotRel,
                    Bytes = byteCount,
                    GitBlobSha = GitHashing.BlobSha1(blob.Content),
                });
            }
        }

        manifest.Entries = manifest.Entries
            .OrderBy(e => e.Project, StringComparer.Ordinal)
            .ThenBy(e => e.FixSha, StringComparer.Ordinal)
            .ThenBy(e => e.Path, StringComparer.Ordinal)
            .ToList();

        Directory.CreateDirectory(corpusDir);
        SaveJson(Path.Combine(corpusDir, "manifest.json"), manifest);
        SaveJson(Path.Combine(corpusDir, "corpus-source.json"), pins);
    }

    /// <summary>Computes the corpus-relative snapshot path for a wanted blob.</summary>
    /// <param name="wanted">The planned entry.</param>
    /// <param name="used">Ledger of already-used paths (letter-case aware) so renames stay stable across reruns.</param>
    /// <returns>Forward-slash relative path.</returns>
    private static string SnapshotPath(WantedBlob wanted, HashSet<string> used)
    {
        var fileName = SanitizeFileName(Path.GetFileName(wanted.Path));
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var dir = $"files/{wanted.Project}/{wanted.FixSha[..8]}";

        var rel = $"{dir}/{fileName}";
        var i = 2;
        while (used.Contains(rel))
        {
            // Same commit fixing two identically-named files (possible with
            // differing directory casing); suffix deterministically.
            rel = $"{dir}/{stem}-{i}.cs";
            i++;
        }

        used.Add(rel);
        return rel.Replace('\\', '/');
    }

    /// <summary>Makes a file name safe for the corpus tree.</summary>
    /// <param name="name">Original file name.</param>
    /// <returns>Sanitized name with a .cs extension preserved.</returns>
    private static string SanitizeFileName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        var safe = new string(chars);
        return safe.Length > 80 ? safe[..77] + ".cs" : safe;
    }

    /// <summary>Parses old and new line ranges from unified diff hunk headers.</summary>
    /// <param name="patch">Unified diff text.</param>
    /// <returns>Inclusive (start, end) pairs for the pre-fix and post-fix coordinates.</returns>
    public static (List<int[]> Old, List<int[]> New) ParseHunkRanges(string patch)
    {
        var old = new List<int[]>();
        var now = new List<int[]>();
        foreach (var line in patch.Split('\n'))
        {
            var match = HunkHeader.Match(line.TrimStart());
            if (!match.Success)
            {
                continue;
            }

            var oldStart = int.Parse(match.Groups[1].Value);
            var oldLen = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
            var newStart = int.Parse(match.Groups[3].Value);
            var newLen = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1;

            if (oldStart > 0 && oldLen > 0)
            {
                old.Add([oldStart, oldStart + oldLen - 1]);
            }

            if (newStart > 0 && newLen > 0)
            {
                now.Add([newStart, newStart + newLen - 1]);
            }
        }

        return (old, now);
    }

    /// <summary>
    /// Integrity validation of a committed corpus: schema invariants, snapshot
    /// presence, recomputed Git blob hashes, category enum validity, license
    /// agreement with the pins. Fully offline.
    /// </summary>
    /// <param name="corpusDir">Committed corpus directory.</param>
    /// <returns>Validation report; <see cref="ValidationReport.Ok"/> is true when everything passed.</returns>
    public static ValidationReport Validate(string corpusDir)
    {
        var manifestPath = Path.Combine(corpusDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new ValidationReport(false, "manifest.json missing", 0, 0);
        }

        var manifest = LoadJson<CorpusManifest>(manifestPath);
        var failures = new List<string>();
        var licenses = manifest.Pins.Projects.ToDictionary(p => p.Project, p => p.License);

        var ids = new HashSet<string>();
        foreach (var entry in manifest.Entries)
        {
            if (!ids.Add(entry.Id))
            {
                failures.Add($"duplicate id: {entry.Id}");
            }

            if (!Enum.TryParse<BugCategory>(entry.Category, out _))
            {
                failures.Add($"bad category: {entry.Category}");
            }

            if (entry.Signals.Count == 0)
            {
                failures.Add($"no signals: {entry.Id}");
            }

            if (entry.PreFixRanges.Count == 0)
            {
                failures.Add($"no pre-fix ranges: {entry.Id}");
            }

            if (!licenses.TryGetValue(entry.Project, out var license) || license != entry.License)
            {
                failures.Add($"license disagrees with pins: {entry.Project}");
            }

            var snapshotPath = Path.Combine(corpusDir, entry.Snapshot);
            if (!File.Exists(snapshotPath))
            {
                failures.Add($"snapshot missing: {entry.Snapshot}");
                continue;
            }

            // Byte-exact re-read: File.ReadAllText silently strips a leading
            // UTF-8 BOM, which would corrupt the recomputed blob SHA and byte
            // counts for snapshots that genuinely start with a BOM.
            var content = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(snapshotPath));
            var blobSha = GitHashing.BlobSha1(content);
            if (blobSha != entry.GitBlobSha)
            {
                failures.Add($"blob sha mismatch: {entry.Snapshot}");
            }

            if (System.Text.Encoding.UTF8.GetByteCount(content) != entry.Bytes)
            {
                failures.Add($"byte count mismatch: {entry.Snapshot}");
            }
        }

        return new ValidationReport(failures.Count == 0, failures.Count == 0 ? "ok" : string.Join("; ", failures.Take(10)), manifest.Entries.Count, failures.Count);
    }

    /// <summary>Outcome of <see cref="Validate"/>.</summary>
    /// <param name="Ok">True when the corpus is internally consistent.</param>
    /// <param name="Detail">Human-readable summary or first failures.</param>
    /// <param name="Entries">Number of manifest entries.</param>
    /// <param name="Failures">Number of failures.</param>
    public readonly record struct ValidationReport(bool Ok, string Detail, int Entries, int Failures);
}

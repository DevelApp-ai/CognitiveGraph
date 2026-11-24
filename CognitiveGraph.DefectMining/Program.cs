using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DevelApp.CognitiveGraph.DefectMining;

/// <summary>
/// Command-line entry point. See <c>docs/measurements/issue-55/corpus/README.md</c>
/// for the end-to-end reproduction walk-through.
/// </summary>
public static partial class Program
{
    /// <summary>Exit code rationale: 0 = success, 2 = usage error, 3 = validation failure.</summary>
    private const int Usage = 2;

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return Usage;
        }

        var options = new Options(args.Skip(1));
        try
        {
            return args[0] switch
            {
                "plan-candidates" => WithRawAndProject(options, Pipeline.PlanCandidates),
                "plan-blobs" => WithRawAndProject(options, Pipeline.PlanBlobs),
                "emit" => EmitCommand(options),
                "validate" => ValidateCommand(options),
                "mine" => MineCommand(options).GetAwaiter().GetResult(),
                "version" => PrintVersion(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int WithRawAndProject(Options o, Action<string, string> phase)
    {
        var raw = Require(o, "raw");
        var project = Require(o, "project");
        phase(raw, project);
        Console.WriteLine("ok");
        return 0;
    }

    private static int EmitCommand(Options o)
    {
        var raw = Require(o, "raw");
        var corpus = Require(o, "out");
        Pipeline.Emit(raw, corpus);
        var report = Pipeline.Validate(corpus);
        Console.WriteLine(report.Detail);
        Console.WriteLine($"entries={report.Entries} failures={report.Failures}");
        return report.Ok ? 0 : 3;
    }

    private static int ValidateCommand(Options o)
    {
        var corpus = Require(o, "corpus");
        var report = Pipeline.Validate(corpus);
        Console.WriteLine(report.Detail);
        Console.WriteLine($"entries={report.Entries} failures={report.Failures}");
        return report.Ok ? 0 : 3;
    }

    private static int PrintVersion()
    {
        Console.WriteLine("defect-mining 1.0 (issue DevelApp-ai/CognitiveGraph#55)");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"unknown command: {command}");
        PrintUsage();
        return Usage;
    }

    private static string Require(Options o, string name)
    {
        var value = o.Get(name);
        if (string.IsNullOrEmpty(value))
        {
            Console.Error.WriteLine($"missing --{name}");
            PrintUsage();
            Environment.Exit(Usage);
        }

        return value;
    }

    private static void PrintUsage() => Console.WriteLine("""
        usage: dotnet run --project CognitiveGraph.DefectMining -- <command> [options]

        Token-free phases over a raw working directory (fetches made by any
        authenticated GitHub client that writes the documented dump files):

          plan-candidates --raw DIR --project owner/repo
          plan-blobs      --raw DIR --project owner/repo
          emit            --raw DIR --out CORPUS_DIR
          validate        --corpus CORPUS_DIR

        Full reproduction (network; reads GITHUB_TOKEN for api.github.com):

          mine            --raw DIR --out CORPUS_DIR [--project owner/repo]

        Each phase is a deterministic transform; the committed corpus under
        docs/measurements/issue-55/corpus must pass `validate` byte-identically.
        """);

    /// <summary>Simple command-line option reader.</summary>
    /// <param name="args">Arguments after the command word.</param>
    private sealed class Options(IEnumerable<string> args)
    {
        private readonly Dictionary<string, string> values = args
            .Select((value, index) => (value, index))
            .Where(t => t.value.StartsWith("--"))
            .ToDictionary(t => t.value[2..], t => args.ElementAtOrDefault(t.index + 1) ?? string.Empty);

        /// <summary>Reads an option value, or <c>null</c>.</summary>
        /// <param name="name">Option name without dashes.</param>
        /// <returns>The value, or <c>null</c> when absent.</returns>
        public string? Get(string name) => values.GetValueOrDefault(name) is { Length: > 0 } value ? value : null;
    }

    /// <summary>Implements the token-driven REST chain that materializes the same dumps the connector path writes.</summary>
    private static async Task<int> MineCommand(Options o)
    {
        var raw = Require(o, "raw");
        var corpus = Require(o, "out");
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("GITHUB_TOKEN is required for mine");
            return 1;
        }

        var pins = Pipeline.LoadPins(raw);
        var targets = pins.Projects;
        if (o.Get("project") is { } only)
        {
            targets = targets.Where(p => p.Project == only).ToList();
        }

        using var http = new HttpClient();
        http.BaseAddress = new Uri("https://api.github.com/", UriKind.Absolute);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CognitiveGraph-DefectMining/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        foreach (var pin in targets)
        {
            Console.WriteLine($"# {pin.Project}");
            var key = Pipeline.ProjectKey(pin.Project);
            Directory.CreateDirectory(Path.Combine(raw, key, "details"));
            Directory.CreateDirectory(Path.Combine(raw, key, "blobs"));

            // 1. Commit window on the default branch.
            var commits = new List<CommitMeta>();
            for (var page = 1; page <= 40; page++)
            {
                var query = $"repos/{pin.Owner}/{pin.Repo}/commits?per_page=100&page={page}";
                if (!string.IsNullOrEmpty(pin.Since))
                {
                    query += $"&since={pin.Since}";
                }

                using var response = await http.GetAsync(query);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var batch = doc.RootElement.EnumerateArray().Select(item => new CommitMeta
                {
                    Sha = item.GetProperty("sha").GetString()!,
                    Date = item.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString() ?? string.Empty,
                    Message = item.GetProperty("commit").GetProperty("message").GetString() ?? string.Empty,
                }).ToList();
                commits.AddRange(batch);
                if (batch.Count < 100 || commits.Count >= (pin.MaxCommits ?? 1500))
                {
                    break;
                }
            }

            var dumpPath = Path.Combine(raw, key, "commits.json");
            await File.WriteAllTextAsync(dumpPath, JsonSerializer.Serialize(new CommitListDump
            {
                Project = pin.Project,
                Since = pin.Since,
                HeadSha = commits.FirstOrDefault()?.Sha ?? string.Empty,
                Commits = commits,
            }));

            // 2. Candidate planning (deterministic, shared with the connector path).
            Pipeline.PlanCandidates(raw, pin.Project);
            var candidatesPath = Path.Combine(raw, key, "candidates.json");
            using var candidateDoc = JsonDocument.Parse(await File.ReadAllTextAsync(candidatesPath));
            var candidates = candidateDoc.RootElement.GetProperty("candidates").EnumerateArray().ToList();

            // 3. Fetch candidate diffs.
            foreach (var candidate in candidates)
            {
                var sha = candidate.GetProperty("sha").GetString()!;
                using var response = await http.GetAsync($"repos/{pin.Owner}/{pin.Repo}/commits/{sha}");
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                var parents = doc.RootElement.GetProperty("parents");
                var detail = new CommitDetailDump
                {
                    Sha = sha,
                    Message = doc.RootElement.GetProperty("commit").GetProperty("message").GetString() ?? string.Empty,
                    Date = doc.RootElement.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString() ?? string.Empty,
                    Merge = parents.GetArrayLength() > 1,
                    PreFixSha = parents[0].GetProperty("sha").GetString() ?? string.Empty,
                    Files = doc.RootElement.GetProperty("files").EnumerateArray()
                        .Where(f => f.TryGetProperty("patch", out _))
                        .Select(f => new FileChangeDump
                        {
                            Filename = f.GetProperty("filename").GetString() ?? string.Empty,
                            Status = f.GetProperty("status").GetString() ?? string.Empty,
                            Patch = f.GetProperty("patch").GetString(),
                        })
                        .ToList(),
                };

                await File.WriteAllTextAsync(Path.Combine(raw, key, "details", $"{sha}.json"), JsonSerializer.Serialize(detail));
            }

            // 4. Blob planning.
            Pipeline.PlanBlobs(raw, pin.Project);

            // 5. Fetch pre-fix snapshots.
            var planPath = Path.Combine(raw, key, "wanted-blobs.json");
            using var planDoc = JsonDocument.Parse(await File.ReadAllTextAsync(planPath));
            foreach (var entry in planDoc.RootElement.GetProperty("entries").EnumerateArray())
            {
                var id = entry.GetProperty("id").GetString()!;
                var path = entry.GetProperty("path").GetString()!;
                var preFix = entry.GetProperty("preFixSha").GetString()!;
                using var response = await http.GetAsync($"repos/{pin.Owner}/{pin.Repo}/contents/{Uri.EscapeDataString(path)}?ref={preFix}");
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var contentBase64 = doc.RootElement.GetProperty("content").GetString() ?? string.Empty;
                var content = Encoding.UTF8.GetString(Convert.FromBase64String(contentBase64.Replace("\n", string.Empty)));
                var blob = new BlobDump
                {
                    Id = id,
                    Content = content,
                    GitBlobSha = doc.RootElement.GetProperty("sha").GetString(),
                };
                await File.WriteAllTextAsync(Path.Combine(raw, key, "blobs", $"{id}.json"), JsonSerializer.Serialize(blob));
            }
        }

        Pipeline.Emit(raw, corpus);
        var report = Pipeline.Validate(corpus);
        Console.WriteLine(report.Detail);
        return report.Ok ? 0 : 3;
    }
}

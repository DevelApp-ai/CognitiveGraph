using System.Security.Cryptography;
using System.Text;

namespace DevelApp.CognitiveGraph.DefectMining;

/// <summary>Git hashing helpers for cross-checking corpus snapshots.</summary>
public static class GitHashing
{
    /// <summary>Computes the Git blob SHA-1 of UTF-8 text content.</summary>
    /// <param name="content">File content with its exact original line endings.</param>
    /// <returns>The 40-character lowercase hex blob SHA-1.</returns>
    public static string BlobSha1(string content)
    {
        var body = Encoding.UTF8.GetBytes(content);
        var header = Encoding.ASCII.GetBytes($"blob {body.Length}\0");
        var payload = new byte[header.Length + body.Length];
        Buffer.BlockCopy(header, 0, payload, 0, header.Length);
        Buffer.BlockCopy(body, 0, payload, header.Length, body.Length);

        var hash = SHA1.HashData(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Short deterministic corpus identifier for a bug file.</summary>
    /// <param name="project">The pinned project slug (owner/repo).</param>
    /// <param name="fixSha">The bug-fixing commit SHA.</param>
    /// <param name="path">Repository-relative file path.</param>
    /// <returns>A stable hex id.</returns>
    public static string EntryId(string project, string fixSha, string path)
    {
        var raw = Encoding.UTF8.GetBytes($"{project}|{fixSha}|{path}");
        return Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
    }
}

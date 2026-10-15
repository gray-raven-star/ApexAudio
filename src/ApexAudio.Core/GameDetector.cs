using System.Diagnostics;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed record DetectedGame(int ProcessId, string Name, string? Path, string[] PackIds, bool VersionMatched);
public static class GameDetector
{
    static readonly Dictionary<string, (long Size, DateTime Modified, string Hash)> hashes = new(StringComparer.OrdinalIgnoreCase);
    public static List<DetectedGame> Detect(IEnumerable<InstalledPack> packs)
    {
        var rules = packs.Where(p => !p.Manifest.Demo).ToArray(); var result = new List<DetectedGame>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var candidates = rules.Where(p => p.Manifest.Games.Any(g => Path.GetFileNameWithoutExtension(g.Executable).Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase))).ToArray();
                    if (candidates.Length == 0) continue;
                    string? path = null; try { path = process.MainModule?.FileName; } catch (System.ComponentModel.Win32Exception) { }
                    string? hash = null;
                    if (path != null && candidates.Any(p => p.Manifest.Games.Any(g => g.Sha256 != null)))
                    {
                        var file = new FileInfo(path);
                        if (!hashes.TryGetValue(path, out var cache) || cache.Size != file.Length || cache.Modified != file.LastWriteTimeUtc)
                        { cache = (file.Length, file.LastWriteTimeUtc, PackageStore.Hash(path)); hashes[path] = cache; }
                        hash = cache.Hash;
                    }
                    var matching = candidates.Where(p => p.Manifest.Games.Any(g => Path.GetFileNameWithoutExtension(g.Executable).Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase) && (g.Sha256 == null || g.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase)))).ToArray();
                    result.Add(new(process.Id, process.ProcessName, path, (matching.Length > 0 ? matching : candidates).Select(p => p.Manifest.Id).ToArray(), matching.Length > 0));
                }
                catch (InvalidOperationException) { }
            }
        }
        return result;
    }
}

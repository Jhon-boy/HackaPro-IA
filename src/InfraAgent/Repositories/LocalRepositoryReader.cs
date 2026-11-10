using System.Text;
using InfraAgent.Abstractions;
using InfraAgent.Validation;

namespace InfraAgent.Repositories;

/// <summary>
/// Lector de SOLO LECTURA de una carpeta local (ideal para demos y desarrollo). Garantiza que ninguna
/// ruta salga de la carpeta raíz (sin '..', sin enlaces simbólicos) y omite carpetas de ruido.
/// </summary>
public sealed class LocalRepositoryReader : IRepositoryReader
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".terraform", "node_modules", "bin", "obj", ".vs", ".idea"
    };

    private readonly string _root;

    public LocalRepositoryReader(string rootPath)
    {
        var full = Path.GetFullPath(rootPath);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"La carpeta del proyecto no existe: {Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar))}");
        _root = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public string Description => $"local:{Path.GetFileName(_root.TrimEnd(Path.DirectorySeparatorChar))}";

    public Task<IReadOnlyList<string>> ListFilesAsync(string? pathPrefix, int maxItems, CancellationToken ct)
    {
        var start = _root;
        if (!string.IsNullOrWhiteSpace(pathPrefix))
        {
            start = Resolve(InputValidators.RelativePath(pathPrefix, "carpeta"));
            if (!Directory.Exists(start)) return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var files = new List<string>();
        Walk(start, files, maxItems, ct);
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public async Task<RepoFile?> GetFileAsync(string path, int maxBytes, CancellationToken ct)
    {
        var full = Resolve(InputValidators.RelativePath(path));
        var info = new FileInfo(full);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;

        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var buffer = new byte[Math.Min(info.Length, (long)maxBytes)];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        return new RepoFile(path.Replace('\\', '/').TrimStart('/'), text, info.Length > maxBytes);
    }

    private void Walk(string directory, List<string> files, int max, CancellationToken ct)
    {
        if (files.Count >= max) return;

        foreach (var file in Directory.EnumerateFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            files.Add(Path.GetRelativePath(_root, file).Replace('\\', '/'));
            if (files.Count >= max) return;
        }

        foreach (var dir in Directory.EnumerateDirectories(directory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (SkippedDirectories.Contains(name)) continue;
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            Walk(dir, files, max, ct);
            if (files.Count >= max) return;
        }
    }

    /// <summary>Resuelve una ruta relativa ya validada y confirma que sigue dentro de la raíz.</summary>
    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) && !(full + Path.DirectorySeparatorChar).Equals(_root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("La ruta queda fuera del proyecto.");
        return full;
    }
}

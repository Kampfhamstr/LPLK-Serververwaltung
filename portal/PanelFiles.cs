using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal sealed record PanelFile(string Name, long Size, bool IsFile, bool IsSymlink, string? ModifiedAt);

internal sealed partial class PanelClient
{
    internal static void ValidateFileRequest(string server, string path)
    {
        if (!Regex.IsMatch(server, "^[a-zA-Z0-9-]{8,36}$")) throw new ArgumentException("Ungültige Serverkennung.");
        if (!path.StartsWith('/') || path.Contains('\\') || path.Any(char.IsControl) ||
            path.Split('/').Any(p => p is "." or "..")) throw new ArgumentException("Ungültiger absoluter Serverpfad.");
    }

    public async Task<List<PanelFile>> ListFilesAsync(string server, string directory)
    {
        ValidateFileRequest(server, directory);
        using var json = JsonDocument.Parse(await GetAsync($"api/client/servers/{server}/files/list?directory={Uri.EscapeDataString(directory)}"));
        var result = new List<PanelFile>();
        foreach (var item in json.RootElement.GetProperty("data").EnumerateArray())
        {
            var a = item.GetProperty("attributes");
            string name = a.GetProperty("name").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0 || name.Any(char.IsControl))
                throw new PanelException("Ungültiger Dateiname in der Panelantwort.");
            result.Add(new(name, a.GetProperty("size").GetInt64(), a.GetProperty("is_file").GetBoolean(),
                a.TryGetProperty("is_symlink", out var link) && link.GetBoolean(),
                a.TryGetProperty("modified_at", out var modified) ? modified.GetString() : null));
        }
        return result;
    }

    public Task<string> ReadFileAsync(string server, string file)
    {
        ValidateFileRequest(server, file);
        return GetAsync($"api/client/servers/{server}/files/contents?file={Uri.EscapeDataString(file)}");
    }
}

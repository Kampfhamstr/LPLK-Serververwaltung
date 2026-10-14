using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2ServerPortal;

internal sealed record AddonFile(string Path, long Size, bool IsFile, bool IsSymlink, string? ModifiedAt);
internal sealed record InventoryGap(string Path, string Reason);
internal sealed record ServerAddonInventory(ServerInfo Server, List<AddonFile> Files, List<InventoryGap> Gaps);
internal sealed record AddonInventoryReport(DateTimeOffset CheckedAt, string Evidence, List<ServerAddonInventory> Servers);

internal static class AddonInventory
{
    internal static bool SkipDirectory(string path)
    {
        string name = path.Split('/').Last();
        if (new[] { "logs", "dumps", "data", ".git", "node_modules", "runtimes", "obj" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        return Regex.IsMatch(path, @"/addons/(swiftlys2|counterstrikesharp)/(bin|gamedata|translations)$", RegexOptions.IgnoreCase)
            || path.EndsWith("/addons/metamod/bin", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<AddonInventoryReport> CollectAsync(PanelClient client)
    {
        var result = new AddonInventoryReport(DateTimeOffset.UtcNow,
            "Read-only file inventory. Present files do not prove runtime loading. Skipped directories and read failures are recorded as gaps.", []);
        foreach (var server in await client.GetServersAsync(true))
        {
            var inventory = new ServerAddonInventory(server, [], []);
            result.Servers.Add(inventory);
            if (server.Cs2Version == null)
            {
                inventory.Gaps.Add(new("/", "CS2 not identified from steam.inf; not scanned."));
                continue;
            }
            var queue = new Queue<(string Path, int Depth)>();
            // Probe documented layouts; absence is recorded, never presented as an empty successful scan.
            foreach (string path in new[] { "/game/csgo/addons", "/csgo/addons" }) queue.Enqueue((path, 0));
            int directories = 0;
            while (queue.TryDequeue(out var current))
            {
                if (++directories > 500 || inventory.Files.Count >= 30000)
                {
                    inventory.Gaps.Add(new(current.Path, "Safety limit reached; inventory incomplete."));
                    break;
                }
                List<PanelFile> entries;
                try { entries = await client.ListFilesAsync(server.Identifier, current.Path); }
                catch (Exception ex) when (ex is PanelException or HttpRequestException or OperationCanceledException or JsonException)
                {
                    inventory.Gaps.Add(new(current.Path, SafeError.Message(ex)));
                    continue;
                }
                foreach (var entry in entries)
                {
                    string path = current.Path + "/" + entry.Name;
                    inventory.Files.Add(new(path, entry.Size, entry.IsFile, entry.IsSymlink, entry.ModifiedAt));
                    if (entry.IsSymlink) inventory.Gaps.Add(new(path, "Symbolic link not followed."));
                    else if (!entry.IsFile)
                    {
                        if (SkipDirectory(path)) inventory.Gaps.Add(new(path, "Runtime/cache/log/data directory; contents intentionally not scanned."));
                        else if (current.Depth >= 10) inventory.Gaps.Add(new(path, "Depth limit reached."));
                        else queue.Enqueue((path, current.Depth + 1));
                    }
                }
                await Task.Delay(200);
            }
            Console.Error.WriteLine($"Inventory {server.Identifier}: {inventory.Files.Count} entries; {inventory.Gaps.Count} recorded exclusions/errors.");
        }
        return result;
    }
}

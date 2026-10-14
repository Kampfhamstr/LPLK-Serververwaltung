// Adapted from the user's HeadshotOnly 1.0.18 WorkshopCollection.cs; bounded nested collection handling retained.
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace LPLK_Maps;
public sealed record WorkshopCollectionSnapshot(string Id, HashSet<string> MemberIds, DateTimeOffset FetchedAtUtc);
public sealed record CollectionFetchResult(WorkshopCollectionSnapshot? Snapshot, string Error);
public sealed record WorkshopCollectionChild(string Id, bool IsCollection);

public static class WorkshopCollections
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetCommandLineW();

    public static string? ReadStartupCollection()
    {
        // Never log the full command line: it can contain GSLT and RCON credentials.
        if (OperatingSystem.IsLinux())
            return ParseStartupArguments(File.ReadAllText("/proc/self/cmdline").Split('\0', StringSplitOptions.RemoveEmptyEntries));
        if (OperatingSystem.IsWindows())
            return ParseStartupCommandLine(Marshal.PtrToStringUni(GetCommandLineW()) ?? string.Empty);
        return ParseStartupArguments(Environment.GetCommandLineArgs());
    }

    public static string? ParseStartupCommandLine(string commandLine) => ParseStartupArguments(
        Regex.Matches(commandLine, "\"([^\"]*)\"|([^\\s\"]+)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToArray());

    public static string? ParseStartupArguments(IReadOnlyList<string> args)
    {
        string? id = null;
        for (int i = 0; i < args.Count; i++)
        {
            const string parameter = "+host_workshop_collection";
            if (args[i].Equals(parameter, StringComparison.OrdinalIgnoreCase))
                id = i + 1 < args.Count ? MapPolicy.WorkshopId(args[i + 1].Trim('"')) : null;
            else if (args[i].StartsWith(parameter + "=", StringComparison.OrdinalIgnoreCase))
                id = MapPolicy.WorkshopId(args[i][(parameter.Length + 1)..].Trim('"'));
        }
        return id;
    }

    public static List<WorkshopCollectionChild> ParseResponse(string json, string expectedId)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement response = document.RootElement.GetProperty("response");
        if (response.TryGetProperty("result", out JsonElement overallResult) && overallResult.GetInt32() != 1)
            throw new InvalidDataException("Steam meldet einen Fehler beim Kollektionsabruf.");
        JsonElement details = response.GetProperty("collectiondetails");
        if (details.GetArrayLength() != 1) throw new InvalidDataException("Steam lieferte keine eindeutige Kollektion.");
        JsonElement detail = details[0];
        static string? Id(JsonElement item) => MapPolicy.WorkshopId(item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText());
        if (Id(detail.GetProperty("publishedfileid")) != expectedId || detail.GetProperty("result").GetInt32() != 1)
            throw new InvalidDataException("Steam-Kollektion nicht verfügbar/privat oder falsche ID.");
        JsonElement children = detail.GetProperty("children");
        if (children.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Unvollständige Kollektionsantwort.");
        List<WorkshopCollectionChild> result = [];
        foreach (JsonElement child in children.EnumerateArray())
        {
            string id = Id(child.GetProperty("publishedfileid")) ?? throw new InvalidDataException("Ungültiges Kollektionsmitglied.");
            int type = child.GetProperty("filetype").GetInt32();
            if (type < 0) throw new InvalidDataException("Ungültiger Workshop-Typ.");
            result.Add(new WorkshopCollectionChild(id, type == 2));
        }
        return result;
    }

    public static async Task<HashSet<string>> ResolveMembersAsync(string rootId,
        Func<string, CancellationToken, Task<string>> fetch, CancellationToken cancellationToken)
    {
        if (MapPolicy.WorkshopId(rootId) != rootId) throw new InvalidDataException("Ungültige Kollektions-ID.");
        HashSet<string> visited = [], members = [];
        Queue<string> pending = new(); pending.Enqueue(rootId);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = pending.Dequeue();
            if (!visited.Add(id)) continue;
            if (visited.Count > 64) throw new InvalidDataException("Mehr als 64 verschachtelte Kollektionen; Abgleich abgebrochen.");
            foreach (WorkshopCollectionChild child in ParseResponse(await fetch(id, cancellationToken).ConfigureAwait(false), id))
            {
                if (child.IsCollection) pending.Enqueue(child.Id);
                else members.Add(child.Id);
            }
            if (members.Count > 20000 || pending.Count > 20000) throw new InvalidDataException("Kollektion zu groß.");
        }
        return members;
    }

    public static async Task<CollectionFetchResult> TryFetchAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
            async Task<string> Fetch(string collection, CancellationToken token)
            {
                using FormUrlEncodedContent form = new(new Dictionary<string, string>
                { ["collectioncount"] = "1", ["publishedfileids[0]"] = collection });
                using HttpResponseMessage response = await client.PostAsync(
                    "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", form, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }
            HashSet<string> members = await ResolveMembersAsync(id, Fetch, cancellationToken).ConfigureAwait(false);
            return new CollectionFetchResult(new WorkshopCollectionSnapshot(id, members, DateTimeOffset.UtcNow), string.Empty);
        }
        catch (OperationCanceledException) { return new CollectionFetchResult(null, "Steam-Abgleich abgebrochen oder Zeitlimit erreicht."); }
        catch (Exception ex) { return new CollectionFetchResult(null, "Steam-Abgleich fehlgeschlagen: " + ex.GetType().Name); }
    }
}


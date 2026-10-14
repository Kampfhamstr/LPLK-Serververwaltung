using System.Text.Json;
using LPLK_Sounds;

internal static class SoundNumbersTests
{
    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "deatheffects-number-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new AssetCatalog();
            foreach (int n in Enumerable.Range(1, 64).Reverse())
                catalog.Sounds[$"fi_{n:00}_sound"] = new() { Title = $"Sound {65 - n:00}" };
            catalog.Cards["stuhl"] = new() { Title = "Nicht in der Soundliste" };
            string path = Path.Combine(root, "sound-numbers.json");
            var store = new SoundNumberStore(path);
            store.Update(catalog);
            var all = store.List(catalog);
            check(all.Length == 64 && all.Select(x => x.Number).SequenceEqual(Enumerable.Range(1, 64)), "64 sounds receive unique global numbers; cards are excluded");
            check(store.Resolve(catalog, 51) == "fi_51_sound", "Direct sound 51 resolves by the same ID as the numbered list");
            var pages = Enumerable.Range(0, 7).Select(index => SoundMenuPolicy.Page(all, index)).ToArray();
            check(pages.All(p => p.Count == 7) && pages.Select(p => p.Items.Count).SequenceEqual(new[] { 10, 10, 10, 10, 10, 10, 4 }), "64 sounds produce six full ten-sound pages and a final page of four");
            check(pages.SelectMany(p => p.Items).SequenceEqual(all), "Paging reaches every sound exactly once without resetting its number");
            check(pages[5].Items[0].Number == 51 && pages[5].Items[0].Id == store.Resolve(catalog, 51), "Page 6 starts at sound 51 and agrees with direct playback lookup");
            check(SoundMenuPolicy.Page(all, -1).Index == 0 && SoundMenuPolicy.Page(all, int.MaxValue).Index == 6, "Obsolete menu page clamps to available range");
            var empty = SoundMenuPolicy.Page([], 100);
            check(empty.Count == 1 && empty.Index == 0 && empty.Items.Count == 0, "Empty catalog still has a usable empty page");
            check(SoundMenuPolicy.Page(all.Take(10).ToArray(), 0).Count == 1 && SoundMenuPolicy.Page(all.Take(11).ToArray(), 1).Items.Count == 1, "Exact page boundary and first item on next page work");

            string saved = File.ReadAllText(path);
            foreach (var sound in catalog.Sounds.Values) sound.Title = "Umbenannt";
            catalog.Sounds = catalog.Sounds.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
            store.Update(catalog);
            check(File.ReadAllText(path) == saved && store.Resolve(catalog, 51) == "fi_51_sound", "Changing titles and catalog order does not rewrite or renumber saved IDs");
            catalog.Sounds["aaa_new"] = new() { Title = "A new sound" }; store.Update(catalog);
            check(store.Resolve(catalog, 65) == "aaa_new" && store.Resolve(catalog, 51) == "fi_51_sound", "Alphabetically earlier additions append a number instead of shifting existing commands");
            catalog.Sounds.Remove("fi_51_sound"); store.Update(catalog);
            check(store.Resolve(catalog, 51) == "fi_52_sound" && store.List(catalog).Select(x=>x.Number).SequenceEqual(Enumerable.Range(1,64)), "Removing a sound closes its number gap and keeps a continuous list");
            catalog.Sounds["newer"] = new() { Title = "Neu" }; store.Update(catalog);
            check(store.Resolve(catalog, 65) == "newer", "New sound appends after the compacted list");
            catalog.Sounds["fi_51_sound"] = new() { Title = "Zurueck" }; store.Update(catalog);
            check(store.Resolve(catalog, 66) == "fi_51_sound", "Returning asset appends without displacing the current order");
            check(File.ReadAllText(path + ".before-renumbering.json") == saved, "Original numbering is backed up before rewriting");
            check(new SoundNumberStore(path).List(catalog).SequenceEqual(store.List(catalog)), "Numbering survives plugin restart with new and retired IDs");
            check(store.Resolve(catalog, 0) is null && store.Resolve(catalog, -1) is null && store.Resolve(catalog, int.MaxValue) is null, "Unknown and invalid numbers resolve to no sound");

            check(SoundMenuPolicy.TryNumber("51", out int number) && number == 51, "Plain command number accepted");
            check(SoundMenuPolicy.TryPageToken("s6", out int page) && page == 6 && !SoundMenuPolicy.TryNumber("s6", out _), "sound s6 selects page 6 without being interpreted as playback");
            check(SoundMenuPolicy.TryPageToken("S6", out page) && page == 6, "Page prefix accepts uppercase S");
            check(!SoundMenuPolicy.TryPageToken("6", out _) && SoundMenuPolicy.TryNumber("6", out number) && number == 6
                && SoundMenuPolicy.TryNumber("16", out number) && number == 16, "sound 6 and sound 16 remain playback numbers");
            foreach (string bad in new[] { "s", "s0", "s-1", "s1.5", "ss6", "s2147483648", "s6 1" })
                check(!SoundMenuPolicy.TryPageToken(bad, out _), "Invalid page token rejected: " + bad);
            foreach (string bad in new[] { "", "0", "-1", "+1", "1.5", "1 2", " 1", "2147483648", "fi_51_sound", "51;quit" })
                check(!SoundMenuPolicy.TryNumber(bad, out _), "Invalid numeric command rejected: " + bad);

            string badPath = Path.Combine(root, "bad.json");
            foreach (string bad in new[] { "null", "[]", "{\"a\":0}", "{\"a\":-1}", "{\"a\":10001}", "{\"a\":1,\"b\":1}", "{\"a\":1,\"a\":2}", "{\"../bad\":1}" })
            {
                File.WriteAllText(badPath, bad);
                bool rejected = false;
                try { _ = new SoundNumberStore(badPath); }
                catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; }
                check(rejected && File.ReadAllText(badPath) == bad, "Invalid number file is rejected intact: " + bad);
            }
            string blockedPath = Path.Combine(root, "blocked"); Directory.CreateDirectory(blockedPath);
            var blocked = new SoundNumberStore(blockedPath);
            bool writeFailed = false;
            try { blocked.Update(catalog); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { writeFailed = true; }
            check(writeFailed && blocked.List(catalog).Length == 0, "Failed persistence never publishes unstable new numbers");
            File.WriteAllText(badPath, "{\"reserved\":10000}");
            var compacted=new SoundNumberStore(badPath);compacted.Update(catalog);
            check(compacted.List(catalog).Select(x=>x.Number).SequenceEqual(Enumerable.Range(1,catalog.Sounds.Count)), "Retired high numbers no longer prevent a continuous active catalog");
            string compactedFile=File.ReadAllText(badPath);
            var huge=new AssetCatalog();
            for(int i=0;i<=10000;i++)huge.Sounds["sound_"+i]=new(){Title="Sound"};
            bool full=false;
            try { compacted.Update(huge); } catch(InvalidDataException) { full=true; }
            check(full && File.ReadAllText(badPath)==compactedFile, "Too many active sounds still fail without rewriting the mapping");
        }
        finally
        {
            string expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("deatheffects-number-tests-", StringComparison.Ordinal))
                throw new IOException("Unsafe test cleanup path");
            Directory.Delete(resolved, true);
        }
    }
}

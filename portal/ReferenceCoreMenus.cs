using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace CS2ServerPortal;
internal static class ReferenceCoreMenus
{
    internal static async Task<object> ApplyAsync(PanelClient client,SshConnection connection,string workspace)
    {
        const string path="/game/csgo/addons/swiftlys2/configs/core.jsonc";
        var options=new JsonDocumentOptions{AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip};
        string original=await client.ReadFileAsync(PanelClient.TestServerId,path),sourceText=await client.ReadFileAsync("11110003",path);
        var source=JsonNode.Parse(sourceText,documentOptions:options)!.AsObject();
        var before=JsonNode.Parse(original,documentOptions:options)!.AsObject();
        var buttons=source["Menu"]!["Buttons"]!;string next=original;
        foreach(string key in new[]{"Scroll","ScrollBack"})
        {
            string expected=before["Menu"]!["Buttons"]![key]!.GetValue<string>();
            string value=buttons[key]!.GetValue<string>();
            if(value.Length>20||!Regex.IsMatch(value,"^[a-z0-9]+$"))throw new InvalidOperationException("Unbekannte Menütaste.");
            var rx=new Regex("(\""+key+"\"\\s*:\\s*)\"([^\"]*)\"");var matches=rx.Matches(next);
            if(matches.Count!=1||matches[0].Groups[2].Value!=expected)throw new InvalidOperationException("Menükonfiguration nicht eindeutig.");
            next=rx.Replace(next,m=>m.Groups[1].Value+JsonSerializer.Serialize(value));
        }
        var after=JsonNode.Parse(next,documentOptions:options)!.AsObject();
        if(!JsonNode.DeepEquals(source["Menu"],after["Menu"]))throw new InvalidOperationException("Weitere Menüunterschiede müssen geprüft werden.");
        before.Remove("Menu");after.Remove("Menu");
        if(!JsonNode.DeepEquals(before,after))throw new InvalidOperationException("Fremde Konfiguration verändert.");
        string Hash(string v)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v))).ToLowerInvariant();
        var result=SshService.ApplyTestMenuKeys(connection,Hash(original),Hash(sourceText),Convert.ToBase64String(Encoding.UTF8.GetBytes(next)));
        if(result.ExitCode!=0)throw new InvalidOperationException("Test-Menükonfiguration konnte nicht bestätigt werden.");
        var verification=await ReadAsync(client,workspace);
        if(await client.ReadFileAsync(PanelClient.TestServerId,path)!=next)throw new InvalidOperationException("Menü-Nachprüfung fehlgeschlagen.");
        return verification;
    }
    internal static async Task<object> ReadAsync(PanelClient client,string workspace)
    {
        const string path="/game/csgo/addons/swiftlys2/configs/core.jsonc";
        var options=new JsonDocumentOptions{AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip};
        using var source=JsonDocument.Parse(await client.ReadFileAsync("11110003",path),options);
        using var target=JsonDocument.Parse(await client.ReadFileAsync(PanelClient.TestServerId,path),options);
        Dictionary<string,JsonElement> Menus(JsonElement root)
        {
            var found=new Dictionary<string,JsonElement>();
            void Walk(JsonElement node,string prefix)
            {
                if(node.ValueKind!=JsonValueKind.Object)return;
                foreach(var p in node.EnumerateObject())
                {
                    var key=prefix+"/"+p.Name;
                    if(p.Name.Contains("menu",StringComparison.OrdinalIgnoreCase))found[key]=p.Value.Clone();
                    else Walk(p.Value,key);
                }
            }
            Walk(root,"");return found;
        }
        var result=new {observedAt=DateTimeOffset.UtcNow,sourceServer="11110003",targetServer=PanelClient.TestServerId,source=Menus(source.RootElement),test=Menus(target.RootElement)};
        File.WriteAllText(Path.Combine(workspace,"state","reference-core-menus.json"),JsonSerializer.Serialize(result,Program.JsonOptions));
        return result;
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace CS2ServerPortal;

internal sealed record GitHubReleaseAsset(string Name,string Path,string Sha256,long Bytes);
internal sealed record GitHubReleasePlan(string Tag,string ExpectedHead,string ManifestSha256,string Body,GitHubReleaseAsset[] Assets);
internal sealed record GitHubReleaseReceipt(string PlanHash,string? Commit=null,long? ReleaseId=null,bool Verified=false);

// Fixed private repository; only reviewed sources and complete package archives.
// Credentials remain inside the existing encrypted-store application boundary.
internal sealed class GitHubRelease : IDisposable
{
    const string Repo="repos/Kampfhamstr/LPLK-Serververwaltung";
    const string Web="https://github.com/Kampfhamstr/LPLK-Serververwaltung";
    readonly string workspace;
    readonly GitHubConnection connection;
    readonly HttpClient http;
    public GitHubRelease(string workspace,GitHubConnection connection,HttpMessageHandler? handler=null)
    {
        this.workspace=Path.GetFullPath(workspace);this.connection=connection;
        http=new(handler??new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){BaseAddress=new("https://api.github.com/"),Timeout=TimeSpan.FromMinutes(2)};
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",connection.Token);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LPLK-Serververwaltung/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2026-03-10");
    }
    static string Str(JsonElement value,string key)=>value.GetProperty(key).GetString()??throw new InvalidDataException("Unvollständige GitHub-Antwort.");
    static string Hash(byte[] value)=>Convert.ToHexStringLower(SHA256.HashData(value));
    internal static void ValidateAsset(string workspace,GitHubReleaseAsset asset)
    {
        if(!Regex.IsMatch(asset.Name,@"\A(?:LPLK_[A-Za-z]+|AddonsManager|EnvironmentCheck|SerververwaltungBridge)\.zip\z")||asset.Path!="Addons/"+asset.Name[..^4]+"/Build/"+asset.Name||asset.Bytes is <=0 or >50*1024*1024||!Regex.IsMatch(asset.Sha256,@"\A[0-9a-f]{64}\z"))throw new InvalidDataException("Unzulässiges Releasepaket.");
        string path=Path.Combine(workspace,asset.Path);GitHubImport.NoLinks(path,workspace);
        byte[] bytes=File.ReadAllBytes(path);if(bytes.LongLength!=asset.Bytes||Hash(bytes)!=asset.Sha256)throw new InvalidDataException("Releasepaket nach Prüfung verändert.");
        using var zip=new ZipArchive(new MemoryStream(bytes));string id=asset.Name[..^4];
        if(zip.GetEntry(id+"/"+id+".dll") is null)throw new InvalidDataException("Plugin-DLL fehlt.");
        foreach(var entry in zip.Entries)
            if(!entry.FullName.StartsWith(id+"/",StringComparison.Ordinal)||entry.FullName.Contains("..")||entry.FullName.Contains('\\')||Regex.IsMatch(entry.FullName,@"(^|/)(private|data|backups|state|logs)(/|$)|\.(sqlite3?|db|pem|key|pfx)$",RegexOptions.IgnoreCase))throw new InvalidDataException("Betriebsdaten oder ungültiger Pfad im Paket.");
    }
    public async Task<object> RunAsync(string expectedPlanHash,bool publish,CancellationToken ct=default)
    {
        string planPath=Path.Combine(workspace,"state/github-release-plan.json");byte[] planBytes=File.ReadAllBytes(planPath);
        string planHash=Hash(planBytes);if(!planHash.Equals(expectedPlanHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Geprüfter Releaseplan wurde geändert.");
        var plan=JsonSerializer.Deserialize<GitHubReleasePlan>(planBytes,Program.JsonOptions)??throw new InvalidDataException("Releaseplan fehlt.");
        if(!Regex.IsMatch(plan.Tag,@"\Atest-\d{4}\.\d{2}\.\d{2}-menus\z")||!Regex.IsMatch(plan.ExpectedHead,@"\A[0-9a-f]{40}\z")||plan.Assets.Length!=21||plan.Assets.Select(a=>a.Name).Distinct().Count()!=21||plan.Body.Length is <50 or >20000)throw new InvalidDataException("Releaseumfang ungültig.");
        foreach(var asset in plan.Assets)ValidateAsset(workspace,asset);
        byte[] manifestBytes=File.ReadAllBytes(Path.Combine(workspace,"state/github-release-manifest.json"));
        if(!Hash(manifestBytes).Equals(plan.ManifestSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Quellmanifest verändert.");
        var manifest=JsonSerializer.Deserialize<GitHubImportManifest>(manifestBytes,Program.JsonOptions)!;
        if(manifest.Root!="state/github-release-source"||manifest.Owner!="Kampfhamstr"||manifest.Name!="LPLK-Serververwaltung"||manifest.AccountId!=201108322||connection.AccountId!=manifest.AccountId)throw new InvalidDataException("Falsches Releaseziel.");
        var files=GitHubImport.LoadPayload(workspace,manifest);
        if(files.Any(f=>f.Text?.Contains(connection.Token,StringComparison.Ordinal)==true)||plan.Body.Contains(connection.Token,StringComparison.Ordinal))throw new InvalidDataException("Zugangsdaten im Export erkannt.");
        using(var repo=await Send(HttpMethod.Get,Repo,null,ct))CheckRepo(repo.RootElement);
        using(var user=await Send(HttpMethod.Get,"user",null,ct))if(user.RootElement.GetProperty("id").GetInt64()!=manifest.AccountId)throw new InvalidDataException("GitHub-Konto geändert.");
        string receiptPath=Path.Combine(workspace,"state/github-release-receipt.json");
        var receipt=File.Exists(receiptPath)?JsonSerializer.Deserialize<GitHubReleaseReceipt>(File.ReadAllText(receiptPath),Program.JsonOptions)!:new(planHash);
        if(receipt.PlanHash!=planHash)throw new InvalidDataException("Anderer Releasevorgang vorhanden.");
        using var reference=await Send(HttpMethod.Get,Repo+"/git/ref/heads/main",null,ct);string head=Str(reference.RootElement.GetProperty("object"),"sha");
        if(head!=plan.ExpectedHead&&head!=receipt.Commit)throw new InvalidOperationException("Repository wurde zwischenzeitlich verändert; keine Dateien überschrieben.");
        using var listing=await Send(HttpMethod.Get,Repo+"/releases?per_page=100",null,ct);
        var existing=listing.RootElement.EnumerateArray().Where(r=>Str(r,"tag_name")==plan.Tag).ToArray();
        if(existing.Length>1||existing.Length==1&&receipt.ReleaseId!=existing[0].GetProperty("id").GetInt64())throw new InvalidOperationException("Release existiert ohne passenden Vorgangsnachweis.");
        if(!publish)return new{repository=Web,head,files=files.Length,packages=plan.Assets.Length,bytes=plan.Assets.Sum(a=>a.Bytes),existingReleases=listing.RootElement.GetArrayLength(),tag=plan.Tag,localValidation=true};
        if(receipt.Commit is null)
        {
            using var parent=await Send(HttpMethod.Get,Repo+"/git/commits/"+head,null,ct);string baseTree=Str(parent.RootElement.GetProperty("tree"),"sha");
            using var remoteTree=await Send(HttpMethod.Get,Repo+"/git/trees/"+baseTree+"?recursive=1",null,ct);
            if(remoteTree.RootElement.GetProperty("truncated").GetBoolean())throw new InvalidDataException("Dateibaum unvollständig.");
            var known=remoteTree.RootElement.GetProperty("tree").EnumerateArray().ToDictionary(e=>Str(e,"path"),e=>Str(e,"sha"));
            var changes=new List<object>();
            foreach(var file in files)
            {
                if(known.TryGetValue(file.Path,out var sha)&&sha==file.GitSha)continue;
                if(file.Text is null)throw new InvalidOperationException("Neue Binärquellen benötigen eine gesonderte Exportprüfung.");
                changes.Add(new{path=file.Path,mode="100644",type="blob",content=file.Text});
            }
            using var tree=await Send(HttpMethod.Post,Repo+"/git/trees",new{base_tree=baseTree,tree=changes},ct);string treeSha=Str(tree.RootElement,"sha");
            await VerifySources(treeSha,files,ct);
            using var commit=await Send(HttpMethod.Post,Repo+"/git/commits",new{message="Simplify player/admin menus and provide verified addon release downloads",tree=treeSha,parents=new[]{head}},ct);
            receipt=receipt with{Commit=Str(commit.RootElement,"sha")};Save();
        }
        if(head!=receipt.Commit)
        {
            using var latest=await Send(HttpMethod.Get,Repo+"/git/ref/heads/main",null,ct);
            if(Str(latest.RootElement.GetProperty("object"),"sha")!=head)throw new InvalidOperationException("Branch inzwischen geändert.");
            using var update=await Send(HttpMethod.Patch,Repo+"/git/refs/heads/main",new{sha=receipt.Commit,force=false},ct);
        }
        if(receipt.ReleaseId is null)
        {
            using var release=await Send(HttpMethod.Post,Repo+"/releases",new{tag_name=plan.Tag,target_commitish=receipt.Commit,name="LPLK Addons – Menüs (Testausgabe 14.10.2026)",body=plan.Body,draft=true,prerelease=true,make_latest="false"},ct);
            receipt=receipt with{ReleaseId=release.RootElement.GetProperty("id").GetInt64()};Save();
        }
        long releaseId=receipt.ReleaseId.Value;
        using var assets=await Send(HttpMethod.Get,Repo+"/releases/"+releaseId+"/assets?per_page=100",null,ct);
        foreach(var asset in plan.Assets)
        {
            var matches=assets.RootElement.EnumerateArray().Where(a=>Str(a,"name")==asset.Name).ToArray();JsonElement item;
            if(matches.Length==0)
            {
                byte[] bytes=File.ReadAllBytes(Path.Combine(workspace,asset.Path));if(Hash(bytes)!=asset.Sha256)throw new InvalidDataException("Paket inzwischen geändert.");
                using var body=new ByteArrayContent(bytes);body.Headers.ContentType=new("application/zip");
                using var request=new HttpRequestMessage(HttpMethod.Post,"https://uploads.github.com/"+Repo+"/releases/"+releaseId+"/assets?name="+Uri.EscapeDataString(asset.Name)){Content=body};
                using var response=await http.SendAsync(request,ct);Ensure(response);
                using var uploaded=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));item=uploaded.RootElement.Clone();
                await Task.Delay(1100,ct);
            }
            else if(matches.Length==1)item=matches[0];else throw new InvalidDataException("Doppelte Releaseanhänge.");
            if(Str(item,"state")!="uploaded"||item.GetProperty("size").GetInt64()!=asset.Bytes||Str(item,"digest")!="sha256:"+asset.Sha256)throw new InvalidDataException("Hochgeladener Anhang nicht identisch.");
            await VerifyDownload(item.GetProperty("id").GetInt64(),asset,ct);
        }
        using(var complete=await Send(HttpMethod.Get,Repo+"/releases/"+releaseId+"/assets?per_page=100",null,ct))
            if(complete.RootElement.GetArrayLength()!=plan.Assets.Length)throw new InvalidDataException("Unvollständige oder zusätzliche Anhänge.");
        using(var repo=await Send(HttpMethod.Get,Repo,null,ct))CheckRepo(repo.RootElement);
        using var published=await Send(HttpMethod.Patch,Repo+"/releases/"+releaseId,new{draft=false,prerelease=true,make_latest="false"},ct);
        using var final=await Send(HttpMethod.Get,Repo+"/releases/"+releaseId,null,ct);
        if(final.RootElement.GetProperty("draft").GetBoolean()||!final.RootElement.GetProperty("prerelease").GetBoolean())throw new InvalidDataException("Veröffentlichungsstatus nicht bestätigt.");
        using var finalCommit=await Send(HttpMethod.Get,Repo+"/git/commits/"+receipt.Commit,null,ct);await VerifySources(Str(finalCommit.RootElement.GetProperty("tree"),"sha"),files,ct);
        receipt=receipt with{Verified=true};Save();
        return new{url=Web+"/releases/tag/"+plan.Tag,commit=receipt.Commit,packages=plan.Assets.Length,privateRepository=true,prerelease=true,downloadHashesVerified=true};
        void Save()=>File.WriteAllText(receiptPath,JsonSerializer.Serialize(receipt,Program.JsonOptions));
    }
    async Task VerifySources(string tree,GitHubImport.Payload[] files,CancellationToken ct)
    {
        using var remote=await Send(HttpMethod.Get,Repo+"/git/trees/"+tree+"?recursive=1",null,ct);if(remote.RootElement.GetProperty("truncated").GetBoolean())throw new InvalidDataException("Dateibaum unvollständig.");
        var entries=remote.RootElement.GetProperty("tree").EnumerateArray().ToDictionary(e=>Str(e,"path"),e=>Str(e,"sha"));
        if(files.Any(f=>!entries.TryGetValue(f.Path,out var sha)||sha!=f.GitSha))throw new InvalidDataException("Quellcodeprüfung fehlgeschlagen.");
    }
    async Task VerifyDownload(long id,GitHubReleaseAsset asset,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,Repo+"/releases/assets/"+id);request.Headers.Accept.Clear();request.Headers.Accept.ParseAdd("application/octet-stream");
        using var response=await http.SendAsync(request,ct);byte[] data;
        if(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect)
        {
            var location=response.Headers.Location;
            if(location is null||location.Scheme!="https"||location.Host!="release-assets.githubusercontent.com")throw new InvalidDataException("Unerwartetes Downloadziel.");
            // Signed asset URLs never receive the GitHub token.
            using var download=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false});
            using var body=await download.GetAsync(location,ct);Ensure(body);data=await body.Content.ReadAsByteArrayAsync(ct);
        }
        else {Ensure(response);data=await response.Content.ReadAsByteArrayAsync(ct);}
        if(data.LongLength!=asset.Bytes||Hash(data)!=asset.Sha256)throw new InvalidDataException("Heruntergeladene ZIP stimmt nicht überein.");
    }
    static void CheckRepo(JsonElement repo)
    {
        if(repo.GetProperty("id").GetInt64()!=1419357077||!repo.GetProperty("private").GetBoolean()||Str(repo,"full_name")!="Kampfhamstr/LPLK-Serververwaltung"||repo.GetProperty("archived").GetBoolean())throw new InvalidDataException("Falsches oder nicht privates Repository.");
    }
    static void Ensure(HttpResponseMessage response){if(!response.IsSuccessStatusCode)throw new InvalidOperationException("GitHub-Auslieferung: HTTP "+(int)response.StatusCode+"; Antwort und Zugangsdaten werden nicht ausgegeben.");}
    async Task<JsonDocument> Send(HttpMethod method,string path,object? body,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(method,path);if(body is not null)request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request,ct);Ensure(response);return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
    }
    public void Dispose()=>http.Dispose();
}

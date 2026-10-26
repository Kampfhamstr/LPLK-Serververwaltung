using System.Text.Json;
using System.Text.RegularExpressions;
using LPLK.Common;
namespace SerververwaltungBridge;
public sealed partial class Plugin
{
 private readonly HashSet<string> pluginMaintenance=[];
 private Task<T> OnEngine<T>(Func<T> action)
 {
  var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
  Core.Scheduler.NextWorldUpdate(()=>{try{if(_unloading)throw new InvalidOperationException("Bridge wird entladen");completion.SetResult(action());}catch(Exception e){completion.SetException(e);}});
  return completion.Task;
 }
 private void QueuePluginMaintenance(BridgeCommand command)
 {
  if(command.Fleet is not null)throw new InvalidOperationException("Verbundaufträge sind abgeschaltet");
  var payload=command.Payload??new Dictionary<string,JsonElement>();
  string id=ValidatePluginId(GetString(payload,"plugin_id",""));
  if(id.Equals("lplk.serververwaltung.bridge",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Bridge kann sich nicht selbst austauschen");
  if(!pluginMaintenance.Add(id))throw new InvalidOperationException("Pluginwartung läuft bereits");
  string? raw=Core.PluginManager.GetPluginPath(id);string version=Core.PluginManager.GetPluginMetadata(id)?.Version??"";
  string root=Path.GetFullPath(Path.Combine(LPLK.Common.RoundFileStore.GamePath(Core),"addons","swiftlys2","plugins"))+Path.DirectorySeparatorChar;
  RoundFileStore.AfterRound(()=>_=MaintainPlugin(command,id,raw,version,root,payload));
 }
 private async Task MaintainPlugin(BridgeCommand command,string id,string? raw,string version,string root,Dictionary<string,JsonElement> payload)
 {
  string? backup=null,path=null,temp=null;bool unloaded=false,replaced=false;Exception? failure=null;
  try
  {
   if(string.IsNullOrWhiteSpace(raw))throw new IOException("Pluginpfad fehlt");
   path=Path.GetFullPath(System.IO.File.Exists(raw)?Path.GetDirectoryName(raw)!:raw);
   if(!(path+Path.DirectorySeparatorChar).StartsWith(root,StringComparison.Ordinal)||!System.IO.Directory.Exists(path)||new System.IO.DirectoryInfo(path).LinkTarget is not null)throw new IOException("Ungültiger Pluginpfad");
   string? source=null;
   if(command.Action=="plugin_update")
   {
    Uri uri=ValidateGitHubReleaseUrl(GetString(payload,"download_url",""));string extension=Path.GetExtension(GetString(payload,"asset_name","")).ToLowerInvariant();
    if(extension is not ".zip" and not ".rar")throw new IOException("Nur ZIP- und RAR-Pakete erlaubt");
    temp=Path.Combine(_root,"plugin_update_tmp",Guid.NewGuid().ToString("N"));string archive=Path.Combine(temp,"release"+extension),extract=Path.Combine(temp,"extract");
    source=RoundFileStore.Physical(()=>{System.IO.Directory.CreateDirectory(extract);DownloadReleaseArchive(uri,archive,GetString(payload,"expected_sha256",""));SafeExtractArchive(archive,extract);return ResolveInstallSource(extract,GetString(payload,"install_subpath",""));});
    if(!System.IO.Directory.EnumerateFiles(source,"*.dll",SearchOption.AllDirectories).Any())throw new IOException("Plugin-DLL fehlt");
   }
   else
   {
    backup=GetString(payload,"backup_id","");if(!Regex.IsMatch(backup,@"^[0-9]{8}T[0-9]{6}Z-[a-f0-9]{8}$",RegexOptions.IgnoreCase))throw new IOException("Ungültige Rückfallkennung");
    if(!System.IO.Directory.Exists(Path.Combine(_pluginBackupsRoot,SafePluginDirectory(id),backup)))throw new IOException("Rückfall fehlt");
   }
   if(!await OnEngine(()=>Core.PluginManager.UnloadPlugin(id,true)))throw new IOException("Plugin konnte nicht entladen werden");unloaded=true;
   if(command.Action=="plugin_update")
   {
    backup=DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")+"-"+Guid.NewGuid().ToString("N")[..8];string destination=Path.Combine(_pluginBackupsRoot,SafePluginDirectory(id),backup);
    RoundFileStore.Physical(()=>{CopyDirectory(path,destination);AtomicWrite(Path.Combine(destination,"_serververwaltung_backup.json"),JsonSerializer.Serialize(new{plugin_id=id,version,created_at=DateTimeOffset.UtcNow},_json));return true;});
    replaced=true;RoundFileStore.Physical(()=>{ClearDirectory(path);CopyDirectory(source!,path);return true;});
   }
   else{replaced=true;RoundFileStore.Physical(()=>{RestorePluginBackup(id,backup!,path);return true;});}
   if(!await OnEngine(()=>Core.PluginManager.LoadPlugin(id,true)))throw new IOException("Plugin konnte nicht geladen werden");unloaded=false;
   string expected=GetString(payload,"expected_version","");
   if(command.Action=="plugin_update"&&expected.Length>0&&!await OnEngine(()=>NormalizeVersion(Core.PluginManager.GetPluginMetadata(id)?.Version??"")==NormalizeVersion(expected)))throw new IOException("Geladene Version stimmt nicht");
  }
  catch(Exception e)
  {
   failure=e;
   if(unloaded&&!replaced){try{await OnEngine(()=>Core.PluginManager.LoadPlugin(id,true));}catch(Exception reload){failure=new AggregateException(e,reload);}}
   else if((unloaded||replaced)&&backup is not null&&path is not null)
   {
    try{if(!unloaded)await OnEngine(()=>Core.PluginManager.UnloadPlugin(id,true));RoundFileStore.Physical(()=>{RestorePluginBackup(id,backup,path);return true;});if(!await OnEngine(()=>Core.PluginManager.LoadPlugin(id,true)))throw new IOException("Rückfall konnte nicht geladen werden");}
    catch(Exception rollback){failure=new AggregateException(e,rollback);}
   }
  }
  finally
  {
   if(temp is not null)try{string checkedRoot=Path.GetFullPath(Path.Combine(_root,"plugin_update_tmp"))+Path.DirectorySeparatorChar;if(Path.GetFullPath(temp).StartsWith(checkedRoot,StringComparison.Ordinal)&&System.IO.Directory.Exists(temp))System.IO.Directory.Delete(temp,true);}catch{}
   try{await OnEngine(()=>{pluginMaintenance.Remove(id);RefreshPluginInventory(true);WriteResult(command.Id,failure is null,failure?.Message??"Pluginwartung abgeschlossen");AddFastLiveResult(command.Id,failure is null,failure?.Message??"Pluginwartung abgeschlossen");RecordAdminAction(command,failure is null,failure?.Message,true);return true;});}catch{}
  }
 }
}

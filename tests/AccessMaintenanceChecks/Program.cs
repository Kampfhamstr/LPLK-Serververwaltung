using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LPLK_Whitelist;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
T Field<T>(object p,string n)=>(T)p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
void Set(object p,string n,object v)=>p.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(p,v);
var w=new MenuWorld{DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"AccessMaintenanceChecks","test-data"),IncludePlayer=true};
Directory.CreateDirectory(w.DataDirectory);
string cfg=Path.Combine(w.DataDirectory,"addons","swiftlys2","configs","plugins","LPLK_Whitelist","config.jsonc"),list=Path.Combine(w.DataDirectory,"whitelist.txt"),status=Path.Combine(w.DataDirectory,"status.json");
Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
string player="76561198000000000",extra="76561198000000001";int kicks=0;var originalPlayer=w.Player;
w.Player=Proxy.Make<IPlayer>((m,a)=>{if(m.Name=="Kick"){kicks++;return null;}return m.Invoke(originalPlayer,a);});
void Save(AccessSettings s){var root=JsonSerializer.SerializeToNode(new{Whitelist=s})!;root["OtherSection"]=new JsonObject{["keep"]=42};root["Whitelist"]!["AddCommand"]="custom_wl";root["Whitelist"]!["RemoveCommand"]="custom_uwl";root["Whitelist"]!["FutureFlag"]=17;File.WriteAllText(cfg,root.ToJsonString());}
Save(new());File.WriteAllText(list,$"# members\n{player}\n");w.Permissions.Add("lplk.admin");
var plugin=new Plugin(w.Core);plugin.Load(false);
AccessSettings Settings()=>Field<AccessSettings>(plugin,"settings");HashSet<string> Ids()=>Field<HashSet<string>>(plugin,"ids");
void Refresh(){Set(plugin,"nextPoll",0L);plugin.Tick();}
async Task Click(string text)=>await w.Click(w.FindExact(text));
ICommandContext Command(string[] args,bool console=false)=>Proxy.Make<ICommandContext>((m,a)=>m.Name switch{"get_Sender"=>console?null:w.Player,"get_IsSentByPlayer"=>!console,"get_Args"=>args,"Reply"=>Reply((string)a![0]!),_=>Proxy.Empty(m.ReturnType)});
object? Reply(string s){w.Messages.Add(s);return null;}
Check(kicks==0&&Ids().SetEquals([player]),"Allowed player survives initial enforcement");
DateTime stamp=File.GetLastWriteTimeUtc(cfg);Save(Settings() with{Mode=2});File.SetLastWriteTimeUtc(cfg,stamp);Refresh();Check(Settings().Mode==2&&kicks==1,"Same timestamp config detected and blacklist enforced");
int priorKicks=kicks;File.WriteAllText(cfg,"\n"+File.ReadAllText(cfg));File.AppendAllText(list,"// formatting only\n");Refresh();Check(kicks==priorKicks,"Formatting does not repeat enforcement");
Save(Settings() with{Mode=1});Refresh();stamp=File.GetLastWriteTimeUtc(list);File.WriteAllText(list,extra+"\n");File.SetLastWriteTimeUtc(list,stamp);Refresh();Check(Ids().SetEquals([extra])&&kicks==priorKicks+1,"Same timestamp list detected and enforced");
File.WriteAllText(list,player+"\n");Refresh();
plugin.Status(w.Context);Save(Settings() with{AdminBypass=true,KickMessage="New external message",PermissionForCommands="custom.manage"});await Click("Aktiv: Ja");
Check(!Settings().Enabled&&Settings().AdminBypass&&Settings().KickMessage=="New external message"&&Settings().PermissionForCommands=="custom.manage","Menu derives edit from latest values");
var doc=JsonNode.Parse(File.ReadAllText(cfg))!;Check((int)doc["OtherSection"]!["keep"]! ==42&&(int)doc["Whitelist"]!["FutureFlag"]! ==17&&(string)doc["Whitelist"]!["AddCommand"]! =="custom_wl"&&(string)doc["Whitelist"]!["RemoveCommand"]! =="custom_uwl","Unknown and legacy fields retained");
string good=File.ReadAllText(cfg),goodList=File.ReadAllText(list);var previous=Settings();
foreach(string bad in new[]{"null","[]","{\"Whitelist\":null}","{\"Whitelist\":{\"Mode\":3}}","{\"Whitelist\":{\"Mode\":1,\"mode\":2}}","{\"Whitelist\":{},\"whitelist\":{}}","{\"Whitelist\":{\"KickMessage\":null}}",new string(' ',AccessFiles.MaxBytes+1)})
{File.WriteAllText(cfg,bad);Refresh();Check(Settings()==previous&&Ids().SetEquals([player])&&Field<string?>(plugin,"reloadError") is not null,"Invalid file preserves coherent access snapshot");await Click("Aktiv: Nein");Check(File.ReadAllText(cfg)==bad&&Settings()==previous,"Menu cannot overwrite invalid config");}
File.WriteAllText(cfg,good);File.WriteAllText(list,"invalid-id");Refresh();await Click("Aktiv: Nein");Check(File.ReadAllText(cfg)==good&&File.ReadAllText(list)=="invalid-id"&&Settings()==previous,"Invalid list blocks rule edits too");
Save(previous with{Mode=2});Refresh();Check(Settings()==previous,"Valid rules with invalid list are not partially applied");
File.WriteAllText(cfg,good);File.WriteAllText(list,goodList);Refresh();Check(Field<string?>(plugin,"reloadError") is null,"Corrected pair recovers");
File.Delete(list);Refresh();Check(!File.Exists(list)&&Settings()==previous,"Missing list retained as error, not replaced");File.WriteAllText(list,goodList);Refresh();
// Revocation in the current config must take effect before an old menu/command mutates files.
w.Permissions.Clear();w.Permissions.Add("custom.manage");plugin.Status(w.Context);Save(Settings() with{PermissionForCommands="new.manage"});string revoked=File.ReadAllText(cfg);await Click("Aktiv: Nein");Check(File.ReadAllText(cfg)==revoked&&!Settings().Enabled,"Configuration permission revocation blocks stale menu");
Save(Settings() with{PermissionForCommands="custom.manage"});Refresh();Save(Settings() with{PermissionForCommands="new.manage"});plugin.Add(Command([extra]));Check(File.ReadAllText(list)==goodList,"Config permission revocation blocks stale command");
w.Permissions.Add("lplk.admin");plugin.Add(Command([extra]));Check(Ids().SetEquals([player,extra]),"Authorized add merges current disk list");plugin.Add(Command([extra]));Check(Ids().Count==2,"Duplicate add idempotent");
plugin.Remove(Command([extra],true));Check(Ids().SetEquals([player]),"Existing console removal retained");plugin.Add(Command(["invalid"]));Check(Ids().Count==1,"Invalid ID rejected");
plugin.Status(w.Context);await Click("Einträge");await Click("Hinzufügen");var input=(InputMenuOption)w.Current!.Options[0];Check(!input.SetValue(w.Player,"bad"),"Input validates SteamID");Check(!input.SetValue(w.Player,extra+"1"),"Overlong SteamID is rejected instead of truncated into another account");Check(Ids().Count==1,"Overlong input never adds an entry");w.Permissions.Clear();input.SetValue(w.Player,extra);Check(Ids().Count==1,"Revoked input blocked");w.Permissions.Add("lplk.admin");w.Session++;input.SetValue(w.Player,extra);Check(Ids().Count==1,"Reconnected slot cannot reuse input");
plugin.Status(w.Context);await Click("Einträge");await Click("Hinzufügen");((InputMenuOption)w.Current!.Options[0]).SetValue(w.Player,extra);Check(Ids().Contains(extra),"Current input adds entry");
var many=Enumerable.Range(0,9).Select(i=>(76561198000000000UL+(ulong)i).ToString()).ToArray();File.WriteAllText(list,string.Join('\n',many));Refresh();plugin.Status(w.Context);await Click("Einträge");await Click("Liste (9)");
var seen=new HashSet<string>();for(int page=0;page<5;page++){foreach(var id in many)if(w.Labels.Any(s=>s.Contains(id)))seen.Add(id);if(!w.Labels.Any(l=>l.Contains("Nächste Seite")))break;await Click("Nächste Seite");}Check(seen.SetEquals(many),"Paged list reaches every entry");await Click(many[6]);await Click("Eintrag entfernen");Check(!Ids().Contains(many[6])&&Ids().Count==8,"Paged remove retains other members");
foreach(var menu in w.AllMenus){Check(menu.Configuration.MaxVisibleItems==5,"At most five menu rows");foreach(var back in menu.Options.Where(o=>o.Text.EndsWith("Zurück</font>")))Check(!System.Text.RegularExpressions.Regex.IsMatch(back.Text,@"\d+[.)]"),"Back unnumbered");}
// Expected-content checks reject intervening edits to either file (not an OS-level CAS).
var snap=AccessFiles.Read(cfg,list);File.AppendAllText(list,"\n");bool conflict=false;try{AccessFiles.WriteSettings(cfg,list,snap,snap.Settings with{Mode=2});}catch(IOException){conflict=true;}Check(conflict,"Concurrent list edit blocks settings save");
snap=AccessFiles.Read(cfg,list);File.AppendAllText(cfg,"\n");conflict=false;try{AccessFiles.WriteIds(cfg,list,snap,[player]);}catch(IOException){conflict=true;}Check(conflict&&!Directory.GetFiles(w.DataDirectory,"*.tmp").Any(),"Concurrent config edit blocks list save and cleans temporary file");
File.Delete(status);Directory.CreateDirectory(status);Save(Settings() with{Enabled=true,Mode=2,AdminBypass=false});priorKicks=kicks;Refresh();Check(kicks==priorKicks+1&&Field<bool>(plugin,"statusWriteFailed"),"Status I/O failure cannot suppress access enforcement");Directory.Delete(status);Refresh();Check(File.Exists(status)&&!Field<bool>(plugin,"statusWriteFailed"),"Status persistence recovers");
// Flat legacy shape/casing and comments remain accepted and extra fields survive edits.
File.WriteAllText(cfg,"{/*note*/\"enabled\":false,\"mode\":1,\"Other\":{\"keep\":7},}");Refresh();snap=AccessFiles.Read(cfg,list);AccessFiles.WriteSettings(cfg,list,snap,snap.Settings with{AdminBypass=true});doc=JsonNode.Parse(File.ReadAllText(cfg))!;Check(doc["Whitelist"] is null&&doc["enabled"] is not null&&(int)doc["Other"]!["keep"]! ==7,"Flat shape, casing and extra fields preserved");
plugin.Unload();Check(w.EntityAccesses==0,"No native entities used");
Console.WriteLine($"AccessMaintenanceChecks: {checks} passed; real menu, input, coherent files, permission revocation and enforcement paths with fake players. Client joins remain deferred.");

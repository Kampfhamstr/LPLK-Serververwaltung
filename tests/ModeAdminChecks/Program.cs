using LPLK_Admin;
using SwiftlyS2.Shared.Players;
var root=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"ModeAdminChecks/current");Directory.CreateDirectory(root);
var world=new MenuWorld{DataDirectory=root};world.Plugins.AddRange(["LPLK_Surf","LPLK_Zombie"]);
bool surf=true,zombie=true;var calls=new List<string>();int checks=0;
void Check(bool okay,string name){checks++;if(!okay)throw new Exception(name);}
foreach(string mode in new[]{"Surf","Zombie"})world.Interfaces.AddSharedInterface<IReadOnlyDictionary<string,Delegate>,Dictionary<string,Delegate>>("LPLK.Player."+mode+".v1",new()
{
 ["allowed"]=(Func<IPlayer,string,bool>)((p,_)=>p.IsValid&&(mode=="Surf"?surf:zombie)),
 ["open"]=(Action<IPlayer,string>)((p,page)=>calls.Add(mode+":"+page))
});
var plugin=new Plugin(world.Core);plugin.UseSharedInterface(world.Interfaces);plugin.Load(false);
plugin.PlayerMenuCommand(world.Context);Check(world.Labels[0].Contains("Mapmenü")&&world.Labels.Any(l=>l.Contains("Surf / KZ"))&&world.Labels.Any(l=>l.Contains("Zombie")),"Mode entries and top map menu");
await world.Click("Mapmenü");Check(calls.SequenceEqual(["Surf:maps"]),"Map menu uses Surf provider");
plugin.PlayerMenuCommand(world.Context);await world.Click("Surf / KZ");Check(calls[^1]=="Surf:main","Surf menu opens");
plugin.PlayerMenuCommand(world.Context);await world.Click("Zombie");Check(calls[^1]=="Zombie:main","Zombie menu opens");
plugin.PlayerMenuCommand(world.Context);var button=world.Find("Surf / KZ");surf=false;int count=calls.Count;await world.Click(button);Check(calls.Count==count,"Mode revoked after menu opening");
plugin.PlayerMenuCommand(world.Context);Check(!world.Labels.Any(l=>l.Contains("Surf / KZ"))&&!world.Labels.Any(l=>l.Contains("Mapmenü")),"Disabled Surf cannot fall through to unrelated map commands");
surf=true;plugin.PlayerMenuCommand(world.Context);button=world.Find("Surf / KZ");world.FailedPlugins.Add("LPLK_Surf");await world.Click(button);Check(calls.Count==count,"Unloaded mode rejected");world.FailedPlugins.Clear();
plugin.PlayerMenuCommand(world.Context);button=world.Find("Zombie");world.Session++;await world.Click(button);Check(calls.Count==count,"Reconnected player cannot use old mode menu");
plugin.PlayerMenuCommand(world.Context);button=world.Find("Zombie");plugin.Unload();await world.Click(button);Check(calls.Count==count,"Admin unload rejects old mode action");
Check(world.Commands.Count==0,"Mode menus never promote commands to console");
Console.WriteLine($"ModeAdminChecks: {checks} passed.");

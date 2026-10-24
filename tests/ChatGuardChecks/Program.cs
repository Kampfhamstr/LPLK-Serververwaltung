using LPLK_ChatGuard;
using System.Text.Json;

int count=0;
void Check(bool result,string name){if(!result)throw new Exception(name);count++;}
void Ad(string text,bool name,bool expected,AntiAdConfig? settings=null,GlobalProtectionSnapshot? snapshot=null)
    =>Check(ChatGuardPolicy.Match(text,name,settings??AntiAdConfig.CreateDefault(),snapshot??new(),out _)==expected,$"Mismatch: name={name}, {text}");
foreach(string domain in new[]{"cs2commends.com","jaegerservice.xyz","sinlyxe.cc","clash.gg","steamlvls.com","steamlevels.com"})
    foreach(bool name in new[]{false,true})Ad(domain,name,true);
foreach(string text in new[]{"CLASH[.]GG","c l a s h . gg","clash\u200b.gg","clash dot gg","ｃｌａｓｈ．ｇｇ","clash(.)gg"})Ad(text,false,true);
foreach(string text in new[]{"Hallo zusammen!","gg wp","lplk.de","https://verwaltung.lplk.de/servers/7","example.com","127.0.0.1:27015","ÄÖÜß",""," ","free cases"})Ad(text,false,false);
Ad("promo code https://evilsite.com",false,true);
Ad("free cases lplk.de",false,false);
Ad("promo code https://verwaltung.lplk.de/servers/7",false,false);
Ad("promo code lplk.de https://evilsite.com",false,true);
Ad("lplk.de clash.gg",false,true);
Ad("discord.gg/test",false,true);
Ad("discord.gg/test",true,true); // Existing keyword list includes discord.gg.
Ad("unknown.com",true,false);
Ad("cheap steam levels unknown.com",true,true);
Ad("free cs2 commends",true,true);
var allow=AntiAdConfig.CreateDefault();allow.AllowedDomains.Add("clash.gg");Ad("clash.gg",false,true,allow);
allow.AllowedDomains.Add("discord.gg");Ad("discord.gg/test",false,false,allow);
Ad("free cases lplk.de.attacker.com",false,true);
Ad("play safely",false,true,snapshot:new(){BlockedPhrases=["play safely"]});
Ad("newblock.site",false,true,snapshot:new(){Domains=["newblock.site"]});
Ad("dealword newsite.com",false,true,snapshot:new(){MarketingKeywords=["dealword"]});
Ad("promo code newsite.com",false,false,snapshot:new(){AllowedDomains=["newsite.com"]});
foreach(var (role,expected) in new[]{("spieler",1),("vip",2),("member",3),("admin",4),("founder",5),("unknown",1),(" ADMIN ",4)})Check(ChatGuardPolicy.GameRoleLevel(role)==expected,"Role "+role);
Check(ChatGuardPolicy.ResolveHighestGameRole(["vip","founder","spieler"])=="founder","Highest role");
Check(ChatGuardPolicy.ResolveHighestGameRole([])=="spieler","Missing role");
Check(ChatGuardPolicy.CanApplyDeferred(true,true,true,false),"Current connection kick");
Check(!ChatGuardPolicy.CanApplyDeferred(true,true,false,false),"Slot reuse kick suppressed");
Check(!ChatGuardPolicy.CanApplyDeferred(true,true,true,true),"Promotion before queued kick");
Check(!ChatGuardPolicy.CanApplyDeferred(false,true,true,false),"Unloaded plugin queued kick");
Check(!ChatGuardPolicy.CanApplyDeferred(true,false,true,false),"Disabled plugin queued kick");
var now=DateTimeOffset.Parse("2026-10-11T18:00:00Z");var defaults=AntiAdConfig.CreateDefault();
var first=ChatGuardPolicy.RegisterViolation(null,now,10);
Check(first.Count==1&&ChatGuardPolicy.ViolationAction(first.Count,defaults)=="warn","First warning");
var second=ChatGuardPolicy.RegisterViolation(first,now.AddMinutes(1),10);
Check(second.Count==2&&ChatGuardPolicy.ViolationAction(second.Count,defaults)=="gag","Second hit gag");
var third=ChatGuardPolicy.RegisterViolation(second,now.AddMinutes(2),10);
Check(third.Count==3&&ChatGuardPolicy.ViolationAction(third.Count,defaults)=="kick","Third hit kick");
Check(ChatGuardPolicy.RegisterViolation(third,now.AddMinutes(10),10).Count==4,"Window boundary inclusive");
Check(ChatGuardPolicy.RegisterViolation(third,now.AddMinutes(10).AddTicks(1),10).Count==1,"Expired window reset");
Check(first.Count==1&&second.Count==2,"Earlier observations not mutated");
defaults.GagAtViolation=4;defaults.KickAtViolation=6;
Check(ChatGuardPolicy.ViolationAction(3,defaults)=="warn"&&ChatGuardPolicy.ViolationAction(4,defaults)=="gag"&&ChatGuardPolicy.ViolationAction(6,defaults)=="kick","Custom sanction thresholds");
ChatGuardPolicy.Validate(AntiAdConfig.CreateDefault());count++;
foreach(Action<AntiAdConfig> edit in new Action<AntiAdConfig>[] {c=>c.GagAtViolation=0,c=>c.KickAtViolation=2,c=>c.GagMinutes=0,c=>c.RepeatWindowMinutes=1441,c=>c.ElevatedRoleWarningCooldownSeconds=0,c=>c.CensoredPlayerName="",c=>c.KickReason="line\nbreak",c=>c.AllowedDomains=null!,c=>c.MarketingKeywords=[new string('a',201)]})
{
    var c=AntiAdConfig.CreateDefault();edit(c);bool rejected=false;try{ChatGuardPolicy.Validate(c);}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid config rejected");
}
var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
string workspace=args.Length>0?args[0]:Directory.GetCurrentDirectory();
var seed=JsonSerializer.Deserialize<AntiAdConfig>(File.ReadAllText(Path.Combine(workspace,"Addons/LPLK_ChatGuard/Rohskripte/resources/settings.seed.json")),options)!;
ChatGuardPolicy.Validate(seed);Check(seed.RepeatWindowMinutes==10&&seed.GagAtViolation==2&&seed.GagMinutes==30&&seed.KickAtViolation==3,"Live migration values");
Check(seed.HardActionsOnlyForPlayerRole&&seed.SuppressAdvertisingDisconnectMessages&&seed.SuppressAdvertisingServerMessages,"Missing old fields inherit 1.0.14 defaults");
Check(!seed.ExportBridgeEvents,"No test exports");
var rules=JsonSerializer.Deserialize<GlobalProtectionSnapshot>(File.ReadAllText(Path.Combine(workspace,"Addons/LPLK_ChatGuard/Rohskripte/resources/rules.seed.json")),options)!;
Check(rules.Version==2&&rules.BlockedSteamIds.Count==0,"Rules-only test seed");
Console.WriteLine($"ChatGuard: {count} fachliche Prüfungen bestanden.");

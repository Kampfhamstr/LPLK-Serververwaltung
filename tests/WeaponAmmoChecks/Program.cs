using LPLK_Weapons;
using System.Text.Json;
int checks=0;void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
void Invalid(WeaponRule rule){bool rejected=false;try{WeaponSettings.Validate(new(){Rules=new(){["weapon_ak47"]=rule}});}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid rule rejected");}
foreach(int value in new[]{-1,0,1,16,17,30,int.MaxValue})foreach(int limit in new[]{0,1,17,30,100000})
 Check(WeaponPolicy.Replenish(value,limit)==(value<0||limit<=0||value>=limit?value:Math.Min((long)value+1,limit)),"bounded ammunition arithmetic never removes existing ammo");
Check(WeaponPolicy.Replenish(90,int.MaxValue)==91,"AWS reserve increment uses live bullets, not magazine count");
Check(WeaponPolicy.Replenish(int.MaxValue,int.MaxValue)==int.MaxValue,"unlimited ammo cannot overflow negative");
foreach(var rule in new[]{new WeaponRule{Clip=-1},new WeaponRule{Ammo=100001},new WeaponRule{BypassPermissions=null!},new WeaponRule{BypassPermissions=[""]}})Invalid(rule);
var valid=new WeaponRule{Clip=17,Ammo=91,UnlimitedClip=true,UnlimitedAmmo=true,ReloadAfterShoot=true,BypassPermissions=["admin","root"],BypassQuota=true,BlockUsing=true,TeamQuota=new(){[0]=0}};
var config=new WeaponSettings{Rules=new(){["weapon_ak47"]=valid}};WeaponSettings.Validate(config);checks++;
Check(WeaponPolicy.Denial(config,"weapon_ak47",false,true,"de_dust2",2,99)==null,"explicit legacy privilege bypasses quota");Check(WeaponPolicy.Denial(config,"weapon_ak47",false,false,"de_dust2",2,0)!=null,"no permission no bypass");
Check(WeaponPolicy.GrenadeAmmoSlot("weapon_flashbang")==14&&WeaponPolicy.GrenadeAmmoSlot("weapon_incgrenade")==16&&WeaponPolicy.GrenadeAmmoSlot("weapon_ak47")==-1,"grenade stack slots");
WeaponSettings.Validate(new(){Rules=new(){["weapon_hegrenade"]=new(){TeamQuota=new(){[0]=3}}}});checks++;
foreach(bool external in new[]{false,true})foreach(bool secondEntity in new[]{false,true})
{
 int limit=30,errors=0;var changes=new OwnedWeaponLimits();changes.Set("definition:clip",17,()=>limit,v=>limit=v);if(secondEntity)changes.Set("definition:clip",17,()=>limit,v=>limit=v);
 Check(limit==17&&changes.Count==1,"one baseline per shared definition");if(external)limit=40;
 changes.Restore(_=>errors++);Check(limit==(external?40:30)&&errors==0&&changes.Count==0,"restore preserves external owner");
}
{
 int value=30;var changes=new OwnedWeaponLimits();changes.Set("d",17,()=>value,v=>value=v);value=40;changes.Set("d",20,()=>value,v=>value=v);changes.Restore(_=>throw new Exception());Check(value==40,"new external baseline adopted before another write");
}
{
 int value=30;var changes=new OwnedWeaponLimits();bool failed=false;try{changes.Set("d",17,()=>value,_=>{});}catch(InvalidOperationException){failed=true;}
 Check(failed&&value==30&&changes.Count==0,"ignored setter not confirmed");
}
{
 int value=30,errors=0;bool fail=false;var changes=new OwnedWeaponLimits();changes.Set("d",17,()=>value,v=>{if(fail)throw new IOException();value=v;});fail=true;changes.Restore(_=>errors++);Check(errors==1&&changes.Count==0,"restore error visible, stale native references discarded");
}
foreach(bool succeeded in new[]{false,true})
{
 var cycle=new WeaponCycle(11,22);uint selected=11;int writes=0;
 Check(cycle.Step(false,11,next=>{writes++;if(succeeded)selected=next;return succeeded;})==succeeded,"outbound selection needs confirmation");
 Check(cycle.Step(true,22,next=>{writes++;selected=next;return true;})==succeeded,"return only after own confirmed outbound");
 Check(writes==(succeeded?2:1),"failed outbound never returns even if player chose alternate");
 Check(!cycle.Step(true,22,_=>throw new Exception()),"duplicate callback rejected");
}
{
 var cycle=new WeaponCycle(11,22);int writes=0;
 Check(!cycle.Step(true,22,_=>{writes++;return true;})&&writes==0,"skipped outbound cannot undo manual selection");
 Check(!cycle.Step(false,11,_=>{writes++;return true;}),"out-of-order callback cancels cycle");
}
{
 var cycle=new WeaponCycle(11,22);Check(!cycle.Step(false,22,_=>throw new Exception()),"manual switch before outbound cancels");
 Check(!cycle.Step(true,22,_=>throw new Exception()),"manual alternate stays selected");
}
{
 var cycle=new WeaponCycle(11,22);Check(cycle.Step(false,11,_=>true),"outbound accepted");
 Check(!cycle.Step(true,33,_=>throw new Exception()),"manual third weapon preserved");
}
{
 var cycle=new WeaponCycle(11,22);try{cycle.Step(false,11,_=>throw new IOException());}catch(IOException){}
 Check(!cycle.Step(true,22,_=>throw new Exception()),"throwing outbound disarms return");
}
Console.WriteLine(JsonSerializer.Serialize(new{checks,result="passed",nativeGameplay=false}));

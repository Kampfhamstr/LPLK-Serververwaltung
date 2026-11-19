using System.Text.Json;
using LPLK.Common;
using SwiftlyS2.Shared.Commands;
namespace LPLK_Maps;
public sealed partial class Plugin
{
 private void NewMapActivationProbe(ICommandContext ctx)
 {
  if(ctx.IsSentByPlayer||ctx.Sender is not null||AnyHuman()||voteEnds>0||pending is not null||nextMap is not null)throw new InvalidOperationException("Leerer Testserver erforderlich.");
  string path=Path.Combine(RoundFileStore.DataPath(Core),"new-map-activation-test.json");
  if(!RamFile.Exists(path)||new RamFileInfo(path).Length>2048)throw new InvalidOperationException("Testfreigabe fehlt.");
  using var auth=JsonDocument.Parse(RamFile.ReadAllText(path));var expires=auth.RootElement.GetProperty("expires").GetDateTimeOffset();
  if(auth.RootElement.GetProperty("server").GetString()!="89450e53-b4c5-4511-a4f6-82696809154b"||expires<DateTimeOffset.UtcNow||expires>DateTimeOffset.UtcNow.AddMinutes(5))throw new InvalidOperationException("Testfreigabe ungültig.");
  RamFile.Delete(path);
  var before=JsonSerializer.Serialize(newMapActivations?.Snapshot());var now=DateTimeOffset.UtcNow;
  var isolated=NewMapActivationState.Migrate(maps.Where(m=>m.Id!="de_nuke").Select(m=>m.Id),new(),now);
  bool observed=ObserveNewMapActivations(isolated,now);
  bool stable=!ObserveNewMapActivations(isolated,now.AddMinutes(1));
  var restored=NewMapActivationState.Parse(JsonSerializer.Serialize(isolated.Snapshot()));
  bool onlyExpected=restored.Markers.Snapshot().Count==1&&restored.Markers.Snapshot().TryGetValue("de_nuke",out var started)&&started==now;
  bool expiry=restored.Markers.IsNew("de_nuke",now.AddDays(14).AddTicks(-1))&&!restored.Markers.IsNew("de_nuke",now.AddDays(14));
  bool preserved=before==JsonSerializer.Serialize(newMapActivations?.Snapshot());
  var result=new{success=observed&&stable&&onlyExpected&&expiry&&preserved,observed,stable,onlyExpected,expiry,liveStatePreserved=preserved,label=restored.Markers.Label("de_nuke","Nuke",now),at=now};
  DeferredJson.Queue(Path.Combine(RoundFileStore.DataPath(Core),"new-map-activation-probe.json"),result);
  ctx.ReplyLplk("LPLK_MAP_ACTIVATION "+JsonSerializer.Serialize(result));
 }
}

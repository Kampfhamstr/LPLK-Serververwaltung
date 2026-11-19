using System.Reflection;
public static class DisplayChecks
{
 // Emulate subsequent display heartbeats without sleeping. Production does not
 // wait for these writes. Three passes cover failure observation and recovery.
 public static void Settle(object plugin)
 {
  var type=plugin.GetType();var drain=type.Assembly.GetType("LPLK.Common.DeferredJson")?.GetMethod("Drain");
  var status=type.GetMethod("WriteStatus",BindingFlags.Instance|BindingFlags.NonPublic)??type.GetMethod("WriteSettingsStatus",BindingFlags.Instance|BindingFlags.NonPublic);
  for(int i=0;i<3;i++){drain?.Invoke(null,null);status?.Invoke(plugin,null);}drain?.Invoke(null,null);
 }
}

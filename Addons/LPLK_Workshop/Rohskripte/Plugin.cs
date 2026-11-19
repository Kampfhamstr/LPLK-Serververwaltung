using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Plugins;

namespace LPLK_Workshop;

[PluginMetadata(Id="LPLK_Workshop",Name="LPLK_Workshop",Version="0.1.3",Author="LPLK",Description="Workshop-Abo-Links und Downloadhilfe",MinimumAPIVersion="1.4.14")]
public sealed class Plugin(ISwiftlyCore core):BasePlugin(core)
{
    // Entirely static responses: no files, network calls, timers or entity hooks.
    private readonly long[] lastReply = new long[128];
    private readonly List<Guid> registrations=[];
    public override void Load(bool hotReload)
    {
        try {
            foreach(string exact in new[]{"sw_addons","sw_workshop","sw_workshob"})
            {
                if(Core.Command.IsCommandRegistered(exact))throw new InvalidOperationException("Befehl bereits belegt: "+exact);
                // Use the same explicit four-argument raw registration as LPLK_Admin.
                var id=Core.Command.RegisterCommand(exact,Workshop,registerRaw:true,permission:"");
                registrations.Add(id);
                if(id==Guid.Empty||!Core.Command.IsCommandRegistered(exact))throw new InvalidOperationException("Befehl nicht registriert: "+exact);
            }
            Core.Logger.LogInformation("LPLK_Workshop 0.1.3 ready: addons, workshop, workshob; RAM-only replies.");
        } catch { Unload();throw; }
    }
    public override void Unload(){foreach(var id in registrations)Core.Command.UnregisterCommand(id);registrations.Clear();Array.Clear(lastReply);}

    public void Workshop(ICommandContext context)
    {
        if(context.Sender is {} player)
        {
            int slot=player.PlayerID;long now=Environment.TickCount64;
            if((uint)slot<lastReply.Length){if(lastReply[slot]!=0&&now-lastReply[slot]<3000)return;lastReply[slot]=now;}
            context.Reply("[gold]Workshop-Pakete & Abo-Links: [lightblue]https://lplk.de/addons.html[default]");
            context.Reply("[gold]Paket auf Steam öffnen, [lightblue]Abonnieren[gold] und den Download in Steam abschließen lassen.[default]");
            context.Reply("[gold]Danach verbinden. Welche [lightblue]Modelle und Sounds[gold] dein Server nutzt, steht auf der Seite.[default]");
        }
        else context.Reply("LPLK_Workshop 0.1.3 | https://lplk.de/addons.html | Auf Steam abonnieren, Download abschließen lassen, dann verbinden. Aliasse: addons, workshop, workshob. RAM-only.");
    }
}

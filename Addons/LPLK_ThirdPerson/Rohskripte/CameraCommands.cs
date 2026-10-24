using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;

namespace LPLK_ThirdPerson;

public static class CameraCommandName
{
    // Empty disables the additional alias, as in the original. Fixed LPLK entry points remain.
    public static string Normalize(string? value)
    {
        if(value is null)throw new InvalidDataException("Befehlsname muss Text sein.");
        value=value.Trim().ToLowerInvariant();
        if(value.Length==0)return "";
        if(value.StartsWith("sw_",StringComparison.Ordinal))value=value[3..];
        if(value.Length is <1 or >40||value.StartsWith("sw_",StringComparison.Ordinal)||value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '_' and not '-' and not '.')||value=="lplk_thirdperson")throw new InvalidDataException("Ungültiger oder reservierter Kamerabefehl.");
        return value;
    }
}

public sealed partial class Plugin
{
    private sealed class CameraRegistration(string name){public string Name=name;public Guid Id;public bool Active;}
    private CameraRegistration? customCommand;
    private readonly List<CameraRegistration> retiredCommands=[];
    private string activeCommandName="tp";
    private string? commandError;
    private bool ReleaseCommand(CameraRegistration entry)
    {
        entry.Active=false;
        try{Core.Command.UnregisterCommand(entry.Id);return true;}
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_ThirdPerson: Eigener Zusatzbefehl noch nicht entfernt.");return false;}
    }
    private void ReconcileCommand()
    {
        foreach(var entry in retiredCommands.ToArray())if(ReleaseCommand(entry))retiredCommands.Remove(entry);
        CameraRegistration? staged=null;
        try
        {
            string name=CameraCommandName.Normalize(settings.CustomTPCommand);
            bool additional=name is not ("" or "tp" or "thirdperson");
            if(additional&&customCommand?.Name!=name)
            {
                if(Core.Command.IsCommandRegistered("sw_"+name)||Core.Command.IsCommandRegistered(name))throw new InvalidDataException("Kamerabefehl bereits belegt: "+name);
                var entry=new CameraRegistration(name);
                entry.Id=Core.Command.RegisterCommand(name,ctx=>
                {
                    if(!module.Active||!entry.Active)return;
                    ToggleCurrent(ctx,()=>entry.Active&&ReferenceEquals(customCommand,entry));
                },false,"");
                if(entry.Id==Guid.Empty)throw new InvalidOperationException("Kamerabefehl nicht registriert.");
                staged=entry;
                if(!Core.Command.IsCommandRegistered("sw_"+name))throw new InvalidOperationException("Kamerabefehl nicht bestätigt.");
            }
            if(customCommand is {} old&&(!additional||old.Name!=name))
            {customCommand=null;if(!ReleaseCommand(old))retiredCommands.Add(old);}
            if(staged is not null){customCommand=staged;staged.Active=true;}
            activeCommandName=name;
            commandError=retiredCommands.Count==0?null:"Alter Zusatzbefehl gesperrt; Entfernung wird wiederholt.";
        }
        catch(Exception e)
        {
            if(staged is not null&&!ReleaseCommand(staged))retiredCommands.Add(staged);
            string message=e is InvalidDataException?e.Message:"Kamerabefehl konnte nicht aktualisiert werden.";
            if(commandError!=message)Core.Logger.LogWarning(e,"LPLK_ThirdPerson: Bisheriger Befehl bleibt erhalten.");
            commandError=message;
        }
    }
    private void RemoveCustomCommand()
    {
        if(customCommand is {} entry)ReleaseCommand(entry);
        foreach(var old in retiredCommands)ReleaseCommand(old);
        customCommand=null;retiredCommands.Clear();
    }
}

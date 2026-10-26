using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;

namespace LPLK_Whitelist;

public sealed record AccessCommandNames(string Add,string Remove)
{
    public static string Normalize(string value)
    {
        value=value.Trim().ToLowerInvariant();
        if(value.StartsWith("sw_",StringComparison.Ordinal))value=value[3..];
        if(value.Length is <1 or >40||value.StartsWith("sw_",StringComparison.Ordinal)||value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '_' and not '-' and not '.'))throw new InvalidDataException("Ungültiger Befehlsname.");
        return value;
    }
    public static AccessCommandNames Read(string text)
    {
        var root=JsonNode.Parse(text,documentOptions:new(){CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true})!.AsObject();
        var sectionKey=root.Select(x=>x.Key).SingleOrDefault(k=>k.Equals("Whitelist",StringComparison.OrdinalIgnoreCase));
        var section=sectionKey is null?root:root[sectionKey]!.AsObject();
        string? Value(string key)
        {
            var found=section.Select(x=>x.Key).SingleOrDefault(k=>k.Equals(key,StringComparison.OrdinalIgnoreCase));
            if(found is null)return null;
            if(section[found] is not JsonValue node||!node.TryGetValue<string>(out var value)||value is null)throw new InvalidDataException("Befehlsname muss Text sein.");
            return Normalize(value);
        }
        string Pick(string modern,string legacy,string fallback)
        {
            var a=Value(modern);var b=Value(legacy);
            if(a is not null&&b is not null&&a!=b)throw new InvalidDataException("Alte und neue Befehlsnamen widersprechen sich.");
            return a??b??fallback;
        }
        var result=new AccessCommandNames(Pick("AddCommand","WhitelistCommand","wl"),Pick("RemoveCommand","UnWhitelistCommand","uwl"));
        if(result.Add==result.Remove)throw new InvalidDataException("Hinzufügen und Entfernen brauchen verschiedene Namen.");
        if(result.Add is "uwl" or "lplk_wl_remove" or "lplk_whitelist"||result.Remove is "wl" or "lplk_wl_add" or "lplk_whitelist")throw new InvalidDataException("Befehlsname gehört bereits zu einer anderen Zugangsaktion.");
        return result;
    }
    public Dictionary<string,bool> Additional()
    {
        var names=new Dictionary<string,bool>(StringComparer.Ordinal);
        if(Add is not ("wl" or "lplk_wl_add"))names.Add(Add,true);
        if(Remove is not ("uwl" or "lplk_wl_remove"))names.Add(Remove,false);
        return names;
    }
}

public sealed partial class Plugin
{
    private sealed class Registration(string name,bool add){public string Name=name;public bool Add=add;public Guid Id;public bool Active;}
    private readonly Dictionary<string,Registration> configuredCommands=new(StringComparer.Ordinal);
    private readonly List<Registration> retiredCommands=[];
    private AccessCommandNames activeCommandNames=new("wl","uwl");
    private string? commandError;
    private bool Release(Registration registration)
    {
        registration.Active=false;
        try{Core.Command.UnregisterCommand(registration.Id);return true;}
        catch(Exception e){Core.Logger.LogWarning(e,"LPLK_Whitelist: Eigener Zusatzbefehl noch nicht entfernt.");return false;}
    }
    private void ReconcileCommands()
    {
        foreach(var old in retiredCommands.ToArray())if(Release(old))retiredCommands.Remove(old);
        var staged=new List<Registration>();
        try
        {
            var names=AccessCommandNames.Read(snapshot.ConfigText);var wanted=names.Additional();
            foreach(var name in wanted.Keys.Where(n=>!configuredCommands.ContainsKey(n)))
                if(Core.Command.IsCommandRegistered("sw_"+name)||Core.Command.IsCommandRegistered(name))throw new InvalidDataException("Zusatzbefehl bereits belegt: "+name);
            foreach(var pair in wanted.Where(x=>!configuredCommands.ContainsKey(x.Key)))
            {
                var entry=new Registration(pair.Key,pair.Value);
                entry.Id=Core.Command.RegisterCommand(entry.Name,c=>
                {
                    if(!module.Active||!entry.Active)return;bool add=entry.Add;
                    CommandChange(c,add,()=>entry.Active&&entry.Add==add&&configuredCommands.TryGetValue(entry.Name,out var current)&&ReferenceEquals(entry,current));
                },false,"");
                if(entry.Id==Guid.Empty)throw new InvalidOperationException("Zusatzbefehl konnte nicht registriert werden.");
                staged.Add(entry);
                if(!Core.Command.IsCommandRegistered("sw_"+entry.Name))throw new InvalidOperationException("Zusatzbefehl wurde nicht bestätigt.");
            }
            foreach(var pair in configuredCommands.Where(x=>!wanted.ContainsKey(x.Key)).ToArray())
            {configuredCommands.Remove(pair.Key);if(!Release(pair.Value))retiredCommands.Add(pair.Value);}
            foreach(var entry in staged)configuredCommands.Add(entry.Name,entry);
            foreach(var pair in wanted){configuredCommands[pair.Key].Add=pair.Value;configuredCommands[pair.Key].Active=true;}
            activeCommandNames=names;
            commandError=retiredCommands.Count==0?null:"Entfernung alter Zusatzbefehle wird wiederholt; alte Aktionen sind gesperrt.";
        }
        catch(Exception e)
        {
            foreach(var entry in staged)if(!Release(entry))retiredCommands.Add(entry);
            string message=e is InvalidDataException?e.Message:"Zusatzbefehle konnten nicht aktualisiert werden.";
            if(commandError!=message)Core.Logger.LogWarning(e,"LPLK_Whitelist: Bisherige Befehle bleiben aktiv; Zugangsregeln werden weiter angewendet.");
            commandError=message;
        }
    }
    private void RemoveConfiguredCommands()
    {
        foreach(var entry in configuredCommands.Values.Concat(retiredCommands).Distinct())Release(entry);
        configuredCommands.Clear();retiredCommands.Clear();
    }
}

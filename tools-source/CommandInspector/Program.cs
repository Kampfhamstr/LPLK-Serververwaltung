using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;
using System.Security.Cryptography;

if(args.Length!=2)throw new ArgumentException("Assembly folder and output JSON required");
if(args[0]=="--api")
{
    using var api=ModuleDefinition.ReadModule(args[1]);
    foreach(var t in api.GetTypes().Where(t=>t.FullName.Contains("CommandInfo")||t.FullName=="SwiftlyS2.Shared.Commands.ICommandService"))
        Console.WriteLine(JsonSerializer.Serialize(new{type=t.FullName,properties=t.Properties.Select(p=>p.PropertyType.FullName+" "+p.Name),fields=t.Fields.Select(p=>p.FieldType.FullName+" "+p.Name),methods=t.Methods.Where(m=>m.Name.StartsWith("Get")||m.Name.StartsWith("Is")).Select(m=>m.FullName)}));
    return;
}
var results=new List<object>();
foreach(string path in Directory.EnumerateFiles(args[0],"*.dll").Order())
{
    using var module=ModuleDefinition.ReadModule(path,new ReaderParameters{ReadSymbols=false,ReadingMode=ReadingMode.Deferred});
    var attributes=new List<object>();var dynamic=new List<object>();var metadata=new Dictionary<string,string>();
    foreach(var type in module.GetTypes())
    {
        foreach(var attribute in type.CustomAttributes.Where(a=>a.AttributeType.Name.StartsWith("PluginMetadata")))
            foreach(var p in attribute.Properties)metadata[p.Name]=p.Argument.Value?.ToString()??"";
        foreach(var method in type.Methods)
        {
            foreach(var a in method.CustomAttributes.Where(a=>a.AttributeType.Namespace=="SwiftlyS2.Shared.Commands"&&a.AttributeType.Name is "Command" or "CommandAlias"))
            {
                string name=(string)a.ConstructorArguments[0].Value;
                // Both SDK attribute constructors explicitly carry registerRaw as their bool argument.
                bool raw=a.ConstructorArguments.Any(x=>x.Type.MetadataType==MetadataType.Boolean&&(bool)x.Value);
                attributes.Add(new{name,raw,effective=raw?name:"sw_"+name,kind=a.AttributeType.Name,method=method.FullName,
                    arguments=a.ConstructorArguments.Select(x=>new{type=x.Type.FullName,value=x.Value?.ToString()}).ToArray()});
            }
            if(!method.HasBody)continue;
            var calls=method.Body.Instructions.Where(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="SwiftlyS2.Shared.Commands.ICommandService"&&m.Name is "RegisterCommand" or "RegisterCommandAlias").ToArray();
            if(calls.Length>0)dynamic.Add(new{method=method.FullName,count=calls.Length,
                literals=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldstr).Select(i=>i.Operand?.ToString()).Where(s=>s!=null&&s.Length<100).ToArray(),
                occupancyCalls=method.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="IsCommandRegistered"),
                il=method.Body.Instructions.Select(i=>i.OpCode+" "+(i.Operand is MethodReference m?m.FullName:i.Operand?.ToString())).ToArray()});
        }
    }
    using var stream=File.OpenRead(path);
    results.Add(new{sha256=Convert.ToHexStringLower(SHA256.HashData(stream)),assembly=module.Assembly.Name.Name,version=module.Assembly.Name.Version.ToString(),metadata,attributes,dynamic});
}
File.WriteAllText(args[1],JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{assemblies=results.Count,output=args[1],executedPluginCode=false}));

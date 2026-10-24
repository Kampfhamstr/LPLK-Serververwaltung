using Mono.Cecil;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
if(args.Length is <3 or >4||args.Length==4&&args[3] is not ("--lplk-menus" or "--fleet-bank" or "--fleet-admin" or "--fleet-modes"))throw new ArgumentException("workspace buildRoot nugetRoot [--lplk-menus|--fleet-bank|--fleet-admin|--fleet-modes]");
bool modesOnly=args.Length==4&&args[3]=="--fleet-modes";
bool adminOnly=args.Length==4&&args[3]=="--fleet-admin";
bool menusOnly=args.Length==4&&args[3]=="--lplk-menus",bankOnly=args.Length==4&&args[3]=="--fleet-bank";
string workspace=Path.GetFullPath(args[0]),build=Path.GetFullPath(args[1]),packages=Path.GetFullPath(args[2]);
var results=new List<object>();int failures=0;
foreach(string directory in Directory.GetDirectories(Path.Combine(workspace,"Addons")))
{
 string id=Path.GetFileName(directory);if(id=="_Gemeinsam"||menusOnly&&!id.StartsWith("LPLK_",StringComparison.Ordinal)||bankOnly&&id!="LPLK_Economy"||adminOnly&&id!="LPLK_Admin"||modesOnly&&id is not ("LPLK_Surf" or "LPLK_Zombie"))continue;
 string project=Path.Combine(directory,"Rohskripte",id+".csproj");if(!File.Exists(project))continue;
 string dll=Path.Combine(build,id,"bin","Release","net10.0",id+".dll");
 if(!File.Exists(dll)){results.Add(new{plugin=id,status="not-built"});failures++;continue;}
 var artifacts=new Dictionary<string,byte[]>{{"build",File.ReadAllBytes(dll)}};
 string zipPath=Path.Combine(directory,"Build",id+".zip");
 if(File.Exists(zipPath)){using var zip=ZipFile.OpenRead(zipPath);var entry=zip.GetEntry(id+"/"+id+".dll")??throw new InvalidDataException("Package DLL missing: "+id);using var input=entry.Open();using var buffer=new MemoryStream();input.CopyTo(buffer);artifacts["package"]=buffer.ToArray();}
 foreach(var artifact in artifacts)
 foreach(string api in bankOnly||adminOnly||modesOnly?new[]{"1.4.14","1.5.0","1.5.0-beta.10","1.5.0-beta.26","1.5.0-beta.28"}:new[]{"1.4.14","1.5.0-beta.10","1.5.0-beta.26","1.5.0-beta.28"})
 {
  using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(Path.Combine(packages,"swiftlys2.cs2",api,"lib","net10.0"));resolver.AddSearchDirectory(Path.GetDirectoryName(dll)!);
  using var bytes=new MemoryStream(artifact.Value);using var module=ModuleDefinition.ReadModule(bytes,new ReaderParameters{AssemblyResolver=resolver});
  bool Framework(TypeReference type)=>type.GetElementType().Scope?.Name.StartsWith("SwiftlyS2.",StringComparison.Ordinal)==true;
  var errors=new List<string>();int references=0;
  foreach(var type in module.GetTypeReferences().Where(Framework)){references++;try{if(type.Resolve() is null)errors.Add(type.FullName);}catch{errors.Add(type.FullName);}}
  foreach(var member in module.GetMemberReferences().Where(m=>Framework(m.DeclaringType)))
  {
   references++;try{if(member is MethodReference method&&method.Resolve() is null||member is FieldReference field&&field.Resolve() is null)errors.Add(member.FullName);}catch{errors.Add(member.FullName);}
  }
  failures+=errors.Count;results.Add(new{plugin=id,artifact=artifact.Key,version=module.Assembly.Name.Version.ToString(),api,references,errors,sha256=Convert.ToHexString(SHA256.HashData(artifact.Value)).ToLowerInvariant()});
 }
}
var report=new{checkedAt=DateTimeOffset.UtcNow,failures,scope=menusOnly?"18 LPLK menu components; static build/package references on four APIs; not native gameplay":"Static references, not native gameplay",results};
File.WriteAllText(Path.Combine(workspace,"state",modesOnly?"fleet-modes-api-checks.json":adminOnly?"fleet-admin-api-checks.json":bankOnly?"fleet-bank-api-checks.json":menusOnly?"menu-redesign-api-checks.json":"all-addon-api-checks.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{failures,results=results.Count}));return failures==0?0:1;

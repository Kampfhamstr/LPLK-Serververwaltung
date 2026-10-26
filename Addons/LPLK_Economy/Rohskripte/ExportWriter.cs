using System.Text.Json;
namespace LPLK_Economy;

// Derived web/status files only. Settings, receipts and the bank journal never
// use this queue. At most one pending snapshot per path; one ordered writer.
public sealed class ExportWriter
{
 private readonly object gate=new();
 private readonly Dictionary<string,string> pending=[];
 private Task? worker;
 private readonly HashSet<string> failed=[];
 public string? Error {get{lock(gate)return failed.Count==0?null:"Hintergrundexport fehlgeschlagen; betroffene Dateien werden beim nächsten Export erneut versucht.";}}
 public void Queue<T>(string path,T value)
 {
  string json=JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true});
  lock(gate){pending[path]=json;if(worker is null||worker.IsCompleted)worker=Task.Run(Write);}
 }
 private void Write()
 {
  while(true)
  {
   KeyValuePair<string,string> item;
   lock(gate){if(pending.Count==0){worker=null;return;}item=pending.First();pending.Remove(item.Key);}
   string temporary=item.Key+".export-"+Guid.NewGuid().ToString("N");
   try{Directory.CreateDirectory(Path.GetDirectoryName(item.Key)!);File.WriteAllText(temporary,item.Value);File.Move(temporary,item.Key,true);lock(gate)failed.Remove(item.Key);}
   catch(Exception e)when(e is IOException or UnauthorizedAccessException){lock(gate)failed.Add(item.Key);}
   finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){lock(gate)failed.Add(item.Key);}}
  }
 }
 public void Drain(){Task? active;lock(gate)active=worker;active?.GetAwaiter().GetResult();}
}

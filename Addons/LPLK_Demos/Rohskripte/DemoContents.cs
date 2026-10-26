namespace LPLK_Demos;
public sealed record DemoContents(int Packets,int FirstTick,int LastTick,bool Stopped)
{
 public double Seconds=>Math.Max(0,LastTick-FirstTick)/64.0;
 public static DemoContents Read(Stream stream)
 {
  stream.Position=0;Span<byte> header=stackalloc byte[16];stream.ReadExactly(header);
  if(!header[..8].SequenceEqual("PBDEMS2\0"u8))throw new InvalidDataException("Ungültiger Demoheader");
  int count=0,first=int.MaxValue,last=0;bool stopped=false;int commands=0;
  while(stream.Position<stream.Length){
   if(++commands>10000000)throw new InvalidDataException("Zu viele Demopakete");
   uint command=Varint(stream)&~64u,tick=Varint(stream),length=Varint(stream);
   if(command>18||length>stream.Length-stream.Position)throw new InvalidDataException("Unvollständiges Demopaket");
   if(command is 7 or 13&&tick<int.MaxValue){count++;first=Math.Min(first,(int)tick);last=Math.Max(last,(int)tick);}
   stream.Seek(length,SeekOrigin.Current);if(command==0){stopped=true;break;}
  }
  return new(count,first==int.MaxValue?0:first,last,stopped);
 }
 private static uint Varint(Stream stream)
 {uint value=0;for(int shift=0;shift<35;shift+=7){int b=stream.ReadByte();if(b<0)throw new EndOfStreamException("Unvollständige Demo");if(shift==28&&(b&0xf0)!=0)throw new InvalidDataException("Ungültiges Demopaket");value|=(uint)(b&127)<<shift;if((b&128)==0)return value;}throw new InvalidDataException("Ungültiges Demopaket");}
 public void Verify(double expected)
 {if(!Stopped||Packets<2||Seconds<Math.Max(0.1,expected-3))throw new InvalidDataException("Demo enthält keine vollständige Aufnahme; Datei bleibt im RAM");}
}

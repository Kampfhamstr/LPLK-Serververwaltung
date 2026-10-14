using System.Text;

namespace LPLK_Maps;

// Workshop packages can wrap a compiled map in maps/<name>.vpk; inspect the bounded directory only.
public static class VpkMaps
{
    public static HashSet<string> Read(Stream stream)
    {
        using var reader=new BinaryReader(stream,Encoding.UTF8,true);
        if(reader.ReadUInt32()!=0x55aa1234)throw new InvalidDataException("VPK signature");
        uint version=reader.ReadUInt32(), length=reader.ReadUInt32();
        if(version is not (1 or 2) || length>16*1024*1024)throw new InvalidDataException("VPK header");
        if(version==2)reader.ReadBytes(16);
        byte[] bytes=reader.ReadBytes((int)length); if(bytes.Length!=length)throw new InvalidDataException("Truncated VPK tree");
        using var tree=new BinaryReader(new MemoryStream(bytes),Encoding.UTF8);
        string Token()
        {
            var data=new List<byte>();
            while(true){byte b=tree.ReadByte();if(b==0)return Encoding.UTF8.GetString(data.ToArray());if(data.Count>=1024)throw new InvalidDataException("VPK token too long");data.Add(b);}
        }
        HashSet<string> maps=[];
        for(string extension=Token();extension.Length>0;extension=Token())
            for(string path=Token();path.Length>0;path=Token())
                for(string name=Token();name.Length>0;name=Token())
                {
                    tree.ReadUInt32();ushort preload=tree.ReadUInt16();tree.ReadUInt16();tree.ReadUInt32();tree.ReadUInt32();
                    if(tree.ReadUInt16()!=0xffff)throw new InvalidDataException("VPK entry terminator");
                    if(tree.ReadBytes(preload).Length!=preload)throw new InvalidDataException("VPK preload truncated");
                    if(((extension=="vmap_c" && (path=="maps" || path.StartsWith("maps/",StringComparison.Ordinal))) || (extension=="vpk" && path=="maps")) && MapPolicy.SafeMapToken(name))maps.Add(name);
                }
        return maps;
    }
}

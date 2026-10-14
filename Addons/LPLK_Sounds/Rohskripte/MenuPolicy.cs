using System.Globalization;
namespace LPLK_Sounds;
public static class MenuPolicy
{
    public static string Label(string text)
    {
        var chars=StringInfo.ParseCombiningCharacters(text);var label=chars.Length>36?text[..chars[35]]+"…":text;
        return label.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
    }
    public static bool SamePlayer(int expectedSlot,ulong expectedSteam,int actualSlot,ulong actualSteam,bool authorized,bool valid,bool fake)=>authorized&&valid&&!fake&&expectedSteam!=0&&expectedSlot==actualSlot&&expectedSteam==actualSteam;
}

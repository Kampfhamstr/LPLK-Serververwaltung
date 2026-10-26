global using LPLK.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace LPLK.Common;

internal static class ChatTheme
{
    private const char Open='\ue000',Close='\ue001';
    private static string addon="LPLK_Admin";
    private static Func<IEnumerable<string>>? playerNames;
    private static readonly Regex Colors=new(@"\[(?:white|default|darkred|green|lightyellow|lightblue|olive|lime|red|lightpurple|purple|grey|gray|yellow|gold|silver|blue|darkblue|bluegrey|magenta|lightred|orange|/)\]|[\x01-\x10]",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    private static readonly Regex Prefix=new(@"^\s*\[(?:LPLK(?: [^\]]+)?|Surf|Zombie|AddonsManager|Serververwaltung)\]\s*",RegexOptions.Compiled);
    private static readonly Regex Values=new(@"(?<![\p{L}\p{N}_/])(?:\d[\d.,]*(?:\s*(?:\$|%|HP|AP|Sekunden?|Minuten?|Runden?|Spieler|Stimmen|Kills|ms|s|min))?|![\p{L}\p{N}_]+)(?![\p{L}\p{N}_])",RegexOptions.Compiled);
    public static void Initialize(string id,Func<IEnumerable<string>> names){addon=id;playerNames=names;}
    public static (string Label,string Body,string Accent) Palette(string id)=>id switch
    {
        "LPLK_Admin" or "SerververwaltungBridge"=>("LPLK Admin","white","red"),
        "LPLK_Economy"=>("LPLK Bank","gold","green"),
        "LPLK_Maps"=>("LPLK Maps","yellow","blue"),
        "LPLK_Sounds"=>("LPLK Sounds","yellow","purple"),
        "LPLK_Surf"=>("LPLK Surf","yellow","lightblue"),
        "LPLK_Zombie"=>("LPLK Zombie","gold","red"),
        "LPLK_Statistics"=>("LPLK Statistik","yellow","green"),
        "LPLK_Demos"=>("LPLK Demos","yellow","lightblue"),
        "LPLK_ChatGuard"=>("LPLK Chat","gold","red"),
        "LPLK_Whitelist"=>("LPLK Zugang","yellow","green"),
        "LPLK_Teams"=>("LPLK Teams","yellow","blue"),
        "LPLK_Weapons"=>("LPLK Waffen","gold","red"),
        "LPLK_CombatRules"=>("LPLK Kampf","gold","red"),
        "LPLK_AntiCamp"=>("LPLK AntiCamp","yellow","red"),
        "LPLK_AntiTeamFlash"=>("LPLK Flash","yellow","blue"),
        "LPLK_BombWires"=>("LPLK Bombe","gold","red"),
        "LPLK_Parachute"=>("LPLK Fallschirm","yellow","lightblue"),
        "LPLK_ThirdPerson"=>("LPLK Kamera","yellow","blue"),
        "LPLK_Fluch"=>("LPLK Fluch","gold","purple"),
        "LPLK_NadeEffects"=>("LPLK Granaten","yellow","green"),
        "AddonsManager"=>("AddonsManager","gold","blue"),
        _=>("LPLK","yellow","lightblue")
    };
    public static string Plain(string text)=>Colors.Replace(text,"").Replace(Open.ToString(),"").Replace(Close.ToString(),"");
    internal static string Value(string text)=>Open+Plain(text)+Close;
    public static string Format(string text,string? module=null,IEnumerable<string>? names=null)
    {
        if(string.IsNullOrWhiteSpace(text))return text;
        var (_,body,accent)=Palette(module??addon);
        text=Prefix.Replace(Colors.Replace(text,""),"",1);
        // Explicit interpolation spans preserve offline names and complete values.
        // Online names also cover translations and helpers which return strings.
        var known=(names??playerNames?.Invoke()??[]).Select(Plain).Where(n=>n.Length>=3&&n.Length<=80).Distinct().OrderByDescending(n=>n.Length).Take(128).ToArray();
        Regex? named=known.Length==0?null:new Regex(@"(?<![\p{L}\p{N}_])(?:"+string.Join('|',known.Select(Regex.Escape))+@")(?![\p{L}\p{N}_])");
        string Mark(string value)=>$"[{accent}]{value}[{body}]";
        string Details(string s)=>Values.Replace(s,m=>Mark(m.Value));
        string Literal(string s)
        {
            if(named is null)return Details(s);
            var result=new StringBuilder();int position=0;
            foreach(Match match in named.Matches(s)){result.Append(Details(s[position..match.Index]));result.Append(Mark(match.Value));position=match.Index+match.Length;}
            return result.Append(Details(s[position..])).ToString();
        }
        var output=new StringBuilder($"[{body}]");int from=0;
        while(from<text.Length)
        {
            int start=text.IndexOf(Open,from);if(start<0){output.Append(Literal(text[from..]));break;}
            output.Append(Literal(text[from..start]));int end=text.IndexOf(Close,start+1);
            if(end<0){output.Append(Literal(text[(start+1)..]));break;}
            output.Append(Mark(text[(start+1)..end]));from=end+1;
        }
        return output.Append("[default]").ToString();
    }
}

[InterpolatedStringHandler]
internal ref struct ChatText
{
    private StringBuilder value;
    public ChatText(int literalLength,int formattedCount){value=new(literalLength+formattedCount*12);}
    public void AppendLiteral(string text)=>value.Append(text);
    public void AppendFormatted<T>(T item)=>AppendFormatted(item,null);
    public void AppendFormatted<T>(T item,string? format)=>value.Append(ChatTheme.Value(item is IFormattable f?f.ToString(format,CultureInfo.CurrentCulture)??"":item?.ToString()??""));
    public void AppendFormatted<T>(T item,int alignment,string? format=null)
    {string text=item is IFormattable f?f.ToString(format,CultureInfo.CurrentCulture)??"":item?.ToString()??"";value.Append(ChatTheme.Value(alignment<0?text.PadRight(-alignment):text.PadLeft(alignment)));}
    public override string ToString()=>value.ToString();
}

internal static class AddonChat
{
    public static void SendLplkChat(this IPlayer player,string text)=>player.SendChat(ChatTheme.Format(text));
    public static void SendLplkChat(this IPlayer player,ref ChatText text)=>player.SendLplkChat(text.ToString());
    public static void SendLplkChat(this IPlayerManagerService players,string text)=>players.SendChat(ChatTheme.Format(text));
    public static void SendLplkChat(this IPlayerManagerService players,ref ChatText text)=>players.SendLplkChat(text.ToString());
    public static void ReplyLplk(this ICommandContext context,string text)=>context.Reply(context.Sender is null?ChatTheme.Plain(text):ChatTheme.Format(text));
    public static void ReplyLplk(this ICommandContext context,ref ChatText text)=>context.ReplyLplk(text.ToString());
    public static void SendLplkMessage(this IPlayer player,MessageType type,string text)=>player.SendMessage(type,type==MessageType.Chat?ChatTheme.Format(text):text);
    public static void SendLplkMessage(this IPlayer player,MessageType type,ref ChatText text)=>player.SendLplkMessage(type,type==MessageType.Chat?text.ToString():ChatTheme.Plain(text.ToString()));
    public static void SendLplkMessage(this IPlayerManagerService players,MessageType type,string text)=>players.SendMessage(type,type==MessageType.Chat?ChatTheme.Format(text):text);
}

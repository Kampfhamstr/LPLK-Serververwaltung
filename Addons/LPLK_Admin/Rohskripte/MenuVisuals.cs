using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using LPLK.Common;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menus;

namespace LPLK_Admin;

public sealed partial class Plugin
{
    private readonly ConditionalWeakTable<IMenuOption,object> visualOptions=new();
    private const string SelectionColor="#8FE9FF";
    private void ApplyMenuVisuals(IMenuAPI menu)
    {
        // Recognize our common markup, including public module menus opened by
        // their own commands. Do not restyle unrelated framework/addon menus.
        if(!ownedMenus.TryGetValue(menu,out _) &&
            !(menu.Configuration.Title?.StartsWith("<font color='#FFFFFF'>",StringComparison.Ordinal)==true &&
              menu.Options.Any(o=>o.Text.StartsWith("<font color=",StringComparison.Ordinal))))return;
        menu.Configuration.NavigationMarkerColor=SelectionColor;
        foreach(var option in menu.Options)
        {
            if(visualOptions.TryGetValue(option,out _))continue;
            visualOptions.Add(option,new());
            string? previous=null,previousPrefix=null,selectedHtml=null,ordinaryHtml=null;
            option.AfterFormat+=(_,args)=>
            {
                if(!active)return;
                var raw=(args.CustomText??"").TrimEnd('\u00a0');
                string prefix=Core.MenusAPI.Configuration.NavigationPrefix??">";
                if(raw!=previous||prefix!=previousPrefix)
                {
                    previous=raw;previousPrefix=prefix;
                    selectedHtml=MenuRowVisual.Format(raw,prefix,true,option.TextSize);
                    ordinaryHtml=MenuRowVisual.Format(raw,prefix,false,option.TextSize);
                }
                args.CustomText=ReferenceEquals(menu.GetCurrentOption(args.Player),option)?selectedHtml:ordinaryHtml;
            };
        }
    }
}

internal static class MenuRowVisual
{
    private const string EmptyPrefix="\u00a0\u00a0\u00a0 ";
    // Classic Swiftly 1.4.14 / 1.5.x prepends the marker or three NBSPs plus a
    // space. Add the opposite run invisibly, so both states reserve the SAME
    // glyphs in the SAME size. Padding alone cannot compensate this accurately.
    internal static string Format(string html,string prefix,bool selected,MenuOptionTextSize size)
    {
        string opposite=selected?EmptyPrefix:MenuLayout.Label(prefix)+" ";
        string plain=System.Net.WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]*>",""));
        int spaces=Math.Clamp((int)Math.Round((26-Helper.EstimateTextWidth(plain))/.5f),0,52);
        string color=selected?"#8FE9FF":"#00000000";
        // Keep the action/status colours and the normal text size. Padding must
        // use the option's font size too, not the smaller outer menu font.
        string css=size switch {MenuOptionTextSize.ExtraSmall=>"xs",MenuOptionTextSize.Small=>"s",MenuOptionTextSize.SmallMedium=>"sm",MenuOptionTextSize.MediumLarge=>"ml",MenuOptionTextSize.Large=>"l",MenuOptionTextSize.ExtraLarge=>"xl",_=>"m"};
        return $"<font class='fontSize-sm' color='#00000000'>{opposite}</font>"+
            $"<font class='fontSize-{css}'><font color='{color}'>[ </font>"+
            html+$"<font color='{color}'> ]</font>"+new string('\u00a0',spaces)+"</font>";
    }
}

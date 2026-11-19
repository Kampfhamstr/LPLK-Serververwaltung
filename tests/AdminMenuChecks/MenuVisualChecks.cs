using LPLK_Admin;
using LPLK.Common;
using SwiftlyS2.Shared.Menus;

internal static class MenuVisualChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var w=new MenuWorld{DataDirectory=Path.Combine(Environment.GetEnvironmentVariable("LPLK_BUILD_ROOT")!,"AdminMenuChecks","visual-"+Guid.NewGuid().ToString("N"))};
        w.Permissions.Add("lplk.admin");var plugin=new Plugin(w.Core);plugin.Load(false);plugin.UseSharedInterface(w.Interfaces);plugin.Admin(w.Context);
        var menu=w.Current!;var first=menu.Options[0];string label=first.Text;
        string focused=first.GetDisplayText(w.Player);
        check(focused.Contains("color='#8FE9FF'>[ ")&&focused.Contains("color='#8FE9FF'> ]"),"Actual selected menu row receives coloured brackets");
        check(focused.Contains(label)&&first.Text==label,"Highlight preserves action text and semantic colours");
        menu.MoveToOptionIndex(w.Player,1);string ordinary=first.GetDisplayText(w.Player);
        check(!ordinary.Contains("color='#8FE9FF'>[ ")&&ordinary.Contains("color='#00000000'>[ "),"Deselected row reserves the same bracket space invisibly");
        check(menu.Options[1].GetDisplayText(w.Player).Contains("color='#8FE9FF'>[ "),"Highlight follows the current native selection");
        check(!focused.EndsWith('\u00a0')&&focused.EndsWith("</font>"),"Trailing alignment spaces use the row font, not the smaller outer font");
        check(menu.Configuration.NavigationMarkerColor=="#8FE9FF","Native marker and brackets share one highlight colour");
        string green=MenuColors.Paint("1. Aktiv: AN",MenuColors.Green);
        check(MenuRowVisual.Format(green,"➤",true,MenuOptionTextSize.Medium).Contains(green),"Enabled state remains green while selected");
        string red=MenuColors.Paint("2. Aktiv: AUS",MenuColors.Red);
        check(MenuRowVisual.Format(red,"➤",true,MenuOptionTextSize.Medium).Contains(red),"Disabled switch state remains red while selected");
        check(MenuRowVisual.Format("Text","<x>",false,MenuOptionTextSize.Medium).Contains("&lt;x&gt;"),"Marker compensation escapes markup");
        check(!MenuRowVisual.Format("Zurück",">",true,MenuOptionTextSize.Medium).Contains("1. "),"Return remains unnumbered");
        plugin.Unload();
    }
}

namespace LPLK_Models;

internal static class HalloweenBanner
{
 internal const int DurationMs=8000;
 // Built only when the map's model pool changes; no markup, files or assets per tick.
 internal static string Create(int total,int terrorists,int ct)=>
  "<font class='fontSize-sm' color='#B69CFF'>☾ DIE GEISTER SIND ERWACHT ☽</font><br>"+
  "<font class='fontSize-l' color='#FF9D3D'>HALLOWEEN SPECIAL</font><br>"+
  "<font class='fontSize-m' color='#F2EEFF'>Traust du dich in eine andere Haut?</font><br>"+
  $"<font class='fontSize-sm' color='#FFFFFF'>[ <font color='#C7ACFF'>{total} kostenlose Modelle</font> · {terrorists} T / {ct} CT ]</font><br>"+
  "<font class='fontSize-m' color='#FFFFFF'>[ <font color='#FFB35C'>!modelle</font> ] <font color='#E4DAF5'>Wähle deine dunkle Gestalt.</font></font>";
}

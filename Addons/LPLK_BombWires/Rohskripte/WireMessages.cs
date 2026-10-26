using LPLK.Common;
namespace LPLK_BombWires;

public static class WireMessages
{
 public static string MenuColor(int color)=>color switch
 {
  0=>"<font color='#FF7070'>Rot</font>",1=>"<font color='#6EA8FF'>Blau</font>",
  2=>"<font color='#FFD966'>Gelb</font>",3=>"<font color='#70E080'>Grün</font>",
  _=>throw new ArgumentOutOfRangeException(nameof(color))
 };
 private static string Color(int color)=>color switch
 {
  0=>"[red]Rot[gold]",1=>"[blue]Blau[gold]",2=>"[yellow]Gelb[gold]",3=>"[green]Grün[gold]",
  _=>throw new ArgumentOutOfRangeException(nameof(color))
 };
 public static string Chosen(int color)=>"[red][gold]Sicherer Draht: "+Color(color)+".[default]";
 public static string Result(string player,int pulled,int correct)
 {
  string start="[red][red]"+ChatTheme.Plain(player)+"[gold] hat "+Color(pulled)+" gezogen. ";
  return start+(pulled==correct?"Richtiger Draht – Bombe wird entschärft!":"Falsch – richtig wäre "+Color(correct)+" gewesen. Die Bombe explodiert!")+"[default]";
 }
}

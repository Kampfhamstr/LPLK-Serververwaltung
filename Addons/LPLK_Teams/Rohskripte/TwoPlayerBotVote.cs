namespace LPLK_Teams;

public sealed class TwoPlayerBotVote
{
 private readonly Dictionary<ulong,bool> ballots=[];
 private ulong[] voters=[];
 public bool Asked {get;private set;}
 public bool Suppressed {get;private set;}
 public bool Active=>voters.Length==2;
 public double Deadline {get;private set;}
 public int Yes=>ballots.Count(x=>x.Value);
 public string Result {get;private set;}="waiting";
 public bool Start(IEnumerable<ulong> participants,double now)
 {
  var ids=participants.Distinct().Order().ToArray();
  if(Asked||Active||Suppressed||ids.Length!=2||ids.Any(x=>x==0))return false;
  voters=ids;Deadline=now+30;ballots.Clear();Result="voting";return true;
 }
 public bool Matches(IEnumerable<ulong> participants)=>voters.SequenceEqual(participants.Distinct().Order());
 public bool Cast(ulong voter,bool yes,double now,IEnumerable<ulong> current)
 {
  if(!Active||now>=Deadline||!Matches(current)||!voters.Contains(voter)||ballots.ContainsKey(voter))return false;
  ballots[voter]=yes;
  if(!yes)Finish("rejected",false);
  else if(Yes==2)Finish("accepted",true);
  return true;
 }
 public bool Check(double now,IEnumerable<ulong> current)
 {
  if(!Active)return false;
  if(!Matches(current)){Finish("players_changed",false);return true;}
  if(now>=Deadline){Finish("timeout",false);return true;}
  return false;
 }
 public void Cancel(){if(Active)Finish("cancelled",false);}
 private void Finish(string result,bool suppressed){Asked=true;Suppressed=suppressed;voters=[];Result=result;}
 public void Restore(bool asked,bool suppressed){voters=[];ballots.Clear();Asked=asked||suppressed;Suppressed=suppressed;Result=suppressed?"accepted":Asked?"already_offered":"waiting";}
 public void NewMap()=>Restore(false,false);
}

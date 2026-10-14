using SwiftlyS2.Shared.Players;
namespace LPLK_Economy;
public sealed partial class Plugin
{
    private IReadOnlyDictionary<string,Action<IPlayer>> TargetActions(IPlayer actor,IPlayer target)
    {
        var result=new Dictionary<string,Action<IPlayer>>();string id=Id(target),name=target.Name;
        if(!Active||!settings.BankEnabled||!MayManage(actor,id)||!bank!.Exists(id))return result;
        result["Konto"]=a=>{if(Active&&settings.BankEnabled&&MayManage(a,id))AccountAdmin(a,id,name);};
        return result;
    }
}

using System.Reflection;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace LPLK_Teams;

public sealed partial class Plugin
{
    // Swiftly 1.5.2 clears this cache BEFORE dispatching MapUnload. A later
    // unload handler can refill it with the outgoing proxy. Its address may
    // subsequently belong to another entity, so IsValidEntity alone cannot
    // establish that it is still a game-rules proxy. Clear on deletion and
    // again at map load; never dereference the cached native GameRules value.
    // Feature detection leaves future framework versions without this field
    // untouched. No native pointer, schema offset or framework binary is patched.
    private static readonly FieldInfo? gameRulesCache=typeof(ISwiftlyCore).Assembly
        .GetType("SwiftlyS2.Core.EntitySystem.EntitySystemService")?
        .GetField("cachedGameRulesProxy",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);

    private static void InvalidateGameRulesCache(nint? deletedAddress=null)
    {
        if(gameRulesCache?.GetValue(null) is not CCSGameRulesProxy cached)return;
        if(deletedAddress is null||cached.Address==deletedAddress.Value)gameRulesCache.SetValue(null,null);
    }

    [EventListener<EventDelegates.OnEntityDeleted>]
    public void EntityDeleted(IOnEntityDeletedEvent e)=>InvalidateGameRulesCache(e.Entity.Address);
}

namespace LPLK_Weapons;

// Each delayed step must follow its own confirmed predecessor. In particular,
// finding the alternate weapon selected does not prove that we selected it.
public sealed class WeaponCycle(uint original,uint alternate)
{
    private int phase;
    public bool Step(bool returning,uint current,Func<uint,bool> select)
    {
        int expected=returning?1:0;
        if(phase!=expected||current!=(returning?alternate:original)){phase=2;return false;}
        phase=2; // Failed/throwing selection must never leave the return step armed.
        if(!select(returning?original:alternate))return false;
        phase=returning?2:1;
        return true;
    }
}

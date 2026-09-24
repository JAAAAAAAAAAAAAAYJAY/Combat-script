using UnityEngine;
using CombatOverride;

[Passive("passive_DivineGrace")]
public class PassiveDivineGrace : PassiveBase
{
    public override string OverrideKey => "passive_DivineGrace";

    const int HealPerTurn = 1;

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[DivineGrace] Attached to {GetOwnerUnit().name}.");
    }

    public override void OnTurnStart(CombatScript combat, int team)
    {
        if (team != OwnerTeam) return;
        if (!IsOwnerAlive()) return;
        var u = GetOwnerUnit();
        if (u.maxHp > 0 && u.hp >= u.maxHp) return;
        HealOwner(HealPerTurn);
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[DivineGrace] {GetOwnerUnit().name} regenerates {HealPerTurn} HP.");
    }
}

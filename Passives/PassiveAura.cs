using UnityEngine;
using CombatOverride;

[Passive("passive_Aura")]
public class PassiveAura : PassiveBase
{
    public override string OverrideKey => "passive_Aura";

    const int BonusAC = 1;

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Aura] Attached to {GetOwnerUnit().name}.");
    }

    public override void ModifyAC(CombatScript combat, int targetTeam, int targetSlot, ref float ac)
    {
        if (targetTeam != OwnerTeam) return;
        if (targetSlot == OwnerSlot) return;
        int distance = Mathf.Abs(targetSlot - OwnerSlot);
        if (distance > 1) return;
        var u = combat.GetUnit(targetTeam, targetSlot);
        if (string.IsNullOrEmpty(u.name) || u.hp <= 0) return;
        ac += BonusAC;
    }
}

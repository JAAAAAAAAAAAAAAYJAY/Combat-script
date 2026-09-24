using UnityEngine;
using CombatOverride;

[Passive("passive_SoulLeech")]
public class PassiveSoulLeech : PassiveBase
{
    public override string OverrideKey => "passive_SoulLeech";

    const float LeechPercent = 0.3f;

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[SoulLeech] Attached to {GetOwnerUnit().name}.");
    }

    public override void OnAnyUnitDefeated(CombatScript combat, int defeatedTeam, int defeatedSlot,
                                           int killerTeam, int killerSlot)
    {
        if (killerTeam != OwnerTeam || killerSlot != OwnerSlot) return;
        if (!IsOwnerAlive()) return;

        var defeated = combat.GetUnit(defeatedTeam, defeatedSlot);
        int baseAmount = defeated.maxHp > 0
            ? Mathf.CeilToInt(defeated.maxHp * LeechPercent)
            : 0;
        if (baseAmount <= 0) return;

        var owner = GetOwnerUnit();
        int missing = Mathf.CeilToInt(Mathf.Max(0f, owner.maxHp - owner.hp));
        if (missing <= 0) return;

        int heal = Mathf.Min(baseAmount, missing);
        if (heal <= 0) return;

        HealOwner(heal);

        if (CombatDebugHandler.UltraDebug)
            Debug.Log($"[SoulLeech] {GetOwnerUnit().name} drained {heal} HP from {defeated.name}'s dying soul.");
    }
}

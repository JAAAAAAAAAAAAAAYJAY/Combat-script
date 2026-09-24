using UnityEngine;
using CombatOverride;

[Passive("passive_Arcane")]
public class PassiveArcane : PassiveBase
{
    public override string OverrideKey => "passive_Arcane";

    const float MagicDamageBonus = 0.25f;

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Arcane] Attached to {GetOwnerUnit().name}.");
    }

    public override void ModifyDamageDealt(CombatScript combat, int attackerTeam, int attackerSlot,
                                           int targetTeam, int targetSlot, ref int amount,
                                           CombatScript.EffectKind kind)
    {
        if (attackerTeam != OwnerTeam || attackerSlot != OwnerSlot) return;
        if (amount <= 0) return;
        if (kind != CombatScript.EffectKind.Magic) return;
        int bonus = Mathf.CeilToInt(amount * MagicDamageBonus);
        amount += bonus;
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Arcane] {GetOwnerUnit().name} +{bonus} arcane magic damage.");
    }
}

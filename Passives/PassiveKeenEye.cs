using UnityEngine;
using CombatOverride;

[Passive("passive_KeenEye")]
public class PassiveKeenEye : PassiveBase
{
    public override string OverrideKey => "passive_KeenEye";

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[KeenEye] Attached to {GetOwnerUnit().name}.");
    }

    public override void ModifyDamageDealt(CombatScript combat, int attackerTeam, int attackerSlot,
                                           int targetTeam, int targetSlot, ref int amount,
                                           CombatScript.EffectKind kind)
    {
        if (attackerTeam != OwnerTeam || attackerSlot != OwnerSlot) return;
        if (amount <= 0) return;
        if (kind != CombatScript.EffectKind.Physical) return;
        bool proc;
        if (combat != null && combat.DiceRollerPublic != null)
            proc = combat.DiceRollerPublic.RollChance(0.15f);
        else
            proc = false;
        if (proc)
        {
            int bonus = amount;
            amount += bonus;
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[KeenEye] {GetOwnerUnit().name} scores a critical hit! +{bonus} damage.");
        }
    }
}

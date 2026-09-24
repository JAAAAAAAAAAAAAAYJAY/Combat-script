using UnityEngine;
using CombatOverride;

[Passive("passive_Shadowstep")]
public class PassiveShadowstep : PassiveBase
{
    public override string OverrideKey => "passive_Shadowstep";

    const int BonusAC = 2;

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Shadowstep] Attached to {GetOwnerUnit().name} (+{BonusAC} AC dynamic).");
    }

    public override void ModifyAC(CombatScript combat, int targetTeam, int targetSlot, ref float ac)
    {
        if (targetTeam != OwnerTeam || targetSlot != OwnerSlot) return;
        ac += BonusAC;
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
            proc = combat.DiceRollerPublic.RollChance(0.2f);
        else
            proc = false;
        if (proc)
        {
            int bonus = Mathf.CeilToInt(amount * 0.5f);
            amount += bonus;
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Shadowstep] {GetOwnerUnit().name} finds a gap! +{bonus} damage.");
        }
    }
}

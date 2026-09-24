using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[Passive("passive_EagleEye")]
public class PassiveEagleEye : PassiveBase
{
    public override string OverrideKey => "passive_EagleEye";

    const int BonusAC = 1;

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[EagleEye] Attached to {GetOwnerUnit().name} (+{BonusAC} AC dynamic).");
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
        int bonus = Mathf.CeilToInt(amount * 0.15f);
        amount += bonus;
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[EagleEye] {GetOwnerUnit().name} +{bonus} precision damage.");
    }
}

using UnityEngine;
using CombatOverride;

[Passive("passive_BloodFury")]
public class PassiveBloodFury : PassiveBase
{
    public override string OverrideKey => "passive_BloodFury";

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[BloodFury] Attached to {GetOwnerUnit().name}.");
    }

    public override void ModifyDamageDealt(CombatScript combat, int attackerTeam, int attackerSlot,
                                           int targetTeam, int targetSlot, ref int amount,
                                           CombatScript.EffectKind kind)
    {
        if (attackerTeam != OwnerTeam || attackerSlot != OwnerSlot) return;
        if (amount <= 0) return;
        var u = GetOwnerUnit();
        if (u.hp <= 0) return;
        float hpFrac = u.maxHp > 0 ? u.hp / u.maxHp : 1f;
        float bonusMult = (1f - hpFrac) * 0.5f;
        if (bonusMult > 0f)
        {
            int bonus = Mathf.CeilToInt(amount * bonusMult);
            amount += bonus;
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[BloodFury] {u.name} is hurt ({hpFrac*100:0}% HP) +{bonus} rage damage.");
        }
    }
}

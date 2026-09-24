using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[Passive("passive_Vampiric")]
public class PassiveVampiric : PassiveBase
{
    public override string OverrideKey => "passive_Vampiric";

    const float LifestealPercent = 1f;   // heal 100% of actual HP damage dealt

    public override void OnPostDamage(CombatScript combat, int attackerTeam, int attackerSlot,
                                       int targetTeam, int targetSlot, int hpDamage,
                                       CombatScript.EffectKind kind)
    {
        // Only the owner's outgoing damage triggers lifesteal.
        if (attackerTeam != OwnerTeam || attackerSlot != OwnerSlot) return;

        if (kind != CombatScript.EffectKind.Physical) return;

        // No HP removed = nothing to leech.
        if (hpDamage <= 0) return;

        // Don't lifesteal off a dead owner.
        if (!IsOwnerAlive()) return;

        var owner = GetOwnerUnit();
        int missing = Mathf.CeilToInt(Mathf.Max(0f, owner.maxHp - owner.hp));
        if (missing <= 0) return;

        int heal = Mathf.Min(Mathf.CeilToInt(hpDamage * LifestealPercent), missing);
        if (heal <= 0) return;

        // Route through HealOwner so the heal respects the full hook pipeline.
        HealOwner(heal);

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Vampiric] {GetOwnerUnit().name} leeched {heal} HP from {hpDamage} {kind} HP damage dealt.");
    }
}

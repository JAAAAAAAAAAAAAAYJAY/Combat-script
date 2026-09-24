using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("ability_Bloodlust")]
public class AbilityBloodlust : AbilityBase
{
    public override string OverrideKey => "ability_Bloodlust";

    const int Duration = 3;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var caster = GetUnit(casterTeam, casterSlot);
        if (string.IsNullOrEmpty(caster.name)) yield break;

        // Targeting: respect the Action's configured recoverHealthCoords if
        // any are set (these are the ally-side buff/heal coordinates in the
        // CSV). Pick the lowest-HP-% ally among them so Bloodlust lands on a
        // unit that both needs help and can leverage the damage boost. Fall
        // back to self-cast if no coords are configured (preserves legacy
        // behavior for actions whose CSV doesn't specify a target pattern).
        int targetTeam = casterTeam;
        int targetSlot = casterSlot;
        var configured = FindLowestHpPercentTarget(action.recoverHealthCoords, casterTeam, casterSlot);
        if (configured != null)
        {
            targetTeam = configured.Value.team;
            targetSlot = configured.Value.slot;
        }

        if (!IsUnitAlive(targetTeam, targetSlot)) yield break;

        CustomStatusRuntime.Apply(targetTeam, targetSlot, new BloodlustStatus
        {
            RemainingRounds = Duration,
            SourceTeam = casterTeam,
            SourceSlot = casterSlot,
        });

        var targetName = GetUnit(targetTeam, targetSlot).name;
        if (CombatDebugHandler.UltraDebug)
            Debug.Log($"[Bloodlust] {caster.name} bloodlusted {targetName}! +{BloodlustStatus.DamageBoostPercent*100:0}% multiplicative damage for {Duration} turns.");
        yield return null;
    }

    private class BloodlustStatus : CustomStatus
    {
        // Multiplicative damage boost. Stacks with BerserkStatus via the ref
        // amount chain. Since both do amount *= (1 + pct), multiplication is
        // commutative: final = amount × (1 + 0.3) × (1 + 0.5) = amount × 1.95,
        // regardless of which status applies first.
        public const float DamageBoostPercent = 0.3f;

        public BloodlustStatus() { Key = "BloodlustStatus"; }

        public override void OnApply()
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name} is bloodlusted!");
        }

        public override void OnDealDamage(ref int amount, CombatScript.EffectKind kind, int hitTeam, int hitSlot)
        {
            if (amount <= 0) return;
            int boost = Mathf.CeilToInt(amount * DamageBoostPercent);
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] Bloodlust adds +{boost} to {amount} -> {amount + boost}.");
            amount += boost;
        }

        public override void OnExpire()
        {
            if (IsTargetAlive())
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s bloodlust fades.");
        }
    }
}

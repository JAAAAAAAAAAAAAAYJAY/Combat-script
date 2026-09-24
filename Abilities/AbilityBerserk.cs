using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData = CombatScript.UnitData;
using CombatOverride;

[CombatOverride("ability_Berserk")]
public class AbilityBerserk : AbilityBase
{
    public override string OverrideKey => "ability_Berserk";

    const int Duration = 3;
    const int BonusTempHP = 5;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var caster = GetUnit(casterTeam, casterSlot);
        if (string.IsNullOrEmpty(caster.name)) yield break;

        // Berserk is an aggressive self-buff (temp HP + damage boost), so it
        // is intentionally self-cast regardless of any configured coord list.
        CustomStatusRuntime.Apply(casterTeam, casterSlot, new BerserkStatus
        {
            RemainingRounds = Duration,
            SourceTeam = casterTeam,
            SourceSlot = casterSlot,
            TempHPGrant = BonusTempHP,
        });

        if (CombatDebugHandler.UltraDebug)
            Debug.Log($"[Berserk] {caster.name} enters a berserk rage! +{BerserkStatus.DamageBoostPercent*100:0}% multiplicative damage for {Duration} turns.");
        yield return null;
    }

    private class BerserkStatus : CustomStatus
    {
        public const float DamageBoostPercent = 0.5f;
        public int TempHPGrant;

        public BerserkStatus() { Key = "BerserkStatus"; }

        public override void OnApply()
        {
            ModifyTarget((ref CombatScript.UnitData u) =>
            {
                u.th += TempHPGrant;
            });
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name} is berserk! (+{TempHPGrant} temp HP)");
        }

        public override void OnDealDamage(ref int amount, CombatScript.EffectKind kind, int hitTeam, int hitSlot)
        {
            if (amount <= 0) return;
            int boost = Mathf.CeilToInt(amount * DamageBoostPercent);
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] Berserk adds +{boost} to {amount} -> {amount + boost} damage.");
            amount += boost;
        }

        public override void OnExpire()
        {
            // Reverse the temp HP grant from OnApply.
            ModifyTarget((ref CombatScript.UnitData u) =>
            {
                u.th = Mathf.Max(0, u.th - TempHPGrant);
            });
            if (IsTargetAlive())
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s berserk fades.");
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("skill_Frostbite")]
public class SkillFrostbite : SkillBase
{
    public override string OverrideKey => "skill_Frostbite";

    const int    FrostbiteDuration = 3;
    const int    AcPenalty         = 2;       // -2 AC while frostbitten
    const string TickDamage        = "1d4";   // frostburn each turn-end

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        // pick the biggest beef steak enemy in range
        var target = FindHighestHpTarget(action.magicCoords, casterTeam, casterSlot);
        if (target == null) yield break;
        var chosen = target.Value;
        if (!IsUnitAlive(chosen.team, chosen.slot)) yield break;

        var caster = GetUnit(casterTeam, casterSlot);
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        // roll to hit
        int targetAC = Mathf.RoundToInt(combat.GetEffectiveAC(chosen.team, chosen.slot));
        var (hitRoll, outcome) = RollHit(action.hitRoll, targetAC, quiet: true);
        rollsToShow.Add(("Attack", hitRoll));

        if (outcome == HitOutcome.Miss)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Frostbite] {caster.name}'s frost blast missed {chosen.unit.name}!");
            ShowRolls(rollsToShow);
            yield break;
        }
        if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

        // roll the instant magic damage
        bool gotDamage = TryRollAndShowDamage(
            action.magicDmg, outcome, chosen.portion,
            rollsToShow, "Frostbite", out var dmg, showRolls: false);
        if (!gotDamage) { ShowRolls(rollsToShow); yield break; }

        // apply the instant hit
        bool defeated = ApplyDamage(
            chosen.team, chosen.slot, dmg.amount,
            CombatScript.EffectKind.Magic,
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Frostbite] {caster.name} strikes {chosen.unit.name} for {dmg.amount} magic (outcome {outcome}).");

        // curse the survivor with Frostbite
        // The status owns ALL the lingering logic (AC debuff + per-turn DOT)
        // Re-applying later just refreshes the duration
        if (!defeated)
        {
            CustomStatusRuntime.Apply(chosen.team, chosen.slot, new FrostbiteStatus
            {
                RemainingRounds = FrostbiteDuration,
                SourceTeam     = casterTeam,
                SourceSlot     = casterSlot,
            });
        }

        ShowRolls(rollsToShow);
        yield return null;
    }

    //  FrostbiteStatus the custom status
    private class FrostbiteStatus : CustomStatus
    {
        const string StatusKey = "Frostbite";

        public FrostbiteStatus() { Key = StatusKey; }

        public override void OnApply()
        {
            float acBefore = Combat.GetUnit(TargetTeam, TargetSlot).ac;
            ModifyTarget((ref CombatScript.UnitData u) =>
            {
                u.ac -= AcPenalty;
            });
            float acAfter = Combat.GetUnit(TargetTeam, TargetSlot).ac;
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Frostbite] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s AC reduced: {acBefore:0} → {acAfter:0} (penalty {AcPenalty}).");
        }
        public override void OnTurnEndTick()
        {
            if (!IsTargetAlive()) return;
            var roll = Roll(TickDamage);
            if (!roll.HasValue) return;
            int dmg = roll.Value.total;
            // Combat.ApplyDamageToTarget routes through the full damage pipeline
            // (FireModifyDamageDealt + FireOnDealDamage) so passive hooks fire.
            Combat.ApplyDamageToTarget(TargetTeam, TargetSlot, dmg,
                CombatScript.EffectKind.Magic, 0, SourceTeam, SourceSlot);
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Frostbite] {Combat.GetUnit(TargetTeam, TargetSlot).name} takes {dmg} frostburn ({RemainingRounds - 1} turn(s) left).");
        }

        public override void OnExpire()
        {
            ModifyTarget((ref CombatScript.UnitData u) => u.ac += AcPenalty);
        }
    }
}
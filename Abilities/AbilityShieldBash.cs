using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("ability_ShieldBash")]
public class AbilityShieldBash : AbilityBase
{
    public override string OverrideKey => "ability_ShieldBash";

    const string StunChanceDie   = "1d2";   // 50% (success on a 2)
    const int    StunChanceValue = 2;
    // Duration 2 ensures the target loses its next turn even if the
    // round-end status decrement (ProcessUnitStatusEffects) runs before
    // the target's next action. A duration-1 stun applied late in the
    // round would tick down to 0 at end-of-round, never actually
    // skipping the target's turn.
    const int    StunDuration    = 2;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        //pick a target
        ResolvedTarget? target = GetTauntOverrideTarget(casterTeam, casterSlot);
        if (target == null)
            target = FindLowestACTarget(action.physicalCoords, casterTeam, casterSlot);

        if (target == null) yield break;
        var chosen = target.Value;

        if (!IsUnitAlive(chosen.team, chosen.slot)) yield break;

        var caster = GetUnit(casterTeam, casterSlot);
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        //roll to hit
        int targetAC = Mathf.RoundToInt(combat.GetEffectiveAC(chosen.team, chosen.slot));
        var (hitRoll, outcome) = RollHit(action.hitRoll, targetAC, quiet: true);
        rollsToShow.Add(("Attack", hitRoll));

        if (outcome == HitOutcome.Miss)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[ShieldBash] {caster.name} swung at {chosen.unit.name} and missed!");
            ShowRolls(rollsToShow);
            yield break;
        }
        if (outcome == HitOutcome.NoTarget)
            outcome = HitOutcome.Hit;

        //roll damage
        bool gotDamage = TryRollAndShowDamage(
            action.physicalDmg, outcome, chosen.portion,
            rollsToShow, "ShieldBash", out var dmg, showRolls: false);

        if (!gotDamage) { ShowRolls(rollsToShow); yield break; }

        //deal the damage
        bool defeated = ApplyDamage(
            chosen.team, chosen.slot, dmg.amount,
            CombatScript.EffectKind.Physical,
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[ShieldBash] {caster.name} bashes {chosen.unit.name} for {dmg.amount} physical (outcome {outcome}).");

        //stun only if the target lived
        if (defeated) { ShowRolls(rollsToShow); yield break; }


        //crits always stun normal hits have a 50% chance
        bool stunned;

        if (outcome == HitOutcome.Crit || outcome == HitOutcome.CrunchyCrit)
        {
            stunned = true;
        }
        else
        {
            var stunDie = TryRoll(StunChanceDie);
            if (stunDie.HasValue) rollsToShow.Add(("Stun Chance", stunDie.Value));
            stunned = stunDie.HasValue && stunDie.Value.total >= StunChanceValue;
        }

        if (stunned)
        {
            ApplyStatus(
                chosen.team,
                chosen.slot,
                CombatScript.EffectKind.Stun,
                StunDuration,
                sourceTeam: casterTeam,
                sourceSlot: casterSlot);

            if (CombatDebugHandler.UltraDebug) Debug.Log($"[ShieldBash] {chosen.unit.name} is stunned for {StunDuration} turn(s)!");
        }

        ShowRolls(rollsToShow);
        yield return null;
    }
}

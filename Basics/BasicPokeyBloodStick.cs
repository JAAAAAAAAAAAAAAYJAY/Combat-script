using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("basic_PokeyBloodStick")]
public class BasicPokeyBloodStick : BasicBase
{
    public override string OverrideKey => "basic_PokeyBloodStick";

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var rollsToShow = new List<(string, diceSystem.RollResult)>();

        // figure out who the primary target is
        CombatHandlerBase.ResolvedTarget? target;

        target = GetTauntOverrideTarget(casterTeam, casterSlot);
        if (target == null)
        {
            target = FindLowestHpPercentTarget(action.physicalCoords, casterTeam, casterSlot);
        }

        if (target == null)
            yield break;

        var chosenTarget = target.Value;

        int centerRawSlot = ToRawSlot(casterTeam, chosenTarget.team, chosenTarget.slot);

        List<SplashTarget> collateral = BuildSplashTargets(
            centerRawSlot, casterTeam, casterSlot, SplashPattern.PlusOne, 1f, 1f);

        int behindTeam = -1;
        int behindSlot = -1;
        int behindSlotIndex = chosenTarget.slot + 1;
        foreach (var victim in collateral)
        {
            if (victim.slot == behindSlotIndex && victim.team == chosenTarget.team)
            {
                behindTeam = victim.team;
                behindSlot = victim.slot;
                break;
            }
        }

        // ---- roll and apply damage against the PRIMARY target ----
        int targetArmorClass = Mathf.RoundToInt(combat.GetEffectiveAC(chosenTarget.team, chosenTarget.slot));
        var frontHit = RollHit(action.hitRoll, targetArmorClass, quiet: true);

        rollsToShow.Add(("Attack", frontHit.result));

        if (frontHit.outcome != HitOutcome.Miss)
        {
            if (TryRollAndShowDamage(action.physicalDmg, frontHit.outcome, chosenTarget.portion,
                                      rollsToShow, "PokeyBloodStick", out var frontDamage,
                                      showRolls: false))
            {
                ApplyDamage(chosenTarget.team, chosenTarget.slot, frontDamage.amount,
                    CombatScript.EffectKind.Physical,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);
            }
        }

        if (behindTeam != -1)
        {
            int behindArmorClass = Mathf.RoundToInt(combat.GetEffectiveAC(behindTeam, behindSlot));
            var backHit = RollHit(action.hitRoll, behindArmorClass, quiet: true);

            rollsToShow.Add(("Attack (back)", backHit.result));

            if (backHit.outcome != HitOutcome.Miss)
            {
                // crit aware bleed roll no manual halving RollDamageWithCrit handles half miss
                var bleedDmg = RollDamageWithCrit(action.bleedDmg, backHit.outcome, 1f);
                if (bleedDmg.baseRoll.HasValue) rollsToShow.Add(("Bleed", bleedDmg.baseRoll.Value));
                if (bleedDmg.critBonusRoll.HasValue) rollsToShow.Add(("Bleed CRIT", bleedDmg.critBonusRoll.Value));

                if (bleedDmg.amount > 0)
                {
                    ApplyDOT(
                        behindTeam, behindSlot,
                        bleedDmg.amount,
                        CombatScript.EffectKind.BleedDmg,
                        duration: action.bleedDuration,
                        notation: action.bleedDmg,
                        sourceTeam: casterTeam, sourceSlot: casterSlot
                    );
                }
            }
        }

        ShowRolls(rollsToShow);

        yield return null;
    }
}

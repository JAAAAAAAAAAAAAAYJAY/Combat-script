using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("basic_MyBasicName")]
public class BasicMyBasicName : BasicBase
{
    public override string OverrideKey => "basic_MyBasicName"; // This is the override key - put it in the CSV Override column verbatim. The "basic_" prefix IS the classifier: it tells the game which folder/registry (Basics) to look this key up in, same as the "skill_"/"ability_"/"passive_" prefixes for the other three types.

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        // figure out who will be attacked
        CombatHandlerBase.ResolvedTarget? target;

        if (GetTauntOverrideTarget(casterTeam, casterSlot) != null) //use this if you want to respect the taunt
        {
            target = GetTauntOverrideTarget(casterTeam, casterSlot);
        }
        else
        {
            target = FindFirstAliveTarget(action.physicalCoords, casterTeam, casterSlot);
        }

        if (target == null)
            yield break;

        var chosenTarget = target.Value;

        // roll to hit
        int targetArmorClass = Mathf.RoundToInt(combat.GetEffectiveAC(chosenTarget.team, chosenTarget.slot));
        var hitResult = RollHit(action.hitRoll, targetArmorClass, quiet: true);
        var attackRoll = hitResult.result;
        var attackOutcome = hitResult.outcome;

        var rollsToDisplay = new List<(string, diceSystem.RollResult)>();
        rollsToDisplay.Add(("Attack", attackRoll));

        if (attackOutcome == HitOutcome.Miss)
        {
            ShowRolls(rollsToDisplay);
            yield break;
        }

        // ---- STEP 3: roll how much damage we do ----
        bool gotDamage = TryRollAndShowDamage(
            action.physicalDmg, attackOutcome, chosenTarget.portion,
            rollsToDisplay, "MyBasic", out var damageResult,
            showRolls: false
        );

        if (!gotDamage)
        {
            ShowRolls(rollsToDisplay);
            yield break;
        }

        // ---- STEP 4: actually deal the damage ----
        ApplyDamage(
            chosenTarget.team, chosenTarget.slot, damageResult.amount,
            CombatScript.EffectKind.Physical,
            sourceTeam: casterTeam, sourceSlot: casterSlot
        );

        ShowRolls(rollsToDisplay);
        yield break;
}
}
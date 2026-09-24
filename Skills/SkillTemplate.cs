using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;


[CombatOverride("skill_MySkillName")]                   // MUST start with "skill_"
public class SkillMySkillName : SkillBase
{
    public override string OverrideKey => "skill_MySkillName";     // must match the attribute above EXACTLY 

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        // pick a target 
        ResolvedTarget? target = FindFirstAliveTarget(action.physicalCoords, casterTeam, casterSlot);
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
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[MySkill] {caster.name} missed {chosen.unit.name}!");
            ShowRolls(rollsToShow);
            yield break;
        }
        if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

        // roll damage (crit-aware) 
        bool gotDamage = TryRollAndShowDamage(
            action.physicalDmg, outcome, chosen.portion,
            rollsToShow, "MySkill", out var dmg, showRolls: false);
        if (!gotDamage) { ShowRolls(rollsToShow); yield break; }

        // apply the damage 
        bool defeated = ApplyDamage(
            chosen.team, chosen.slot, dmg.amount,
            CombatScript.EffectKind.Physical,        // match your CSV damage column
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        // flush the dice popup 
        ShowRolls(rollsToShow);
        yield return null;
    }
}

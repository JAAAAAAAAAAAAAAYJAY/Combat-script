using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("skill_PoisonDart")]
public class SkillPoisonDart : SkillBase
{
    public override string OverrideKey => "skill_PoisonDart";

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var target = FindFirstAliveTarget(action.physicalCoords, casterTeam, casterSlot);
        if (target == null) yield break;
        var chosen = target.Value;
        if (!IsUnitAlive(chosen.team, chosen.slot)) yield break;

        var caster = GetUnit(casterTeam, casterSlot);
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        int targetAC = Mathf.RoundToInt(combat.GetEffectiveAC(chosen.team, chosen.slot));
        var (hitRoll, outcome) = RollHit(action.hitRoll, targetAC, quiet: true);
        rollsToShow.Add(("Attack", hitRoll));

        if (outcome == HitOutcome.Miss)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[PoisonDart] {caster.name} missed {chosen.unit.name}!");
            ShowRolls(rollsToShow);
            yield break;
        }
        if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

        var dmg = RollDamageWithCrit(action.physicalDmg, outcome, chosen.portion);
        if (dmg.baseRoll.HasValue) rollsToShow.Add(("PoisonDart Dmg", dmg.baseRoll.Value));
        if (dmg.amount > 0)
            ApplyDamage(chosen.team, chosen.slot, dmg.amount, CombatScript.EffectKind.Physical,
                sourceTeam: casterTeam, sourceSlot: casterSlot);

        if (IsUnitAlive(chosen.team, chosen.slot))
        {
            var poisonRoll = TryRoll(action.poisonDmg);
            if (poisonRoll.HasValue)
            {
                rollsToShow.Add(("Poison DOT", poisonRoll.Value));
                ApplyDOT(chosen.team, chosen.slot, poisonRoll.Value.total,
                    CombatScript.EffectKind.PoisonDmg, action.poisonDuration, action.poisonDmg,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[PoisonDart] {chosen.unit.name} is poisoned for {action.poisonDuration} turns!");
            }
        }

        ShowRolls(rollsToShow);
        yield return null;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("basic_Haymaker")]
public class BasicHaymaker : BasicBase
{
    public override string OverrideKey => "basic_Haymaker";

    // Roll 1d100 and succeed on >= StunChance. For exactly 50% use 51 (rolls 51-100).
    const int StunChance = 51;
    const int StunDuration = 2;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var target = FindLowestHpTarget(action.physicalCoords, casterTeam, casterSlot);
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
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Haymaker] {caster.name} missed {chosen.unit.name}!");
            ShowRolls(rollsToShow);
            yield break;
        }
        if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

        var dmg = RollDamageWithCrit(action.physicalDmg, outcome, chosen.portion);
        if (dmg.baseRoll.HasValue) rollsToShow.Add(("Haymaker Dmg", dmg.baseRoll.Value));
        if (dmg.critBonusRoll.HasValue) rollsToShow.Add(("Haymaker CRIT", dmg.critBonusRoll.Value));

        if (dmg.amount <= 0) { ShowRolls(rollsToShow); yield break; }

        bool defeated = ApplyDamage(chosen.team, chosen.slot, dmg.amount,
            CombatScript.EffectKind.Physical,
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        if (defeated) { ShowRolls(rollsToShow); yield break; }

        if (TryApplyChanceEffect("1d100", StunChance,
            chosen.team, chosen.slot,
            CombatScript.EffectKind.Stun, StunDuration,
            casterTeam, casterSlot))
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Haymaker] {chosen.unit.name} is stunned by the blow!");
        }

        ShowRolls(rollsToShow);
        yield return null;
    }
}

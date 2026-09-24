using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("ability_PiercingArrow")]
public class AbilityPiercingArrow : AbilityBase
{
    public override string OverrideKey => "ability_PiercingArrow";

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var targets = GetAliveTargets(action.physicalCoords, casterTeam, casterSlot);
        if (targets.Count == 0) yield break;

        var caster = GetUnit(casterTeam, casterSlot);
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        foreach (var t in targets)
        {
            if (!IsUnitAlive(t.team, t.slot)) continue;

            int targetAC = Mathf.RoundToInt(combat.GetEffectiveAC(t.team, t.slot));
            var (hitRoll, outcome) = RollHit(action.hitRoll, targetAC, quiet: true);
            rollsToShow.Add(("Attack→" + t.unit.name, hitRoll));

            if (outcome == HitOutcome.Miss)
            {
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[PiercingArrow] {caster.name} missed {t.unit.name}!");
                continue;
            }
            if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

            var dmg = RollDamageWithCrit(action.physicalDmg, outcome, t.portion);
            if (dmg.baseRoll.HasValue) rollsToShow.Add(("Arrow→" + t.unit.name, dmg.baseRoll.Value));
            if (dmg.critBonusRoll.HasValue) rollsToShow.Add(("Arrow CRIT→" + t.unit.name, dmg.critBonusRoll.Value));
            if (dmg.amount > 0)
                ApplyDamage(t.team, t.slot, dmg.amount, CombatScript.EffectKind.Physical,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);
        }

        ShowRolls(rollsToShow);
        yield return null;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("skill_Fireball")]
public class SkillFireball : SkillBase
{
    public override string OverrideKey => "skill_Fireball";

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var targets = GetAliveTargets(action.fireCoords, casterTeam, casterSlot);
        if (targets.Count == 0) yield break;

        var caster = GetUnit(casterTeam, casterSlot);

        // per-target hit roll + DOT only on hit targets avoids dot-on-miss
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        foreach (var t in targets)
        {
            if (!IsUnitAlive(t.team, t.slot)) continue;

            int targetAC = Mathf.RoundToInt(combat.GetEffectiveAC(t.team, t.slot));
            var (hitRoll, outcome) = RollHit(action.hitRoll, targetAC, quiet: true);
            rollsToShow.Add(("Attack", hitRoll));

            if (outcome == HitOutcome.Miss)
            {
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[Fireball] {caster.name} missed {t.unit.name}!");
                continue;
            }
            if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

            var dmg = RollDamageWithCrit(action.fireDmg, outcome, t.portion);
            if (dmg.baseRoll.HasValue) rollsToShow.Add(("Fireball Dmg", dmg.baseRoll.Value));
            if (dmg.critBonusRoll.HasValue) rollsToShow.Add(("Fireball CRIT", dmg.critBonusRoll.Value));

            if (dmg.amount > 0)
                ApplyDamage(t.team, t.slot, dmg.amount, CombatScript.EffectKind.FireDmg,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);

            // dot only on this target since it was hit
            ApplyDOT(t.team, t.slot, 0, CombatScript.EffectKind.FireDmg,
                action.fireDuration, action.fireDmg,
                sourceTeam: casterTeam, sourceSlot: casterSlot);

            if (CombatDebugHandler.UltraDebug)
                Debug.Log($"[Fireball] {caster.name} hits {t.unit.name} for {dmg.amount} fire (outcome {outcome}) and ignites a burn.");
        }

        ShowRolls(rollsToShow);
        yield return null;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData    = CombatScript.UnitData;
using CombatOverride;

[CombatOverride("skill_Drain")]
public class SkillDrain : SkillBase
{
    public override string OverrideKey => "skill_Drain";

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
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Drain] {caster.name} missed {chosen.unit.name}!");
            ShowRolls(rollsToShow);
            yield break;
        }
        if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

        var dmg = RollDamageWithCrit(action.physicalDmg, outcome, chosen.portion);
        if (dmg.baseRoll.HasValue) rollsToShow.Add(("Drain Dmg", dmg.baseRoll.Value));
        if (dmg.critBonusRoll.HasValue) rollsToShow.Add(("Drain CRIT", dmg.critBonusRoll.Value));

        ShowRolls(rollsToShow);

        if (dmg.amount <= 0) yield break;

        UnitData targetBefore = GetUnit(chosen.team, chosen.slot);
        float hpBefore = targetBefore.hp;

        ApplyDamage(chosen.team, chosen.slot, dmg.amount,
            CombatScript.EffectKind.Physical,
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        UnitData targetAfter = GetUnit(chosen.team, chosen.slot);
        float hpAfter = Mathf.Max(0f, targetAfter.hp);
        float actualHpLost = Mathf.Max(0f, hpBefore - hpAfter);

        var healRoll = TryRoll(action.recoverHealth);
        if (healRoll.HasValue && healRoll.Value.total > 0 && actualHpLost > 0f)
        {
            int heal = Mathf.Min(healRoll.Value.total, Mathf.CeilToInt(actualHpLost));
            if (heal > 0)
            {
                ApplyDamage(casterTeam, casterSlot, heal,
                    CombatScript.EffectKind.RecoverHealth,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[Drain] {caster.name} drains {heal} HP from {chosen.unit.name} (actual HP lost: {actualHpLost:0}).");
            }
        }
        yield return null;
    }
}

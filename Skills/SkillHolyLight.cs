using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;
using UnitData = CombatScript.UnitData;

[CombatOverride("skill_HolyLight")]
public class SkillHolyLight : SkillBase
{
    public override string OverrideKey => "skill_HolyLight";

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var caster = GetUnit(casterTeam, casterSlot);
        if (string.IsNullOrEmpty(caster.name)) yield break;

        ResolvedTarget? target = FindLowestHpPercentTarget(action.recoverHealthCoords, casterTeam, casterSlot);
        if (target == null)
            target = FindLowestHpPercentTarget_Team(casterTeam);
        if (target == null) yield break;
        var chosen = target.Value;
        if (!IsUnitAlive(chosen.team, chosen.slot)) yield break;

        var roll = TryRoll(action.recoverHealth);
        if (!roll.HasValue) yield break;

        // accumulate all rolls in one list and flush once at the end
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();
        rollsToShow.Add(("Holy Light Heal", roll.Value));

        int heal = roll.Value.total;

        float hpBefore = chosen.unit.hp;
        ApplyDamage(chosen.team, chosen.slot, heal,
            CombatScript.EffectKind.RecoverHealth,
            sourceTeam: casterTeam, sourceSlot: casterSlot);
        UnitData afterHeal = GetUnit(chosen.team, chosen.slot);
        int actualHealed = Mathf.Max(0, Mathf.CeilToInt(afterHeal.hp - hpBefore));

        if (CombatDebugHandler.UltraDebug)
            Debug.Log($"[HolyLight] {caster.name} bathes {chosen.unit.name} in holy light for {heal} HP (rolled) -> {actualHealed} HP (actual).");

        if (IsUnitAlive(chosen.team, chosen.slot) && actualHealed > 0)
        {
            int enemyTeam = GetEnemyTeam(casterTeam);
            for (int s = 0; s < 4; s++)
            {
                UnitData u = GetUnit(enemyTeam, s);
                if (string.IsNullOrEmpty(u.name) || u.hp <= 0) continue;

                // AUD-039 capture sear roll so it shows in the dice popup
                var searRoll = TryRoll("1d2");
                if (!searRoll.HasValue || searRoll.Value.total < 2) continue;
                rollsToShow.Add(($"Sear→{u.name}", searRoll.Value));

                int searDamage = Mathf.CeilToInt(actualHealed * 0.25f);
                ApplyDamage(enemyTeam, s, searDamage,
                    CombatScript.EffectKind.Magic,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[HolyLight] Holy light sears {u.name} for {searDamage} magic damage (scaled from {actualHealed} actual HP healed).");
            }
        }

        ShowRolls(rollsToShow);
        yield return null;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("skill_Heal")]
public class SkillHeal : SkillBase
{
    public override string OverrideKey => "skill_Heal";

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

        int heal = roll.Value.total;
        ApplyDamage(chosen.team, chosen.slot, heal,
            CombatScript.EffectKind.RecoverHealth,
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Heal] {caster.name} heals {chosen.unit.name} for {heal} HP.");
        var rollsToShow = new List<(string label, diceSystem.RollResult)> { ("Heal", roll.Value) };
        ShowRolls(rollsToShow);
        yield return null;
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("ability_LayOnHands")]
public class AbilityLayOnHands : AbilityBase
{
    public override string OverrideKey => "ability_LayOnHands";

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var caster = GetUnit(casterTeam, casterSlot);
        if (string.IsNullOrEmpty(caster.name)) yield break;

        // AUD-040 consult configured coords first then fall back to team wide
        ResolvedTarget? target = FindLowestHpPercentTarget(action.recoverHealthCoords, casterTeam, casterSlot);
        if (target == null)
            target = FindLowestHpPercentTarget_Team(casterTeam);
        if (target == null) yield break;
        var chosen = target.Value;
        if (!IsUnitAlive(chosen.team, chosen.slot)) yield break;

        // Skip if the target is already at full HP - the heal would be entirely wasted.
        if (chosen.unit.maxHp > 0 && chosen.unit.hp >= chosen.unit.maxHp)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[LayOnHands] {chosen.unit.name} is already at full HP - Lay on Hands skipped.");
            yield break;
        }

        int healAmount = Mathf.CeilToInt(chosen.unit.maxHp * 0.5f);
        ApplyDamage(chosen.team, chosen.slot, healAmount,
            CombatScript.EffectKind.RecoverHealth,
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[LayOnHands] {caster.name} heals {chosen.unit.name} for {healAmount} HP.");
        yield return null;
    }
}

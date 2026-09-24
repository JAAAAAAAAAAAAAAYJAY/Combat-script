using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("skill_Taunt")]
public class SkillTaunt : SkillBase
{
    public override string OverrideKey => "skill_Taunt";

    // uses physicalCoords as custom coords column in csv
    const int TauntDuration = 3;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var targets = GetAliveTargets(action.physicalCoords, casterTeam, casterSlot);
        if (targets.Count == 0) yield break;

        var caster = GetUnit(casterTeam, casterSlot);

        foreach (var t in targets)
        {
            if (!IsUnitAlive(t.team, t.slot)) continue;
            int casterRawSlotFromEnemyPOV = ToRawSlot(t.team, casterTeam, casterSlot);
            Combat.SetTaunt(t.team, t.slot, casterRawSlotFromEnemyPOV, TauntDuration);
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Taunt] {caster.name} taunts {t.unit.name} for {TauntDuration} turns (forced to attack caster at raw slot {casterRawSlotFromEnemyPOV} from enemy POV).");
        }
        yield return null;
    }
}

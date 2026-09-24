using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("skill_PiercingShot")]
public class SkillPiercingShot : SkillBase
{
    public override string OverrideKey => "skill_PiercingShot";

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var targets = GetAliveTargets(action.physicalCoords, casterTeam, casterSlot);
        if (targets.Count == 0) yield break;

        var caster = GetUnit(casterTeam, casterSlot);

        yield return ResolveGroupAttack(
            action, casterTeam, casterSlot, targets,
            action.physicalDmg, CombatScript.EffectKind.Physical,
            action.hitRoll, "PiercingShot");

        if (CombatDebugHandler.UltraDebug)
            Debug.Log($"[PiercingShot] {caster.name} pierces through {targets.Count} target(s).");
    }
}

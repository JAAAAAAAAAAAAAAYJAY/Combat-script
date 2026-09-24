using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("skill_Bless")]
public class SkillBless : SkillBase
{
    public override string OverrideKey => "skill_Bless";

    // uses physicalCoords as custom coords column in csv
    const int BuffDuration = 3;
    const int BonusAC = 2;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var targets = GetAliveTargets(action.physicalCoords, casterTeam, casterSlot);
        if (targets.Count == 0) yield break;

        var caster = GetUnit(casterTeam, casterSlot);

        foreach (var t in targets)
        {
            if (!IsUnitAlive(t.team, t.slot)) continue;
            CustomStatusRuntime.Apply(t.team, t.slot, new BlessStatus
            {
                RemainingRounds = BuffDuration,
                SourceTeam = casterTeam,
                SourceSlot = casterSlot,
            });
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Bless] {caster.name} blesses {t.unit.name} (+{BonusAC} AC for {BuffDuration} turns).");
        }
        yield return null;
    }

    private class BlessStatus : CustomStatus
    {
        public int BonusAC => SkillBless.BonusAC;

        public BlessStatus() { Key = "BlessStatus"; }

        public override void OnApply()
        {
            ModifyTarget((ref CombatScript.UnitData u) => { u.ac += BonusAC; });
        }

        public override void OnExpire()
        {
            ModifyTarget((ref CombatScript.UnitData u) => { u.ac = Mathf.Max(0, u.ac - BonusAC); });
            if (IsTargetAlive())
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s blessing fades.");
        }
    }
}

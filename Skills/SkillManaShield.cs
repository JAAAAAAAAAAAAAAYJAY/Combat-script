using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("skill_ManaShield")]
public class SkillManaShield : SkillBase
{
    public override string OverrideKey => "skill_ManaShield";

    const int ShieldDuration = 3;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var targets = GetAliveTargets(action.addShieldCoords, casterTeam, casterSlot);
        if (targets.Count == 0) yield break;

        var caster = GetUnit(casterTeam, casterSlot);

        foreach (var t in targets)
        {
            if (!IsUnitAlive(t.team, t.slot)) continue;
            var roll = TryRoll(action.addShield);
            if (roll.HasValue && roll.Value.total > 0)
            {
                CustomStatusRuntime.Apply(t.team, t.slot, new ManaShieldStatus
                {
                    RemainingRounds = ShieldDuration,
                    SourceTeam = casterTeam,
                    SourceSlot = casterSlot,
                    ShieldAmount = roll.Value.total,
                });

                if (CombatDebugHandler.UltraDebug) Debug.Log($"[ManaShield] {caster.name} shields {t.unit.name} for {roll.Value.total} ({ShieldDuration} turns).");
            }
        }
        yield return null;
    }

    private class ManaShieldStatus : CustomStatus
    {
        public int ShieldAmount;

        public ManaShieldStatus() { Key = "ManaShieldStatus"; }

        public override void OnApply()
        {
            ModifyTarget((ref CombatScript.UnitData u) =>
            {
                u.shield += ShieldAmount;
            });
            if (CombatDebugHandler.UltraDebug)
                Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name} gains {ShieldAmount} temporary shield.");
        }

        public override void OnExpire()
        {
            ModifyTarget((ref CombatScript.UnitData u) =>
            {
                int removable = Mathf.Min(ShieldAmount, Mathf.Max(0, Mathf.RoundToInt(u.shield)));
                u.shield = Mathf.Max(0f, u.shield - removable);
            });
            if (IsTargetAlive())
                if (CombatDebugHandler.UltraDebug)
                    Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s mana shield expires.");
        }
    }
}

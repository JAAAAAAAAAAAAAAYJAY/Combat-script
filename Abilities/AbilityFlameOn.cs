using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("ability_FlameOn")]
public class AbilityFlameOn : AbilityBase
{
    public override string OverrideKey => "ability_FlameOn";

    const int    BrandDuration   = 3;
    const string BonusFireDie    = "1d2";
    const int    FireDotDuration = 2;
    const string FireDotTickDie  = "1d2";

    [Tooltip("Only units whose army code matches this filter get ignited. Empty = all armies (default; CSV coords already control targeting).")]
    [SerializeField] string armyFilter = "";

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        List<ResolvedTarget> targets = GetAliveTargets(action.physicalCoords, casterTeam, casterSlot);
        if (targets.Count == 0) yield break;

        var caster = GetUnit(casterTeam, casterSlot);

        foreach (var t in targets)
        {
            if (!IsUnitAlive(t.team, t.slot)) continue;
            string army = GetArmy(t.unit.unitcode);
            if (string.IsNullOrEmpty(armyFilter) || army == armyFilter)
            {
                CustomStatusRuntime.Apply(t.team, t.slot, new FlameOnStatus
                {
                    RemainingRounds = BrandDuration,
                    SourceTeam     = casterTeam,
                    SourceSlot     = casterSlot,
                    BonusDamageDie = BonusFireDie,
                    DotDuration    = FireDotDuration,
                    DotTickDie     = FireDotTickDie,
                });

                if (CombatDebugHandler.UltraDebug) Debug.Log($"[IgniteWeapons] {caster.name} ignites {t.unit.name}'s weapon for {BrandDuration} turn(s).");
            }
        }

        yield return null;
    }

    private class FlameOnStatus : CustomStatus
    {
        const string StatusKey = "FlameOnStatus";

        public string BonusDamageDie;
        public int    DotDuration;
        public string DotTickDie;

        public FlameOnStatus() { Key = StatusKey; }

        public override void OnApply()
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s attacks are now flamed on.");
        }

        // AUD-019 damage proc moved to PassiveFlameOn this status just tracks duration now

        public override void OnExpire()
        {
            if (IsTargetAlive())
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s Flames no longer on.");
        }
    }
}

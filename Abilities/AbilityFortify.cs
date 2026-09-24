using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("ability_Fortify")]
public class AbilityFortify : AbilityBase
{
    public override string OverrideKey => "ability_Fortify";

    const int Duration = 3;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var caster = GetUnit(casterTeam, casterSlot);
        if (string.IsNullOrEmpty(caster.name)) yield break;

        CustomStatusRuntime.Apply(casterTeam, casterSlot, new FortifyStatus
        {
            RemainingRounds = Duration,
            SourceTeam = casterTeam,
            SourceSlot = casterSlot,
        });

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Fortify] {caster.name} fortifies! +{FortifyStatus.BonusArmor} armor +{FortifyStatus.BonusShield} shield for {Duration} turns.");
        yield return null;
    }

    private class FortifyStatus : CustomStatus
    {
        public const int BonusArmor = 3;
        public const int BonusShield = 3;

        int grantedArmor;
        int grantedShield;

        public FortifyStatus() { Key = "FortifyStatus"; }

        public override void OnApply()
        {
            grantedArmor = BonusArmor;
            grantedShield = BonusShield;
            ModifyTarget((ref CombatScript.UnitData u) =>
            {
                u.armor += grantedArmor;
                u.shield += grantedShield;
            });
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name} is fortified (+{grantedArmor} AR, +{grantedShield} SH).");
        }

        public override void OnExpire()
        {
            ModifyTarget((ref CombatScript.UnitData u) =>
            {
                u.armor = Mathf.Max(0, u.armor - grantedArmor);
                u.shield = Mathf.Max(0, u.shield - grantedShield);
            });
            if (IsTargetAlive())
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s fortification wears off.");
        }
    }
}

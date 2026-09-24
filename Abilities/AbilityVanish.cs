using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

// Design decision (Phase 4):
//   Vanish grants the caster stealth for `StealthDuration` turns. Stealth is
//   implemented as the VanishStatus custom status, which:
//     - grants +BonusAC for the duration (defensive cooldown), and
//     - is queryable via CombatScript.IsUnitVanished(team, slot).
//   The AI (CombatAi.GenerateActions) in Phase 5 will consult
//   IsUnitVanished to skip vanished units when picking targets - effectively
//   making the unit untargetable while stealthed.
//
//   "Breaks on damage" / "breaks on attack" semantics are NOT implemented in
//   this pass - Vanish is treated as a pure defensive cooldown that runs its
//   full `StealthDuration` regardless of subsequent actions. This keeps the
//   interaction surface simple for the Phase 5 AI integration.
[CombatOverride("ability_Vanish")]
public class AbilityVanish : AbilityBase
{
    public override string OverrideKey => "ability_Vanish";

    const int StealthDuration = 2;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var caster = GetUnit(casterTeam, casterSlot);
        if (string.IsNullOrEmpty(caster.name)) yield break;

        // Apply the VanishStatus custom status. This both grants the +AC buff
        // (via OnApply) and registers the "VanishStatus" key so
        // CombatScript.IsUnitVanished returns true for the duration.
        CustomStatusRuntime.Apply(casterTeam, casterSlot, new VanishStatus
        {
            RemainingRounds = StealthDuration,
            SourceTeam = casterTeam,
            SourceSlot = casterSlot,
        });

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Vanish] {caster.name} vanishes into the shadows! +{VanishStatus.BonusAC} AC for {StealthDuration} turns. (AI will skip targeting them via CombatScript.IsUnitVanished.)");
        yield return null;
    }

    private class VanishStatus : CustomStatus
    {
        public const int BonusAC = 5;

        int grantedAC;

        public VanishStatus() { Key = "VanishStatus"; }

        public override void OnApply()
        {
            grantedAC = BonusAC;
            ModifyTarget((ref CombatScript.UnitData u) => { u.ac += grantedAC; });
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name} vanishes (+{grantedAC} AC, stealthed).");
        }

        public override void OnExpire()
        {
            ModifyTarget((ref CombatScript.UnitData u) => { u.ac = Mathf.Max(0, u.ac - grantedAC); });
            if (IsTargetAlive())
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name} reappears.");
        }
    }
}

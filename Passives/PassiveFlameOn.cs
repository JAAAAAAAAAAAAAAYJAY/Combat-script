using UnityEngine;
using CombatOverride;

// AUD-019 flame on proc moved out of FlameOnStatus into this passive
// the status now just tracks duration this passive adds the bonus fire and dot
// requires the unit to list passive_FlameOn in its passiveKeys in the CSV
[Passive("passive_FlameOn")]
public class PassiveFlameOn : PassiveBase
{
    public override string OverrideKey => "passive_FlameOn";

    const string StatusKey       = "FlameOnStatus";
    const string BonusFireDie    = "1d2";
    const int    FireDotDuration = 2;
    const string FireDotTickDie  = "1d2";

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[FlameOn] Attached to {GetOwnerUnit().name}.");
    }

    public override void ModifyDamageDealt(CombatScript combat, int attackerTeam, int attackerSlot,
                                           int targetTeam, int targetSlot, ref int amount,
                                           CombatScript.EffectKind kind)
    {
        // only the owner's outgoing physical or magic hits trigger the proc
        if (attackerTeam != OwnerTeam || attackerSlot != OwnerSlot) return;
        if (amount <= 0) return;
        if (kind != CombatScript.EffectKind.Physical && kind != CombatScript.EffectKind.Magic) return;

        // only proc while the flame on status is active on the owner
        if (!CustomStatusRuntime.IsActive(OwnerTeam, OwnerSlot, StatusKey)) return;

        // reentry guard one proc per target per damage event
        string procKey = targetTeam + ":" + targetSlot;
        if (!combat.TryProc(this, procKey)) return;

        var hitUnit = combat.GetUnit(targetTeam, targetSlot);
        if (string.IsNullOrEmpty(hitUnit.name) || hitUnit.hp <= 0) return;

        var roll = combat.TryRoll(BonusFireDie);
        if (roll.HasValue && roll.Value.total > 0)
        {
            combat.ApplyProcDamage(targetTeam, targetSlot, roll.Value.total,
                CombatScript.EffectKind.FireDmg,
                OwnerTeam, OwnerSlot);
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[FlameOn] {GetOwnerUnit().name}'s attack burns {hitUnit.name} for {roll.Value.total} bonus fire.");
        }

        // fire dot on whoever got hit keep stronger notation via ResolveDotNotationPublic
        if (FireDotDuration > 0)
        {
            var u = combat.GetUnit(targetTeam, targetSlot);
            if (!string.IsNullOrEmpty(u.name) && u.hp > 0)
            {
                u.fireRemainingRounds = Mathf.Max(u.fireRemainingRounds, FireDotDuration);
                u.fireDamageNotation = Combat.ResolveDotNotationPublic(
                    u.fireRemainingRounds, u.fireDamageNotation, FireDotTickDie);
                combat.SetUnit(targetTeam, targetSlot, u);
            }
        }
    }
}

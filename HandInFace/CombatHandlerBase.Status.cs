using UnityEngine;
using UnitData = CombatScript.UnitData;

public abstract partial class CombatHandlerBase
{
    // apply status with alive and kind guards
    protected bool ApplyStatus(int targetTeam, int targetSlot,
        CombatScript.EffectKind kind, int duration,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        if (!IsStatusKind(kind))
        {
            Debug.LogWarning($"[ApplyStatus] {kind} is not a status kind use ApplyDamage or ApplyDOT instead");
            return false;
        }
        if (duration <= 0)
        {
            if (UltraDebug) Debug.Log($"[ApplyStatus] duration {duration} skip");
            return false;
        }
        if (!IsUnitAlive(targetTeam, targetSlot))
        {
            if (UltraDebug) Debug.Log($"[ApplyStatus] target at {targetTeam},{targetSlot} is dead skip");
            return false;
        }
        return Combat.ApplyDamageToTarget(targetTeam, targetSlot, 0, kind, duration, sourceTeam, sourceSlot);
    }

    // apply dot with kind alive and amount guards
    protected void ApplyDOT(int targetTeam, int targetSlot,
        int amount, CombatScript.EffectKind kind, int duration, string notation,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        if (!IsDotKind(kind))
        {
            Debug.LogWarning($"[ApplyDOT] {kind} is not a DOT kind use FireDmg or BleedDmg or PoisonDmg");
            return;
        }

        if (!IsUnitAlive(targetTeam, targetSlot)) return;

        if (amount < 0) amount = 0;

        if (amount > 0)
            ApplyDamage(targetTeam, targetSlot, amount, kind, duration: 0, sourceTeam, sourceSlot);

        // store the dot bookkeeping so the end of turn tick fires
        if (duration > 0 && !string.IsNullOrWhiteSpace(notation))
        {
            UnitData u = GetUnit(targetTeam, targetSlot);
            if (IsUnitAlive(u))
            {
                switch (kind)
                {
                    case CombatScript.EffectKind.FireDmg:
                        u.fireRemainingRounds = Mathf.Max(u.fireRemainingRounds, duration);
                        u.fireDamageNotation = Combat.ResolveDotNotationPublic(u.fireRemainingRounds, u.fireDamageNotation, notation);
                        break;
                    case CombatScript.EffectKind.BleedDmg:
                        u.bleedRemainingRounds = Mathf.Max(u.bleedRemainingRounds, duration);
                        u.bleedDamageNotation = Combat.ResolveDotNotationPublic(u.bleedRemainingRounds, u.bleedDamageNotation, notation);
                        break;
                    case CombatScript.EffectKind.PoisonDmg:
                        u.poisonRemainingRounds = Mathf.Max(u.poisonRemainingRounds, duration);
                        u.poisonDamageNotation = Combat.ResolveDotNotationPublic(u.poisonRemainingRounds, u.poisonDamageNotation, notation);
                        break;
                }
                SetUnit(targetTeam, targetSlot, u);
                if (UltraDebug) Debug.Log($"[ApplyDOT] {u.name} gains {kind} for {duration} turns ({notation})");
            }
        }
    }

    // roll percentage with notation validation
    protected bool RollPercentage(string dieNotation, int successValue)
    {
        if (string.IsNullOrWhiteSpace(dieNotation) || !Combat.IsDiceNotationPublic(dieNotation))
        {
            if (UltraDebug) Debug.Log($"[RollPercentage] bad notation {dieNotation} auto fail");
            return false;
        }
        var roll = TryRoll(dieNotation);
        if (!roll.HasValue) return false;
        return roll.Value.total >= successValue;
    }

    protected bool TryApplyChanceEffect(string dieNotation, int successValue,
        int targetTeam, int targetSlot,
        CombatScript.EffectKind kind, int duration,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        bool success = RollPercentage(dieNotation, successValue);
        if (!success) return false;
        return ApplyStatus(targetTeam, targetSlot, kind, duration, sourceTeam, sourceSlot);
    }
}

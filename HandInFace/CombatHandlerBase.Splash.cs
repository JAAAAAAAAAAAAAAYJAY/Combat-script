using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData    = CombatScript.UnitData;

public abstract partial class CombatHandlerBase
{
    protected enum SplashPattern { CenterOnly, PlusOne, MinusOne, PlusAndMinus }

    protected struct SplashTarget
    {
        public int      team;
        public int      slot;
        public float    damagePercent; // 0-1
    }

    protected static List<int> GetSplashSlots(int centerSlot, SplashPattern pattern)
    {
        var slots = new List<int>();
        if (centerSlot < 0 || centerSlot > 3) return slots;

        switch (pattern)
        {
            case SplashPattern.CenterOnly:
                slots.Add(centerSlot);
                break;

            case SplashPattern.PlusOne:
                slots.Add(centerSlot);
                if (centerSlot + 1 <= 3) slots.Add(centerSlot + 1);
                break;

            case SplashPattern.MinusOne:
                if (centerSlot - 1 >= 0) slots.Add(centerSlot - 1);
                slots.Add(centerSlot);
                break;

            case SplashPattern.PlusAndMinus:
                if (centerSlot - 1 >= 0) slots.Add(centerSlot - 1);
                slots.Add(centerSlot);
                if (centerSlot + 1 <= 3) slots.Add(centerSlot + 1);
                break;
        }
        return slots;
    }

    protected List<SplashTarget> BuildSplashTargets(
        int centerRawSlot, int casterTeam, int casterSlot,
        SplashPattern pattern, float centerPercent, float sidePercent)
    {
        var result = new List<SplashTarget>();
        var (centerTeam, centerSlot) = Combat.ResolveTarget(casterTeam, centerRawSlot);

        foreach (int s in GetSplashSlots(centerSlot, pattern))
        {
            UnitData u = GetUnit(centerTeam, s);
            if (!IsUnitAlive(u)) continue;
            result.Add(new SplashTarget
            {
                team           = centerTeam,
                slot           = s,
                damagePercent  = (s == centerSlot) ? centerPercent : sidePercent
            });
        }
        return result;
    }

    protected IEnumerator ResolveSplashDamage(
        ActionData action,
        int casterTeam, int casterSlot,
        int centerRawSlot,
        SplashPattern pattern,
        float centerPercent, float sidePercent,
        string damageNotation,
        CombatScript.EffectKind kind,
        string hitRollNotation = null,
        float basePortion = 1f,
        string label = null)
    {
        var victims = BuildSplashTargets(centerRawSlot, casterTeam, casterSlot,
                                         pattern, centerPercent, sidePercent);
        if (victims.Count == 0) yield break;

        string lbl = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label;
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        bool useHitRoll = !string.IsNullOrWhiteSpace(hitRollNotation) &&
                          Combat.IsDiceNotationPublic(hitRollNotation);

        for (int i = 0; i < victims.Count; i++)
        {
            var v = victims[i];
            UnitData u = GetUnit(v.team, v.slot);
            if (!IsUnitAlive(u)) continue;

            HitOutcome outcome = HitOutcome.Hit;
            diceSystem.RollResult hitRoll = default;

            if (useHitRoll)
            {
                int ac = Mathf.RoundToInt(Combat.GetEffectiveAC(v.team, v.slot));
                var (h, o) = RollHit(hitRollNotation, ac, quiet: true);
                hitRoll = h;
                outcome = o == HitOutcome.NoTarget ? HitOutcome.Hit : o;
                rollsToShow.Add(($"{lbl} Attack→{u.name}", hitRoll));

                if (outcome == HitOutcome.Miss)
                {
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"[{lbl}] splash missed {u.name}!");
                    continue;
                }
            }

            float portion = basePortion * v.damagePercent;
            var dmg = RollDamageWithCrit(damageNotation, outcome, portion);
            if (dmg.baseRoll.HasValue)      rollsToShow.Add(($"{lbl} Dmg→{u.name}", dmg.baseRoll.Value));
            if (dmg.critBonusRoll.HasValue) rollsToShow.Add(($"{lbl} CRIT→{u.name}", dmg.critBonusRoll.Value));

            if (dmg.amount <= 0) continue;

            ApplyDamage(v.team, v.slot, dmg.amount, kind,
                sourceTeam: casterTeam, sourceSlot: casterSlot);

            if (UltraDebug)
                Debug.Log($"[{lbl}] splash {GetUnit(casterTeam, casterSlot).name} → {u.name} for {dmg.amount} {kind} ({v.damagePercent*100:0}% of base, outcome {outcome}).");
        }

        ShowRolls(rollsToShow);
        yield return null;
    }
}

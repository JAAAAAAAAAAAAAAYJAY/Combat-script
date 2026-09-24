using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData    = CombatScript.UnitData;

public abstract partial class CombatHandlerBase
{
    protected struct DamageRollResult
    {
        public int                       amount;
        public diceSystem.RollResult?    baseRoll;
        public diceSystem.RollResult?    critBonusRoll;
    }

    protected DamageRollResult RollDamageWithCrit(string baseNotation, HitOutcome outcome, float portion)
    {
        var result = new DamageRollResult();
        if (string.IsNullOrWhiteSpace(baseNotation) || !Combat.IsDiceNotationPublic(baseNotation))
        {
            Debug.LogWarning($"[RollDamageWithCrit] damage notation '{baseNotation}' is empty or invalid zero damage");
            return result;
        }

        var dmg = TryRoll(baseNotation);
        if (!dmg.HasValue) return result;
        result.baseRoll = dmg.Value;

        int amount = Mathf.CeilToInt(dmg.Value.total * portion);

        if (outcome == HitOutcome.CrunchyCrit)
        {
            // nat 20 max plus bonus roll
            int maxDmg = Mathf.CeilToInt(Combat.DiceRollerPublic.MaxPossible(baseNotation) * portion);
            amount = maxDmg;
            var bonus = TryRoll(baseNotation);
            if (bonus.HasValue)
            {
                result.critBonusRoll = bonus.Value;
                int bonusAmount = Mathf.CeilToInt(bonus.Value.total * portion);
                amount += bonusAmount;
                if (UltraDebug) Debug.Log($"[RollDamageWithCrit] NAT20 max {maxDmg} + bonus {bonus.Value.total} -> {amount}");
            }
            else if (UltraDebug) Debug.Log($"[RollDamageWithCrit] NAT20 -> max {amount}");
        }
        else if (outcome == HitOutcome.Crit)
        {
            // soft crit max only no bonus
            amount = Mathf.CeilToInt(Combat.DiceRollerPublic.MaxPossible(baseNotation) * portion);
            if (UltraDebug) Debug.Log($"[RollDamageWithCrit] soft crit -> max {amount}");
        }
        else if (outcome == HitOutcome.HalfMiss)
        {
            amount = Mathf.CeilToInt(amount / 2f);
            if (UltraDebug) Debug.Log($"[RollDamageWithCrit] glancing -> half {amount}");
        }

        result.amount = amount;
        return result;
    }

    protected bool TryRollAndShowDamage(
        string damageNotation,
        HitOutcome outcome,
        float portion,
        List<(string label, diceSystem.RollResult)> rolls,
        string label,
        out DamageRollResult dmg,
        bool showRolls = true)
    {
        dmg = RollDamageWithCrit(damageNotation, outcome, portion);

        if (dmg.baseRoll.HasValue)      rolls.Add(($"{label} Damage", dmg.baseRoll.Value));
        if (dmg.critBonusRoll.HasValue) rolls.Add(($"{label} CRIT Bonus", dmg.critBonusRoll.Value));

        if (showRolls) ShowRolls(rolls);

        return dmg.amount > 0;
    }

    // single target attack with hitAlive retarget support
    protected IEnumerator ResolveSingleTargetAttack(
        ActionData action,
        int casterTeam, int casterSlot,
        int targetTeam, int targetSlot,
        float portion,
        string damageNotation,
        CombatScript.EffectKind kind,
        string hitRollNotation = null,
        string label = null,
        bool hitAlive = false)
    {
        string lbl = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label;
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        // dead target hitAlive retarget
        if (!IsUnitAlive(targetTeam, targetSlot))
        {
            if (!hitAlive) yield break;
            var redirect = ResolveHitAliveTarget(casterTeam, targetTeam, targetSlot, isHeal: false);
            if (redirect == null) yield break;
            targetTeam = redirect.Value.team;
            targetSlot = redirect.Value.slot;
        }

        UnitData target = GetUnit(targetTeam, targetSlot);
        if (!IsUnitAlive(target)) yield break;

        // hit roll auto hit if none provided
        HitOutcome outcome = HitOutcome.Hit;
        diceSystem.RollResult hitRoll = default;
        if (!string.IsNullOrWhiteSpace(hitRollNotation) && Combat.IsDiceNotationPublic(hitRollNotation))
        {
            int targetAC = Mathf.RoundToInt(Combat.GetEffectiveAC(targetTeam, targetSlot));
            var (h, o) = RollHit(hitRollNotation, targetAC, quiet: true);
            hitRoll = h;
            outcome = o;
            rollsToShow.Add(("Attack", hitRoll));

            if (outcome == HitOutcome.Miss)
            {
                if (UltraDebug) Debug.Log($"[{lbl}] missed {target.name}");
                ShowRolls(rollsToShow);
                yield break;
            }
            if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;
        }

        // damage roll crit aware
        var dmg = RollDamageWithCrit(damageNotation, outcome, portion);
        if (dmg.baseRoll.HasValue)       rollsToShow.Add(($"{lbl} Damage", dmg.baseRoll.Value));
        if (dmg.critBonusRoll.HasValue)  rollsToShow.Add(($"{lbl} CRIT Bonus", dmg.critBonusRoll.Value));

        ShowRolls(rollsToShow);

        if (dmg.amount <= 0) yield break;

        ApplyDamage(targetTeam, targetSlot, dmg.amount, kind,
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        if (UltraDebug) Debug.Log($"[{lbl}] {GetUnit(casterTeam, casterSlot).name} -> {target.name} for {dmg.amount} {kind} outcome {outcome}");

        yield return null;
    }

    // group attack with hitAlive retarget per target
    protected IEnumerator ResolveGroupAttack(
        ActionData action,
        int casterTeam, int casterSlot,
        List<ResolvedTarget> targets,
        string damageNotation,
        CombatScript.EffectKind kind,
        string hitRollNotation = null,
        string label = null,
        bool hitAlive = false)
    {
        if (targets == null || targets.Count == 0) yield break;

        string lbl = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label;
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        bool useHitRoll = !string.IsNullOrWhiteSpace(hitRollNotation) &&
                          Combat.IsDiceNotationPublic(hitRollNotation);

        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            UnitData u = GetUnit(t.team, t.slot);

            // dead target hitAlive retarget
            if (!IsUnitAlive(u))
            {
                if (!hitAlive) continue;
                var redirect = ResolveHitAliveTarget(casterTeam, t.team, t.slot, isHeal: false);
                if (redirect == null) continue;
                t = redirect.Value;
                u = t.unit;
            }

            // per target hit roll
            HitOutcome outcome = HitOutcome.Hit;
            diceSystem.RollResult hitRoll = default;
            if (useHitRoll)
            {
                int targetAC = Mathf.RoundToInt(Combat.GetEffectiveAC(t.team, t.slot));
                var (h, o) = RollHit(hitRollNotation, targetAC, quiet: true);
                hitRoll = h;
                outcome = o;
                rollsToShow.Add(($"{lbl} Attack→{u.name}", hitRoll));

                if (outcome == HitOutcome.Miss)
                {
                    if (UltraDebug) Debug.Log($"[{lbl}] missed {u.name}");
                    continue;
                }
                if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;
            }

            // per target damage roll crit aware
            var dmg = RollDamageWithCrit(damageNotation, outcome, t.portion);
            if (dmg.baseRoll.HasValue)      rollsToShow.Add(($"{lbl} Dmg→{u.name}", dmg.baseRoll.Value));
            if (dmg.critBonusRoll.HasValue) rollsToShow.Add(($"{lbl} CRIT→{u.name}", dmg.critBonusRoll.Value));

            if (dmg.amount <= 0) continue;

            ApplyDamage(t.team, t.slot, dmg.amount, kind,
                sourceTeam: casterTeam, sourceSlot: casterSlot);

            if (UltraDebug) Debug.Log($"[{lbl}] {GetUnit(casterTeam, casterSlot).name} -> {u.name} for {dmg.amount} {kind} outcome {outcome}");
        }

        ShowRolls(rollsToShow);
        yield return null;
    }
}

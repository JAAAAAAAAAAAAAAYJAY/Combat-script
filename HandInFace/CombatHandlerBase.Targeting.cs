using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnitData = CombatScript.UnitData;

public abstract partial class CombatHandlerBase
{
    protected struct ResolvedTarget
    {
        public int      team;
        public int      slot;
        public float    portion;   // CSV portion (0-1), used by damage scaling
        public UnitData unit;

        public bool IsValid => IsUnitAlive(unit);
    }

    // -----------------------------------------------------------------------
    // Core enum (private backbone for every picker)
    // -----------------------------------------------------------------------

    protected List<ResolvedTarget> ResolveCoordList(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot, bool aliveOnly = true)
    {
        var result = new List<ResolvedTarget>();
        if (coords == null || coords.Count == 0) return result;

        var entries = Combat.GetEffectiveTargetsPublic(coords, casterSlot);
        foreach (var te in entries)
        {
            var (t, s) = Combat.ResolveTarget(casterTeam, te.slot);
            if (t < 0 || s < 0) continue;
            UnitData u = Combat.GetUnit(t, s);
            if (aliveOnly && (!IsUnitAlive(u) || Combat.IsUnitVanished(t, s))) continue;
            result.Add(new ResolvedTarget { team = t, slot = s, portion = te.portion, unit = u });
        }

        // warn if coords had entries but no alive targets found
        if (result.Count == 0 && entries.Count > 0 && UltraDebug)
            Debug.Log($"[ResolveCoordList] {entries.Count} coord entries but no alive targets found");

        return result;
    }

    protected List<ResolvedTarget> GetAliveTargets(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => ResolveCoordList(coords, casterTeam, casterSlot, aliveOnly: true);

    // -----------------------------------------------------------------------
    // Single-target picks (scoped to the coord list)
    // -----------------------------------------------------------------------
    protected ResolvedTarget? FindFirstAliveTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
    {
        var list = ResolveCoordList(coords, casterTeam, casterSlot, aliveOnly: true);
        return list.Count == 0 ? (ResolvedTarget?)null : list[0];
    }

    protected ResolvedTarget? FindLowestHpTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => PickByMin(coords, casterTeam, casterSlot, u => u.hp);

    protected ResolvedTarget? FindLowestHpPercentTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => PickByMin(coords, casterTeam, casterSlot, u => u.maxHp > 0 ? u.hp / u.maxHp : float.MaxValue);

    protected ResolvedTarget? FindHighestHpTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => PickByMax(coords, casterTeam, casterSlot, u => u.hp);

    protected ResolvedTarget? FindHighestHpPercentTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => PickByMax(coords, casterTeam, casterSlot, u => u.maxHp > 0 ? u.hp / u.maxHp : 0f);

    protected ResolvedTarget? FindLowestACTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => PickFromCoords(coords, casterTeam, casterSlot, t => Combat.GetEffectiveAC(t.team, t.slot), min: true);

    protected ResolvedTarget? FindHighestACTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => PickFromCoords(coords, casterTeam, casterSlot, t => Combat.GetEffectiveAC(t.team, t.slot), min: false);

    protected ResolvedTarget? FindLowestShieldTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => PickByMin(coords, casterTeam, casterSlot, u => u.shield);

    protected ResolvedTarget? FindLowestArmorTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => PickByMin(coords, casterTeam, casterSlot, u => u.armor);

    protected ResolvedTarget? FindMostDebuffedTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot)
        => PickByMax(coords, casterTeam, casterSlot, u => CountActiveDebuffs(u));

    protected ResolvedTarget? FindRandomAliveTarget(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot, string dieNotation)
    {
        var list = ResolveCoordList(coords, casterTeam, casterSlot, aliveOnly: true);
        if (list.Count == 0) return null;
        if (list.Count == 1) return list[0];

        string effectiveNotation = dieNotation;
        int requestedDieSize = ParseDieSize(dieNotation);
        if (requestedDieSize > 0 && requestedDieSize != list.Count)
            effectiveNotation = $"1d{list.Count}";

        int idx;
        var roll = TryRoll(effectiveNotation);
        if (roll.HasValue)
        {
            idx = roll.Value.total - 1;
            if (idx < 0) idx = 0;
            if (idx >= list.Count) idx = list.Count - 1;
        }
        else
        {
            if (Combat.DiceRollerPublic != null)
                idx = Combat.DiceRollerPublic.RangeInt(0, list.Count);
            else
                idx = 0;
        }
        return list[idx];
    }

    protected ResolvedTarget? FindTargetMatching(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot, Func<UnitData, bool> predicate)
    {
        if (predicate == null) return FindFirstAliveTarget(coords, casterTeam, casterSlot);
        var list = ResolveCoordList(coords, casterTeam, casterSlot, aliveOnly: true);
        foreach (var t in list)
            if (predicate(t.unit)) return t;
        return null;
    }

    // -----------------------------------------------------------------------
    // Taunt override
    // -----------------------------------------------------------------------

    protected ResolvedTarget? GetTauntOverrideTarget(int casterTeam, int casterSlot)
    {
        UnitData caster = GetUnit(casterTeam, casterSlot);
        if (caster.tauntRemainingRounds <= 0 || caster.tauntTargetSlot < 0) return null;

        int forcedSlot = caster.tauntTargetSlot; // raw 0-7, from caster's POV
        var (tTeam, tSlot) = Combat.ResolveTarget(casterTeam, forcedSlot);

        if (tTeam == casterTeam)
        {
            if (CombatDebugHandler.UltraDebug)
                Debug.LogWarning($"[GetTauntOverrideTarget] {caster.name}'s taunt target slot {forcedSlot} resolves to ally team {tTeam} - ignoring (taunt must target opposing team).");
            return null;
        }

        UnitData u = Combat.GetUnit(tTeam, tSlot);
        if (!IsUnitAlive(u))
        {
            if (CombatDebugHandler.UltraDebug)
                Debug.Log($"[GetTauntOverrideTarget] {caster.name}'s taunt target (slot {forcedSlot}) is dead - falling back to normal targeting.");
            return null;
        }

        return new ResolvedTarget { team = tTeam, slot = tSlot, portion = 1f, unit = u };
    }

    // -----------------------------------------------------------------------
    // Sorting
    // -----------------------------------------------------------------------

    protected List<ResolvedTarget> SortTargetsByHp(List<ResolvedTarget> targets, bool ascending = true)
    {
        if (targets == null) return new List<ResolvedTarget>();
        var copy = new List<ResolvedTarget>(targets);
        copy.Sort((a, b) => ascending
            ? a.unit.hp.CompareTo(b.unit.hp)
            : b.unit.hp.CompareTo(a.unit.hp));
        return copy;
    }

    protected ResolvedTarget? FindLowestHpTarget_Team(int team)
        => PickTeamByMin(team, u => u.hp);

    protected ResolvedTarget? FindHighestHpTarget_Team(int team)
        => PickTeamByMax(team, u => u.hp);

    protected ResolvedTarget? FindLowestHpPercentTarget_Team(int team)
        => PickTeamByMin(team, u => u.maxHp > 0 ? u.hp / u.maxHp : float.MaxValue);

    protected ResolvedTarget? FindHighestHpPercentTarget_Team(int team)
        => PickTeamByMax(team, u => u.maxHp > 0 ? u.hp / u.maxHp : 0f);

    protected float GetTeamTotalHp(int team)
    {
        float total = 0f;
        for (int s = 0; s < 4; s++)
        {
            UnitData u = GetUnit(team, s);
            if (IsUnitAlive(u)) total += u.hp;
        }
        return total;
    }

    protected float GetTeamAverageHpPercent(int team)
    {
        float sum = 0f;
        int count = 0;
        for (int s = 0; s < 4; s++)
        {
            UnitData u = GetUnit(team, s);
            if (IsUnitAlive(u)) { sum += u.maxHp > 0 ? u.hp / u.maxHp : 0f; count++; }
        }
        return count == 0 ? 0f : sum / count;
    }

    // -----------------------------------------------------------------------
    // Private picker backbones
    // -----------------------------------------------------------------------


    ResolvedTarget? PickByMin(List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot,
        Func<UnitData, float> key)
        => PickFromCoords(coords, casterTeam, casterSlot, t => key(t.unit), min: true);

    ResolvedTarget? PickByMax(List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot,
        Func<UnitData, float> key)
        => PickFromCoords(coords, casterTeam, casterSlot, t => key(t.unit), min: false);

    ResolvedTarget? PickFromCoords(List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot,
        Func<ResolvedTarget, float> score, bool min)
    {
        var list = ResolveCoordList(coords, casterTeam, casterSlot, aliveOnly: true);
        if (list.Count == 0) return null;
        return PickBest(list, score, min);
    }

    ResolvedTarget? PickTeamByMin(int team, Func<UnitData, float> key)
        => PickTeam(team, key, min: true);

    ResolvedTarget? PickTeamByMax(int team, Func<UnitData, float> key)
        => PickTeam(team, key, min: false);

    ResolvedTarget? PickTeam(int team, Func<UnitData, float> key, bool min)
    {
        var list = new List<ResolvedTarget>(4);
        for (int s = 0; s < 4; s++)
        {
            UnitData u = GetUnit(team, s);
            if (!IsUnitAlive(u)) continue;
            list.Add(new ResolvedTarget { team = team, slot = s, portion = 1f, unit = u });
        }
        if (list.Count == 0) return null;
        return PickBest(list, t => key(t.unit), min);
    }

    static ResolvedTarget? PickBest(
        List<ResolvedTarget> candidates, Func<ResolvedTarget, float> score, bool min)
    {
        if (candidates == null || candidates.Count == 0) return null;
        ResolvedTarget best = candidates[0];
        float bestVal = score(best);
        for (int i = 1; i < candidates.Count; i++)
        {
            float v = score(candidates[i]);
            if ((min && v < bestVal) || (!min && v > bestVal))
            {
                best = candidates[i];
                bestVal = v;
            }
        }
        return best;
    }

    static readonly Regex _dieSizeRegex = new Regex(@"d(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static int ParseDieSize(string notation)
    {
        if (string.IsNullOrWhiteSpace(notation)) return -1;
        var m = _dieSizeRegex.Match(notation);
        if (!m.Success) return -1;
        return int.TryParse(m.Groups[1].Value, out int faces) ? faces : -1;
    }
}

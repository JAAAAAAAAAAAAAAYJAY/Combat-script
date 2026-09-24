using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData    = CombatScript.UnitData;

public abstract partial class CombatHandlerBase : MonoBehaviour
{
    protected CombatScript Combat { get; private set; }

    protected virtual void Awake()
    {
        Combat = FindFirstObjectByType<CombatScript>();

        if (Combat == null)
            Debug.LogError($"{GetType().Name}: no CombatScript in scene - override will fail.");
    }

    protected UnitData GetUnit(int team, int slot) => Combat.GetUnit(team, slot);

    protected void SetUnit(int team, int slot, UnitData data) => Combat.SetUnit(team, slot, data);

    protected (int team, int slot) ResolveTarget(int casterTeam, int rawSlot)
        => Combat.ResolveTarget(casterTeam, rawSlot);

    protected diceSystem.RollResult? TryRoll(string notation) => Combat.TryRoll(notation);

    protected enum HitOutcome { Miss, HalfMiss, Hit, Crit, CrunchyCrit, NoTarget }

    protected (diceSystem.RollResult result, HitOutcome outcome) RollHit(
        string hitRoll, int targetAC, bool quiet = true)
    {
        if (string.IsNullOrWhiteSpace(hitRoll) || !Combat.IsDiceNotationPublic(hitRoll))
            return (default, HitOutcome.NoTarget);

        if (targetAC < 0)
        {
            var autoResult = new diceSystem.RollResult { total = 0, isHit = true };
            return (autoResult, HitOutcome.Hit);
        }

        if (Combat.DiceRollerPublic == null)
        {
            Debug.LogError($"[{GetType().Name}] CombatScript.DiceRollerPublic is null - cannot roll hit.");
            return (default, HitOutcome.NoTarget);
        }

        // use RollHit not Roll so isHit is set for crits too
        var r = Combat.DiceRollerPublic.RollHit(hitRoll, targetAC, quiet: quiet);

        HitOutcome o;
        if (r.isCrunchyCrit)         o = HitOutcome.CrunchyCrit;
        else if (r.isCrit)           o = HitOutcome.Crit;
        else if (r.isFumble)         o = HitOutcome.Miss;
        else if (r.isHit)            o = HitOutcome.Hit;
        else if (r.isHalfMiss)       o = HitOutcome.HalfMiss;
        else                         o = HitOutcome.Miss;
        return (r, o);
    }

    // apply damage with guards dead target and amount check
    protected bool ApplyDamage(int targetTeam, int targetSlot, int amount,
        CombatScript.EffectKind kind, int duration = 0,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        if (amount <= 0) return false;
        if (!IsUnitAlive(targetTeam, targetSlot))
        {
            if (UltraDebug) Debug.Log($"[ApplyDamage] target at {targetTeam},{targetSlot} is dead or empty skip");
            return false;
        }
        return Combat.ApplyDamageToTarget(targetTeam, targetSlot, amount, kind, duration, sourceTeam, sourceSlot);
    }

    // apply damage to raw slot with range and dead guards
    protected bool ApplyDamageToRawSlot(int casterTeam, int rawSlot, int amount,
        CombatScript.EffectKind kind, int duration = 0, int sourceSlot = -1)
    {
        if (rawSlot < 0 || rawSlot > 7)
        {
            Debug.LogWarning($"[ApplyDamageToRawSlot] rawSlot {rawSlot} out of range 0-7");
            return false;
        }
        if (amount <= 0) return false;
        var (tTeam, tSlot) = Combat.ResolveTarget(casterTeam, rawSlot);
        return ApplyDamage(tTeam, tSlot, amount, kind, duration, casterTeam, sourceSlot);
    }

    protected System.Collections.Generic.List<CombatScript.TargetEntry>
        GetTargets(System.Collections.Generic.List<CombatScript.CasterPattern> coords, int casterSlot)
        => Combat.GetEffectiveTargetsPublic(coords, casterSlot);

    protected void ShowRolls(System.Collections.Generic.List<(string label, diceSystem.RollResult)> rolls)
    {
        if (Combat.DiceRollerPublic == null) return;
        var viz = Combat.DiceRollerPublic.GetComponent<diceVizualizer>();
        if (viz != null) viz.ShowRolls(rolls);
    }

    protected static bool UltraDebug => CombatDebugHandler.UltraDebug;

    #region Swap (2 units)

    protected void SwapUnits(int team, int slotA, int slotB)
    {
        if (slotA == slotB) return;
        if (slotA < 0 || slotA > 3 || slotB < 0 || slotB > 3)
        {
            Debug.LogWarning($"[SwapUnits] slots out of range (0-3): {slotA}, {slotB}");
            return;
        }
        Combat.SwapUnitsWithPassives(team, slotA, slotB);
    }

    protected void SwapUnitsByRawSlot(int casterTeam, int rawSlotA, int rawSlotB)
    {
        var (teamA, sA) = ResolveTarget(casterTeam, rawSlotA);
        var (teamB, sB) = ResolveTarget(casterTeam, rawSlotB);
        if (teamA != teamB)
        {
            Debug.LogWarning($"[SwapUnitsByRawSlot] raw slots {rawSlotA} and {rawSlotB} are on different teams - can't swap.");
            return;
        }
        SwapUnits(teamA, sA, sB);
    }

    protected bool SwapUnitsAlive(int team, int slotA, int slotB)
    {
        if (!IsUnitAlive(team, slotA) || !IsUnitAlive(team, slotB)) return false;
        SwapUnits(team, slotA, slotB);
        return true;
    }

    #endregion

    #region Rotate (3+ units)

    protected void RotateForward(int team, params int[] slots)
    {
        if (slots == null || slots.Length < 2) return;
        for (int i = slots.Length - 1; i > 0; i--)
            Combat.SwapUnitsWithPassives(team, slots[i], slots[i - 1]);
        if (UltraDebug) Debug.Log($"[RotateForward] team {team}: rotated {slots.Length} slots forward.");
    }

    protected void RotateBackward(int team, params int[] slots)
    {
        if (slots == null || slots.Length < 2) return;
        for (int i = 0; i < slots.Length - 1; i++)
            Combat.SwapUnitsWithPassives(team, slots[i], slots[i + 1]);
        if (UltraDebug) Debug.Log($"[RotateBackward] team {team}: rotated {slots.Length} slots backward.");
    }

    protected void RotateTeamForward(int team)
        => RotateForward(team, 0, 1, 2, 3);

    protected void RotateTeamBackward(int team)
        => RotateBackward(team, 0, 1, 2, 3);

    #endregion

    #region Reverse

    protected void ReverseUnits(int team, params int[] slots)
    {
        if (slots == null || slots.Length < 2) return;
        int left = 0, right = slots.Length - 1;
        while (left < right)
        {
            Combat.SwapUnitsWithPassives(team, slots[left], slots[right]);
            left++;
            right--;
        }
        if (UltraDebug) Debug.Log($"[ReverseUnits] team {team}: reversed {slots.Length} slots.");
    }

    protected void ReverseTeam(int team)
        => ReverseUnits(team, 0, 1, 2, 3);

    #endregion

    #region Shuffle (random)

    protected void ShuffleUnits(int team, params int[] slots)
    {
        if (slots == null || slots.Length < 2) return;

        for (int i = slots.Length - 1; i > 0; i--)
        {
            int j;
            var roll = TryRoll($"1d{i + 1}");
            if (roll.HasValue)
                j = roll.Value.total - 1;
            else if (Combat != null && Combat.DiceRollerPublic != null)
                j = Combat.DiceRollerPublic.RangeInt(0, i + 1);
            else
                j = 0;
            if (i != j)
                Combat.SwapUnitsWithPassives(team, slots[i], slots[j]);
        }

        if (UltraDebug) Debug.Log($"[ShuffleUnits] team {team}: shuffled {slots.Length} slots.");
    }

    protected void ShuffleTeam(int team)
        => ShuffleUnits(team, 0, 1, 2, 3);

    protected void ShuffleAliveUnits(int team)
    {
        var aliveSlots = new List<int>();
        for (int s = 0; s < 4; s++)
            if (IsUnitAlive(team, s)) aliveSlots.Add(s);
        if (aliveSlots.Count < 2) return;
        ShuffleUnits(team, aliveSlots.ToArray());
    }

    #endregion

    #region Move (single unit to a specific slot)

    protected void MoveUnit(int team, int fromSlot, int toSlot)
    {
        if (fromSlot == toSlot) return;
        if (fromSlot < 0 || fromSlot > 3 || toSlot < 0 || toSlot > 3)
        {
            Debug.LogWarning($"[MoveUnit] slots out of range (0-3): {fromSlot}, {toSlot}");
            return;
        }

        if (fromSlot < toSlot)
        {
            for (int s = fromSlot; s < toSlot; s++)
                Combat.SwapUnitsWithPassives(team, s, s + 1);
        }
        else
        {
            for (int s = fromSlot; s > toSlot; s--)
                Combat.SwapUnitsWithPassives(team, s, s - 1);
        }
        if (UltraDebug) Debug.Log($"[MoveUnit] team {team}: slot {fromSlot} -> slot {toSlot}.");
    }

    protected bool MoveUnitToFirstEmpty(int team, int fromSlot)
    {
        if (fromSlot < 0 || fromSlot > 3) return false;
        for (int s = 0; s < 4; s++)
        {
            if (s == fromSlot) continue;
            UnitData candidate = GetUnit(team, s);
            if (string.IsNullOrEmpty(candidate.name))
            {
                SwapUnits(team, fromSlot, s);
                return true;
            }
        }
        return false;
    }

    #endregion

    #region Collect / Scatter

    protected void CollectAliveToFront(int team)
    {
        int writeSlot = 0;
        for (int readSlot = 0; readSlot < 4; readSlot++)
        {
            if (!IsUnitAlive(team, readSlot)) continue;
            if (writeSlot != readSlot)
                Combat.SwapUnitsWithPassives(team, writeSlot, readSlot);
            writeSlot++;
        }

        if (UltraDebug) Debug.Log($"[CollectAliveToFront] team {team}: {writeSlot} alive gathered to front.");
    }

    protected void CollectAliveToBack(int team)
    {
        int writeSlot = 3;
        for (int readSlot = 3; readSlot >= 0; readSlot--)
        {
            if (!IsUnitAlive(team, readSlot)) continue;
            if (writeSlot != readSlot)
                Combat.SwapUnitsWithPassives(team, writeSlot, readSlot);
            writeSlot--;
        }

        if (UltraDebug) Debug.Log($"[CollectAliveToBack] team {team}: {3 - writeSlot} alive gathered to back.");
    }

    #endregion

    #region Repositioning Queries

    protected List<int> GetAliveSlots(int team)
    {
        var slots = new List<int>(4);
        for (int s = 0; s < 4; s++)
            if (IsUnitAlive(team, s)) slots.Add(s);
        return slots;
    }

    protected List<int> GetEmptySlots(int team)
    {
        var slots = new List<int>(4);
        for (int s = 0; s < 4; s++)
            if (!IsUnitAlive(team, s)) slots.Add(s);
        return slots;
    }

    protected int FindFirstEmptySlot(int team)
    {
        for (int s = 0; s < 4; s++)
            if (!IsUnitAlive(team, s)) return s;
        return -1;
    }

    protected int CountAlive(int team)
    {
        int n = 0;
        for (int s = 0; s < 4; s++)
            if (IsUnitAlive(team, s)) n++;
        return n;
    }

    #endregion
}

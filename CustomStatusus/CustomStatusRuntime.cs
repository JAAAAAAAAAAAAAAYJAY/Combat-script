using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CustomStatusRuntime : MonoBehaviour
{
    public static CustomStatusRuntime Instance { get; private set; }

    readonly Dictionary<(int team, int slot), List<CustomStatus>> active =
        new Dictionary<(int team, int slot), List<CustomStatus>>();

    readonly List<(int team, int slot)> _keysScratch = new List<(int, int)>(16);

    readonly List<CustomStatus> _dealDmgScratch = new List<CustomStatus>(8);
    bool _isInFireOnDealDamage;

    CombatScript combat;

    void Awake()
    {
        FindCombat();
    }

    void OnEnable()
    {
        FindCombat();
    }

    public void FindCombat()
    {
        Instance = this;
        combat = FindFirstObjectByType<CombatScript>();
        if (combat == null)
            Debug.LogError("[CustomStatusRuntime] No CombatScript in scene - custom statuses can't resolve targets.");
    }
    // -----------------------------------------------------------------------
    // Apply / Remove / Query
    // -----------------------------------------------------------------------

    public static void Apply(int targetTeam, int targetSlot, CustomStatus status)
    {
        var rt = EnsureExists();
        if (status == null || rt == null || rt.combat == null) return;

        status.TargetTeam = targetTeam;
        status.TargetSlot = targetSlot;
        status.Combat     = rt.combat;

        var list = rt.GetList(targetTeam, targetSlot);

        // refresh path: remove old then apply new so per-cast data is not lost
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Key == status.Key)
            {
                int maxRounds = Mathf.Max(list[i].RemainingRounds, status.RemainingRounds);
                try { list[i].OnRemove(); }
                catch (System.Exception e) { Debug.LogError($"[CustomStatusRuntime] OnRemove on refresh threw: {e}"); }
                list.RemoveAt(i);
                status.RemainingRounds = maxRounds;
                list.Add(status);
                try { status.OnApply(); }
                catch (System.Exception e) { Debug.LogError($"[CustomStatusRuntime] OnApply on refresh threw: {e}"); }
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[CustomStatus] Refreshed '{status.Key}' to {rt.combat.GetUnit(targetTeam, targetSlot).name} for {maxRounds} turn(s).");
                return;
            }
        }

        // new instance path
        list.Add(status);
        try { status.OnApply(); }
        catch (System.Exception e) { Debug.LogError($"[CustomStatusRuntime] OnApply threw: {e}"); }
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[CustomStatus] Applied '{status.Key}' to {rt.combat.GetUnit(targetTeam, targetSlot).name} for {status.RemainingRounds} turn(s).");
    }

    public static void Remove(int targetTeam, int targetSlot, string key)
    {
        if (Instance == null || string.IsNullOrEmpty(key)) return;
        var list = Instance.GetList(targetTeam, targetSlot);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Key == key)
            {
                list[i].OnRemove();
                list.RemoveAt(i);
                return;
            }
        }
    }

    public static bool IsActive(int targetTeam, int targetSlot, string key)
    {
        if (Instance == null || string.IsNullOrEmpty(key)) return false;
        var list = Instance.GetList(targetTeam, targetSlot);
        foreach (var s in list) if (s.Key == key) return true;
        return false;
    }

    public static IReadOnlyList<CustomStatus> GetAll(int targetTeam, int targetSlot)
    {
        if (Instance == null) return System.Array.Empty<CustomStatus>();
        return Instance.GetList(targetTeam, targetSlot);
    }

    // -----------------------------------------------------------------------
    // Outgoing-damage hook
    // -----------------------------------------------------------------------

    public static void FireOnDealDamage(int sourceTeam, int sourceSlot,
        ref int amount, CombatScript.EffectKind kind, int hitTeam, int hitSlot)
    {
        if (Instance == null) return;
        var list = Instance.GetList(sourceTeam, sourceSlot);
        if (list.Count == 0) return;

        bool isReentry = Instance._isInFireOnDealDamage;
        List<CustomStatus> snapshot;
        if (isReentry)
        {
            snapshot = new List<CustomStatus>(list);
        }
        else
        {
            snapshot = Instance._dealDmgScratch;
            snapshot.Clear();
            foreach (var s in list) snapshot.Add(s);
            Instance._isInFireOnDealDamage = true;
        }

        try
        {
            foreach (var s in snapshot)
                s.OnDealDamage(ref amount, kind, hitTeam, hitSlot);
        }
        finally
        {
            if (!isReentry)
                Instance._isInFireOnDealDamage = false;
        }
    }

    // -----------------------------------------------------------------------
    // Tick
    // -----------------------------------------------------------------------

    public void TickAll()
    {
        if (combat == null) return;
        var keys = _keysScratch;
        keys.Clear();
        foreach (var k in active.Keys) keys.Add(k);

        foreach (var key in keys)
        {
            var (team, slot) = key;
            if (!active.TryGetValue(key, out var list)) continue;

            var snapshot = new List<CustomStatus>(list);
            for (int i = snapshot.Count - 1; i >= 0; i--)
            {
                var s = snapshot[i];
                // Skip if it was already removed from the live list by an
                // earlier tick in this same pass.
                if (!list.Contains(s)) continue;

                var u = combat.GetUnit(team, slot);
                bool alive = !string.IsNullOrEmpty(u.name) && u.hp > 0;
                if (!alive)
                {
                    s.OnTargetDefeated();
                    list.Remove(s);
                    continue;
                }
                s.OnTurnEndTick();
                // The tick may have removed/added statuses; skip if this one
                // was removed during the tick.
                if (!list.Contains(s)) continue;

                s.RemainingRounds--;
                if (s.RemainingRounds <= 0)
                {
                    s.OnExpire();
                    list.Remove(s);
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"[CustomStatus] '{s.Key}' expired on {combat.GetUnit(team, slot).name}.");
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    List<CustomStatus> GetList(int team, int slot)
    {
        if (!active.TryGetValue((team, slot), out var list))
        {
            list = new List<CustomStatus>();
            active[(team, slot)] = list;
        }
        return list;
    }

    public static void NotifyUnitDefeated(int team, int slot)
    {
        if (Instance == null) return;
        var rt = Instance;
        if (!rt.active.TryGetValue((team, slot), out var list)) return;
        if (list.Count == 0) return;

        var snapshot = new List<CustomStatus>(list);
        list.Clear();
        foreach (var s in snapshot)
        {
            try { s.OnTargetDefeated(); }
            catch (System.Exception e)
            {
                Debug.LogError($"[CustomStatusRuntime] OnTargetDefeated threw for '{s.Key}' on ({team},{slot}): {e}");
            }
        }
    }

    public void ClearAll()
    {
        var keys = _keysScratch;
        keys.Clear();
        foreach (var k in active.Keys) keys.Add(k);

        foreach (var key in keys)
        {
            if (!active.TryGetValue(key, out var list)) continue;
            var snapshot = new List<CustomStatus>(list);
            list.Clear();
            foreach (var s in snapshot)
            {
                try { s.OnRemove(); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CustomStatusRuntime] OnRemove threw for '{s.Key}' on {key}: {e}");
                }
            }
        }
        active.Clear();
    }

    public static void SwapStatuses(int team, int slotA, int slotB)
    {
        if (Instance == null) return;
        if (slotA == slotB) return;
        var rt = Instance;
        var keyA = (team, slotA);
        var keyB = (team, slotB);

        var listA = rt.GetList(team, slotA);
        var listB = rt.GetList(team, slotB);

        rt.active[keyA] = listB;
        rt.active[keyB] = listA;

        foreach (var s in listA) s.TargetSlot = slotB;
        foreach (var s in listB) s.TargetSlot = slotA;
    }

    public static void ClearForUnit(int team, int slot)
    {
        if (Instance == null) return;
        var rt = Instance;
        if (!rt.active.TryGetValue((team, slot), out var list)) return;
        if (list == null || list.Count == 0) { rt.active.Remove((team, slot)); return; }

        var snapshot = new List<CustomStatus>(list);
        list.Clear();
        rt.active.Remove((team, slot));
        foreach (var s in snapshot)
        {
            try { s.OnRemove(); }
            catch (System.Exception e)
            {
                Debug.LogError($"[CustomStatusRuntime] ClearForUnit OnRemove threw for '{s.Key}' on ({team},{slot}): {e}");
            }
        }
    }

    static CustomStatusRuntime EnsureExists()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("__CustomStatusRuntime");
        DontDestroyOnLoad(go);
        return go.AddComponent<CustomStatusRuntime>();
    }
}
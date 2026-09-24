using UnityEngine;
using System;

public abstract class CustomStatus
{
    public string     Key             { get; protected set; }
    public int        RemainingRounds  { get; set; }
    public int        TargetTeam      { get; set; }
    public int        TargetSlot      { get; set; }
    public int        SourceTeam      { get; set; }
    public int        SourceSlot      { get; set; }
    public CombatScript Combat        { get; set; }

    // --- lifecycle hooks ---
    public virtual void OnApply()          { }
    public virtual void OnTurnEndTick()    { }

    public virtual void OnExpire()         { }

    public virtual void OnRemove()         { OnExpire(); }

    public virtual void OnTargetDefeated() { OnExpire(); }

    public virtual void OnDealDamage(ref int amount, CombatScript.EffectKind kind,
        int hitTeam, int hitSlot)
    { }

    public delegate void UnitDataMutator(ref CombatScript.UnitData unit);

    // --- convenience accessors ---
    protected CombatScript.UnitData GetTargetUnit() => Combat.GetUnit(TargetTeam, TargetSlot);

    protected bool IsTargetAlive()
    {
        var u = GetTargetUnit();
        return !string.IsNullOrEmpty(u.name) && u.hp > 0;
    }

    protected void SetTargetUnit(CombatScript.UnitData u) => Combat.SetUnit(TargetTeam, TargetSlot, u);

    protected void ModifyTarget(UnitDataMutator mutator)
    {
        var u = GetTargetUnit();
        mutator(ref u);
        SetTargetUnit(u);
    }

    protected diceSystem.RollResult? Roll(string notation) => Combat.TryRoll(notation);

    // source unit access
    protected CombatScript.UnitData GetSourceUnit()
    {
        if (SourceTeam < 0 || SourceSlot < 0) return default;
        return Combat.GetUnit(SourceTeam, SourceSlot);
    }

    protected bool IsSourceAlive()
    {
        var u = GetSourceUnit();
        return !string.IsNullOrEmpty(u.name) && u.hp > 0;
    }

    // check if a specific status is also active on the target
    protected bool HasStatus(string key)
        => CustomStatusRuntime.IsActive(TargetTeam, TargetSlot, key);

    // get remaining rounds of another status on the target
    protected int GetStatusRounds(string key)
    {
        var all = CustomStatusRuntime.GetAll(TargetTeam, TargetSlot);
        foreach (var s in all)
            if (s.Key == key) return s.RemainingRounds;
        return 0;
    }

    // apply damage to the target through the full pipeline
    protected void DamageTarget(int amount, CombatScript.EffectKind kind,
        int duration = 0, bool isDot = false)
    {
        if (amount <= 0) return;
        Combat.ApplyDamageToTarget(TargetTeam, TargetSlot, amount, kind, duration,
            SourceTeam, SourceSlot, isDot);
    }

    // heal the target
    protected void HealTarget(int amount)
    {
        if (amount <= 0) return;
        Combat.ApplyDamageToTarget(TargetTeam, TargetSlot, amount,
            CombatScript.EffectKind.RecoverHealth, 0, SourceTeam, SourceSlot);
    }

    // show floating text on the target
    protected void ShowFloatingText(int amount, CombatScript.EffectKind kind,
        bool isHeal = false, bool isDot = false, bool isCrit = false)
    {
        CombatScript.FireFloatingText(TargetTeam, TargetSlot, amount, kind, isHeal, isDot, isCrit);
    }

    // log helper
    protected void Log(string msg)
    {
        if (CombatDebugHandler.UltraDebug)
            Debug.Log($"[{Key}] {Combat.GetUnit(TargetTeam, TargetSlot).name} {msg}");
    }
}
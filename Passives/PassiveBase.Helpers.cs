using UnityEngine;
using UnitData = CombatScript.UnitData;

public abstract partial class PassiveBase
{
    // owner access
    protected UnitData GetOwnerUnit() => GetUnit(OwnerTeam, OwnerSlot);
    protected bool IsOwnerAlive() => IsUnitAlive(GetOwnerUnit());

    // owner modify
    protected void HealOwner(int amount)
    {
        if (amount <= 0) return;
        ApplyDamage(OwnerTeam, OwnerSlot, amount, CombatScript.EffectKind.RecoverHealth);
    }

    protected void DamageOwner(int amount, CombatScript.EffectKind kind,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        if (amount <= 0) return;
        ApplyDamage(OwnerTeam, OwnerSlot, amount, kind, sourceTeam: sourceTeam, sourceSlot: sourceSlot);
    }

    protected void BuffOwner(CombatScript.EffectKind kind, int amount)
    {
        if (amount <= 0) return;
        ApplyDamage(OwnerTeam, OwnerSlot, amount, kind);
    }

    protected void ModifyOwner(CustomStatus.UnitDataMutator mutator)
    {
        var u = GetOwnerUnit();
        mutator(ref u);
        SetUnit(OwnerTeam, OwnerSlot, u);
    }

    // proc guard inherited from CombatHandlerBase.TryProc
    // it checks this is iPassive and calls Combat.TryProc automatically

    // feedback
    protected void ShowFloatingTextOnOwner(int amount, CombatScript.EffectKind kind,
        bool isHeal = false, bool isDot = false, bool isCrit = false)
    {
        CombatScript.FireFloatingText(OwnerTeam, OwnerSlot, amount, kind, isHeal, isDot, isCrit);
    }

    protected void JiggleOwner()
    {
        var u = GetOwnerUnit();
        if (u.PlayerUnit != null) Combat.Jiggle(u.PlayerUnit);
    }

    // owner status helpers
    protected bool OwnerHasStatus(CombatScript.EffectKind kind)
        => HasStatus(OwnerTeam, OwnerSlot, kind);

    protected bool OwnerHasDOT()
        => HasDOT(OwnerTeam, OwnerSlot);

    protected bool OwnerHasTaunt()
        => HasTaunt(OwnerTeam, OwnerSlot);

    // owner AP query
    protected float GetOwnerTeamAP()
        => GetAP(OwnerTeam);

    // check if attacker is the owner
    protected bool IsOwnerAttacker(int attackerTeam, int attackerSlot)
        => attackerTeam == OwnerTeam && attackerSlot == OwnerSlot;

    // check if target is the owner
    protected bool IsOwnerTarget(int targetTeam, int targetSlot)
        => targetTeam == OwnerTeam && targetSlot == OwnerSlot;

    // log helper
    protected void LogOwner(string msg)
    {
        if (UltraDebug) Debug.Log($"[{GetOwnerUnit().name}] {msg}");
    }
}

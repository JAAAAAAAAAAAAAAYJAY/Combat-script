using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;


namespace CombatOverride
{
    public class PassiveAttribute : CombatOverrideAttribute
    {
        public PassiveAttribute(string key) : base(key) { }
    }
}

public interface iPassive
{
    string OverrideKey { get; }

    void OnAttach(CombatScript combat, int team, int slot);

    void OnDetach(CombatScript combat, int team, int slot);

    void OnTurnStart(CombatScript combat, int team);

    void OnTurnEnd(CombatScript combat, int team);

    void OnRoundStart(CombatScript combat);

    void OnRoundEnd(CombatScript combat);

    void OnDamageTaken(CombatScript combat, ref int amount, CombatScript.EffectKind kind,
                       int sourceTeam, int sourceSlot);

    void OnDefeat(CombatScript combat, ref bool preventDeath, ref float survivalPercent);

    void OnAnyUnitDefeated(CombatScript combat, int defeatedTeam, int defeatedSlot,
                           int killerTeam, int killerSlot);

    void OnPostDamage(CombatScript combat, int attackerTeam, int attackerSlot,
                      int targetTeam, int targetSlot, int hpDamage,
                      CombatScript.EffectKind kind);

    void OnActionCast(CombatScript combat, int casterTeam, int casterSlot, ActionData action);

    void ModifyAC(CombatScript combat, int targetTeam, int targetSlot, ref float ac);

    void ModifyDamageDealt(CombatScript combat, int attackerTeam, int attackerSlot,
                           int targetTeam, int targetSlot, ref int amount, CombatScript.EffectKind kind);
}

public abstract partial class PassiveBase : CombatHandlerBase, iPassive
{
    public abstract string OverrideKey { get; }

    protected int OwnerTeam { get; private set; }
    protected int OwnerSlot { get; private set; }

    public void Initialize(int team, int slot)
    {
        OwnerTeam = team;
        OwnerSlot = slot;
    }

    public void ReassignOwner(int team, int slot)
    {
        OwnerTeam = team;
        OwnerSlot = slot;
    }

    public virtual void OnAttach(CombatScript combat, int team, int slot) { }

    public virtual void OnDetach(CombatScript combat, int team, int slot) { }

    public virtual void OnTurnStart(CombatScript combat, int team) { }

    public virtual void OnTurnEnd(CombatScript combat, int team) { }

    public virtual void OnRoundStart(CombatScript combat) { }

    public virtual void OnRoundEnd(CombatScript combat) { }

    public virtual void OnDamageTaken(CombatScript combat, ref int amount, CombatScript.EffectKind kind,
                                      int sourceTeam, int sourceSlot) { }

    public virtual void OnDefeat(CombatScript combat, ref bool preventDeath, ref float survivalPercent) { }

    public virtual void OnAnyUnitDefeated(CombatScript combat, int defeatedTeam, int defeatedSlot,
                                          int killerTeam, int killerSlot) { }

    public virtual void OnPostDamage(CombatScript combat, int attackerTeam, int attackerSlot,
                                     int targetTeam, int targetSlot, int hpDamage,
                                     CombatScript.EffectKind kind) { }

    public virtual void OnActionCast(CombatScript combat, int casterTeam, int casterSlot, ActionData action) { }

    public virtual void ModifyAC(CombatScript combat, int targetTeam, int targetSlot, ref float ac) { }

    public virtual void ModifyDamageDealt(CombatScript combat, int attackerTeam, int attackerSlot,
                                          int targetTeam, int targetSlot, ref int amount, CombatScript.EffectKind kind) { }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData = CombatScript.UnitData;
using CombatOverride;

[Passive("passive_MyPassiveName")]            // MUST start with "passive_"
public class PassiveMyPassiveName : PassiveBase
{
    public override string OverrideKey => "passive_MyPassiveName";   // must match the attribute above EXACTLY

    // int someCounter = 0;

    // OnAttach fired ONCE when the passive is first attached to its owner
    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        // OwnerTeam/OwnerSlot are already set by Initialize() before this fires
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[MyPassive] Attached to {GetOwnerUnit().name}.");
    }

    // OnTurnStart fired at the start of THIS unit's team's turn
    // (NOT every turn only the owners team) 
    public override void OnTurnStart(CombatScript combat, int team)
    {
        if (team != OwnerTeam) return;
        // Example: regen 1 HP at the start of each of the owne's turns
        // HealOwner(1);
    }

    // OnTurnEnd fired at the end of THIS unit's team's turn
    // CustomStatusRuntimes ticker passive uses this hook to drive all
    // custom status ticks so this is also when CustomStatus.OnTurnEndTick runs
    public override void OnTurnEnd(CombatScript combat, int team)
    {
        if (team != OwnerTeam) return;
        // Example: reset a per turn counter
        // someCounter = 0;
    }

    public override void OnDamageTaken(CombatScript combat, ref int amount,
        CombatScript.EffectKind kind, int sourceTeam, int sourceSlot)
    {
    }

    public override void OnDefeat(CombatScript combat, ref bool preventDeath, ref float survivalPercent)
    {
        // Example revive at 25% HP, once per battle:
        // if (usedThisBattle) return;
        // preventDeath = true;
        // survivalPercent = 0.25f;   // owner survives at 25% maxHp
        // var unit = GetOwnerUnit();
        // ClearAllStatuses(ref unit);    // optional: wipe harmful DOTs so they don't re-kill
        // SetUnit(OwnerTeam, OwnerSlot, unit);
        // usedThisBattle = true;
    }

    public override void OnAnyUnitDefeated(CombatScript combat, int defeatedTeam, int defeatedSlot,
                                           int killerTeam, int killerSlot)
    {
    }

    public override void OnActionCast(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
    }

    // ModifyAC fires whenever the engine computes a unit's effective AC
    public override void ModifyAC(CombatScript combat, int targetTeam, int targetSlot, ref float ac)
    {
    }

    public override void ModifyDamageDealt(CombatScript combat, int attackerTeam, int attackerSlot,
                                           int targetTeam, int targetSlot, ref int amount,
                                           CombatScript.EffectKind kind)
    {
    }
}

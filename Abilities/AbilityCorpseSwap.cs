using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;
using UnitData    = CombatScript.UnitData;


[CombatOverride("ability_CorpseSwap")]
public class AbilityCorpseSwap : AbilityBase
{
    public override string OverrideKey => "ability_CorpseSwap";


    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        // AUD-026 revive an ally not an enemy
        int allyTeam = casterTeam;
        var caster = GetUnit(casterTeam, casterSlot); 

        // find all DEAD units on the ally team
        List<int> deadSlots = new List<int>();
        for (int s = 0; s < 4; s++)
        {
            UnitData u = GetUnit(allyTeam, s);
            if (!string.IsNullOrEmpty(u.name) && u.hp <= 0)
                deadSlots.Add(s);
        }

        if (deadSlots.Count == 0)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[NecroticSwap] {caster.name} found no dead allies - Action fizzles.");
            yield break;
        }

        // pick a random dead slot
        int deadSlot;
        if (deadSlots.Count == 1)
        {
            deadSlot = deadSlots[0];
        }
        else
        {
            string randomDeadDie = $"1d{deadSlots.Count}";
            var roll = TryRoll(randomDeadDie);
            if (!roll.HasValue)
            {
                Debug.LogWarning("[NecroticSwap] dice roll failed - picking first dead slot.");
                deadSlot = deadSlots[0];
            }
            else
            {
                int idx = (roll.Value.total - 1) % deadSlots.Count;
                if (idx < 0) idx = 0;
                deadSlot = deadSlots[idx];
            }
        }

        UnitData deadUnit = GetUnit(allyTeam, deadSlot);

        // find the highest-HP LIVING unit on the ally team
        ResolvedTarget? highest = FindHighestHpTarget_Team(allyTeam);
        if (highest == null)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[NecroticSwap] {caster.name} found no living allies to swap with - Action fizzles.");
            yield break;
        }
        var victim = highest.Value;

        // swap their positions
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[NecroticSwap] {caster.name} swaps dead {deadUnit.name} (slot {deadSlot}) " +
                  $"with {victim.unit.name} (slot {victim.slot}, HP {victim.unit.hp:0}).");

        UnitData beforeA = GetUnit(allyTeam, deadSlot);
        GameObject visA = beforeA.PlayerUnit;
        GameObject visSpaceA = beforeA.PlayerUnitSpace;
        UnitData beforeB = GetUnit(allyTeam, victim.slot);
        GameObject visB = beforeB.PlayerUnit;
        GameObject visSpaceB = beforeB.PlayerUnitSpace;

        Combat.SwapUnitsWithPassives(allyTeam, deadSlot, victim.slot);

        // Restore the visual refs so each slot keeps its original sprite.
        UnitData afterA = GetUnit(allyTeam, deadSlot);
        afterA.PlayerUnit = visA; afterA.PlayerUnitSpace = visSpaceA;
        SetUnit(allyTeam, deadSlot, afterA);
        UnitData afterB = GetUnit(allyTeam, victim.slot);
        afterB.PlayerUnit = visB; afterB.PlayerUnitSpace = visSpaceB;
        SetUnit(allyTeam, victim.slot, afterB);

        UnitData revived = GetUnit(allyTeam, victim.slot);  // dead unit is now here
        ClearAllStatuses(ref revived);
        // AUD-029 wipe pre death defenses armor stays unchanged
        revived.shield = 0;
        revived.th = 0;
        revived.hp = Mathf.Max(1, Mathf.CeilToInt(revived.maxHp * 0.25f));    // revive at 25% HP
        SetUnit(allyTeam, victim.slot, revived);
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[NecroticSwap] {revived.name} revives at {revived.hp:0} HP in slot {victim.slot}!");

        // The revived unit may carry passiveKeys from its original data, but
        // SwapUnitsWithPassives() above moves the *living* unit's passives to
        // the dead unit's old slot. The dead unit's passives were detached on
        // death (DrainPendingDeaths -> DetachPassives), so the revived slot
        // ends up with passiveKeys but no passive instances. Reinitialise the
        // passives on the revived slot so they actually exist and point at
        // their new (team, slot) owner.
        Combat.ReinitializePassivesForSlot(allyTeam, victim.slot);

        yield return null;
    }
}

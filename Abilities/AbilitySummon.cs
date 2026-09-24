using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;

[CombatOverride("ability_Summon")]
public class AbilitySummon : AbilityBase
{
    public override string OverrideKey => "ability_Summon";

    const int TempHP = 8;

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        var caster = GetUnit(casterTeam, casterSlot);
        if (string.IsNullOrEmpty(caster.name)) yield break;

        int emptySlot = FindFirstEmptySlot(casterTeam);

        if (emptySlot < 0)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Summon] {caster.name} has no empty slot - summon fizzles.");
            yield break;
        }

        // Build enemy-team target slots relative to the caster's team.
        // Raw slots 4-7 resolve to the enemy team regardless of which side the caster is on.
        var enemyTargets = new List<CombatScript.TargetEntry>
        {
            new CombatScript.TargetEntry { slot = 4, portion = 1f },
            new CombatScript.TargetEntry { slot = 5, portion = 1f },
            new CombatScript.TargetEntry { slot = 6, portion = 1f },
            new CombatScript.TargetEntry { slot = 7, portion = 1f },
        };

        var summon = new CombatScript.UnitData
        {
            name = "Shadow Fiend",
            hp = TempHP,
            maxHp = TempHP,
            ac = 12,
            armor = 0,
            maxArmor = 0,
            armorBlock = 0,
            shield = 0,
            maxShield = 0,
            ap = 1,
            actions = new List<ActionData>(),
            ActionUseCounts = new List<int>(),
            passiveKeys = new List<string>(),
            unitcode = "summon_fiend",
        };
        summon.actions.Add(new ActionData
        {
            fullName = "basic_ShadowClaw",
            ActionName = "ShadowClaw",
            physicalDmg = "1d6",
            hitRoll = "1d20+3",
            APCost = 0,
            useLimit = 0,
            hitAlive = true,
            physicalCoords = new List<CombatScript.CasterPattern>
            {
                new CombatScript.CasterPattern
                {
                    casterSlot = -1,
                    targets = enemyTargets,
                }
            },
        });
        summon.ActionUseCounts.Add(0);

        SetUnit(casterTeam, emptySlot, summon);
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Summon] {caster.name} summons a Shadow Fiend in slot {emptySlot}!");
        yield return null;
    }
}

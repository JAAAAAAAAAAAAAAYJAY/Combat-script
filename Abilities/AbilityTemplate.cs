using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;


[CombatOverride("ability_MyAbilityName")] // your key (MUST start with "ability_")
public class AbilityMyAbilityName : AbilityBase
{
    public override string OverrideKey => "ability_MyAbilityName";   //must match the attribute above EXACTLY and match csv

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        // pick a target
        // Common pickers (all return ResolvedTarget?, null = nobody valid):
        //  FindFirstAliveTarget      - first alive enemy in the coord list
        //  FindLowestHpTarget        - weakest enemy by raw HP
        //  FindLowestHpPercentTarget - weakest enemy by HP%
        //  FindHighestHpTarget       - highest hp enemy
        //  FindLowestACTarget        - lowest ac
        //  FindMostDebuffedTarget    - most DOTs/status
        //  FindRandomAliveTarget     - die-rolled random pick
        //  GetTauntOverrideTarget    - respect a Taunt if one is active currently always respect taunt since modified Action data gets passed through via execute Action
        // Wrap with taunt-respect like this:
        //   ResolvedTarget? target = GetTauntOverrideTarget(casterTeam, casterSlot)
        //                          ?? FindLowestHpTarget(action.physicalCoords, casterTeam, casterSlot);
        ResolvedTarget? target = FindFirstAliveTarget(action.physicalCoords, casterTeam, casterSlot);
        if (target == null) yield break;
        var chosen = target.Value;
        if (!IsUnitAlive(chosen.team, chosen.slot)) yield break;

        var caster = GetUnit(casterTeam, casterSlot);
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        // roll to hit
        // Omit this block entirely for actions that auto-hit (heals, buffs,
        // certain utility) HitOutcome values: Miss, HalfMiss, Hit, Crit,
        // CrunchyCrit (nat 20 = max damage), NoTarget (bad notation).
        int targetAC = Mathf.RoundToInt(combat.GetEffectiveAC(chosen.team, chosen.slot));
        var (hitRoll, outcome) = RollHit(action.hitRoll, targetAC, quiet: true);
        rollsToShow.Add(("Attack", hitRoll));

        if (outcome == HitOutcome.Miss)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[MyAction] {caster.name} missed {chosen.unit.name}!");
            ShowRolls(rollsToShow);
            yield break;
        }
        if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

        // roll damage (crit-aware)
        // TryRollAndShowDamage fills rollsToShow with the base + crit-bonus
        // rolls and returns false if the rolled amount is 0 (caller yields)
        // For AoE, swap this for ResolveGroupAttack / ResolveSplashDamage
        bool gotDamage = TryRollAndShowDamage(
            action.physicalDmg, outcome, chosen.portion,
            rollsToShow, "MyAction", out var dmg, showRolls: false);
        if (!gotDamage) { ShowRolls(rollsToShow); yield break; }

        // apply the damage
        // EffectKind options: Physical, AP, FireDmg, BleedDmg, PoisonDmg,
        // RecoverHealth, AddHealth, AddTempHealth, RecoverShield, AddShield,
        // RecoverArmor, AddArmor, Stun, Sleep, Confusion, Petrification
        ApplyDamage(
            chosen.team, chosen.slot, dmg.amount,
            CombatScript.EffectKind.Physical,        // match your CSV damage column
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        // secondary effects (optional)

        // flush the dice popup
        ShowRolls(rollsToShow);
        yield return null;
    }
}

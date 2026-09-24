using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using CombatOverride;
using UnitData    = CombatScript.UnitData;


[CombatOverride("skill_PumpkinThrow")]                   // MUST start with "skill_"
public class SkillPumpkinThrow : SkillBase
{
    public override string OverrideKey => "skill_PumpkinThrow";     // must match the attribute above EXACTLY

    public override IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, ActionData action)
    {
        UnitData caster = GetUnit(casterTeam, casterSlot);

        // pumpkin check (local: this handler is a shared singleton instance)
        bool pumpkin = false;
        string casterArmy   = GetArmy(caster.unitcode);
        string casterNumber = GetUnitNumber(caster.unitcode);

        int[] neighborSlots = { casterSlot - 1, casterSlot + 1 };
        foreach (int slot in neighborSlots)
        {
            if (slot < 0 || slot > 3) continue;
            if (!IsUnitAlive(casterTeam, slot)) continue;

            UnitData neighbor = GetUnit(casterTeam, slot);
            if (GetArmy(neighbor.unitcode) == casterArmy && GetUnitNumber(neighbor.unitcode) == casterNumber)
            {
                pumpkin = true;
                break;
            }
        }

        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        if (pumpkin)
        {
            // pick a target
            ResolvedTarget? target = FindHighestHpPercentTarget(action.physicalCoords, casterTeam, casterSlot);
            if (target == null) yield break;
            var chosen = target.Value;
            if (!IsUnitAlive(chosen.team, chosen.slot)) yield break;

            int centerRawSlot = ToRawSlot(casterTeam, chosen.team, chosen.slot);
            List<SplashTarget> splashZone = BuildSplashTargets(
                centerRawSlot, casterTeam, casterSlot,
                SplashPattern.PlusAndMinus, 1f, 0.5f);

            foreach (SplashTarget splashedTarget in splashZone)
            {
                int targetAC = Mathf.RoundToInt(combat.GetEffectiveAC(splashedTarget.team, splashedTarget.slot));
                var (hitRoll, outcome) = RollHit(action.hitRoll, targetAC, quiet: true);
                rollsToShow.Add(("Attack", hitRoll));

                if (outcome == HitOutcome.Miss)
                {
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"[PumpkinThrow] {caster.name} missed {GetUnit(splashedTarget.team, splashedTarget.slot).name}!");
                    continue;
                }
                if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

                bool gotDamage = TryRollAndShowDamage(
                    "2d6", outcome, splashedTarget.damagePercent,
                    rollsToShow, "Pumpkin", out var dmg, showRolls: false);
                if (!gotDamage) continue;

                ApplyDamage(
                    splashedTarget.team, splashedTarget.slot, dmg.amount,
                    CombatScript.EffectKind.Physical,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);

                if (CombatDebugHandler.UltraDebug) Debug.Log($"[PumpkinThrow] {caster.name} lobs a pumpkin at {GetUnit(splashedTarget.team, splashedTarget.slot).name} for {dmg.amount} (outcome {outcome}).");
            }

            // flush the dice popup once, after all splash targets are rolled
            ShowRolls(rollsToShow);
            yield return null;
        }
        else
        {

            // pick a target
            ResolvedTarget? target = FindRandomAliveTarget(action.physicalCoords, casterTeam, casterSlot, "1d4");
            if (target == null) yield break;
            var chosen = target.Value;
            if (!IsUnitAlive(chosen.team, chosen.slot)) yield break;

            // roll to hit
            int targetAC = Mathf.RoundToInt(combat.GetEffectiveAC(chosen.team, chosen.slot));
            var (hitRoll, outcome) = RollHit(action.hitRoll, targetAC, quiet: true);
            rollsToShow.Add(("Attack", hitRoll));

            if (outcome == HitOutcome.Miss)
            {
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[PumpkinThrow] {caster.name} missed {chosen.unit.name}!");
                ShowRolls(rollsToShow);
                yield break;
            }
            if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;

            diceSystem.RollResult? roll = TryRoll("1d4");
            if (roll.HasValue) rollsToShow.Add(("Veggie", roll.Value));
            if (!roll.HasValue) { ShowRolls(rollsToShow); yield break; }

            string veggieName;
            bool hotPotato;
            string veggieDamage;
            switch (roll.Value.total)
            {
                case 1:  veggieName = "onion";      hotPotato = false; veggieDamage = "1d4"; break;
                case 2:  veggieName = "carrot";     hotPotato = false; veggieDamage = "1d6"; break;
                case 3:  veggieName = "tomato";     hotPotato = false; veggieDamage = "1d8"; break;
                case 4:  veggieName = "hot potato"; hotPotato = true;  veggieDamage = "2d6"; break;
                default: veggieName = "carrot";      hotPotato = false; veggieDamage = "1d6"; break;
            }

            bool gotDamage = TryRollAndShowDamage(
                veggieDamage, outcome, chosen.portion,
                rollsToShow, $"Pumpkin ({veggieName})", out var dmg, showRolls: false);

            if (gotDamage)
            {
                ApplyDamage(
                    chosen.team, chosen.slot, dmg.amount,
                    CombatScript.EffectKind.Physical,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);

                if (CombatDebugHandler.UltraDebug) Debug.Log($"[PumpkinThrow] {caster.name} hits {chosen.unit.name} with a {veggieName} for {dmg.amount} (outcome {outcome}).");

                if (hotPotato)
                {
                    // hot potato also burns
                    ApplyDOT(chosen.team, chosen.slot, 0,
                        CombatScript.EffectKind.FireDmg,
                        action.fireDuration, action.fireDmg,
                        casterTeam, casterSlot);
                }
            }

            // flush the dice popup once
            ShowRolls(rollsToShow);
            yield return null;
        }
    }
}

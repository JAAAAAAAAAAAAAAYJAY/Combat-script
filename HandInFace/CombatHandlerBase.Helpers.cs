using System;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData = CombatScript.UnitData;

// high level helpers for override attacks and actions
// one function per concept uses EffectKind switch for multi use
public abstract partial class CombatHandlerBase
{
    // result from any attack call
    protected struct AttackResult
    {
        public bool hit;
        public bool defeated;
        public int damage;
        public HitOutcome outcome;
        public ResolvedTarget target;
        public List<(string label, diceSystem.RollResult)> rolls;
    }

    // result from a group attack
    protected struct GroupAttackResult
    {
        public int hitCount;
        public int killCount;
        public int totalDamage;
        public List<(string label, diceSystem.RollResult)> rolls;
    }

    // ===================================================================
    // ONE CALL ATTACK HELPERS
    // ===================================================================

    // single target attack from coords
    // picks target rolls hit rolls damage applies shows rolls
    // hitAlive redirects to nearest alive if target is dead
    protected AttackResult AttackSingle(
        List<CombatScript.CasterPattern> coords,
        int casterTeam, int casterSlot,
        ActionData action,
        CombatScript.EffectKind kind,
        bool hitAlive = false,
        string label = null)
    {
        var result = new AttackResult();
        result.rolls = new List<(string, diceSystem.RollResult)>();
        string lbl = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label;

        // get raw targets from coords includes dead ones
        var rawTargets = ResolveCoordList(coords, casterTeam, casterSlot, aliveOnly: false);
        if (rawTargets.Count == 0)
        {
            if (UltraDebug) Debug.Log($"[{lbl}] no targets in coords");
            return result;
        }

        var first = rawTargets[0];
        result.target = first;

        // dead target hitAlive retarget
        if (!IsUnitAlive(first.team, first.slot))
        {
            if (!hitAlive)
            {
                if (UltraDebug) Debug.Log($"[{lbl}] target dead and hitAlive false skip");
                return result;
            }
            var redirect = ResolveHitAliveTarget(casterTeam, first.team, first.slot, isHeal: false);
            if (redirect == null) return result;
            result.target = redirect.Value;
        }

        var caster = GetUnit(casterTeam, casterSlot);

        // roll hit
        HitOutcome outcome = HitOutcome.Hit;
        if (!string.IsNullOrWhiteSpace(action.hitRoll) && Combat.IsDiceNotationPublic(action.hitRoll))
        {
            int targetAC = Mathf.RoundToInt(Combat.GetEffectiveAC(result.target.team, result.target.slot));
            var (hitRoll, o) = RollHit(action.hitRoll, targetAC, quiet: true);
            outcome = o;
            result.rolls.Add(($"Attack→{result.target.unit.name}", hitRoll));

            if (outcome == HitOutcome.Miss)
            {
                if (UltraDebug) Debug.Log($"[{lbl}] missed {result.target.unit.name}");
                return result;
            }
            if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;
        }
        result.outcome = outcome;
        result.hit = true;

        // roll damage crit aware
        string dmgNotation = GetDamageNotation(action, kind);
        if (string.IsNullOrWhiteSpace(dmgNotation))
        {
            ShowRolls(result.rolls);
            return result;
        }

        var dmg = RollDamageWithCrit(dmgNotation, outcome, result.target.portion);
        if (dmg.baseRoll.HasValue) result.rolls.Add(($"{lbl} Dmg", dmg.baseRoll.Value));
        if (dmg.critBonusRoll.HasValue) result.rolls.Add(($"{lbl} CRIT", dmg.critBonusRoll.Value));

        if (dmg.amount <= 0)
        {
            ShowRolls(result.rolls);
            return result;
        }

        result.damage = dmg.amount;

        // apply damage
        result.defeated = ApplyDamage(result.target.team, result.target.slot, dmg.amount, kind,
            sourceTeam: casterTeam, sourceSlot: casterSlot);

        if (UltraDebug) Debug.Log($"[{lbl}] {caster.name} -> {result.target.unit.name} for {dmg.amount} {kind} outcome {outcome}");

        ShowRolls(result.rolls);
        return result;
    }

    // group attack all targets in coords get independent hit rolls
    protected GroupAttackResult AttackGroup(
        List<CombatScript.CasterPattern> coords,
        int casterTeam, int casterSlot,
        ActionData action,
        CombatScript.EffectKind kind,
        bool hitAlive = false,
        string label = null)
    {
        var result = new GroupAttackResult();
        result.rolls = new List<(string, diceSystem.RollResult)>();
        string lbl = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label;

        var targets = GetAliveTargets(coords, casterTeam, casterSlot);
        if (targets.Count == 0) return result;

        var caster = GetUnit(casterTeam, casterSlot);
        bool useHitRoll = !string.IsNullOrWhiteSpace(action.hitRoll) && Combat.IsDiceNotationPublic(action.hitRoll);
        string dmgNotation = GetDamageNotation(action, kind);

        foreach (var orig in targets)
        {
            var t = orig;
            if (!IsUnitAlive(t.team, t.slot))
            {
                if (!hitAlive) continue;
                var redirect = ResolveHitAliveTarget(casterTeam, t.team, t.slot, isHeal: false);
                if (redirect == null) continue;
                t = redirect.Value;
            }

            HitOutcome outcome = HitOutcome.Hit;
            if (useHitRoll)
            {
                int ac = Mathf.RoundToInt(Combat.GetEffectiveAC(t.team, t.slot));
                var (hr, o) = RollHit(action.hitRoll, ac, quiet: true);
                outcome = o;
                result.rolls.Add(($"Attack→{t.unit.name}", hr));
                if (outcome == HitOutcome.Miss) continue;
                if (outcome == HitOutcome.NoTarget) outcome = HitOutcome.Hit;
            }

            var dmg = RollDamageWithCrit(dmgNotation, outcome, t.portion);
            if (dmg.baseRoll.HasValue) result.rolls.Add(($"{lbl} Dmg→{t.unit.name}", dmg.baseRoll.Value));
            if (dmg.critBonusRoll.HasValue) result.rolls.Add(($"{lbl} CRIT→{t.unit.name}", dmg.critBonusRoll.Value));
            if (dmg.amount <= 0) continue;

            bool defeated = ApplyDamage(t.team, t.slot, dmg.amount, kind, sourceTeam: casterTeam, sourceSlot: casterSlot);
            result.hitCount++;
            result.totalDamage += dmg.amount;
            if (defeated) result.killCount++;
        }

        ShowRolls(result.rolls);
        return result;
    }

    // splash attack center plus adjacent slots
    protected GroupAttackResult AttackSplash(
        int centerRawSlot,
        int casterTeam, int casterSlot,
        ActionData action,
        CombatScript.EffectKind kind,
        SplashPattern pattern,
        float centerPercent = 1f,
        float sidePercent = 0.5f,
        string label = null)
    {
        var result = new GroupAttackResult();
        result.rolls = new List<(string, diceSystem.RollResult)>();
        string lbl = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label;

        var victims = BuildSplashTargets(centerRawSlot, casterTeam, casterSlot, pattern, centerPercent, sidePercent);
        if (victims.Count == 0) return result;

        var caster = GetUnit(casterTeam, casterSlot);
        bool useHitRoll = !string.IsNullOrWhiteSpace(action.hitRoll) && Combat.IsDiceNotationPublic(action.hitRoll);
        string dmgNotation = GetDamageNotation(action, kind);

        foreach (var v in victims)
        {
            if (!IsUnitAlive(v.team, v.slot)) continue;

            HitOutcome outcome = HitOutcome.Hit;
            if (useHitRoll)
            {
                int ac = Mathf.RoundToInt(Combat.GetEffectiveAC(v.team, v.slot));
                var (hr, o) = RollHit(action.hitRoll, ac, quiet: true);
                outcome = o == HitOutcome.NoTarget ? HitOutcome.Hit : o;
                result.rolls.Add(($"Attack→{GetUnit(v.team, v.slot).name}", hr));
                if (outcome == HitOutcome.Miss) continue;
            }

            float portion = v.damagePercent;
            var dmg = RollDamageWithCrit(dmgNotation, outcome, portion);
            if (dmg.baseRoll.HasValue) result.rolls.Add(($"{lbl} Dmg→{GetUnit(v.team, v.slot).name}", dmg.baseRoll.Value));
            if (dmg.critBonusRoll.HasValue) result.rolls.Add(($"{lbl} CRIT→{GetUnit(v.team, v.slot).name}", dmg.critBonusRoll.Value));
            if (dmg.amount <= 0) continue;

            bool defeated = ApplyDamage(v.team, v.slot, dmg.amount, kind, sourceTeam: casterTeam, sourceSlot: casterSlot);
            result.hitCount++;
            result.totalDamage += dmg.amount;
            if (defeated) result.killCount++;
        }

        ShowRolls(result.rolls);
        return result;
    }

    // attack plus secondary effect on hit
    // applies dot or status only if the attack hit
    protected AttackResult AttackWithEffect(
        List<CombatScript.CasterPattern> coords,
        int casterTeam, int casterSlot,
        ActionData action,
        CombatScript.EffectKind dmgKind,
        CombatScript.EffectKind effectKind,
        int effectDuration = 0,
        string effectNotation = null,
        bool hitAlive = false,
        string label = null)
    {
        var result = AttackSingle(coords, casterTeam, casterSlot, action, dmgKind, hitAlive, label);

        if (result.hit && result.damage > 0 && IsUnitAlive(result.target.team, result.target.slot))
        {
            // switch on effect type
            if (IsDotKind(effectKind) && effectDuration > 0 && !string.IsNullOrWhiteSpace(effectNotation))
            {
                ApplyDOT(result.target.team, result.target.slot, 0, effectKind, effectDuration, effectNotation,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);
            }
            else if (IsStatusKind(effectKind) && effectDuration > 0)
            {
                ApplyStatus(result.target.team, result.target.slot, effectKind, effectDuration,
                    sourceTeam: casterTeam, sourceSlot: casterSlot);
            }
        }

        return result;
    }

    // ===================================================================
    // UNIFIED EFFECT HELPER
    // ===================================================================

    // one function for all effect types uses switch
    // damage kinds apply damage  heal kinds heal  status kinds apply status  dot kinds apply dot
    protected bool ApplyEffect(
        int targetTeam, int targetSlot,
        CombatScript.EffectKind kind,
        int amount = 0,
        int duration = 0,
        string notation = null,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        if (!IsUnitAlive(targetTeam, targetSlot)) return false;

        switch (kind)
        {
            // damage kinds
            case CombatScript.EffectKind.Physical:
            case CombatScript.EffectKind.Magic:
            case CombatScript.EffectKind.FireDmg:
            case CombatScript.EffectKind.BleedDmg:
            case CombatScript.EffectKind.PoisonDmg:
                if (amount <= 0) return false;
                return ApplyDamage(targetTeam, targetSlot, amount, kind, sourceTeam: sourceTeam, sourceSlot: sourceSlot);

            // heal kinds
            case CombatScript.EffectKind.RecoverHealth:
            case CombatScript.EffectKind.AddHealth:
            case CombatScript.EffectKind.AddTempHealth:
            case CombatScript.EffectKind.RecoverShield:
            case CombatScript.EffectKind.AddShield:
            case CombatScript.EffectKind.RecoverArmor:
            case CombatScript.EffectKind.AddArmor:
                if (amount <= 0) return false;
                return ApplyDamage(targetTeam, targetSlot, amount, kind, sourceTeam: sourceTeam, sourceSlot: sourceSlot);

            // status kinds
            case CombatScript.EffectKind.Stun:
            case CombatScript.EffectKind.Sleep:
            case CombatScript.EffectKind.Confusion:
            case CombatScript.EffectKind.Petrification:
                if (duration <= 0) return false;
                return ApplyStatus(targetTeam, targetSlot, kind, duration, sourceTeam, sourceSlot);

            default:
                Debug.LogWarning($"[ApplyEffect] unsupported kind {kind}");
                return false;
        }
    }

    // ===================================================================
    // HEAL HELPERS
    // ===================================================================

    // heal a target returns actual hp gained
    protected int Heal(int targetTeam, int targetSlot, int amount,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        if (!IsUnitAlive(targetTeam, targetSlot)) return 0;
        if (amount <= 0) return 0;

        UnitData u = GetUnit(targetTeam, targetSlot);
        float hpBefore = u.hp;
        ApplyDamage(targetTeam, targetSlot, amount, CombatScript.EffectKind.RecoverHealth,
            sourceTeam: sourceTeam, sourceSlot: sourceSlot);
        u = GetUnit(targetTeam, targetSlot);
        return Mathf.Max(0, Mathf.CeilToInt(u.hp - hpBefore));
    }

    // heal lowest hp ally auto target
    protected int HealLowestHp(int casterTeam, int amount,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        var target = FindLowestHpPercentTarget_Team(casterTeam);
        if (target == null) return 0;
        var t = target.Value;
        return Heal(t.team, t.slot, amount, sourceTeam, sourceSlot);
    }

    // heal all alive allies
    protected int HealAllAllies(int casterTeam, int amount,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        int totalHealed = 0;
        for (int s = 0; s < 4; s++)
        {
            if (IsUnitAlive(casterTeam, s))
                totalHealed += Heal(casterTeam, s, amount, sourceTeam, sourceSlot);
        }
        return totalHealed;
    }

    // heal with retarget when original dead and hitAlive
    protected int HealWithRetarget(int targetTeam, int targetSlot, int amount, bool hitAlive,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        if (!IsUnitAlive(targetTeam, targetSlot))
        {
            if (!hitAlive) return 0;
            var redirect = ResolveHitAliveTarget(sourceTeam, targetTeam, targetSlot, isHeal: true);
            if (redirect == null) return 0;
            return Heal(redirect.Value.team, redirect.Value.slot, amount, sourceTeam, sourceSlot);
        }
        return Heal(targetTeam, targetSlot, amount, sourceTeam, sourceSlot);
    }

    // ===================================================================
    // BUFF HELPERS unified via EffectKind
    // ===================================================================

    // buff a target shield armor or temp hp
    // kind = AddShield AddArmor AddTempHealth RecoverShield RecoverArmor
    protected bool Buff(int targetTeam, int targetSlot, CombatScript.EffectKind kind, int amount,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        if (!IsUnitAlive(targetTeam, targetSlot)) return false;
        if (amount <= 0) return false;

        switch (kind)
        {
            case CombatScript.EffectKind.AddShield:
            case CombatScript.EffectKind.AddArmor:
            case CombatScript.EffectKind.AddTempHealth:
            case CombatScript.EffectKind.RecoverShield:
            case CombatScript.EffectKind.RecoverArmor:
            case CombatScript.EffectKind.RecoverHealth:
            case CombatScript.EffectKind.AddHealth:
                return ApplyDamage(targetTeam, targetSlot, amount, kind, sourceTeam: sourceTeam, sourceSlot: sourceSlot);
            default:
                Debug.LogWarning($"[Buff] kind {kind} is not a buff or heal kind");
                return false;
        }
    }

    // ===================================================================
    // TARGETING SHORTCUTS
    // ===================================================================

    // all alive enemies on opposing team
    protected List<ResolvedTarget> FindAllEnemies(int casterTeam)
    {
        int enemyTeam = GetEnemyTeam(casterTeam);
        return FindAllAliveOnTeam(enemyTeam);
    }

    // all alive allies on caster team
    protected List<ResolvedTarget> FindAllAllies(int casterTeam)
    {
        return FindAllAliveOnTeam(casterTeam);
    }

    // all alive units on a team
    protected List<ResolvedTarget> FindAllAliveOnTeam(int team)
    {
        var result = new List<ResolvedTarget>(4);
        for (int s = 0; s < 4; s++)
        {
            UnitData u = GetUnit(team, s);
            if (!IsUnitAlive(u)) continue;
            result.Add(new ResolvedTarget { team = team, slot = s, portion = 1f, unit = u });
        }
        return result;
    }

    // return caster as resolved target
    protected ResolvedTarget FindSelf(int casterTeam, int casterSlot)
    {
        UnitData u = GetUnit(casterTeam, casterSlot);
        return new ResolvedTarget { team = casterTeam, slot = casterSlot, portion = 1f, unit = u };
    }

    // nearest alive target to a slot on same team
    protected ResolvedTarget? FindNearestAlive(int team, int originSlot)
    {
        int slot = Combat.FindNearestAliveSlot(team, originSlot);
        if (slot < 0) return null;
        UnitData u = GetUnit(team, slot);
        return new ResolvedTarget { team = team, slot = slot, portion = 1f, unit = u };
    }

    // find target with lowest of any stat value
    protected ResolvedTarget? FindLowestStat(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot,
        Func<UnitData, float> statSelector)
        => PickByMin(coords, casterTeam, casterSlot, statSelector);

    // find target with highest of any stat value
    protected ResolvedTarget? FindHighestStat(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot,
        Func<UnitData, float> statSelector)
        => PickByMax(coords, casterTeam, casterSlot, statSelector);

    // find targets matching a predicate
    protected List<ResolvedTarget> FindAllMatching(
        List<CombatScript.CasterPattern> coords, int casterTeam, int casterSlot,
        Func<UnitData, bool> predicate)
    {
        var list = ResolveCoordList(coords, casterTeam, casterSlot, aliveOnly: true);
        if (predicate == null) return list;
        var result = new List<ResolvedTarget>();
        foreach (var t in list)
            if (predicate(t.unit)) result.Add(t);
        return result;
    }

    // ===================================================================
    // UNIT MANAGEMENT
    // ===================================================================

    // summon a new unit in first empty slot returns slot or -1
    protected int SummonUnit(int team, UnitData summonData)
    {
        int slot = FindFirstEmptySlot(team);
        if (slot < 0) return -1;
        SetUnit(team, slot, summonData);
        if (UltraDebug) Debug.Log($"[SummonUnit] {summonData.name} summoned in slot {slot}");
        return slot;
    }

    // revive a dead unit at hp percent of max
    // clears statuses and optionally shield th armor stays
    protected bool ReviveUnit(int team, int slot, float hpPercent, bool clearDefenses = true)
    {
        UnitData u = GetUnit(team, slot);
        if (string.IsNullOrEmpty(u.name)) return false;
        if (u.hp > 0) return false;

        ClearAllStatuses(ref u);
        u.hp = Mathf.Max(1, Mathf.CeilToInt(u.maxHp * Mathf.Clamp01(hpPercent)));
        if (clearDefenses)
        {
            u.shield = 0;
            u.th = 0;
            // armor stays unchanged
        }
        SetUnit(team, slot, u);

        // reinit passives so they exist on the revived slot
        Combat.ReinitializePassivesForSlot(team, slot);

        if (UltraDebug) Debug.Log($"[ReviveUnit] {u.name} revived at {u.hp} hp");
        return true;
    }

    // kill a unit triggers death pipeline
    protected bool KillUnit(int team, int slot, int sourceTeam = -1, int sourceSlot = -1)
    {
        if (!IsUnitAlive(team, slot)) return false;
        return ApplyDamage(team, slot, 999999, CombatScript.EffectKind.Physical,
            sourceTeam: sourceTeam, sourceSlot: sourceSlot);
    }

    // move unit to front slot 0
    protected void SwapToFront(int team, int fromSlot)
    {
        if (fromSlot == 0) return;
        MoveUnit(team, fromSlot, 0);
    }

    // move unit to back slot 3
    protected void SwapToBack(int team, int fromSlot)
    {
        if (fromSlot == 3) return;
        MoveUnit(team, fromSlot, 3);
    }

    // ===================================================================
    // AP HELPERS
    // ===================================================================

    // get team current AP
    protected float GetAP(int team)
        => Combat.GetAPBank(team).current;

    // get team AP cap
    protected float GetAPCap(int team)
        => Combat.GetTeamAPCap(team);

    // check if team can afford cost
    protected bool CanAfford(int team, int cost)
    {
        if (cost <= 0) return true;
        return GetAP(team) >= cost;
    }

    // spend AP from team returns false if cant afford
    protected bool SpendAP(int team, int cost)
    {
        if (cost <= 0) return true;
        return Combat.TrySpendAP(team, cost);
    }

    // ===================================================================
    // CONDITIONAL HELPERS
    // ===================================================================

    // roll hit vs ac if hit execute action returns hit bool
    protected bool IfHitThen(string hitRoll, int targetAC, Action action)
    {
        if (string.IsNullOrWhiteSpace(hitRoll) || !Combat.IsDiceNotationPublic(hitRoll))
        {
            action?.Invoke();
            return true;
        }
        var (hr, outcome) = RollHit(hitRoll, targetAC, quiet: true);
        if (outcome == HitOutcome.Miss) return false;
        action?.Invoke();
        return true;
    }

    // roll chance if success execute action
    protected bool IfChanceThen(string dieNotation, int successValue, Action action)
    {
        bool success = RollPercentage(dieNotation, successValue);
        if (success) action?.Invoke();
        return success;
    }

    // if target alive execute action
    protected bool IfAliveThen(int team, int slot, Action action)
    {
        if (!IsUnitAlive(team, slot)) return false;
        action?.Invoke();
        return true;
    }

    // if target dead execute action
    protected bool IfDeadThen(int team, int slot, Action action)
    {
        UnitData u = GetUnit(team, slot);
        if (IsUnitAlive(u)) return false;
        if (string.IsNullOrEmpty(u.name)) return false;
        action?.Invoke();
        return true;
    }

    // ===================================================================
    // STATUS QUERY HELPERS
    // ===================================================================

    // check if unit has a specific status active
    protected bool HasStatus(int team, int slot, CombatScript.EffectKind kind)
    {
        UnitData u = GetUnit(team, slot);
        switch (kind)
        {
            case CombatScript.EffectKind.Stun:          return u.stunRemainingRounds > 0;
            case CombatScript.EffectKind.Sleep:         return u.sleepRemainingRounds > 0;
            case CombatScript.EffectKind.Confusion:     return u.confusionRemainingRounds > 0;
            case CombatScript.EffectKind.Petrification: return u.petrificationRemainingRounds > 0;
            case CombatScript.EffectKind.FireDmg:       return u.fireRemainingRounds > 0;
            case CombatScript.EffectKind.BleedDmg:      return u.bleedRemainingRounds > 0;
            case CombatScript.EffectKind.PoisonDmg:     return u.poisonRemainingRounds > 0;
            default: return false;
        }
    }

    // check if unit has any dot
    protected bool HasDOT(int team, int slot)
    {
        UnitData u = GetUnit(team, slot);
        return u.fireRemainingRounds > 0 || u.bleedRemainingRounds > 0 || u.poisonRemainingRounds > 0;
    }

    // check if unit has any debuff
    protected bool HasDebuff(int team, int slot)
    {
        UnitData u = GetUnit(team, slot);
        return CountActiveDebuffs(u) > 0;
    }

    // check if unit has taunt
    protected bool HasTaunt(int team, int slot)
    {
        UnitData u = GetUnit(team, slot);
        return u.tauntRemainingRounds > 0 && u.tauntTargetSlot >= 0;
    }

    // ===================================================================
    // CLEAR HELPERS
    // ===================================================================

    // clear only dots on a unit
    protected void ClearDOTs(ref UnitData u)
    {
        u.fireRemainingRounds = 0;
        u.bleedRemainingRounds = 0;
        u.poisonRemainingRounds = 0;
        u.fireDamageNotation = "";
        u.bleedDamageNotation = "";
        u.poisonDamageNotation = "";
    }

    // clear only statuses on a unit
    protected void ClearStatusesOnly(ref UnitData u)
    {
        u.stunRemainingRounds = 0;
        u.sleepRemainingRounds = 0;
        u.confusionRemainingRounds = 0;
        u.petrificationRemainingRounds = 0;
        u.tauntRemainingRounds = 0;
        u.tauntTargetSlot = -1;
    }

    // clear everything on a unit
    protected void ClearAll(ref UnitData u)
    {
        ClearDOTs(ref u);
        ClearStatusesOnly(ref u);
    }

    // clear dots on a unit by team slot
    protected void ClearDOTsOn(int team, int slot)
    {
        UnitData u = GetUnit(team, slot);
        ClearDOTs(ref u);
        SetUnit(team, slot, u);
    }

    // clear statuses on a unit by team slot
    protected void ClearStatusesOn(int team, int slot)
    {
        UnitData u = GetUnit(team, slot);
        ClearStatusesOnly(ref u);
        SetUnit(team, slot, u);
    }

    // clear everything on a unit by team slot
    protected void ClearAllOn(int team, int slot)
    {
        UnitData u = GetUnit(team, slot);
        ClearAll(ref u);
        SetUnit(team, slot, u);
    }

    // ===================================================================
    // PROC GUARD FOR PASSIVES
    // ===================================================================

    // proc guard for passives once per key per Action execution
    protected bool TryProc(string procKey)
    {
        if (this is iPassive p)
            return Combat.TryProc(p, procKey);
        return true;
    }

    // ===================================================================
    // FEEDBACK HELPERS
    // ===================================================================

    // show floating text at a unit
    protected void ShowFloatingText(int team, int slot, int amount, CombatScript.EffectKind kind,
        bool isHeal = false, bool isDot = false, bool isCrit = false)
    {
        CombatScript.FireFloatingText(team, slot, amount, kind, isHeal, isDot, isCrit);
    }

    // jiggle a unit by team slot
    protected void JiggleUnit(int team, int slot)
    {
        UnitData u = GetUnit(team, slot);
        if (u.PlayerUnit != null)
            Combat.Jiggle(u.PlayerUnit);
    }

    // conditional debug log
    protected void Log(string msg)
    {
        if (UltraDebug) Debug.Log(msg);
    }

    // ===================================================================
    // ROLL HELPERS
    // ===================================================================

    // roll and push to rolls list in one call
    protected diceSystem.RollResult? RollAndShow(string notation, string label,
        List<(string, diceSystem.RollResult)> rolls)
    {
        var roll = TryRoll(notation);
        if (roll.HasValue)
            rolls.Add((label, roll.Value));
        return roll;
    }

    // seeded chance roll
    protected bool RollChance(float pct)
    {
        if (Combat.DiceRollerPublic != null)
            return Combat.DiceRollerPublic.RollChance(pct);
        return false;
    }

    // max possible for a notation
    protected int MaxDamage(string notation)
    {
        if (Combat.DiceRollerPublic == null) return 0;
        return Combat.DiceRollerPublic.MaxPossible(notation);
    }

    // ===================================================================
    // CUSTOM STATUS HELPERS
    // ===================================================================

    // check if custom status active
    protected bool HasCustomStatus(int team, int slot, string key)
        => CustomStatusRuntime.IsActive(team, slot, key);

    // remove custom status by key
    protected void RemoveCustomStatus(int team, int slot, string key)
        => CustomStatusRuntime.Remove(team, slot, key);

    // get remaining rounds of a custom status by key
    protected int GetStatusRemainingRounds(int team, int slot, string key)
    {
        var all = CustomStatusRuntime.GetAll(team, slot);
        foreach (var s in all)
            if (s.Key == key) return s.RemainingRounds;
        return 0;
    }

    // ===================================================================
    // INTERNAL get damage notation from Action by kind
    // ===================================================================
    string GetDamageNotation(ActionData action, CombatScript.EffectKind kind)
    {
        switch (kind)
        {
            case CombatScript.EffectKind.Physical: return action.physicalDmg;
            case CombatScript.EffectKind.Magic:   return action.magicDmg;
            case CombatScript.EffectKind.FireDmg:  return action.fireDmg;
            case CombatScript.EffectKind.BleedDmg: return action.bleedDmg;
            case CombatScript.EffectKind.PoisonDmg:return action.poisonDmg;
            default: return null;
        }
    }

    // get coords from Action by kind
    protected List<CombatScript.CasterPattern> GetCoords(ActionData action, CombatScript.EffectKind kind)
    {
        switch (kind)
        {
            case CombatScript.EffectKind.Physical: return action.physicalCoords;
            case CombatScript.EffectKind.Magic:   return action.magicCoords;
            case CombatScript.EffectKind.FireDmg:  return action.fireCoords;
            case CombatScript.EffectKind.BleedDmg: return action.bleedCoords;
            case CombatScript.EffectKind.PoisonDmg:return action.poisonCoords;
            case CombatScript.EffectKind.Stun:     return action.stunCoords;
            case CombatScript.EffectKind.Sleep:    return action.sleepCoords;
            case CombatScript.EffectKind.Confusion:return action.confusionCoords;
            case CombatScript.EffectKind.Petrification: return action.petrificationCoords;
            case CombatScript.EffectKind.RecoverHealth: return action.recoverHealthCoords;
            case CombatScript.EffectKind.AddHealth:     return action.addHealthCoords;
            case CombatScript.EffectKind.AddTempHealth: return action.addTempHealthCoords;
            case CombatScript.EffectKind.RecoverShield: return action.recoverShieldCoords;
            case CombatScript.EffectKind.AddShield:     return action.addShieldCoords;
            case CombatScript.EffectKind.RecoverArmor:  return action.recoverArmorCoords;
            case CombatScript.EffectKind.AddArmor:      return action.addArmorCoords;
            default: return null;
        }
    }

    // get duration from Action by kind
    protected int GetDuration(ActionData action, CombatScript.EffectKind kind)
    {
        switch (kind)
        {
            case CombatScript.EffectKind.FireDmg:   return action.fireDuration;
            case CombatScript.EffectKind.BleedDmg:  return action.bleedDuration;
            case CombatScript.EffectKind.PoisonDmg: return action.poisonDuration;
            case CombatScript.EffectKind.Stun:
                return int.TryParse(action.stunTime, out int s) ? s : 0;
            case CombatScript.EffectKind.Sleep:
                return int.TryParse(action.sleepTime, out int sl) ? sl : 0;
            case CombatScript.EffectKind.Confusion:
                return int.TryParse(action.confusionTime, out int c) ? c : 0;
            case CombatScript.EffectKind.Petrification:
                return int.TryParse(action.petrificationTime, out int p) ? p : 0;
            default: return 0;
        }
    }

    // get value notation from Action by kind
    protected string GetNotation(ActionData action, CombatScript.EffectKind kind)
    {
        switch (kind)
        {
            case CombatScript.EffectKind.Physical: return action.physicalDmg;
            case CombatScript.EffectKind.Magic:   return action.magicDmg;
            case CombatScript.EffectKind.FireDmg:  return action.fireDmg;
            case CombatScript.EffectKind.BleedDmg: return action.bleedDmg;
            case CombatScript.EffectKind.PoisonDmg:return action.poisonDmg;
            case CombatScript.EffectKind.RecoverHealth: return action.recoverHealth;
            case CombatScript.EffectKind.AddHealth:     return action.addHealth;
            case CombatScript.EffectKind.AddTempHealth: return action.addTempHealth;
            case CombatScript.EffectKind.RecoverShield: return action.recoverShield;
            case CombatScript.EffectKind.AddShield:     return action.addShield;
            case CombatScript.EffectKind.RecoverArmor:  return action.recoverArmor;
            case CombatScript.EffectKind.AddArmor:      return action.addArmor;
            default: return null;
        }
    }
}

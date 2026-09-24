using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData    = CombatScript.UnitData;

public enum AiActionKind
{
    Idle,
    UseAction,
    Move,
    MoveAndUseAction,
    BenchSwap,
    MoveAndBenchSwap,
}

public struct AiExpectedEffect
{
    public int   team;
    public int   slot;
    public float portion;
    public CombatScript.EffectKind kind;
    public float expectedAmount;
    public float expectedHitChance;
    public bool  wouldKill;
    public string label;
}

public struct AiScoreBreakdown
{
    public float damageScore;
    public float killScore;
    public float survivalScore;
    public float healScore;
    public float buffScore;
    public float debuffScore;
    public float positionScore;
    public float resourceScore;
    public float APCostScore;
    public float futureScore;
    public float benchSwapScore;
    public float fatigueScore;
    public float repeatSwapPenalty;
    public float randomnessBonus;

    public float Total => damageScore + killScore + survivalScore + healScore + buffScore
                        + debuffScore + positionScore + resourceScore + APCostScore
                        + futureScore + benchSwapScore + fatigueScore + repeatSwapPenalty + randomnessBonus;

    public string BuildBreakdownString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Damage Score:       {damageScore,+8:0.0}");
        sb.AppendLine($"Kill Score:         {killScore,+8:0.0}");
        sb.AppendLine($"Survival Score:      {survivalScore,+8:0.0}");
        sb.AppendLine($"Heal Score:         {healScore,+8:0.0}");
        sb.AppendLine($"Buff Score:         {buffScore,+8:0.0}");
        sb.AppendLine($"Debuff Score:       {debuffScore,+8:0.0}");
        sb.AppendLine($"Position Score:      {positionScore,+8:0.0}");
        sb.AppendLine($"Resource Value:      {resourceScore,+8:0.0}");
        sb.AppendLine($"AP Cost:        {APCostScore,+8:0.0}");
        sb.AppendLine($"Future Value:       {futureScore,+8:0.0}");
        sb.AppendLine($"Bench Swap Score:    {benchSwapScore,+8:0.0}");
        sb.AppendLine($"Fatigue Score:      {fatigueScore,+8:0.0}");
        if (repeatSwapPenalty != 0f)
            sb.AppendLine($"Repeat-Swap Penalty: {repeatSwapPenalty,+8:0.0}");
        if (randomnessBonus != 0f)
            sb.AppendLine($"Randomness Bonus:    {randomnessBonus,+8:0.0}");
        sb.AppendLine(new string('-', 40));
        sb.AppendLine($"FINAL:              {Total,+8:0.0}");
        return sb.ToString();
    }
}

public struct AiAction
{
    public AiActionKind kind;
    public int  team;
    public int  casterSlot;
    public int  ActionIndex;
    public CombatScript.ActionData action;
    public int  moveDestination;
    public int  benchIndex;

    public List<AiExpectedEffect> effects;
    public AiScoreBreakdown breakdown;
    public string description;

    public float Score => breakdown.Total;

    public string Category
    {
        get
        {
            switch (kind)
            {
                case AiActionKind.UseAction:
                case AiActionKind.MoveAndUseAction:
                    return PrimaryCategory();
                case AiActionKind.Move:      return "MOVEMENT";
                case AiActionKind.BenchSwap: return "BENCH";
                case AiActionKind.MoveAndBenchSwap: return "BENCH";
                default:                     return "OTHER";
            }
        }
    }

    string PrimaryCategory()
    {
        if (effects == null || effects.Count == 0) return "OTHER";
        bool hasDamage = false, hasHeal = false, hasBuff = false, hasDebuff = false;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (IsDamage(e.kind)) hasDamage = true;
            else if (IsHeal(e.kind)) hasHeal = true;
            else if (IsBuff(e.kind)) hasBuff = true;
            else if (IsDebuff(e.kind)) hasDebuff = true;
        }
        if (hasDamage) return "ATTACKS";
        if (hasHeal)   return "HEALS";
        if (hasBuff)   return "BUFFS";
        if (hasDebuff) return "DEBUFFS";
        return "OTHER";
    }

    public static bool IsDamage(CombatScript.EffectKind k)
        => k == CombatScript.EffectKind.Physical
        || k == CombatScript.EffectKind.Magic
        || k == CombatScript.EffectKind.FireDmg
        || k == CombatScript.EffectKind.BleedDmg
        || k == CombatScript.EffectKind.PoisonDmg;

    public static bool IsHeal(CombatScript.EffectKind k)
        => k == CombatScript.EffectKind.RecoverHealth
        || k == CombatScript.EffectKind.AddHealth
        || k == CombatScript.EffectKind.AddTempHealth;

    public static bool IsBuff(CombatScript.EffectKind k)
        => k == CombatScript.EffectKind.RecoverShield
        || k == CombatScript.EffectKind.AddShield
        || k == CombatScript.EffectKind.RecoverArmor
        || k == CombatScript.EffectKind.AddArmor;

    public static bool IsDebuff(CombatScript.EffectKind k)
        => k == CombatScript.EffectKind.Stun
        || k == CombatScript.EffectKind.Sleep
        || k == CombatScript.EffectKind.Confusion
        || k == CombatScript.EffectKind.Petrification;

    public string BuildDescription()
    {
        if (!string.IsNullOrEmpty(description)) return description;
        var sb = new StringBuilder();
        switch (kind)
        {
            case AiActionKind.Idle: sb.Append("Idle"); break;
            case AiActionKind.Move: sb.Append($"Move → slot {moveDestination}"); break;
            case AiActionKind.UseAction:
                sb.Append($"{(string.IsNullOrEmpty(action.ActionName) ? action.fullName : action.ActionName)}");
                AppendTargetSummary(sb);
                break;
            case AiActionKind.MoveAndUseAction:
                sb.Append($"Move → slot {moveDestination} + {(string.IsNullOrEmpty(action.ActionName) ? action.fullName : action.ActionName)}");
                AppendTargetSummary(sb);
                break;
            case AiActionKind.BenchSwap:
                sb.Append($"Swap slot {casterSlot} ↔ bench[{benchIndex}]");
                break;
            case AiActionKind.MoveAndBenchSwap:
                sb.Append($"Move slot {casterSlot} → slot {moveDestination} + Swap ↔ bench[{benchIndex}]");
                break;
        }
        return sb.ToString();
    }

    void AppendTargetSummary(StringBuilder sb)
    {
        if (effects == null || effects.Count == 0) return;
        var seen = new List<string>();
        var parts = new List<string>();
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            string label = e.team == CombatScript.TeamPlayer ? $"Ally {e.slot}" : $"Enemy {e.slot}";
            if (seen.Contains(label)) continue;
            seen.Add(label);
            parts.Add(label);
        }
        if (parts.Count > 0)
            sb.Append(" → ").Append(string.Join(" + ", parts));
    }
}


public class CombatAi : MonoBehaviour
{
    #region Config

    public CombatScript combat;
    public CombatRuntimeManager runtime;

    public enum Perspective { Active, Opposition }

    [SerializeField]
    public WeightsTemplate weights;

    public System.Random AiRng = new System.Random(0);

    #endregion

    #region Team-strength evaluation

    [System.Serializable]
    public struct UnitStrengthEntry
    {
        public int slot;
        public CombatScript.UnitData unit;
        public float strength;
        public float hpScore;
        public float armorScore;
        public float shieldScore;
        public float hpMissingFrac;
        public float armorMissingFrac;
        public float shieldMissingFrac;
    }

    public List<UnitStrengthEntry> lastActiveStrength     = new List<UnitStrengthEntry>();
    public List<UnitStrengthEntry> lastOppositionStrength = new List<UnitStrengthEntry>();

    public float lastActiveStrengthTotal;
    public float lastOppositionStrengthTotal;
    public float lastOppositionFinalStrength;
    public float lastAgressionMultiplier;

    #endregion

    #region Action-selection results

    [HideInInspector] public List<AiAction> lastActionEvaluation = new List<AiAction>();
    [HideInInspector] public AiAction lastChosenAction;
    [HideInInspector] public int lastChosenActionIndex = -1;
    [HideInInspector] public bool hasActionEvaluation = false;
    [HideInInspector] public int lastActionTeam = -1;

    #endregion

    #region Fatigue tracker

    private Dictionary<(int team, int slot, int action), float> fatigueMap = new Dictionary<(int, int, int), float>();
    private Dictionary<(int team, string unitName), float> unitFatigueMap = new Dictionary<(int, string), float>();

    private readonly Dictionary<(int unitSlot, int fromSlot, bool requireUsable), float> _slotValueCache =
        new Dictionary<(int, int, bool), float>(16);
    private int _slotValueCacheDecision = -1;

    public void OnActionUsed(int team, int slot, int ActionIndex)
    {
        var key = (team, slot, ActionIndex);
        if (!fatigueMap.ContainsKey(key)) fatigueMap[key] = 0f;
        fatigueMap[key] += 1f;

        // Track fatigue per acting unit (by name, so it follows the unit through
        // moves/bench swaps rather than resetting when a different unit occupies the slot).
        if (combat != null)
        {
            UnitData actor = combat.GetUnit(team, slot);
            if (!string.IsNullOrEmpty(actor.name))
            {
                var uKey = (team, actor.name);
                if (!unitFatigueMap.ContainsKey(uKey)) unitFatigueMap[uKey] = 0f;
                unitFatigueMap[uKey] += 1f;
            }
        }
        InvalidateThreatCache();
    }

    public void OnTurnStart(int team)
    {
        swapCountThisTurn[team] = 0;


        if (weights == null) return;
        float recovery = weights.ai.fatigueRecoveryPerTurn;
        var keys = new List<(int team, int slot, int action)>(fatigueMap.Keys);
        foreach (var key in keys)
        {
            if (key.team != team) continue;
            fatigueMap[key] = Mathf.Max(0f, fatigueMap[key] - recovery);
            if (fatigueMap[key] <= 0f) fatigueMap.Remove(key);
        }

        var unitKeys = new List<(int team, string unitName)>(unitFatigueMap.Keys);
        foreach (var key in unitKeys)
        {
            if (key.team != team) continue;
            unitFatigueMap[key] = Mathf.Max(0f, unitFatigueMap[key] - recovery);
            if (unitFatigueMap[key] <= 0f) unitFatigueMap.Remove(key);
        }
    }

    public float GetFatigue(int team, int slot, int ActionIndex)
    {
        var key = (team, slot, ActionIndex);
        return fatigueMap.TryGetValue(key, out float f) ? f : 0f;
    }

    public float GetUnitFatigue(int team, string unitName)
    {
        if (string.IsNullOrEmpty(unitName)) return 0f;
        var key = (team, unitName);
        return unitFatigueMap.TryGetValue(key, out float f) ? f : 0f;
    }

    public void ClearFatigue()
    {
        fatigueMap.Clear();
        unitFatigueMap.Clear();
    }

    #endregion

    #region Repeat-swap tracker (per turn)

    private Dictionary<int, int> swapCountThisTurn = new Dictionary<int, int>();

    // Call whenever a team actually performs a swap-type action (plain move,
    // bench swap, or either half of a move+bench-swap) so later ScoreAction
    // calls this same turn know how many swaps have already happened.
    public void OnSwapUsed(int team)
    {
        if (!swapCountThisTurn.ContainsKey(team)) swapCountThisTurn[team] = 0;
        swapCountThisTurn[team] += 1;
        InvalidateThreatCache();
    }

    public int GetSwapsUsedThisTurn(int team)
        => swapCountThisTurn.TryGetValue(team, out int c) ? c : 0;

    #endregion

    #region Unity

    void Awake()
    {
        if (combat == null)  combat  = FindFirstObjectByType<CombatScript>();
        if (runtime == null) runtime = FindFirstObjectByType<CombatRuntimeManager>();
    }

    #endregion

    #region Team helpers

    public int ActivePerspectiveTeam
    {
        get
        {
            if (runtime != null)
            {
                int t = runtime.ActiveTeamId;
                if (t >= 0) return t;
            }
            return CombatScript.TeamPlayer;
        }
    }

    public int OppositionTeam
    {
        get
        {
            int active = ActivePerspectiveTeam;
            return active == CombatScript.TeamPlayer ? CombatScript.TeamEnemy : CombatScript.TeamPlayer;
        }
    }

    #endregion

    #region Team-strength evaluation methods

    public void EvaluateAllTeams()
    {
        EvaluateTeamStrength(Perspective.Active);
        EvaluateTeamStrength(Perspective.Opposition);
    }

    public float EvaluateTeamStrength(Perspective team)
    {
        if (combat == null) { Debug.LogError("CombatAi: CombatScript reference is missing!"); return 0f; }
        if (weights == null) { Debug.LogError("CombatAi: WeightsTemplate (weights) is not assigned!"); return 0f; }

        int combatTeam = ToCombatTeam(team);
        TeamWeightsBase w = team == Perspective.Active ? (TeamWeightsBase)weights.allies : weights.enemies;
        var targetList = team == Perspective.Active ? lastActiveStrength : lastOppositionStrength;
        targetList.Clear();

        float summedMaxHp = 0f, summedCurrentHp = 0f, unitStrengthTotal = 0f;

        for (int slot = 0; slot < 4; slot++)
        {
            CombatScript.UnitData unit = combat.GetUnit(combatTeam, slot);
            if (string.IsNullOrEmpty(unit.name) || unit.hp <= 0f) continue;

            summedMaxHp     += unit.maxHp;
            summedCurrentHp += Mathf.Max(0f, unit.hp) + Mathf.Max(0f, unit.th);

            UnitStrengthEntry entry = BuildUnitStrength(unit, team, slot, w);
            targetList.Add(entry);
            unitStrengthTotal += entry.strength;
        }

        // Aggression is symmetric: both player and enemy AI use the allies tab
        // for their own team's aggression (see ComputeAgressionMultiplier).
        // Using the enemies tab here for the opposition made lastOppositionFinalStrength
        // inconsistent with the multiplier actually used in ScoreActionEffect.
        TeamWeightsBase aggressionW = weights.allies;
        float agressionMultiplier = aggressionW.totalHpAgressionWeight *
                               aggressionW.agressionHpCurve.Evaluate(MissingFrac(summedMaxHp, summedCurrentHp));
        float teamScore = unitStrengthTotal * agressionMultiplier;

        if (team == Perspective.Active) lastActiveStrengthTotal = unitStrengthTotal;
        else { lastOppositionStrengthTotal = unitStrengthTotal; lastOppositionFinalStrength = teamScore; }

        // Only stamp lastAgressionMultiplier from the active-team evaluation so
        // it matches the team being scored. DecideBestAction re-computes it via
        // ComputeAgressionMultiplier, which is the canonical source.
        if (team == Perspective.Active)
            lastAgressionMultiplier = agressionMultiplier;
        return teamScore;
    }

    UnitStrengthEntry BuildUnitStrength(CombatScript.UnitData unit, Perspective team, int slot, TeamWeightsBase w)
    {
        float hpMissing     = MissingFrac(unit.maxHp, unit.hp + unit.th);
        float armorMissing  = MissingFrac(unit.maxArmor, unit.armor);
        float shieldMissing = MissingFrac(unit.maxShield, unit.shield);

        float hpScore     = w.hpWeight     * w.hpCurve.Evaluate(hpMissing);
        float armorScore  = w.armorWeight  * w.armorCurve.Evaluate(armorMissing);
        float shieldScore = w.shieldWeight * w.shieldCurve.Evaluate(shieldMissing);

        return new UnitStrengthEntry
        {
            slot = slot,
            unit = unit,
            hpScore = hpScore,
            armorScore = armorScore,
            shieldScore = shieldScore,
            strength = hpScore + armorScore + shieldScore,
            hpMissingFrac = hpMissing,
            armorMissingFrac = armorMissing,
            shieldMissingFrac = shieldMissing,
        };
    }

    int ToCombatTeam(Perspective team) => team == Perspective.Active ? ActivePerspectiveTeam : OppositionTeam;

    static float MissingFrac(float full, float part)
        => full <= 0f ? 0f : Mathf.Clamp01(1f - (part / full));

    // Aggression scales with how hurt `team` currently is, using AllyWeights'
    // totalHpAgressionWeight/agressionHpCurve (they're explicitly symmetric -
    // "Both player AI and enemy AI use this tab for their own units"). Computed
    // directly from `team` rather than the Active/Opposition perspective
    // helpers above, so it stays correct even when DecideBestAction evaluates
    // a team that isn't the current runtime.ActiveTeamId (e.g. AI suggestion
    // previews).
    float ComputeAgressionMultiplier(int team)
    {
        if (combat == null || weights == null) return 1f;
        TeamWeightsBase w = weights.allies;

        float summedMaxHp = 0f, summedCurrentHp = 0f;
        for (int slot = 0; slot < 4; slot++)
        {
            UnitData unit = combat.GetUnit(team, slot);
            if (string.IsNullOrEmpty(unit.name) || unit.hp <= 0f) continue;
            summedMaxHp     += unit.maxHp;
            summedCurrentHp += Mathf.Max(0f, unit.hp) + Mathf.Max(0f, unit.th);
        }

        return w.totalHpAgressionWeight * w.agressionHpCurve.Evaluate(MissingFrac(summedMaxHp, summedCurrentHp));
    }

    #endregion

    #region Decide best action

    public AiAction DecideBestAction(int team, bool allowBenchSwaps = true)
    {
        lastActionEvaluation.Clear();
        lastChosenActionIndex = -1;
        hasActionEvaluation = false;
        lastActionTeam = team;

        // Invalidate the slot-value cache for this decision pass.
        _slotValueCache.Clear();
        _slotValueCacheDecision = Time.frameCount;

        BeginThreatCache(team);

        if (combat == null || weights == null) return default(AiAction);

        lastAgressionMultiplier = ComputeAgressionMultiplier(team);

        var actions = GenerateActions(team);
        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            ScoreAction(team, ref a);
            actions[i] = a;
        }

        if (!allowBenchSwaps)
        {
            actions.RemoveAll(a => a.kind == AiActionKind.BenchSwap || a.kind == AiActionKind.MoveAndBenchSwap);
        }

        lastActionEvaluation = actions;

        int idx = SelectIndex(actions, weights.ai.aiDecisionRandomness, AiRng);
        lastChosenActionIndex = idx;
        if (idx >= 0 && idx < actions.Count) lastChosenAction = actions[idx];
        hasActionEvaluation = true;

        if (CombatDebugHandler.UltraDebug && idx >= 0)
            Debug.Log($"[CombatAi] Team {team} chose #{idx}: {lastChosenAction.BuildDescription()}  score={lastChosenAction.Score:0.0}");

        return lastChosenAction;
    }

    #endregion

    #region Action generation

    bool CanAffordMove(int team)
    {
        if (combat == null || weights == null) return false;
        float AP = combat.GetAPBank(team).current;
        return AP >= weights.ai.teamPositionSwapAPCost;
    }

    bool CanAffordMoveAndAction(int team, ActionData action)
    {
        if (combat == null || weights == null) return false;
        float AP = combat.GetAPBank(team).current;
        return AP >= weights.ai.teamPositionSwapAPCost + action.APCost;
    }

    bool CanAffordBenchSwap(int team)
    {
        if (combat == null || weights == null) return false;
        float AP = combat.GetAPBank(team).current;
        return AP >= weights.ai.benchSwapAPCost;
    }

    bool CanAffordMoveAndBenchSwap(int team)
    {
        if (combat == null || weights == null) return false;
        float AP = combat.GetAPBank(team).current;
        return AP >= weights.ai.teamPositionSwapAPCost + weights.ai.benchSwapAPCost;
    }

    List<AiAction> GenerateActions(int team)
    {
        var list = new List<AiAction>();
        if (combat == null || weights == null) return list;

        bool canAffordMove = CanAffordMove(team);
        bool canAffordBench = CanAffordBenchSwap(team);
        bool canAffordMoveBench = CanAffordMoveAndBenchSwap(team);
        bool benchHasLiving = runtime != null && runtime.BenchHasLiving(team);

        for (int slot = 0; slot < 4; slot++)
        {
            UnitData u = combat.GetUnit(team, slot);
            bool slotAlive = !string.IsNullOrEmpty(u.name) && u.hp > 0;

            if (slotAlive)
            {
                bool canAct = u.stunRemainingRounds <= 0
                           && u.sleepRemainingRounds <= 0
                           && u.petrificationRemainingRounds <= 0;
                if (canAct)
                {
                    AddActionActions(team, slot, fromSlot: slot, list, prefixMove: -1);
                    if (canAffordMove)
                        AddPureMoveActions(team, slot, list);
                    if (canAffordMove)
                        AddMoveAndActionActions(team, slot, list, CanAffordMoveAndAction);
                }
            }

            if (runtime != null && weights.ai.enableBenchSwaps && weights.ai.IsBenchSwapSlot(slot) && benchHasLiving && canAffordBench)
                AddBenchSwapActions(team, slot, list);

            if (runtime != null && weights.ai.enableBenchSwaps && !weights.ai.IsBenchSwapSlot(slot) && benchHasLiving && canAffordMoveBench)
                AddMoveAndBenchSwapActions(team, slot, list);
        }

        list.Add(new AiAction
        {
            kind = AiActionKind.Idle,
            team = team,
            casterSlot = -1,
            ActionIndex = -1,
            description = "Idle (end turn)",
            effects = new List<AiExpectedEffect>(),
        });

        return list;
    }

    void AddActionActions(int team, int slot, int fromSlot, List<AiAction> list, int prefixMove)
    {
        UnitData u = combat.GetUnit(team, slot);
        if (u.actions == null) return;
        for (int i = 0; i < u.actions.Count; i++)
        {
            if (!combat.CanUseAction(team, slot, i)) continue;
            var act = u.actions[i];
            var effects = BuildEffects(act, team, fromSlot);
            if (effects.Count == 0) continue;

            var action = new AiAction
            {
                kind = prefixMove >= 0 ? AiActionKind.MoveAndUseAction : AiActionKind.UseAction,
                team = team,
                casterSlot = slot,
                ActionIndex = i,
                action = act,
                moveDestination = prefixMove,
                effects = effects,
            };
            action.description = action.BuildDescription();
            list.Add(action);
        }
    }

    void AddPureMoveActions(int team, int slot, List<AiAction> list)
    {
        for (int dest = 0; dest < 4; dest++)
        {
            if (dest == slot) continue;
            var action = new AiAction
            {
                kind = AiActionKind.Move,
                team = team,
                casterSlot = slot,
                ActionIndex = -1,
                action = default(ActionData),
                moveDestination = dest,
                effects = new List<AiExpectedEffect>(),
            };
            action.description = action.BuildDescription();
            list.Add(action);
        }
    }

    void AddMoveAndActionActions(int team, int slot, List<AiAction> list, System.Func<int, ActionData, bool> canAfford)
    {
        UnitData u = combat.GetUnit(team, slot);
        if (u.actions == null) return;
        for (int dest = 0; dest < 4; dest++)
        {
            if (dest == slot) continue;
            for (int i = 0; i < u.actions.Count; i++)
            {
                if (!combat.CanUseAction(team, slot, i)) continue;
                if (canAfford != null && !canAfford(team, u.actions[i])) continue;
                var act = u.actions[i];
                var effects = BuildEffects(act, team, dest);
                if (effects.Count == 0) continue;

                var action = new AiAction
                {
                    kind = AiActionKind.MoveAndUseAction,
                    team = team,
                    casterSlot = slot,
                    ActionIndex = i,
                    action = act,
                    moveDestination = dest,
                    effects = effects,
                };
                action.description = action.BuildDescription();
                list.Add(action);
            }
        }
    }

    void AddBenchSwapActions(int team, int slot, List<AiAction> list)
    {
        var benchList = runtime.GetBench(team);
        for (int bi = 0; bi < benchList.Count; bi++)
        {
            UnitData b = benchList[bi];
            if (string.IsNullOrEmpty(b.name) || b.hp <= 0) continue;

            var action = new AiAction
            {
                kind = AiActionKind.BenchSwap,
                team = team,
                casterSlot = slot,
                ActionIndex = -1,
                action = default(ActionData),
                moveDestination = -1,
                benchIndex = bi,
                effects = new List<AiExpectedEffect>(),
            };
            action.description = action.BuildDescription();
            list.Add(action);
        }
    }

    void AddMoveAndBenchSwapActions(int team, int fromSlot, List<AiAction> list)
    {
        if (runtime == null || weights == null) return;
        var benchList = runtime.GetBench(team);
        if (benchList == null || benchList.Count == 0) return;

        int[] benchSlots = weights.ai.benchSwapSlots;
        if (benchSlots == null || benchSlots.Length == 0)
            benchSlots = new int[] { 2, 3 };

        for (int bi = 0; bi < benchList.Count; bi++)
        {
            UnitData b = benchList[bi];
            if (string.IsNullOrEmpty(b.name) || b.hp <= 0) continue;

            for (int bsIdx = 0; bsIdx < benchSlots.Length; bsIdx++)
            {
                int benchSlot = benchSlots[bsIdx];
                if (benchSlot == fromSlot) continue;

                var action = new AiAction
                {
                    kind = AiActionKind.MoveAndBenchSwap,
                    team = team,
                    casterSlot = fromSlot,
                    ActionIndex = -1,
                    action = default(ActionData),
                    moveDestination = benchSlot,
                    benchIndex = bi,
                    effects = new List<AiExpectedEffect>(),
                };
                action.description = action.BuildDescription();
                list.Add(action);
            }
        }
    }

    #endregion

    #region Effect projection

    List<AiExpectedEffect> BuildEffects(ActionData action, int casterTeam, int fromSlot, bool applyRedirect = true)
    {
        var effects = new List<AiExpectedEffect>();
        if (combat == null) return effects;

        ActionData resolved = applyRedirect
            ? ResolveForCaster(casterTeam, fromSlot, action)
            : action;
        int firstAC = FirstTargetAC(resolved, casterTeam, fromSlot);
        var hp = ExpectedHitProfile(resolved.hitRoll, firstAC);

        // Table-driven effect projection: (valueExpr, coords, kind, duration).
        // Each row maps one CSV damage/heal/buff column to its EffectKind and
        // optional DOT duration. Adding a new effect type is a one-line change.
        var coordEffects = new (string expr, List<CombatScript.CasterPattern> coords, CombatScript.EffectKind kind, int duration)[]
        {
            // Damage types
            (resolved.physicalDmg, resolved.physicalCoords, CombatScript.EffectKind.Physical,  0),
            (resolved.magicDmg,   resolved.magicCoords,   CombatScript.EffectKind.Magic,    0),
            (resolved.fireDmg,     resolved.fireCoords,     CombatScript.EffectKind.FireDmg,   resolved.fireDuration),
            (resolved.bleedDmg,    resolved.bleedCoords,    CombatScript.EffectKind.BleedDmg,  resolved.bleedDuration),
            (resolved.poisonDmg,   resolved.poisonCoords,   CombatScript.EffectKind.PoisonDmg, resolved.poisonDuration),
            // Heals
            (resolved.recoverHealth, resolved.recoverHealthCoords, CombatScript.EffectKind.RecoverHealth,  0),
            (resolved.addHealth,     resolved.addHealthCoords,     CombatScript.EffectKind.AddHealth,      0),
            (resolved.addTempHealth, resolved.addTempHealthCoords, CombatScript.EffectKind.AddTempHealth,  0),
            // Buffs (armor/shield)
            (resolved.recoverArmor,  resolved.recoverArmorCoords,  CombatScript.EffectKind.RecoverArmor,   0),
            (resolved.addArmor,      resolved.addArmorCoords,      CombatScript.EffectKind.AddArmor,       0),
            (resolved.recoverShield, resolved.recoverShieldCoords, CombatScript.EffectKind.RecoverShield,  0),
            (resolved.addShield,     resolved.addShieldCoords,     CombatScript.EffectKind.AddShield,      0),
        };
        for (int i = 0; i < coordEffects.Length; i++)
        {
            var ce = coordEffects[i];
            AddCoordEffect(ce.expr, ce.coords, resolved, casterTeam, fromSlot, ce.kind, effects, ref hp, ce.duration);
        }

        // Status effects (stun/sleep/confusion/petrification) use a separate
        // path because they parse an integer duration, not a dice notation.
        var statusEffects = new (string timeExpr, List<CombatScript.CasterPattern> coords, CombatScript.EffectKind kind)[]
        {
            (resolved.stunTime,          resolved.stunCoords,          CombatScript.EffectKind.Stun),
            (resolved.sleepTime,         resolved.sleepCoords,         CombatScript.EffectKind.Sleep),
            (resolved.confusionTime,     resolved.confusionCoords,     CombatScript.EffectKind.Confusion),
            (resolved.petrificationTime, resolved.petrificationCoords, CombatScript.EffectKind.Petrification),
        };
        for (int i = 0; i < statusEffects.Length; i++)
        {
            var se = statusEffects[i];
            AddStatusEffect(se.timeExpr, se.coords, resolved, casterTeam, fromSlot, se.kind, effects, ref hp);
        }

        return effects;
    }

    ActionData ResolveForCaster(int casterTeam, int casterSlot, ActionData action)
        => combat.ResolveActionForCaster(casterTeam, casterSlot, action);

    static float OutgoingDamageMultiplier(UnitData caster, CombatScript.EffectKind kind)
    {
        if (caster.passiveKeys == null || caster.passiveKeys.Count == 0) return 1f;
        float mult = 1f;
        bool physical = kind == CombatScript.EffectKind.Physical;
        bool AP   = kind == CombatScript.EffectKind.Magic;
        float bloodFuryMult = 1f;
        bool hasBloodFury = false;
        for (int i = 0; i < caster.passiveKeys.Count; i++)
        {
            string key = caster.passiveKeys[i];
            if (string.IsNullOrEmpty(key)) continue;
            switch (key)
            {
                case "passive_BloodFury":
                    hasBloodFury = true;
                    break;
                case "passive_EagleEye":
                    if (physical) mult *= 1.15f;
                    break;
                case "passive_Shadowstep":
                    if (physical) mult *= 1.10f;
                    break;
                case "passive_Arcane":
                    if (AP) mult *= 1.25f;
                    break;
                case "passive_KeenEye":
                    if (physical) mult *= 1.15f;
                    break;
            }
        }
        if (hasBloodFury)
        {
            float hpFrac = caster.maxHp > 0 ? Mathf.Clamp01(caster.hp / caster.maxHp) : 1f;
            bloodFuryMult = 1f + (1f - hpFrac) * 0.5f;
            mult *= bloodFuryMult;
        }
        return mult;
    }

    static float IncomingDamageMultiplier(UnitData target, CombatScript.EffectKind kind)
    {
        if (target.passiveKeys == null || target.passiveKeys.Count == 0) return 1f;
        float mult = 1f;
        for (int i = 0; i < target.passiveKeys.Count; i++)
        {
            string key = target.passiveKeys[i];
            if (string.IsNullOrEmpty(key)) continue;
            switch (key)
            {
                case "passive_ThickSkin":
                    mult *= 0.8f;
                    break;
            }
        }
        return mult;
    }

    void AddCoordEffect(string valueExpr, List<CombatScript.CasterPattern> coords,
        ActionData a, int casterTeam, int fromSlot, CombatScript.EffectKind kind,
        List<AiExpectedEffect> effects, ref HitProfile hp, int duration)
    {
        if (string.IsNullOrWhiteSpace(valueExpr) || coords == null || coords.Count == 0) return;
        if (combat == null || combat.DiceRollerPublic == null) return;

        float baseVal = combat.DiceRollerPublic.ExpectedValue(valueExpr);
        if (baseVal <= 0f && !AiAction.IsDebuff(kind) && !AiAction.IsHeal(kind) && !AiAction.IsBuff(kind)) return;

        UnitData caster = combat.GetUnit(casterTeam, fromSlot);

        var entries = combat.GetEffectiveTargetsPublic(coords, fromSlot);
        foreach (var te in entries)
        {
            var (t, s) = combat.ResolveTarget(casterTeam, te.slot);
            if (t < 0 || s < 0) continue;

            int finalSlot = s;
            UnitData target = combat.GetUnit(t, finalSlot);
            if (a.hitAlive && (string.IsNullOrEmpty(target.name) || target.hp <= 0))
            {
                int aliveSlot = combat.FindNearestAliveSlot(t, finalSlot);
                if (aliveSlot >= 0) { finalSlot = aliveSlot; target = combat.GetUnit(t, finalSlot); }
            }
            if (string.IsNullOrEmpty(target.name) || target.hp <= 0) continue;
            if (t != casterTeam && combat.IsUnitVanished(t, finalSlot)) continue;

            float portion = te.portion;
            float expected = baseVal * portion * hp.expectedMultiplier;
            if (duration > 0)
            {
                float tickDmg = baseVal * portion * hp.expectedMultiplier;
                if (AiAction.IsDamage(kind) && tickDmg > 0f)
                {
                    float targetHp = Mathf.Max(0f, target.hp) + Mathf.Max(0f, target.th);
                    if (targetHp > 0f && tickDmg >= targetHp)
                    {
                        expected += tickDmg * 0.6f;
                    }
                    else
                    {
                        int effectiveTicks = targetHp > 0f
                            ? Mathf.Max(1, Mathf.CeilToInt(targetHp / tickDmg))
                            : duration;
                        effectiveTicks = Mathf.Min(effectiveTicks, duration);
                        expected += tickDmg * effectiveTicks * 0.6f;
                    }
                }
                else
                {
                    expected += baseVal * portion * hp.expectedMultiplier * duration * 0.6f;
                }
            }

            if (AiAction.IsDamage(kind) && expected > 0f)
            {
                expected *= OutgoingDamageMultiplier(caster, kind);
                expected *= IncomingDamageMultiplier(target, kind);
            }

            bool wouldKill = false;
            if (AiAction.IsDamage(kind) && expected > 0f)
                wouldKill = expected >= EffectiveHpForKill(target, kind);

            effects.Add(new AiExpectedEffect
            {
                team = t,
                slot = finalSlot,
                portion = portion,
                kind = kind,
                expectedAmount = expected,
                expectedHitChance = hp.hitChance,
                wouldKill = wouldKill,
                label = kind.ToString(),
            });
        }
    }

    void AddStatusEffect(string timeExpr, List<CombatScript.CasterPattern> coords,
        ActionData a, int casterTeam, int fromSlot, CombatScript.EffectKind kind,
        List<AiExpectedEffect> effects, ref HitProfile hp)
    {
        if (string.IsNullOrWhiteSpace(timeExpr) || coords == null || coords.Count == 0) return;
        int duration = 0;
        if (!int.TryParse(timeExpr.Trim(), out duration) || duration <= 0) return;

        var entries = combat.GetEffectiveTargetsPublic(coords, fromSlot);
        foreach (var te in entries)
        {
            var (t, s) = combat.ResolveTarget(casterTeam, te.slot);
            if (t < 0 || s < 0) continue;
            int finalSlot = s;
            UnitData target = combat.GetUnit(t, finalSlot);
            if (a.hitAlive && (string.IsNullOrEmpty(target.name) || target.hp <= 0))
            {
                int aliveSlot = combat.FindNearestAliveSlot(t, finalSlot);
                if (aliveSlot >= 0) { finalSlot = aliveSlot; target = combat.GetUnit(t, finalSlot); }
            }
            if (string.IsNullOrEmpty(target.name) || target.hp <= 0) continue;
            if (t != casterTeam && combat.IsUnitVanished(t, finalSlot)) continue;

            effects.Add(new AiExpectedEffect
            {
                team = t,
                slot = finalSlot,
                portion = te.portion,
                kind = kind,
                expectedAmount = duration * hp.hitChance,
                expectedHitChance = hp.hitChance,
                wouldKill = false,
                label = kind.ToString(),
            });
        }
    }

    int FirstTargetAC(ActionData a, int casterTeam, int fromSlot)
    {
        var coordLists = new List<List<CombatScript.CasterPattern>>
        {
            a.physicalCoords, a.fireCoords, a.bleedCoords, a.magicCoords, a.poisonCoords,
            a.recoverHealthCoords, a.addHealthCoords, a.addTempHealthCoords,
            a.recoverShieldCoords, a.addShieldCoords, a.recoverArmorCoords,
            a.addArmorCoords, a.stunCoords, a.sleepCoords, a.confusionCoords, a.petrificationCoords,
        };
        foreach (var coords in coordLists)
        {
            if (coords == null || coords.Count == 0) continue;
            var entries = combat.GetEffectiveTargetsPublic(coords, fromSlot);
            if (entries.Count == 0) continue;
            var (t, s) = combat.ResolveTarget(casterTeam, entries[0].slot);
            if (t < 0 || s < 0) continue;
            UnitData tu = combat.GetUnit(t, s);
            if (string.IsNullOrEmpty(tu.name)) continue;
            return Mathf.RoundToInt(combat.GetEffectiveAC(t, s));
        }
        return -1;
    }

    #endregion

    #region Slot attack-value prediction

    float ScoreEffectsValue(int team, List<AiExpectedEffect> effects)
    {
        if (effects == null || effects.Count == 0) return 0f;
        float total = 0f;
        // Apply the aggression multiplier to damage/kill values so future-attack
        // predictions (move scoring, bench-swap repAttackValue, EstimateFutureValue)
        // are scored on the same scale as direct ScoreActionEffect damage.
        float dmgMult = lastAgressionMultiplier;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            bool isAlly = (e.team == team);

            if (AiAction.IsDamage(e.kind))
            {
                if (!isAlly)
                    total += weights.enemies.scoring.damageWeight * e.expectedAmount * dmgMult;
                else
                    total -= weights.enemies.scoring.damageWeight * e.expectedAmount * dmgMult;
            }
            else if (AiAction.IsHeal(e.kind))
            {
                if (isAlly)
                {
                    UnitData target = combat.GetUnit(e.team, e.slot);
                    float missing = Mathf.Max(0f, target.maxHp - target.hp);
                    float effectiveHeal = Mathf.Min(e.expectedAmount, missing);
                    total += weights.allies.scoring.healWeight * effectiveHeal;
                }
            }
            else if (AiAction.IsBuff(e.kind))
            {
                if (isAlly)
                    total += weights.allies.scoring.buffEffectValueWeight * e.expectedAmount;
            }
            else if (AiAction.IsDebuff(e.kind))
            {
                if (!isAlly)
                    total += weights.enemies.scoring.debuffWeight * e.expectedAmount;
            }
        }
        return total;
    }

    float ScoreSlotAttackValue(int team, int unitSlot, int fromSlot, bool requireUsable)
    {
        var key = (unitSlot, fromSlot, requireUsable);
        if (_slotValueCacheDecision == Time.frameCount && _slotValueCache.TryGetValue(key, out float cached))
            return cached;

        UnitData u = combat.GetUnit(team, unitSlot);
        float value = ComputeSlotAttackValue(team, fromSlot, u, requireUsable, applyRedirect: true);

        if (_slotValueCacheDecision == Time.frameCount)
            _slotValueCache[key] = value;
        return value;
    }

    float ScoreSlotAttackValueForUnit(int team, int fromSlot, UnitData u, bool requireUsable)
    {
        return ComputeSlotAttackValue(team, fromSlot, u, requireUsable, applyRedirect: false);
    }

    float ComputeSlotAttackValue(int team, int fromSlot, UnitData u, bool requireUsable, bool applyRedirect)
    {
        if (u.actions == null || u.actions.Count == 0) return 0f;
        if (string.IsNullOrEmpty(u.name) || u.hp <= 0) return 0f;

        // CC check (applies to both immediate and future)
        if (u.stunRemainingRounds > 0 || u.sleepRemainingRounds > 0 || u.petrificationRemainingRounds > 0)
            return 0f;

        float total = 0f;
        for (int i = 0; i < u.actions.Count; i++)
        {
            var act = u.actions[i];
            if (!HasAnyUsefulEffect(act)) continue;

            if (requireUsable)
            {
                if (!combat.CanUseAction(u, team, i)) continue;
            }
            else
            {
                // Future-turn: ignore AP, check use-limit only
                if (act.useLimit > 0 && u.ActionUseCounts != null && i < u.ActionUseCounts.Count)
                {
                    if (u.ActionUseCounts[i] >= act.useLimit) continue;
                }
            }

            var effects = BuildEffects(act, team, fromSlot, applyRedirect);
            if (effects.Count == 0) continue;

            total += ScoreEffectsValue(team, effects);
        }
        return total;
    }

    #endregion

    #region Hit profile

    public struct HitProfile
    {
        public float expectedMultiplier;
        public float hitChance;
        public float critChance;
        public float fumbleChance;
    }

    public HitProfile ExpectedHitProfile(string hitRoll, float ac)
    {
        var p = new HitProfile { expectedMultiplier = 1f, hitChance = 1f };
        if (combat == null) return p;
        if (string.IsNullOrWhiteSpace(hitRoll) || !combat.IsDiceNotationPublic(hitRoll)) return p;

        var roller = combat.DiceRollerPublic;
        if (roller == null) return p;
        if (!diceSystem.IsValidNotation(hitRoll)) return p;

        var parsed = roller.ParsePublic(hitRoll);
        int d20Idx = -1;
        for (int i = 0; i < parsed.groups.Count; i++)
            if (parsed.groups[i].faces == 20) { d20Idx = i; break; }

        if (d20Idx < 0)
        {
            float mean = roller.ExpectedValue(hitRoll);
            float half = Mathf.CeilToInt(ac * 0.5f);
            if (ac <= 0)     { p.expectedMultiplier = 1f; p.hitChance = 1f; }
            else if (mean >= ac) { p.expectedMultiplier = 1f; p.hitChance = 1f; }
            else if (mean >= half) { p.expectedMultiplier = 0.5f; p.hitChance = 1f; }
            else { p.expectedMultiplier = 0f; p.hitChance = 0f; }
            return p;
        }

        float bonus = parsed.flat;
        for (int i = 0; i < parsed.groups.Count; i++)
            if (i != d20Idx) bonus += roller.ExpectedValue(SingleGroupNotation(parsed.groups[i]));

        float sumMult = 0f;
        int hits = 0, crits = 0, fumbles = 0;
        for (int face = 1; face <= 20; face++)
        {
            float total = face + bonus;
            if (face == 1)   { fumbles++; continue; }
            if (face == 20)  { crits++; hits++; sumMult += 2f; continue; }
            if (ac <= 0)     { hits++; sumMult += 1f; continue; }
            if (total >= ac)  { hits++; sumMult += 1f; continue; }
            float half = Mathf.CeilToInt(ac * 0.5f);
            if (total >= half){ hits++; sumMult += 0.5f; continue; }
        }
        p.expectedMultiplier = sumMult / 20f;
        p.hitChance = hits / 20f;
        p.critChance = crits / 20f;
        p.fumbleChance = fumbles / 20f;
        return p;
    }

    static string SingleGroupNotation(diceSystem.DiceGroup g)
    {
        string sign = g.sign < 0 ? "-" : "+";
        string mod = "";
        switch (g.modifier)
        {
            case diceSystem.DiceModifier.KeepHighest: mod = "kh" + g.modifierCount; break;
            case diceSystem.DiceModifier.KeepLowest:  mod = "kl" + g.modifierCount; break;
            case diceSystem.DiceModifier.Explode:      mod = "!"; break;
            case diceSystem.DiceModifier.Reroll:       mod = "r" + g.modifierCount; break;
        }
        return $"{sign}{g.amount}d{g.faces}{mod}";
    }

    public static float EffectiveHpForKill(UnitData u, CombatScript.EffectKind kind)
    {
        float hp = Mathf.Max(0f, u.hp) + Mathf.Max(0f, u.th);
        bool absorbShield, absorbArmor;
        switch (kind)
        {
            case CombatScript.EffectKind.Physical:
            case CombatScript.EffectKind.FireDmg:
                absorbShield = true; absorbArmor = true; break;
            case CombatScript.EffectKind.BleedDmg:
                absorbShield = false; absorbArmor = true; break;
            case CombatScript.EffectKind.Magic:
            case CombatScript.EffectKind.PoisonDmg:
            default:
                absorbShield = false; absorbArmor = false; break;
        }
        if (absorbShield) hp += Mathf.Max(0f, u.shield);
        if (absorbArmor) hp += Mathf.Min(Mathf.Max(0f, u.armor), Mathf.Max(0f, u.armorBlock));
        return hp;
    }

    public static float UnitImportance(UnitData u)
    {
        if (string.IsNullOrEmpty(u.name) || u.hp <= 0) return 0f;
        float hpFrac     = u.maxHp > 0 ? Mathf.Clamp01(u.hp / u.maxHp) : 0f;
        float armorFrac  = u.maxArmor > 0 ? Mathf.Clamp01(u.armor / u.maxArmor) : 0f;
        float shieldFrac = u.maxShield > 0 ? Mathf.Clamp01(u.shield / u.maxShield) : 0f;
        float imp = hpFrac * 0.5f + armorFrac * 0.3f + shieldFrac * 0.2f;

        if (u.passiveKeys != null)
        {
            for (int i = 0; i < u.passiveKeys.Count; i++)
            {
                string key = u.passiveKeys[i];
                if (string.IsNullOrEmpty(key)) continue;
                switch (key)
                {
                    case "passive_EagleEye":    imp += 0.10f; break;
                    case "passive_Shadowstep":  imp += 0.12f; break;
                    case "passive_Vampiric":    imp += 0.15f; break;
                    case "passive_SoulLeech":   imp += 0.10f; break;
                    case "passive_ThickSkin":   imp += 0.10f; break;
                    case "passive_Aura":        imp += 0.08f; break;
                    case "passive_DivineGrace": imp += 0.08f; break;
                    case "passive_BloodFury":   imp += 0.12f; break;
                    case "passive_Stalwart":    imp += 0.10f; break;
                    default: break;
                }
            }
        }
        return Mathf.Clamp01(imp);
    }

    public float LikelyToAttack(int team, int slot, UnitData u)
    {
        if (u.actions == null || u.actions.Count == 0) return 0f;
        int damaging = 0, total = 0;
        for (int i = 0; i < u.actions.Count; i++)
        {
            if (!combat.CanUseAction(team, slot, i)) continue;
            total++;
            if (HasDamage(u.actions[i])) damaging++;
        }
        return total == 0 ? 0f : (float)damaging / total;
    }

    public static bool HasDamage(ActionData a)
        => !string.IsNullOrWhiteSpace(a.physicalDmg) || !string.IsNullOrWhiteSpace(a.magicDmg)
        || !string.IsNullOrWhiteSpace(a.fireDmg) || !string.IsNullOrWhiteSpace(a.bleedDmg)
        || !string.IsNullOrWhiteSpace(a.poisonDmg);

    public static bool HasAnyUsefulEffect(ActionData a)
        => HasDamage(a) || !string.IsNullOrWhiteSpace(a.recoverHealth) || !string.IsNullOrWhiteSpace(a.addHealth)
        || !string.IsNullOrWhiteSpace(a.addTempHealth) || !string.IsNullOrWhiteSpace(a.recoverArmor)
        || !string.IsNullOrWhiteSpace(a.addArmor) || !string.IsNullOrWhiteSpace(a.recoverShield)
        || !string.IsNullOrWhiteSpace(a.addShield) || !string.IsNullOrWhiteSpace(a.stunTime)
        || !string.IsNullOrWhiteSpace(a.sleepTime) || !string.IsNullOrWhiteSpace(a.confusionTime)
        || !string.IsNullOrWhiteSpace(a.petrificationTime);

    #endregion

    #region Threat estimation

    Dictionary<(int team, int slot), float> _threatCache;
    int _threatCacheDecision = -1;

    void BeginThreatCache(int decisionTeam)
    {
        _threatCache = new Dictionary<(int, int), float>();
        for (int team = 0; team < 2; team++)
            for (int s = 0; s < 4; s++)
            {
                UnitData u = combat.GetUnit(team, s);
                if (string.IsNullOrEmpty(u.name) || u.hp <= 0) continue;
                _threatCache[(team, s)] = EstimateIncomingThreatUncached(team, s);
            }
        _threatCacheDecision = decisionTeam;
    }

    float CachedThreat(int team, int slot)
    {
        if (_threatCache != null)
            return _threatCache.TryGetValue((team, slot), out float t) ? t : 0f;
        return EstimateIncomingThreatUncached(team, slot);
    }

    public float EstimateIncomingThreat(int targetTeam, int targetSlot)
    {
        if (_threatCache != null)
            return _threatCache.TryGetValue((targetTeam, targetSlot), out float t) ? t : 0f;
        return EstimateIncomingThreatUncached(targetTeam, targetSlot);
    }

    public void InvalidateThreatCache()
    {
        _threatCache = null;
        _threatCacheDecision = -1;
    }

    float EstimateIncomingThreatUncached(int targetTeam, int targetSlot)
    {
        if (combat == null) return 0f;
        int opp = targetTeam == CombatScript.TeamPlayer ? CombatScript.TeamEnemy : CombatScript.TeamPlayer;
        float threat = 0f;
        for (int s = 0; s < 4; s++)
        {
            UnitData u = combat.GetUnit(opp, s);
            if (string.IsNullOrEmpty(u.name) || u.hp <= 0 || u.actions == null) continue;
            for (int i = 0; i < u.actions.Count; i++)
            {
                if (!combat.CanUseAction(opp, s, i)) continue;
                threat += ExpectedActionDamageToSlot(opp, s, u.actions[i], targetTeam, targetSlot);
            }
        }
        return threat;
    }

    float ExpectedActionDamageToSlot(int casterTeam, int casterSlot, ActionData a,
        int targetTeam, int targetSlot)
    {
        if (combat == null) return 0f;
        float sum = 0f;
        sum += AddCoordDamage(a, casterTeam, casterSlot, a.physicalDmg, a.physicalCoords, targetTeam, targetSlot, CombatScript.EffectKind.Physical);
        sum += AddCoordDamage(a, casterTeam, casterSlot, a.magicDmg,   a.magicCoords,   targetTeam, targetSlot, CombatScript.EffectKind.Magic);
        sum += AddCoordDamage(a, casterTeam, casterSlot, a.fireDmg,     a.fireCoords,     targetTeam, targetSlot, CombatScript.EffectKind.FireDmg);
        sum += AddCoordDamage(a, casterTeam, casterSlot, a.bleedDmg,    a.bleedCoords,    targetTeam, targetSlot, CombatScript.EffectKind.BleedDmg);
        sum += AddCoordDamage(a, casterTeam, casterSlot, a.poisonDmg,   a.poisonCoords,   targetTeam, targetSlot, CombatScript.EffectKind.PoisonDmg);
        return sum;
    }

    float AddCoordDamage(ActionData a, int casterTeam, int casterSlot,
        string dmgExpr, List<CombatScript.CasterPattern> coords,
        int targetTeam, int targetSlot, CombatScript.EffectKind kind)
    {
        if (string.IsNullOrWhiteSpace(dmgExpr) || coords == null || coords.Count == 0) return 0f;
        var entries = combat.GetEffectiveTargetsPublic(coords, casterSlot);
        float ac = combat.GetEffectiveAC(targetTeam, targetSlot);
        var hp = ExpectedHitProfile(a.hitRoll, ac);
        float baseDmg = combat.DiceRollerPublic != null ? combat.DiceRollerPublic.ExpectedValue(dmgExpr) : 0f;
        float total = 0f;
        foreach (var te in entries)
        {
            var (t, s) = combat.ResolveTarget(casterTeam, te.slot);
            if (t != targetTeam || s != targetSlot) continue;
            total += baseDmg * te.portion * hp.expectedMultiplier;
        }
        return total;
    }

    #endregion

    #region Scoring

    void ScoreAction(int team, ref AiAction action)
    {
        if (combat == null || weights == null) return;
        var gs = weights.ai;
        var b = new AiScoreBreakdown();

        float teamAP = combat.GetAPBank(team).current;
        float teamAPCap = Mathf.Max(1f, combat.GetTeamAPCap(team));
        float APFrac = Mathf.Clamp01(teamAP / teamAPCap);

        int cost = ActionAPCost(team, ref action);
        if (cost > 0 && gs.enableAPCostPenalty)
            b.APCostScore = -gs.APCostWeight * cost * gs.APScarcityCurve.Evaluate(APFrac);

        if (action.kind == AiActionKind.Idle)
        {
            if (gs.enableIdlePenalty)
                b.APCostScore -= gs.idlePenalty;
        }
        else if (gs.enableAPOverflowPenalty && APFrac >= 0.99f && cost == 0)
        {
            b.APCostScore -= gs.APOverflowPenalty;
        }

        switch (action.kind)
        {
            case AiActionKind.UseAction:
            case AiActionKind.MoveAndUseAction:
                // ai never factors taunt or confusion into scoring
                ScoreActionEffect(team, ref action, ref b);
                break;
            case AiActionKind.Move:
                ScorePureMove(team, ref action, ref b);
                break;
            case AiActionKind.BenchSwap:
                ScoreBenchSwap(team, ref action, ref b, APFrac);
                break;
            case AiActionKind.MoveAndBenchSwap:
                ScoreMoveAndBenchSwap(team, ref action, ref b, APFrac);
                break;
        }

        // An action that changed literally nothing - e.g. a RecoverHealth
        // heal on a target already at max HP, which CombatScript caps to
        // +0 HP - shouldn't be able to beat Idle just because of the flat
        // "used my kit" resourceScore bonus below. Heals (and buffs/debuffs)
        // are deliberately still generated as candidates even when their
        // baseVal can't help anyone (see AddCoordEffect), so this is the
        // place that has to catch a fully-wasted one.
        bool actionHadNoRealEffect = false;
        if (action.kind == AiActionKind.UseAction || action.kind == AiActionKind.MoveAndUseAction)
        {
            actionHadNoRealEffect = action.effects != null && action.effects.Count > 0
                && Mathf.Approximately(b.damageScore, 0f)
                && Mathf.Approximately(b.killScore, 0f)
                && Mathf.Approximately(b.healScore, 0f)
                && Mathf.Approximately(b.buffScore, 0f)
                && Mathf.Approximately(b.debuffScore, 0f)
                && Mathf.Approximately(b.survivalScore, 0f);
        }

        bool isSwapTypeAction = action.kind == AiActionKind.Move
                             || action.kind == AiActionKind.BenchSwap
                             || action.kind == AiActionKind.MoveAndBenchSwap;
        if (isSwapTypeAction && gs.enableRepeatSwapPenalty)
        {
            int swapsAlready = GetSwapsUsedThisTurn(team);
            if (swapsAlready >= 1)
                b.repeatSwapPenalty = -gs.repeatSwapPenaltyWeight * Mathf.Pow(gs.repeatSwapPenaltyGrowth, swapsAlready - 1);
        }

        if ((action.kind == AiActionKind.UseAction || action.kind == AiActionKind.MoveAndUseAction)
            && action.ActionIndex >= 0 && action.casterSlot >= 0)
        {
            UnitData caster = combat.GetUnit(team, action.casterSlot);
            if (caster.actions != null && action.ActionIndex < caster.actions.Count
                && caster.ActionUseCounts != null && action.ActionIndex < caster.ActionUseCounts.Count)
            {
                var act = caster.actions[action.ActionIndex];
                int used = caster.ActionUseCounts[action.ActionIndex];

                if (gs.enableResourceScoring && !actionHadNoRealEffect)
                {
                    if (act.useLimit > 0)
                    {
                        float remainingFrac = Mathf.Clamp01((act.useLimit - used) / (float)act.useLimit);
                        b.resourceScore = gs.resourceWeight * gs.resourceCurve.Evaluate(remainingFrac);
                    }
                    else b.resourceScore = gs.resourceWeight;
                }

                if (gs.preferVariedActions)
                {
                    float fatigue = GetFatigue(team, action.casterSlot, action.ActionIndex);
                    if (fatigue > 0f)
                        b.fatigueScore = -(gs.actionFatigueWeight + fatigue);

                    float unitFatigue = GetUnitFatigue(team, caster.name);
                    if (unitFatigue > 0f)
                        b.fatigueScore -= (gs.unitFatigueWeight + unitFatigue);
                }
            }
        }

        if (gs.aiLookaheadDepth > 0)
            b.futureScore = EstimateFutureValue(team, ref action, gs);

        // A wasted action still spent AP for nothing, so it should never be
        // able to score better than just ending the turn would.
        if (actionHadNoRealEffect && gs.enableIdlePenalty)
            b.APCostScore -= gs.idlePenalty;

        action.breakdown = b;
    }

    void ScoreActionEffect(int team, ref AiAction action, ref AiScoreBreakdown b)
    {
        if (action.effects == null || action.effects.Count == 0) return;
        if (weights == null) return;

        float maxThreat = 0f;
        if (weights.enemies.scoring.enableThreatTargeting)
        {
            int opp = team == CombatScript.TeamPlayer ? CombatScript.TeamEnemy : CombatScript.TeamPlayer;
            for (int s = 0; s < 4; s++)
            {
                UnitData u = combat.GetUnit(opp, s);
                if (string.IsNullOrEmpty(u.name) || u.hp <= 0) continue;
                float t = CachedThreat(opp, s);
                if (t > maxThreat) maxThreat = t;
            }
        }

        for (int i = 0; i < action.effects.Count; i++)
        {
            var e = action.effects[i];
            UnitData target = combat.GetUnit(e.team, e.slot);
            if (string.IsNullOrEmpty(target.name)) continue;

            bool isAlly = (e.team == team);
            float importance = UnitImportance(target);
            float hpFrac = target.maxHp > 0 ? Mathf.Clamp01(target.hp / target.maxHp) : 0f;

            if (AiAction.IsDamage(e.kind))
            {
                if (isAlly)
                {
                    var awFF = weights.enemies.scoring;
                    b.damageScore -= awFF.damageWeight * e.expectedAmount * lastAgressionMultiplier;
                    if (e.wouldKill && awFF.enableKillBonus)
                        b.killScore -= awFF.killWeight;
                }
                else
                {
                    var aw = weights.enemies.scoring;
                    float focusFire = aw.enableFocusFire ? aw.damageFocusFireCurve.Evaluate(hpFrac) : 1f;
                    float importanceMult = aw.enableDamageImportance ? aw.targetImportanceCurve.Evaluate(importance) : 1f;
                    b.damageScore += aw.damageWeight * e.expectedAmount * focusFire * importanceMult * lastAgressionMultiplier;
                    if (e.wouldKill && aw.enableKillBonus)
                    {
                        float impCurve = aw.targetImportanceCurve.Evaluate(importance);
                        b.killScore += aw.killWeight * aw.killHpFractionCurve.Evaluate(hpFrac) * impCurve * lastAgressionMultiplier;
                    }
                    if (aw.enableThreatTargeting && maxThreat > 0f)
                    {
                        float targetThreat = CachedThreat(e.team, e.slot);
                        float normThreat = maxThreat > 0f ? targetThreat / maxThreat : 0f;
                        b.damageScore += aw.threatTargetingWeight * aw.threatTargetingCurve.Evaluate(normThreat) * lastAgressionMultiplier;
                    }
                }
            }            
            else if (AiAction.IsHeal(e.kind))
            {
                if (isAlly)
                {
                    var aw = weights.allies.scoring;
                    float impCurve = aw.targetImportanceCurve.Evaluate(importance);
                    float missing = Mathf.Max(0f, target.maxHp - target.hp);
                    float effectiveHeal = Mathf.Min(e.expectedAmount, missing);
                    float missingFrac = 1f - hpFrac;
                    float healVal = aw.healWeight * effectiveHeal * aw.healMissingCurve.Evaluate(missingFrac);
                    if (aw.enableOverhealPenalty)
                    {
                        float overheal = Mathf.Max(0f, e.expectedAmount - missing);
                        if (overheal > 0f) healVal -= aw.overhealPenaltyWeight * overheal;
                    }
                    if (aw.enableSurvival)
                    {
                        float threat = CachedThreat(e.team, e.slot);
                        if (threat > 0f && target.hp > 0f)
                        {
                            float hpAfterNoHeal = target.hp - threat;
                            float hpAfterHeal = (target.hp + effectiveHeal) - threat;
                            if (hpAfterNoHeal <= 0f && hpAfterHeal > 0f)
                            {
                                if (aw.enableHealPreventsDeathBonus) healVal *= aw.healPreventsDeathBonus;
                                b.survivalScore += aw.survivalWeight * weights.ai.lowHealthCurve.Evaluate(hpFrac) * impCurve;
                            }
                        }
                    }
                    b.healScore += healVal;
                }
                else
                {
                    float missing = Mathf.Max(0f, target.maxHp - target.hp);
                    float effectiveHeal = Mathf.Min(e.expectedAmount, missing);
                    b.healScore -= weights.allies.scoring.healWeight * effectiveHeal;
                }
            }
            else if (AiAction.IsBuff(e.kind))
            {
                if (isAlly)
                {
                    var aw = weights.allies.scoring;
                    float impCurve = aw.targetImportanceCurve.Evaluate(importance);
                    float strength = aw.buffTargetStrengthCurve.Evaluate(importance);
                    float likely = LikelyToAttack(e.team, e.slot, target);
                    float lowHealthMult = aw.enableBuffLowHealthBonus
                        ? (1f + aw.buffLowHealthWeight * weights.ai.lowHealthCurve.Evaluate(hpFrac))
                        : 1f;
                    b.buffScore += aw.buffEffectValueWeight * e.expectedAmount
                                 * aw.buffTargetStrengthWeight * strength
                                 * aw.buffLikelyToAttackWeight * likely
                                 * lowHealthMult;
                }
                else
                {
                    b.buffScore -= weights.allies.scoring.buffEffectValueWeight * e.expectedAmount;
                }
            }
            else if (AiAction.IsDebuff(e.kind))
            {
                if (isAlly)
                {
                    b.debuffScore -= weights.enemies.scoring.debuffWeight * e.expectedAmount;
                }
                else
                {
                    var aw = weights.enemies.scoring;
                    float debuffCurve = aw.debuffTargetCurve.Evaluate(importance);
                    float highThreat = (aw.enableDebuffHighThreat && importance > 0.5f) ? aw.debuffHighThreatBonus : 1f;
                    b.debuffScore += aw.debuffWeight * e.expectedAmount * debuffCurve * highThreat;
                    if (aw.enableThreatTargeting && maxThreat > 0f)
                    {
                        float targetThreat = CachedThreat(e.team, e.slot);
                        float normThreat = maxThreat > 0f ? targetThreat / maxThreat : 0f;
                        b.debuffScore += aw.threatTargetingWeight * aw.threatTargetingCurve.Evaluate(normThreat);
                    }
                }
            }
        }
    }

    void ScorePureMove(int team, ref AiAction action, ref AiScoreBreakdown b)
    {
        var aw = weights.ai;
        if (action.casterSlot < 0 || action.moveDestination < 0) return;
        UnitData u = combat.GetUnit(team, action.casterSlot);
        if (string.IsNullOrEmpty(u.name)) return;

        int reachNow  = CountReachableEnemyTargets(team, action.casterSlot, u);
        int reachNext = CountReachableEnemyTargets(team, action.moveDestination, u);
        int delta = reachNext - reachNow;
        if (delta > 0)
            b.positionScore += aw.positionWeight * delta;

        float attackNow  = ScoreSlotAttackValue(team, action.casterSlot, action.casterSlot, requireUsable: true);
        float attackNext = ScoreSlotAttackValue(team, action.casterSlot, action.moveDestination, requireUsable: true);
        float attackDelta = attackNext - attackNow;

        if (attackDelta > 0)
            b.positionScore += attackDelta;

        if (attackNow <= 0f && attackNext > 0f)
            b.positionScore += aw.positionWeight * 5f + attackNext;

        bool canActNow = HasUsableUsefulAttack(team, action.casterSlot, u);
        if (!canActNow && HasPotentialToAct(u))
        {
            float futureNow  = ScoreSlotAttackValue(team, action.casterSlot, action.casterSlot, requireUsable: false);
            float futureNext = ScoreSlotAttackValue(team, action.casterSlot, action.moveDestination, requireUsable: false);
            float futureDelta = futureNext - futureNow;
            if (futureDelta > 0)
                b.futureScore += futureDelta * 0.5f; // discounted: happens next turn
            else if (reachNow == 0 && reachNext > 0)
                b.futureScore += aw.positionWeight * 0.5f * reachNext; // fallback reach bonus
            else
                b.futureScore += aw.positionWeight * 0.25f;
        }

        float hpFrac = u.maxHp > 0 ? Mathf.Clamp01(u.hp / u.maxHp) : 0f;
        float dangerNow  = aw.slotDangerCurve.Evaluate(action.casterSlot);
        float dangerNext = aw.slotDangerCurve.Evaluate(action.moveDestination);

        if (dangerNow > dangerNext)
        {
            // Moving to a safer slot: always give a bonus (scaled up for low HP).
            float safetyMult = 0.5f + 0.5f * aw.lowHealthCurve.Evaluate(hpFrac);
            b.positionScore += aw.positionSafetyWeight * safetyMult * (dangerNow - dangerNext);
        }
        else if (dangerNext > dangerNow && attackDelta <= 0f)
        {
            // Moving to a MORE dangerous slot with NO attack gain: penalize.
            float dangerMult = 0.5f + 0.5f * aw.lowHealthCurve.Evaluate(hpFrac);
            b.positionScore -= aw.positionSafetyWeight * dangerMult * (dangerNext - dangerNow);
        }

    }

    int CountReachableEnemyTargets(int team, int casterSlot, UnitData u)
    {
        if (u.actions == null) return 0;
        var seen = new HashSet<int>();
        bool anyHitAlive = false;
        for (int i = 0; i < u.actions.Count; i++)
        {
            var a = u.actions[i];
            if (a.hitAlive) anyHitAlive = true;
            AddReachableSlots(team, casterSlot, a.physicalCoords, seen);
            AddReachableSlots(team, casterSlot, a.magicCoords, seen);
            AddReachableSlots(team, casterSlot, a.fireCoords, seen);
            AddReachableSlots(team, casterSlot, a.bleedCoords, seen);
            AddReachableSlots(team, casterSlot, a.poisonCoords, seen);
        }
        int n = 0;
        foreach (int raw in seen)
        {
            var (t, s) = combat.ResolveTarget(team, raw);
            if (t != team)
            {
                UnitData enemy = combat.GetUnit(t, s);
                if (!string.IsNullOrEmpty(enemy.name) && enemy.hp > 0)
                    n++;
                else if (anyHitAlive)
                    n++; // hitAlive will redirect to nearest living enemy
            }
        }
        return n;
    }

    void AddReachableSlots(int team, int casterSlot, List<CombatScript.CasterPattern> coords, HashSet<int> seen)
    {
        if (coords == null) return;
        var entries = combat.GetEffectiveTargetsPublic(coords, casterSlot);
        for (int i = 0; i < entries.Count; i++) seen.Add(entries[i].slot);
    }

    void ScoreMoveAndBenchSwap(int team, ref AiAction action, ref AiScoreBreakdown b, float APFrac)
    {
        var aw = weights.ai;
        if (runtime == null) return;
        if (action.casterSlot < 0 || action.benchIndex < 0 || action.moveDestination < 0) return;

        var benchList = runtime.GetBench(team);
        if (action.benchIndex >= benchList.Count) return;
        UnitData replacement = benchList[action.benchIndex];
        if (string.IsNullOrEmpty(replacement.name) || replacement.hp <= 0) return;

        // The unit being swapped OUT is at casterSlot (a non-bench slot).
        UnitData target = combat.GetUnit(team, action.casterSlot);

        // The unit currently at the bench slot (moveDestination) gets displaced to casterSlot.
        UnitData displaced = combat.GetUnit(team, action.moveDestination);

        float s = 0f;
        bool targetDead = string.IsNullOrEmpty(target.name) || target.hp <= 0;

        if (targetDead)
            s += aw.swapDeadUnitWeight;
        else
        {
            float hpFrac = target.maxHp > 0 ? Mathf.Clamp01(target.hp / target.maxHp) : 0f;
            s += aw.swapLowHpWeight * aw.lowHealthCurve.Evaluate(hpFrac);
            s += aw.swapLowRemainingUsesWeight * (1f - RemainingUsesFraction(target));
            if (!HasUsableUsefulAttack(team, action.casterSlot, target))
                s += aw.swapNoUsefulAttacksWeight;
        }

        // Replacement value (same logic as regular bench swap).
        float repImp = UnitImportance(replacement);
        s += aw.swapReplacementValueWeight * aw.swapReplacementCurve.Evaluate(repImp);
        if (HasUsableUsefulAttack(replacement, team, action.moveDestination))
            s += aw.swapReplacementValueWeight * 0.5f;
        // Discounted: this is a predicted future attack, not one that happened this action.
        float repAttackValue = ScoreSlotAttackValueForUnit(team, action.moveDestination, replacement, requireUsable: true);
        s += repAttackValue * aw.swapFutureAttackDiscount;

        // Displacement evaluation: the unit at the bench slot gets moved to casterSlot.
        bool displacedAlive = !string.IsNullOrEmpty(displaced.name) && displaced.hp > 0;
        if (displacedAlive)
        {
            // Penalize if the displaced unit is moving to a more dangerous slot.
            float dangerNow  = aw.slotDangerCurve.Evaluate(action.moveDestination);
            float dangerNext = aw.slotDangerCurve.Evaluate(action.casterSlot);
            if (dangerNext > dangerNow)
            {
                float hpFrac = displaced.maxHp > 0 ? Mathf.Clamp01(displaced.hp / displaced.maxHp) : 0f;
                s -= aw.positionSafetyWeight * aw.lowHealthCurve.Evaluate(hpFrac) * (dangerNext - dangerNow);
            }
        }

        b.benchSwapScore = s;

        // AP cost penalty for the bench-swap portion (the move portion is
        // handled by the general AP cost penalty in ScoreAction). Scaled by
        // the same scarcity curve so it also bites harder when AP is already low.
        int swapCost = aw.benchSwapAPCost;
        if (swapCost > 0)
            b.APCostScore -= aw.swapAPCostWeight * swapCost * aw.APScarcityCurve.Evaluate(APFrac);
    }

    void ScoreBenchSwap(int team, ref AiAction action, ref AiScoreBreakdown b, float APFrac)
    {
        var aw = weights.ai;
        if (runtime == null) return;
        if (action.casterSlot < 0 || action.benchIndex < 0) return;

        var benchList = runtime.GetBench(team);
        if (action.benchIndex >= benchList.Count) return;
        UnitData replacement = benchList[action.benchIndex];
        UnitData current = combat.GetUnit(team, action.casterSlot);
        if (string.IsNullOrEmpty(replacement.name) || replacement.hp <= 0) return;

        float s = 0f;
        bool currentDead = string.IsNullOrEmpty(current.name) || current.hp <= 0;

        if (currentDead) s += aw.swapDeadUnitWeight;
        else
        {
            float hpFrac = current.maxHp > 0 ? Mathf.Clamp01(current.hp / current.maxHp) : 0f;
            s += aw.swapLowHpWeight * aw.lowHealthCurve.Evaluate(hpFrac);
            s += aw.swapLowRemainingUsesWeight * (1f - RemainingUsesFraction(current));
            if (!HasUsableUsefulAttack(team, action.casterSlot, current))
                s += aw.swapNoUsefulAttacksWeight;
        }

        float repImp = UnitImportance(replacement);
        s += aw.swapReplacementValueWeight * aw.swapReplacementCurve.Evaluate(repImp);
        if (HasUsableUsefulAttack(replacement, team, action.casterSlot))
            s += aw.swapReplacementValueWeight * 0.5f;

        float repAttackValue = ScoreSlotAttackValueForUnit(team, action.casterSlot, replacement, requireUsable: true);
        s += repAttackValue * aw.swapFutureAttackDiscount;

        b.benchSwapScore = s;
        int cost = aw.benchSwapAPCost;
        if (cost > 0)
            b.APCostScore -= aw.swapAPCostWeight * cost * aw.APScarcityCurve.Evaluate(APFrac);
    }

    bool HasUsableUsefulAttack(int team, int slot, UnitData u)
    {
        if (u.actions == null) return false;
        for (int i = 0; i < u.actions.Count; i++)
        {
            if (!combat.CanUseAction(team, slot, i)) continue;
            if (!HasAnyUsefulEffect(u.actions[i])) continue;

            var effects = BuildEffects(u.actions[i], team, slot);
            if (effects.Count > 0) return true;
        }
        return false;
    }

    bool HasUsableUsefulAttack(UnitData replacement, int team, int deploySlot)
    {
        if (replacement.actions == null) return false;
        if (!AnyEnemyAlive(team)) return false;
        for (int i = 0; i < replacement.actions.Count; i++)
        {
            if (!combat.CanUseAction(replacement, team, i)) continue;
            if (!HasAnyUsefulEffect(replacement.actions[i])) continue;

            // Verify the Action actually produces effects from the deploy slot
            // (coords may not reach any living enemy from this position).
            var effects = BuildEffects(replacement.actions[i], team, deploySlot);
            if (effects.Count > 0) return true;
        }
        return false;
    }

    bool AnyEnemyAlive(int team)
    {
        int enemyTeam = team == CombatScript.TeamPlayer ? CombatScript.TeamEnemy : CombatScript.TeamPlayer;
        for (int s = 0; s < 4; s++)
        {
            UnitData u = combat.GetUnit(enemyTeam, s);
            if (!string.IsNullOrEmpty(u.name) && u.hp > 0) return true;
        }
        return false;
    }

    static bool HasPotentialToAct(UnitData u)
    {
        if (u.actions == null || u.actions.Count == 0) return false;
        if (u.stunRemainingRounds > 0 || u.sleepRemainingRounds > 0 || u.petrificationRemainingRounds > 0)
            return false;
        for (int i = 0; i < u.actions.Count; i++)
        {
            var a = u.actions[i];
            // Check use limit only (AP regenerates next turn).
            if (a.useLimit > 0 && u.ActionUseCounts != null && i < u.ActionUseCounts.Count)
            {
                if (u.ActionUseCounts[i] >= a.useLimit) continue;
            }
            if (HasAnyUsefulEffect(a)) return true;
        }
        return false;
    }

    static float RemainingUsesFraction(UnitData u)
    {
        if (u.actions == null || u.actions.Count == 0) return 1f;
        float sum = 0f; int n = 0;
        for (int i = 0; i < u.actions.Count; i++)
        {
            var a = u.actions[i];
            if (a.useLimit <= 0) { sum += 1f; n++; continue; }
            if (u.ActionUseCounts != null && i < u.ActionUseCounts.Count)
            {
                int used = u.ActionUseCounts[i];
                sum += Mathf.Clamp01((a.useLimit - used) / (float)a.useLimit);
                n++;
            }
        }
        return n == 0 ? 1f : sum / n;
    }

    int ActionAPCost(int team, ref AiAction action)
    {
        var gs = weights.ai;
        switch (action.kind)
        {
            case AiActionKind.UseAction:
                return action.action.APCost;
            case AiActionKind.MoveAndUseAction:
                return gs.teamPositionSwapAPCost + action.action.APCost;
            case AiActionKind.Move:
                return gs.teamPositionSwapAPCost;
            case AiActionKind.BenchSwap:
                return gs.benchSwapAPCost;
            case AiActionKind.MoveAndBenchSwap:
                return gs.teamPositionSwapAPCost + gs.benchSwapAPCost;
            default:
                return 0;
        }
    }

    float EstimateFutureValue(int team, ref AiAction action, AiSettings gs)
    {
        if (gs.aiLookaheadDepth <= 0) return 0f;

        var killedEnemies = new HashSet<int>();
        int enemyTeam = team == CombatScript.TeamPlayer ? CombatScript.TeamEnemy : CombatScript.TeamPlayer;

        // Estimate how much this action reduces future threat.
        float threatReduction = 0f;
        if (action.effects != null)
        {
            for (int i = 0; i < action.effects.Count; i++)
            {
                var e = action.effects[i];
                if (e.team == team) continue;
                if (AiAction.IsDamage(e.kind) || AiAction.IsDebuff(e.kind))
                {
                    float targetThreat = CachedThreat(e.team, e.slot);
                    if (e.wouldKill)
                    {
                        threatReduction += targetThreat;
                        if (e.team == enemyTeam)
                            killedEnemies.Add(e.slot);
                    }
                    else
                        threatReduction += Mathf.Min(targetThreat, e.expectedAmount * 0.5f);
                }
            }
        }
        float attackGain = 0f;
        if (action.kind == AiActionKind.Move || action.kind == AiActionKind.MoveAndUseAction)
        {
            UnitData caster = combat.GetUnit(team, action.casterSlot);
            if (!string.IsNullOrEmpty(caster.name) && caster.hp > 0 && HasPotentialToAct(caster))
            {
                float futureNow  = ScoreSlotAttackValue(team, action.casterSlot, action.casterSlot, requireUsable: false);
                float futureNext = ScoreSlotAttackValue(team, action.casterSlot, action.moveDestination, requireUsable: false);
                attackGain = Mathf.Max(0f, futureNext - futureNow);
            }
        }
        // For bench swaps, the replacement's future attack potential.
        else if (action.kind == AiActionKind.BenchSwap || action.kind == AiActionKind.MoveAndBenchSwap)
        {
            if (runtime != null)
            {
                var benchList = runtime.GetBench(team);
                if (action.benchIndex >= 0 && action.benchIndex < benchList.Count)
                {
                    UnitData replacement = benchList[action.benchIndex];
                    if (!string.IsNullOrEmpty(replacement.name) && replacement.hp > 0 && HasPotentialToAct(replacement))
                    {
                        int deploySlot = action.kind == AiActionKind.MoveAndBenchSwap
                            ? action.moveDestination
                            : action.casterSlot;
                        attackGain = ScoreSlotAttackValueForUnit(team, deploySlot, replacement, requireUsable: false);
                    }
                }
            }
        }

        float oppReplyDamage = 0f;
        int livingEnemyCount = 0;
        for (int s = 0; s < 4; s++)
        {
            UnitData eu = combat.GetUnit(enemyTeam, s);
            if (!string.IsNullOrEmpty(eu.name) && eu.hp > 0) livingEnemyCount++;
        }
        for (int s = 0; s < 4; s++)
        {
            UnitData u = combat.GetUnit(team, s);
            if (string.IsNullOrEmpty(u.name) || u.hp <= 0) continue;
            oppReplyDamage += CachedThreat(team, s);
        }
        if (killedEnemies.Count > 0 && livingEnemyCount > 0)
        {
            float killedShare = (float)killedEnemies.Count / livingEnemyCount;
            oppReplyDamage *= (1f - killedShare);
        }

        return gs.futureWeight * (threatReduction + attackGain * 0.5f - oppReplyDamage * 0.3f) * (gs.aiLookaheadDepth / 3f);
    }

    #endregion

    #region Selection

    public static int SelectIndex(List<AiAction> actions, float temperature, System.Random rng)
    {
        if (actions == null || actions.Count == 0) return -1;
        if (actions.Count == 1) return 0;

        if (rng == null) rng = new System.Random(0);

        bool greedy = !(temperature > 0f) && !float.IsInfinity(temperature);
        // Infinite temperature = uniform random = softmax with all weights equal.
        bool uniform = float.IsInfinity(temperature);

        int n = actions.Count;
        float[] scores = new float[n];
        for (int i = 0; i < n; i++)
        {
            float s = actions[i].Score;
            scores[i] = float.IsNaN(s) || float.IsInfinity(s) ? float.NegativeInfinity : s;
        }

        if (uniform)
        {
            return rng.Next(n);
        }

        if (greedy)
        {
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
                if (scores[i] > bestScore) bestScore = scores[i];

            var tied = new List<int>();
            for (int i = 0; i < n; i++)
                if (scores[i] == bestScore) tied.Add(i);

            if (tied.Count == 0) return 0;
            if (tied.Count == 1) return tied[0];

            // Use CompareActionTie to prefer non-idle/Action actions, but
            // among exact ties (same kind/slot/Action), pick uniformly.
            int bestIdx = tied[0];
            for (int i = 1; i < tied.Count; i++)
            {
                int cmp = CompareActionTie(actions[bestIdx], actions[tied[i]]);
                if (cmp > 0) bestIdx = tied[i];
                else if (cmp == 0)
                {
                    // Among identical-category ties, pick uniformly from the
                    // remaining candidates including this one.
                    int remaining = tied.Count - i + 1;
                    if (rng.Next(remaining) == 0) bestIdx = tied[i];
                }
            }
            return bestIdx;
        }

        float maxS = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
            if (scores[i] > maxS) maxS = scores[i];
        if (float.IsNegativeInfinity(maxS))
        {
            return GreedyFallbackIndex(actions);
        }

        double[] w = new double[n];
        double sum = 0.0;
        for (int i = 0; i < n; i++)
        {
            double delta = (scores[i] - maxS) / (double)temperature;
            if (delta < -700.0) delta = -700.0;
            if (delta >  700.0) delta =  700.0;
            w[i] = System.Math.Exp(delta);
            sum += w[i];
        }
        if (!(sum > 0.0))
        {
            return GreedyFallbackIndex(actions);
        }
        double r2 = rng.NextDouble() * sum;
        double acc = 0.0;
        for (int i = 0; i < n; i++)
        {
            acc += w[i];
            if (r2 <= acc) return i;
        }
        return n - 1;
    }

    static int GreedyFallbackIndex(List<AiAction> actions)
    {
        int n = actions.Count;
        for (int i = 0; i < n; i++)
            if (actions[i].kind != AiActionKind.Idle) return i;
        return 0;
    }

    static int CompareActionTie(AiAction a, AiAction b)
    {
        bool aIdle = a.kind == AiActionKind.Idle;
        bool bIdle = b.kind == AiActionKind.Idle;
        if (aIdle != bIdle) return aIdle ? 1 : -1;

        bool aAction = a.kind == AiActionKind.UseAction || a.kind == AiActionKind.MoveAndUseAction;
        bool bAction = b.kind == AiActionKind.UseAction || b.kind == AiActionKind.MoveAndUseAction;
        if (aAction != bAction) return aAction ? -1 : 1;

        if (a.casterSlot != b.casterSlot) return a.casterSlot.CompareTo(b.casterSlot);
        if (a.ActionIndex != b.ActionIndex) return a.ActionIndex.CompareTo(b.ActionIndex);
        return 0;
    }

    #endregion
}
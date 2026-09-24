using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(50)]
public class FightClub : MonoBehaviour
{
    #region Config

    public CombatRuntimeManager runtime;
    public CombatScript combat;
    public combatSetup combatSetup;
    public sheetParser sheetParser;
    public CombatAi combatAi;

    [Header("Tournament")]
    public bool autoStart = false;
    public int maxRoundsPerMatch = 50;

    [Tooltip("All WeightsTemplate assets to compete. Assign your 15 preset .asset files here (or any number).")]
    public List<WeightsTemplate> competitorWeights = new List<WeightsTemplate>();

    [Tooltip("If assigned, enabling Include Random switches the tournament into Improve Mode: a whole gene pool of mutated copies of this template competes, breeds, and evolves over Cycles generations. If left empty, Include Random just adds fully-random one-off opponents like before.")]
    public WeightsTemplate randomTemplateBase;
    [Tooltip("Turns on random / evolving opponents. Combined with Random Template Base, this activates Improve Mode.")]
    public bool includeRandom = false;
    [Tooltip("If Random Template Base is empty: how many fully-random opponents to add. If Random Template Base is assigned: the gene pool size (population) per generation in Improve Mode.")]
    public int randomEntries = 1;

    [Header("Improve Mode (Evolutionary Training)")]
    [Tooltip("Only active when Improve Mode is on (Include Random + Random Template Base). If true, each generation only fights within the gene pool (faster, purely evolutionary). If false, the gene pool also fights your fixed competitorWeights presets each generation.")]
    public bool onlyFightGenePool = false;
    [Tooltip("Number of generations to run in Improve Mode: fight -> pick best -> breed + mutate -> fight again.")]
    public int cycles = 5;
    [Tooltip("How many of the top gene-pool performers breed together to produce the next generation. 2 or 3 works well.")]
    [Range(1, 4)] public int breedersCount = 2;
    [Tooltip("How much randomness/mutation is injected each generation, like a learning rate. 0 = children are exact clones of their parents, 1 = wide random jumps.")]
    [Range(0f, 1f)] public float mutationStrength = 0.15f;

    [Tooltip("When on, the previous generation's breeders (the parents) are added to the next generation's roster so children fight their parents directly.")]
    public bool parentsCompeteWithChildren = true;
    [Tooltip("Cap how many parents are carried into the next generation's roster. 0 = no cap.")]
    [Range(0, 8)] public int maxParentsInRoster = 4;

    [Header("Available Units (auto-scanned)")]
    [HideInInspector] public string[] availableUnitCodes = new string[0];

    [Header("Action Counter (balancing, inside Standings overlay)")]
    [Tooltip("Appends a per-unit, per-Action usage section to the bottom of the FightClub standings overlay - for balancing. Toggle with the 'Toggle Action Counter Overlay' context menu or this field.")]
    public bool showActionCounter = true;
    [Tooltip("Reset the aggregate counters at the start of every tournament so each run gives clean per-tournament stats.")]
    public bool resetActionCounterOnStart = true;

    [Header("Standings Overlay - Layout")]
    [Tooltip("Width/height of the combined FightClub overlay (standings + action counter). The panel scrolls, so a taller value shows more at once.")]
    public float overlayWidth = 460f;
    public float overlayHeight = 820f;
    [Tooltip("Margin from the screen edges.")]
    public float overlayMargin = 10f;

    #endregion

    #region State

    public enum TournamentState { Idle, Running, Finished }
    public TournamentState state = TournamentState.Idle;

    [System.Serializable]
    public struct StandingEntry
    {
        public string name;
        public WeightsTemplate weights;
        public bool isRandom;
        public int wins;
        public int losses;
        public int draws;
        public int roundsToDecision;
        public float remainingWinnerHpFraction;
        public int remainingWinnerUnits;
    }

    [System.NonSerialized] public List<StandingEntry> standings = new List<StandingEntry>();
    public int currentMatch = 0;
    public int totalMatches = 0;
    public string currentMatchup = "";
    public string lastResult = "";
    public string champion = "";

    [Header("Diagnostics")]
    [Tooltip("The most recent match seed, logged for reproducibility. True replay requires deterministic RNG everywhere (TODO).")]
    [HideInInspector] public int lastMatchSeed = 0;

    [System.NonSerialized] private readonly List<WeightsTemplate> _generatedWeights = new List<WeightsTemplate>();

    [Header("Improve Mode - Live State (read only)")]
    public bool improveMode = false;
    public int currentCycle = 0;
    public int totalCycles = 0;
    [Tooltip("The gene-pool entries that were selected to breed at the end of the most recent completed generation. Used by 'Export Top Breeders'.")]
    [System.NonSerialized] [HideInInspector] public List<StandingEntry> lastBreeders = new List<StandingEntry>();
    private Coroutine tournamentRoutine;

    // ---- Action counter  ----
    private readonly Dictionary<string, Dictionary<string, int>> _actionUsage = new Dictionary<string, Dictionary<string, int>>();
    private int _actionMatchesCounted = 0;
    private long _actionTotalUses = 0;

    private Vector2 _standingsScrollPos;

    #endregion

    #region Unity

    void Awake()
    {
        if (runtime == null) runtime = FindFirstObjectByType<CombatRuntimeManager>();
        if (combat == null) combat = FindFirstObjectByType<CombatScript>();
        if (combatSetup == null) combatSetup = FindFirstObjectByType<combatSetup>();
        if (sheetParser == null) sheetParser = FindFirstObjectByType<sheetParser>();
        if (combatAi == null) combatAi = FindFirstObjectByType<CombatAi>();
    }

    void OnValidate()
    {
        if (competitorWeights != null)
            competitorWeights.RemoveAll(w => w == null);
    }

    void Start()
    {
        bool fightClubActive = autoStart;
        if (fightClubActive && runtime != null)
            runtime.autoStartOnStart = false;

        ScanAvailableUnits();
        if (autoStart) StartTournament();
    }

    void OnDestroy()
    {
        if (Application.isPlaying)
        {
            CleanupGeneratedWeights();
            ResetWeightOverrides();
        }
    }

    #endregion

    #region Tournament hygiene

    void PreFlightChecks()
    {
        if (CombatDebugHandler.GodModePlayer)
        {
            Debug.LogWarning("[FightClub] GodModePlayer was ON at tournament start - forcing it OFF so the results reflect real combat.");
            CombatDebugHandler.GodModePlayer = false;
        }
        CombatDebugHandler.SetDebugMode(false);
    }

    void CleanupGeneratedWeights()
    {
        if (_generatedWeights == null) return;
        if (standings != null) standings.Clear();
        if (lastBreeders != null) lastBreeders.Clear();
        for (int i = 0; i < _generatedWeights.Count; i++)
        {
            var w = _generatedWeights[i];
            if (w != null)
            {
                try { UnityEngine.Object.Destroy(w); }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[FightClub] Failed to destroy generated weights '{w.name}': {e.Message}");
                }
            }
        }
        _generatedWeights.Clear();
    }

    void ResetWeightOverrides()
    {
        if (runtime == null) return;
        runtime.playerWeightsOverride = null;
        runtime.enemyWeightsOverride = null;
    }

    float TeamHpFraction(int team)
    {
        if (combat == null) return 0f;
        float max = 0f, current = 0f;
        for (int s = 0; s < 4; s++)
        {
            var u = combat.GetUnit(team, s);
            if (string.IsNullOrEmpty(u.name)) continue;
            max += Mathf.Max(0f, u.maxHp);
            current += Mathf.Max(0f, u.hp);
        }
        return max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    int LivingUnitsOnTeam(int team)
    {
        if (combat == null) return 0;
        int n = 0;
        for (int s = 0; s < 4; s++)
        {
            var u = combat.GetUnit(team, s);
            if (!string.IsNullOrEmpty(u.name) && u.hp > 0) n++;
        }
        return n;
    }

    static int CompareStandings(StandingEntry x, StandingEntry y)
    {
        int ws = y.wins.CompareTo(x.wins);
        if (ws != 0) return ws;
        int ls = x.losses.CompareTo(y.losses);
        if (ls != 0) return ls;
        int rt = x.roundsToDecision.CompareTo(y.roundsToDecision);
        if (rt != 0) return rt;
        return y.remainingWinnerHpFraction.CompareTo(x.remainingWinnerHpFraction);
    }

    #endregion

    #region Unit scanning

    void ScanAvailableUnits()
    {
        var codes = new List<string>();
        if (sheetParser == null || sheetParser.grid == null || sheetParser.grid.Count < 1) return;

        var sheet1 = sheetParser.grid[0];
        if (sheet1 == null) return;

        const string TARGET_ARMY = "FIGHTCLUB";
        bool inTargetArmy = false;

        for (int r = 0; r < sheet1.Count; r++)
        {
            var row = sheet1[r];
            string col0 = row.Count > 0 ? row[0].Trim() : "";
            string col1 = row.Count > 1 ? row[1].Trim() : "";
            string col2 = row.Count > 2 ? row[2].Trim() : "";

            if (!string.IsNullOrEmpty(col0))
                inTargetArmy = col0 == TARGET_ARMY;

            if (!inTargetArmy) continue;
            if (string.IsNullOrEmpty(col1) || string.IsNullOrEmpty(col2)) continue;
            if (col1 == "Value Attached to Unit") continue;
            if (col2 == "Base Stat" || col2 == "HP" || col2 == "AC" || col2 == "AR" || col2 == "SH" || col2 == "TH" || col2 == "AP" || col2 == "EN" || col2 == "Actions")
                continue;

            string code = TARGET_ARMY + "_" + col1;
            if (!codes.Contains(code)) codes.Add(code);
        }
        availableUnitCodes = codes.ToArray();
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Scanned {availableUnitCodes.Length} FIGHTCLUB unit codes from sheet.");
    }

    [ContextMenu("Rescan Units")]
    public void RescanUnits() => ScanAvailableUnits();

    #endregion

    #region Team generation

    void GenerateRandomTeam()
    {
        if (availableUnitCodes == null || availableUnitCodes.Length == 0)
        {
            ScanAvailableUnits();
            if (availableUnitCodes.Length == 0)
            {
                Debug.LogError("[FightClub] No unit codes available. Assign sheetParser or check CSV.");
                return;
            }
        }

        int teamSize = Mathf.Min(4, availableUnitCodes.Length);
        var picks = new List<string>();
        var pool = new List<string>(availableUnitCodes);

        for (int i = 0; i < teamSize && pool.Count > 0; i++)
        {
            int idx = Random.Range(0, pool.Count);
            picks.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        var benchPicks = new List<string>();
        for (int i = 0; i < 2 && pool.Count > 0; i++)
        {
            int idx = Random.Range(0, pool.Count);
            benchPicks.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        combat.SetUnit(CombatScript.TeamPlayer, 0, new CombatScript.UnitData { unitcode = picks.Count > 0 ? picks[0] : "" });
        combat.SetUnit(CombatScript.TeamPlayer, 1, new CombatScript.UnitData { unitcode = picks.Count > 1 ? picks[1] : "" });
        combat.SetUnit(CombatScript.TeamPlayer, 2, new CombatScript.UnitData { unitcode = picks.Count > 2 ? picks[2] : "" });
        combat.SetUnit(CombatScript.TeamPlayer, 3, new CombatScript.UnitData { unitcode = picks.Count > 3 ? picks[3] : "" });

        combat.SetUnit(CombatScript.TeamEnemy, 0, new CombatScript.UnitData { unitcode = picks.Count > 0 ? picks[0] : "" });
        combat.SetUnit(CombatScript.TeamEnemy, 1, new CombatScript.UnitData { unitcode = picks.Count > 1 ? picks[1] : "" });
        combat.SetUnit(CombatScript.TeamEnemy, 2, new CombatScript.UnitData { unitcode = picks.Count > 2 ? picks[2] : "" });
        combat.SetUnit(CombatScript.TeamEnemy, 3, new CombatScript.UnitData { unitcode = picks.Count > 3 ? picks[3] : "" });

        runtime.ClearAllBenches();

        var playerBench = runtime.PlayerBench;
        var enemyBench = runtime.EnemyBench;
        playerBench.Clear();
        enemyBench.Clear();

        foreach (var code in benchPicks)
        {
            var bu = default(CombatScript.UnitData);
            bu.unitcode = code;
            playerBench.Add(bu);

            var eu = default(CombatScript.UnitData);
            eu.unitcode = code;
            enemyBench.Add(eu);
        }

        if (combatSetup != null) combatSetup.LoadAllUnits();
        runtime.FillBenchFromSheet(combatSetup);
        combat.ClearAllDOTsForAllUnits();

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Generated team: {string.Join(", ", picks)}  Bench: {string.Join(", ", benchPicks)}");
    }

    #endregion

    #region Random weights / mutation / breeding

    delegate float FloatStrategy(float a, float b, float min, float max);
    delegate int   IntStrategy  (int a,   int b,   int min, int max);
    delegate bool  BoolStrategy (bool a,  bool b);

    static void PopulateFloatsAndInts(WeightsTemplate a, WeightsTemplate b, WeightsTemplate dst,
        FloatStrategy ff, IntStrategy fi)
    {
        // enemies scoring floats
        dst.enemies.scoring.damageWeight           = ff(a.enemies.scoring.damageWeight,           b.enemies.scoring.damageWeight,           0.5f, 3f);
        dst.enemies.scoring.killWeight             = ff(a.enemies.scoring.killWeight,             b.enemies.scoring.killWeight,             10f,  60f);
        dst.enemies.scoring.debuffWeight           = ff(a.enemies.scoring.debuffWeight,           b.enemies.scoring.debuffWeight,           0f,   15f);
        dst.enemies.scoring.threatTargetingWeight  = ff(a.enemies.scoring.threatTargetingWeight,  b.enemies.scoring.threatTargetingWeight,  2f,   15f);
        dst.enemies.scoring.debuffHighThreatBonus  = ff(a.enemies.scoring.debuffHighThreatBonus,  b.enemies.scoring.debuffHighThreatBonus,  1f,   2.5f);
        // allies scoring floats
        dst.allies.scoring.healWeight                   = ff(a.allies.scoring.healWeight,                   b.allies.scoring.healWeight,                   0f,   3f);
        dst.allies.scoring.buffEffectValueWeight        = ff(a.allies.scoring.buffEffectValueWeight,        b.allies.scoring.buffEffectValueWeight,        0f,   2f);
        dst.allies.scoring.survivalWeight               = ff(a.allies.scoring.survivalWeight,               b.allies.scoring.survivalWeight,               10f,  50f);
        dst.allies.scoring.healPreventsDeathBonus       = ff(a.allies.scoring.healPreventsDeathBonus,       b.allies.scoring.healPreventsDeathBonus,       1f,   2.5f);
        dst.allies.scoring.buffTargetStrengthWeight     = ff(a.allies.scoring.buffTargetStrengthWeight,     b.allies.scoring.buffTargetStrengthWeight,     0f,   2f);
        dst.allies.scoring.buffLikelyToAttackWeight     = ff(a.allies.scoring.buffLikelyToAttackWeight,     b.allies.scoring.buffLikelyToAttackWeight,     0f,   2f);
        dst.allies.scoring.buffLowHealthWeight          = ff(a.allies.scoring.buffLowHealthWeight,          b.allies.scoring.buffLowHealthWeight,          0f,   1.5f);
        dst.allies.scoring.overhealPenaltyWeight        = ff(a.allies.scoring.overhealPenaltyWeight,        b.allies.scoring.overhealPenaltyWeight,        0.1f, 0.5f);
        // ai floats
        dst.ai.aiDecisionRandomness       = ff(a.ai.aiDecisionRandomness,       b.ai.aiDecisionRandomness,       0f,   0.8f);
        dst.ai.actionFatigueWeight       = ff(a.ai.actionFatigueWeight,       b.ai.actionFatigueWeight,       1f,   8f);
        dst.ai.unitFatigueWeight          = ff(a.ai.unitFatigueWeight,          b.ai.unitFatigueWeight,          0f,   5f);
        dst.ai.fatigueRecoveryPerTurn     = ff(a.ai.fatigueRecoveryPerTurn,     b.ai.fatigueRecoveryPerTurn,     0.1f, 1.5f);
        dst.ai.positionWeight             = ff(a.ai.positionWeight,             b.ai.positionWeight,             1f,   6f);
        dst.ai.positionSafetyWeight       = ff(a.ai.positionSafetyWeight,       b.ai.positionSafetyWeight,       0f,   4f);
        dst.ai.APCostWeight           = ff(a.ai.APCostWeight,           b.ai.APCostWeight,           0.5f, 2f);
        dst.ai.futureWeight               = ff(a.ai.futureWeight,               b.ai.futureWeight,               0.1f, 1f);
        dst.ai.idlePenalty                = ff(a.ai.idlePenalty,                b.ai.idlePenalty,                5f,   20f);
        dst.ai.swapLowHpWeight            = ff(a.ai.swapLowHpWeight,            b.ai.swapLowHpWeight,            0f,   20f);
        dst.ai.swapDeadUnitWeight         = ff(a.ai.swapDeadUnitWeight,         b.ai.swapDeadUnitWeight,         0f,   30f);
        dst.ai.swapLowRemainingUsesWeight = ff(a.ai.swapLowRemainingUsesWeight, b.ai.swapLowRemainingUsesWeight, 0f,   10f);
        dst.ai.swapNoUsefulAttacksWeight  = ff(a.ai.swapNoUsefulAttacksWeight,  b.ai.swapNoUsefulAttacksWeight,  0f,   12f);
        dst.ai.swapReplacementValueWeight = ff(a.ai.swapReplacementValueWeight, b.ai.swapReplacementValueWeight, 0f,   8f);
        dst.ai.swapAPCostWeight       = ff(a.ai.swapAPCostWeight,       b.ai.swapAPCostWeight,       0f,   5f);
        dst.ai.swapFutureAttackDiscount   = ff(a.ai.swapFutureAttackDiscount,   b.ai.swapFutureAttackDiscount,   0f,   1f);
        dst.ai.repeatSwapPenaltyWeight    = ff(a.ai.repeatSwapPenaltyWeight,    b.ai.repeatSwapPenaltyWeight,    0f,   12f);
        dst.ai.repeatSwapPenaltyGrowth    = ff(a.ai.repeatSwapPenaltyGrowth,    b.ai.repeatSwapPenaltyGrowth,    1f,   3f);
        // ai ints
        dst.ai.aiLookaheadDepth           = fi(a.ai.aiLookaheadDepth,           b.ai.aiLookaheadDepth,           0, 2);
        dst.ai.benchSwapAPCost        = fi(a.ai.benchSwapAPCost,        b.ai.benchSwapAPCost,        2, 8);
        dst.ai.teamPositionSwapAPCost = fi(a.ai.teamPositionSwapAPCost, b.ai.teamPositionSwapAPCost, 1, 5);
    }

    static void PopulateBools(WeightsTemplate a, WeightsTemplate b, WeightsTemplate dst, BoolStrategy bs)
    {
        dst.enemies.scoring.enableFocusFire        = bs(a.enemies.scoring.enableFocusFire,        b.enemies.scoring.enableFocusFire);
        dst.enemies.scoring.enableKillBonus        = bs(a.enemies.scoring.enableKillBonus,        b.enemies.scoring.enableKillBonus);
        dst.enemies.scoring.enableThreatTargeting  = bs(a.enemies.scoring.enableThreatTargeting,  b.enemies.scoring.enableThreatTargeting);
        dst.enemies.scoring.enableDamageImportance = bs(a.enemies.scoring.enableDamageImportance, b.enemies.scoring.enableDamageImportance);
        dst.enemies.scoring.enableDebuffHighThreat = bs(a.enemies.scoring.enableDebuffHighThreat, b.enemies.scoring.enableDebuffHighThreat);

        dst.allies.scoring.enableSurvival               = bs(a.allies.scoring.enableSurvival,               b.allies.scoring.enableSurvival);
        dst.allies.scoring.enableHealPreventsDeathBonus = bs(a.allies.scoring.enableHealPreventsDeathBonus, b.allies.scoring.enableHealPreventsDeathBonus);
        dst.allies.scoring.enableBuffLowHealthBonus     = bs(a.allies.scoring.enableBuffLowHealthBonus,     b.allies.scoring.enableBuffLowHealthBonus);
        dst.allies.scoring.enableOverhealPenalty        = bs(a.allies.scoring.enableOverhealPenalty,        b.allies.scoring.enableOverhealPenalty);

        dst.ai.preferVariedActions         = bs(a.ai.preferVariedActions,         b.ai.preferVariedActions);
        dst.ai.enableMoveActions           = bs(a.ai.enableMoveActions,           b.ai.enableMoveActions);
        dst.ai.enableMoveAndAction        = bs(a.ai.enableMoveAndAction,        b.ai.enableMoveAndAction);
        dst.ai.enableBenchSwaps            = bs(a.ai.enableBenchSwaps,            b.ai.enableBenchSwaps);
        dst.ai.enableResourceScoring       = bs(a.ai.enableResourceScoring,       b.ai.enableResourceScoring);
        dst.ai.enableAPCostPenalty     = bs(a.ai.enableAPCostPenalty,     b.ai.enableAPCostPenalty);
        dst.ai.enableAPOverflowPenalty = bs(a.ai.enableAPOverflowPenalty, b.ai.enableAPOverflowPenalty);
        dst.ai.enableRepeatSwapPenalty     = bs(a.ai.enableRepeatSwapPenalty,     b.ai.enableRepeatSwapPenalty);
    }

    WeightsTemplate Register(WeightsTemplate w)
    {
        if (w != null) _generatedWeights.Add(w);
        return w;
    }

    WeightsTemplate CreateRandomWeights()
    {
        var w = ScriptableObject.CreateInstance<WeightsTemplate>();

        RandomizeTeamWeights(w.enemies);
        RandomizeTeamWeights(w.allies);

        PopulateFloatsAndInts(w, w, w,
            (a, b, min, max) => Random.Range(min, max),
            (a, b, min, max) => Random.Range(min, max));

        // Random bools use per-field thresholds, so set them inline.
        w.enemies.scoring.enableFocusFire        = Random.value > 0.4f;
        w.enemies.scoring.enableKillBonus        = Random.value > 0.3f;
        w.enemies.scoring.enableThreatTargeting  = Random.value > 0.4f;
        w.enemies.scoring.enableDamageImportance = Random.value > 0.4f;
        w.enemies.scoring.enableDebuffHighThreat = Random.value > 0.3f;

        w.allies.scoring.enableSurvival               = Random.value > 0.3f;
        w.allies.scoring.enableHealPreventsDeathBonus = Random.value > 0.3f;
        w.allies.scoring.enableBuffLowHealthBonus     = Random.value > 0.3f;
        w.allies.scoring.enableOverhealPenalty        = Random.value > 0.5f;

        w.ai.preferVariedActions         = Random.value > 0.4f;
        w.ai.enableMoveActions           = Random.value > 0.4f;
        w.ai.enableMoveAndAction        = Random.value > 0.4f;
        w.ai.enableBenchSwaps            = Random.value > 0.5f;
        w.ai.enableResourceScoring       = Random.value > 0.3f;
        w.ai.enableAPCostPenalty     = Random.value > 0.3f;
        w.ai.enableAPOverflowPenalty = Random.value > 0.3f;
        w.ai.enableRepeatSwapPenalty     = Random.value > 0.3f;
        w.ai.enableIdlePenalty           = true;

        return w;
    }


    static float MutateFloat(float value, float min, float max, float strength)
    {
        float range = max - min;
        float mutated = value + Random.Range(-strength, strength) * range;
        return Mathf.Clamp(mutated, min, max);
    }

    static int MutateInt(int value, int min, int max, float strength)
    {
        if (Random.value < strength)
            value += Random.value < 0.5f ? -1 : 1;
        return Mathf.Clamp(value, min, max);
    }

    static bool MutateBool(bool value, float flipChance)
    {
        return Random.value < flipChance ? !value : value;
    }

    static float PickFloat(float a, float b) => Random.value < 0.5f ? a : b;
    static bool PickBool(bool a, bool b) => Random.value < 0.5f ? a : b;
    static int PickInt(int a, int b) => Random.value < 0.5f ? a : b;

    static void MutateTeamWeights(TeamWeightsBase src, TeamWeightsBase dst, float strength)
    {
        dst.hpWeight              = MutateFloat(src.hpWeight,              0f, 1f, strength);
        dst.armorWeight           = MutateFloat(src.armorWeight,           0f, 1f, strength);
        dst.shieldWeight          = MutateFloat(src.shieldWeight,          0f, 1f, strength);
        dst.totalHpAgressionWeight= MutateFloat(src.totalHpAgressionWeight,0f, 5f, strength);
    }

    static void BreedTeamWeights(TeamWeightsBase a, TeamWeightsBase b, TeamWeightsBase dst, float strength)
    {
        dst.hpWeight              = MutateFloat(PickFloat(a.hpWeight,               b.hpWeight),                0f, 1f, strength);
        dst.armorWeight           = MutateFloat(PickFloat(a.armorWeight,            b.armorWeight),             0f, 1f, strength);
        dst.shieldWeight          = MutateFloat(PickFloat(a.shieldWeight,           b.shieldWeight),            0f, 1f, strength);
        dst.totalHpAgressionWeight= MutateFloat(PickFloat(a.totalHpAgressionWeight, b.totalHpAgressionWeight), 0f, 5f, strength);
    }

    static AnimationCurve PickCurve(AnimationCurve a, AnimationCurve b)
    {
        AnimationCurve src = Random.value < 0.5f ? a : b;
        if (src == null) return new AnimationCurve();
        return new AnimationCurve(src.keys);
    }

    static AnimationCurve CloneCurve(AnimationCurve src)
    {
        if (src == null) return new AnimationCurve();
        return new AnimationCurve(src.keys);
    }

    static void RandomizeTeamWeights(TeamWeightsBase w)
    {
        w.hpWeight               = Random.Range(0f,  1f);
        w.armorWeight            = Random.Range(0f,  1f);
        w.shieldWeight           = Random.Range(0f,  1f);
        w.totalHpAgressionWeight = Random.Range(0f,  5f);
    }

    static void CloneAllCurves(WeightsTemplate src, WeightsTemplate dst)
    {
        if (src == null || dst == null) return;
        dst.allies.hpCurve          = CloneCurve(src.allies.hpCurve);
        dst.allies.armorCurve       = CloneCurve(src.allies.armorCurve);
        dst.allies.shieldCurve      = CloneCurve(src.allies.shieldCurve);
        dst.allies.agressionHpCurve = CloneCurve(src.allies.agressionHpCurve);
        dst.allies.scoring.targetImportanceCurve   = CloneCurve(src.allies.scoring.targetImportanceCurve);
        dst.allies.scoring.healMissingCurve        = CloneCurve(src.allies.scoring.healMissingCurve);
        dst.allies.scoring.buffTargetStrengthCurve = CloneCurve(src.allies.scoring.buffTargetStrengthCurve);
        dst.enemies.hpCurve          = CloneCurve(src.enemies.hpCurve);
        dst.enemies.armorCurve       = CloneCurve(src.enemies.armorCurve);
        dst.enemies.shieldCurve      = CloneCurve(src.enemies.shieldCurve);
        dst.enemies.agressionHpCurve = CloneCurve(src.enemies.agressionHpCurve);
        dst.enemies.scoring.targetImportanceCurve    = CloneCurve(src.enemies.scoring.targetImportanceCurve);
        dst.enemies.scoring.damageFocusFireCurve     = CloneCurve(src.enemies.scoring.damageFocusFireCurve);
        dst.enemies.scoring.killHpFractionCurve       = CloneCurve(src.enemies.scoring.killHpFractionCurve);
        dst.enemies.scoring.threatTargetingCurve     = CloneCurve(src.enemies.scoring.threatTargetingCurve);
        dst.enemies.scoring.debuffTargetCurve        = CloneCurve(src.enemies.scoring.debuffTargetCurve);
        dst.ai.slotDangerCurve        = CloneCurve(src.ai.slotDangerCurve);
        dst.ai.lowHealthCurve         = CloneCurve(src.ai.lowHealthCurve);
        dst.ai.resourceCurve          = CloneCurve(src.ai.resourceCurve);
        dst.ai.APScarcityCurve    = CloneCurve(src.ai.APScarcityCurve);
        dst.ai.swapReplacementCurve   = CloneCurve(src.ai.swapReplacementCurve);
    }

    static void BreedAllCurves(WeightsTemplate a, WeightsTemplate b, WeightsTemplate dst)
    {
        if (a == null || b == null || dst == null) return;
        dst.allies.hpCurve          = PickCurve(a.allies.hpCurve,           b.allies.hpCurve);
        dst.allies.armorCurve       = PickCurve(a.allies.armorCurve,        b.allies.armorCurve);
        dst.allies.shieldCurve      = PickCurve(a.allies.shieldCurve,      b.allies.shieldCurve);
        dst.allies.agressionHpCurve = PickCurve(a.allies.agressionHpCurve, b.allies.agressionHpCurve);
        dst.allies.scoring.targetImportanceCurve   = PickCurve(a.allies.scoring.targetImportanceCurve,   b.allies.scoring.targetImportanceCurve);
        dst.allies.scoring.healMissingCurve        = PickCurve(a.allies.scoring.healMissingCurve,        b.allies.scoring.healMissingCurve);
        dst.allies.scoring.buffTargetStrengthCurve = PickCurve(a.allies.scoring.buffTargetStrengthCurve, b.allies.scoring.buffTargetStrengthCurve);
        dst.enemies.hpCurve          = PickCurve(a.enemies.hpCurve,           b.enemies.hpCurve);
        dst.enemies.armorCurve       = PickCurve(a.enemies.armorCurve,        b.enemies.armorCurve);
        dst.enemies.shieldCurve      = PickCurve(a.enemies.shieldCurve,      b.enemies.shieldCurve);
        dst.enemies.agressionHpCurve = PickCurve(a.enemies.agressionHpCurve, b.enemies.agressionHpCurve);
        dst.enemies.scoring.targetImportanceCurve    = PickCurve(a.enemies.scoring.targetImportanceCurve,    b.enemies.scoring.targetImportanceCurve);
        dst.enemies.scoring.damageFocusFireCurve     = PickCurve(a.enemies.scoring.damageFocusFireCurve,     b.enemies.scoring.damageFocusFireCurve);
        dst.enemies.scoring.killHpFractionCurve       = PickCurve(a.enemies.scoring.killHpFractionCurve,       b.enemies.scoring.killHpFractionCurve);
        dst.enemies.scoring.threatTargetingCurve     = PickCurve(a.enemies.scoring.threatTargetingCurve,     b.enemies.scoring.threatTargetingCurve);
        dst.enemies.scoring.debuffTargetCurve        = PickCurve(a.enemies.scoring.debuffTargetCurve,        b.enemies.scoring.debuffTargetCurve);
        dst.ai.slotDangerCurve        = PickCurve(a.ai.slotDangerCurve,        b.ai.slotDangerCurve);
        dst.ai.lowHealthCurve         = PickCurve(a.ai.lowHealthCurve,         b.ai.lowHealthCurve);
        dst.ai.resourceCurve          = PickCurve(a.ai.resourceCurve,          b.ai.resourceCurve);
        dst.ai.APScarcityCurve    = PickCurve(a.ai.APScarcityCurve,    b.ai.APScarcityCurve);
        dst.ai.swapReplacementCurve   = PickCurve(a.ai.swapReplacementCurve,   b.ai.swapReplacementCurve);
    }

    WeightsTemplate CreateMutatedWeights(WeightsTemplate baseTemplate, float strength)
    {
        var w = ScriptableObject.CreateInstance<WeightsTemplate>();

        MutateTeamWeights(baseTemplate.enemies, w.enemies, strength);
        MutateTeamWeights(baseTemplate.allies, w.allies, strength);

        PopulateFloatsAndInts(baseTemplate, baseTemplate, w,
            (a, b, min, max) => MutateFloat(a, min, max, strength),
            (a, b, min, max) => MutateInt(a, min, max, strength));

        PopulateBools(baseTemplate, baseTemplate, w,
            (a, b) => MutateBool(a, strength));

        w.ai.enableIdlePenalty = true;
        CloneAllCurves(baseTemplate, w);
        return w;
    }

    WeightsTemplate BreedWeights(WeightsTemplate parentA, WeightsTemplate parentB, float strength)
    {
        var w = ScriptableObject.CreateInstance<WeightsTemplate>();

        BreedTeamWeights(parentA.enemies, parentB.enemies, w.enemies, strength);
        BreedTeamWeights(parentA.allies, parentB.allies, w.allies, strength);

        PopulateFloatsAndInts(parentA, parentB, w,
            (a, b, min, max) => MutateFloat(PickFloat(a, b), min, max, strength),
            (a, b, min, max) => MutateInt(PickInt(a, b), min, max, strength));

        PopulateBools(parentA, parentB, w,
            (a, b) => MutateBool(PickBool(a, b), strength));

        w.ai.enableIdlePenalty = true;
        BreedAllCurves(parentA, parentB, w);
        return w;
    }

    #endregion

    #region Tournament

    [ContextMenu("Start Tournament")]
    public void StartTournament()
    {
        if (state == TournamentState.Running) return;
        if (runtime == null || combat == null || combatAi == null) return;

        PreFlightChecks();
        CleanupGeneratedWeights();
        ResetWeightOverrides();

        tournamentRoutine = StartCoroutine(RunTournamentRouter());
    }

    [ContextMenu("Stop Tournament")]
    public void StopTournament()
    {
        if (tournamentRoutine != null) StopCoroutine(tournamentRoutine);
        tournamentRoutine = null;
        state = TournamentState.Idle;
        improveMode = false;
        currentCycle = 0;
        if (runtime != null)
        {
            runtime.bothTeamsAi = false;
            runtime.UnloadBattle();
        }
        if (combatAi != null) combatAi.ClearFatigue();

        CleanupGeneratedWeights();
        ResetWeightOverrides();
    }

    IEnumerator RunTournamentRouter()
    {
        improveMode = includeRandom && randomTemplateBase != null;

        if (resetActionCounterOnStart)
            ResetActionCounter();

        if (improveMode)
            yield return RunImproveMode();
        else
            yield return RunSingleTournament();
    }

    List<StandingEntry> BuildFixedCompetitors()
    {
        var competitors = new List<StandingEntry>();
        foreach (var w in competitorWeights)
        {
            if (w == null) continue;
            competitors.Add(new StandingEntry { name = w.name, weights = w, isRandom = false });
        }
        return competitors;
    }

    IEnumerator RunGenerationMatches(List<StandingEntry> competitors)
    {
        standings = competitors;
        currentMatch = 0;
        lastResult = "";
        totalMatches = competitors.Count * (competitors.Count - 1) / 2;

        for (int a = 0; a < competitors.Count; a++)
        {
            for (int b = a + 1; b < competitors.Count; b++)
            {
                currentMatch++;
                currentMatchup = $"{competitors[a].name} vs {competitors[b].name}";
                yield return RunMatch(competitors[a], competitors[b], a, b);
            }
        }

        standings.Sort(CompareStandings);
    }

    IEnumerator RunSingleTournament()
    {
        state = TournamentState.Running;
        champion = "";
        currentCycle = 0;
        totalCycles = 0;

        runtime.bothTeamsAi = true;
        runtime.enemyAiActionDelay = 0f;

        try
        {
            var competitors = BuildFixedCompetitors();

            if (includeRandom)
            {
                for (int i = 0; i < randomEntries; i++)
                {
                    var rw = Register(CreateRandomWeights());
                    competitors.Add(new StandingEntry { name = $"Random #{i + 1}", weights = rw, isRandom = true });
                }
            }

            if (competitors.Count < 2)
            {
                Debug.LogWarning("[FightClub] Need at least 2 competitors. Assign WeightsTemplate assets to competitorWeights.");
                yield break;
            }

            if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Tournament started: {competitors.Count} AIs, {competitors.Count * (competitors.Count - 1) / 2} matches.");

            yield return RunGenerationMatches(competitors);

            champion = standings[0].name;

            if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Tournament finished! Champion: {standings[0].name} ({standings[0].wins}W/{standings[0].losses}L/{standings[0].draws}D)");
            LogStandings();
        }
        finally
        {
            state = TournamentState.Finished;
            currentMatchup = "";
            runtime.bothTeamsAi = false;
            ResetWeightOverrides();
        }
    }

    IEnumerator RunImproveMode()
    {
        state = TournamentState.Running;
        champion = "";
        totalCycles = Mathf.Max(1, cycles);
        currentCycle = 0;

        runtime.bothTeamsAi = true;
        runtime.enemyAiActionDelay = 0f;

        int popSize = Mathf.Max(2, randomEntries);

        var genePool = new List<WeightsTemplate>();
        for (int i = 0; i < popSize; i++)
            genePool.Add(Register(CreateMutatedWeights(randomTemplateBase, mutationStrength)));

        var fixedRoster = onlyFightGenePool ? new List<WeightsTemplate>() : new List<WeightsTemplate>(competitorWeights);

        List<WeightsTemplate> parentWeights = null;

        try
        {
            while (currentCycle < totalCycles)
            {
                currentCycle++;

                var competitors = new List<StandingEntry>();
                foreach (var w in fixedRoster)
                {
                    if (w == null) continue;
                    competitors.Add(new StandingEntry { name = w.name, weights = w, isRandom = false });
                }

                if (parentsCompeteWithChildren && parentWeights != null && parentWeights.Count > 0)
                {
                    int pCount = parentWeights.Count;
                    if (maxParentsInRoster > 0) pCount = Mathf.Min(pCount, maxParentsInRoster);
                    for (int i = 0; i < pCount; i++)
                    {
                        competitors.Add(new StandingEntry
                        {
                            name = $"Parent#{i + 1}",
                            weights = parentWeights[i],
                            isRandom = false
                        });
                    }
                }

                for (int i = 0; i < genePool.Count; i++)
                {
                    competitors.Add(new StandingEntry
                    {
                        name = $"Gen{currentCycle}#{i + 1}",
                        weights = genePool[i],
                        isRandom = true
                    });
                }

                if (competitors.Count < 2)
                {
                    Debug.LogWarning("[FightClub] Improve Mode needs at least 2 competitors this generation. Check randomEntries / competitorWeights / Only Fight Gene Pool.");
                    break;
                }

                int childCount = genePool.Count;
                int parentCount = (parentsCompeteWithChildren && parentWeights != null)
                    ? Mathf.Min(parentWeights.Count, maxParentsInRoster > 0 ? maxParentsInRoster : parentWeights.Count)
                    : 0;
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Improve Mode - cycle {currentCycle}/{totalCycles}: {competitors.Count} AIs ({childCount} children, {parentCount} parents, {fixedRoster.Count} fixed).");

                yield return RunGenerationMatches(competitors);
                LogStandings();

                var poolStandings = standings.FindAll(s => s.isRandom);
                poolStandings.Sort(CompareStandings);

                if (poolStandings.Count == 0)
                {
                    Debug.LogWarning("[FightClub] No gene pool entries produced results this cycle; stopping Improve Mode.");
                    break;
                }

                int breederCount = Mathf.Clamp(breedersCount, 1, poolStandings.Count);
                lastBreeders = poolStandings.GetRange(0, breederCount);

                if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Cycle {currentCycle} breeders: {string.Join(", ", lastBreeders.ConvertAll(b => $"{b.name} ({b.wins}W/{b.losses}L/{b.draws}D)"))}");

                if (parentsCompeteWithChildren && parentWeights != null && parentWeights.Count > 0)
                {
                    var childBest = poolStandings[0];
                    var parentEntries = standings.FindAll(s => s.name != null && s.name.StartsWith("Parent#"));
                    if (parentEntries.Count > 0)
                    {
                        parentEntries.Sort(CompareStandings);
                        var bestParent = parentEntries[0];
                        if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Cycle {currentCycle} parent-vs-child: best child {childBest.name} ({childBest.wins}W/{childBest.losses}L) vs best parent {bestParent.name} ({bestParent.wins}W/{bestParent.losses}L). " +
                                  ((childBest.wins >= bestParent.wins) ? "Children held their own." : "Parents still ahead - keep evolving."));
                    }
                }

                parentWeights = lastBreeders.ConvertAll(b => b.weights);

                if (currentCycle < totalCycles)
                {
                    var nextGen = new List<WeightsTemplate>();
                    for (int i = 0; i < popSize; i++)
                    {
                        var parentA = lastBreeders[Random.Range(0, lastBreeders.Count)].weights;
                        var parentB = lastBreeders[Random.Range(0, lastBreeders.Count)].weights;
                        nextGen.Add(Register(BreedWeights(parentA, parentB, mutationStrength)));
                    }
                    genePool = nextGen;
                }
            }

            champion = standings.Count > 0 ? standings[0].name : "";
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Improve Mode finished after {currentCycle}/{totalCycles} cycles. Best of final generation: {champion}");
        }
        finally
        {
            state = TournamentState.Finished;
            currentMatchup = "";
            runtime.bothTeamsAi = false;
            ResetWeightOverrides();
        }
    }

    IEnumerator RunMatch(StandingEntry entryA, StandingEntry entryB, int idxA, int idxB)
    {
        int seed = System.Guid.NewGuid().GetHashCode();
        lastMatchSeed = seed;
        if (CombatDebugHandler.UltraDebug)
            Debug.Log($"[FightClub] Match {currentMatch}/{totalMatches}: {entryA.name} vs {entryB.name} - seed={seed}");

        if (combat != null && combat.DiceRollerPublic != null)
            combat.DiceRollerPublic.SetSeed(seed);
        if (combatAi != null)
            combatAi.AiRng = new System.Random(seed);
        UnityEngine.Random.InitState(seed);

        GenerateRandomTeam();

        runtime.playerWeightsOverride = entryA.weights;
        runtime.enemyWeightsOverride = entryB.weights;

        if (combatAi != null) combatAi.ClearFatigue();

        runtime.InitBattle();

        int waitFrames = 0;
        while (!combat.IsBattleOver() && waitFrames < 18000)
        {
            if (runtime.CurrentRound > maxRoundsPerMatch) break;
            yield return null;
            waitFrames++;
        }

        bool pViable = combat.IsTeamViable(CombatScript.TeamPlayer);
        bool eViable = combat.IsTeamViable(CombatScript.TeamEnemy);

        int winnerTeam = -1;
        if (pViable && !eViable) winnerTeam = CombatScript.TeamPlayer;
        else if (eViable && !pViable) winnerTeam = CombatScript.TeamEnemy;
        int roundsToDecision = runtime.CurrentRound;
        float winnerHpFrac = winnerTeam >= 0 ? TeamHpFraction(winnerTeam) : 0f;
        int winnerUnits     = winnerTeam >= 0 ? LivingUnitsOnTeam(winnerTeam) : 0;

        string result;
        var a = standings[idxA];
        var b = standings[idxB];
        if (winnerTeam == CombatScript.TeamPlayer)
        {
            a.wins++; b.losses++;
            result = $"{entryA.name} WINS";
        }
        else if (winnerTeam == CombatScript.TeamEnemy)
        {
            b.wins++; a.losses++;
            result = $"{entryB.name} WINS";
        }
        else
        {
            a.draws++; b.draws++;
            result = "DRAW";
        }
        a.roundsToDecision = roundsToDecision;
        b.roundsToDecision = roundsToDecision;
        a.remainingWinnerHpFraction = winnerHpFrac;
        b.remainingWinnerHpFraction = winnerHpFrac;
        a.remainingWinnerUnits = winnerUnits;
        b.remainingWinnerUnits = winnerUnits;
        standings[idxA] = a;
        standings[idxB] = b;

        lastResult = result;
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Match {currentMatch}/{totalMatches}: {currentMatchup} → {result} (round {runtime.CurrentRound}, winner HP {winnerHpFrac*100:0}%, {winnerUnits} units alive)  seed={seed}");

        CollectActionUsage();

        runtime.UnloadBattle();
        yield return null;
    }

    #endregion

    #region Results / Export

    [ContextMenu("Log Standings")]
    public void LogStandings()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ FIGHT CLUB STANDINGS ═══");
        for (int i = 0; i < standings.Count; i++)
        {
            var s = standings[i];
            sb.AppendLine($"  {i+1}. {s.name,-20} {s.wins}W  {s.losses}L  {s.draws}D");
        }
        if (CombatDebugHandler.UltraDebug) Debug.Log(sb.ToString());
    }

    [ContextMenu("Export Champion Weights")]
    public void ExportChampion()
    {
        if (standings.Count == 0) { Debug.LogWarning("[FightClub] No standings yet."); return; }
        var champ = standings[0];
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Champion: {champ.name}\n{SerializeWeights(champ.weights)}");

#if UNITY_EDITOR
        string safeName = string.Join("_", champ.name.Split(System.IO.Path.GetInvalidFileNameChars()));
        var path = $"Assets/FightClub_Champion_{safeName}.asset";

        // Always Instantiate so we never corrupt the original preset asset reference.
        var exportedAsset = UnityEngine.Object.Instantiate(champ.weights);
        exportedAsset.name = champ.name;
        UnityEditor.AssetDatabase.CreateAsset(exportedAsset, path);
        UnityEditor.AssetDatabase.SaveAssets();
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Champion weights saved to {path}");
#else
        if (CombatDebugHandler.UltraDebug) Debug.Log("[FightClub] Editor-only asset export unavailable in build. Weights logged above.");
#endif
    }

    [ContextMenu("Export Top Breeders (Improve Mode)")]
    public void ExportTopBreeders()
    {
        var source = (lastBreeders != null && lastBreeders.Count > 0)
            ? lastBreeders
            : standings.FindAll(s => s.isRandom);

        if (source == null || source.Count == 0)
        {
            Debug.LogWarning("[FightClub] No Improve Mode breeders/standings to export yet. Run Improve Mode first.");
            return;
        }

        int count = Mathf.Min(breedersCount, source.Count);

#if UNITY_EDITOR
        for (int i = 0; i < count; i++)
        {
            var entry = source[i];
            if (entry.weights == null) continue;

            string safeName = string.Join("_", entry.name.Split(System.IO.Path.GetInvalidFileNameChars()));
            var path = $"Assets/FightClub_Gen{currentCycle}_{i + 1}_{safeName}.asset";

            var newAsset = ScriptableObject.Instantiate(entry.weights);
            UnityEditor.AssetDatabase.CreateAsset(newAsset, path);
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Exported #{i + 1}: {entry.name} -> {path}");
        }
        UnityEditor.AssetDatabase.SaveAssets();
#else
        for (int i = 0; i < count; i++)
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Top #{i + 1}: {source[i].name}\n{SerializeWeights(source[i].weights)}");
        if (CombatDebugHandler.UltraDebug) Debug.Log("[FightClub] Editor-only asset export unavailable in build. Weights logged above.");
#endif
    }

    public static string SerializeWeights(WeightsTemplate w)
    {
        if (w == null) return "(null)";
        var sb = new StringBuilder();
        sb.AppendLine($"// {w.name}");
        sb.AppendLine($"enemies.scoring.damageWeight = {w.enemies.scoring.damageWeight}");
        sb.AppendLine($"enemies.scoring.enableFocusFire = {w.enemies.scoring.enableFocusFire}");
        sb.AppendLine($"enemies.scoring.killWeight = {w.enemies.scoring.killWeight}");
        sb.AppendLine($"enemies.scoring.enableKillBonus = {w.enemies.scoring.enableKillBonus}");
        sb.AppendLine($"enemies.scoring.debuffWeight = {w.enemies.scoring.debuffWeight}");
        sb.AppendLine($"enemies.scoring.enableThreatTargeting = {w.enemies.scoring.enableThreatTargeting}");
        sb.AppendLine($"enemies.scoring.threatTargetingWeight = {w.enemies.scoring.threatTargetingWeight}");
        sb.AppendLine($"allies.scoring.healWeight = {w.allies.scoring.healWeight}");
        sb.AppendLine($"allies.scoring.buffEffectValueWeight = {w.allies.scoring.buffEffectValueWeight}");
        sb.AppendLine($"allies.scoring.enableSurvival = {w.allies.scoring.enableSurvival}");
        sb.AppendLine($"allies.scoring.survivalWeight = {w.allies.scoring.survivalWeight}");
        sb.AppendLine($"ai.aiDecisionRandomness = {w.ai.aiDecisionRandomness}");
        sb.AppendLine($"ai.preferVariedActions = {w.ai.preferVariedActions}");
        sb.AppendLine($"ai.actionFatigueWeight = {w.ai.actionFatigueWeight}");
        sb.AppendLine($"ai.enableIdlePenalty = {w.ai.enableIdlePenalty}");
        sb.AppendLine($"ai.positionWeight = {w.ai.positionWeight}");
        sb.AppendLine($"ai.enableMoveActions = {w.ai.enableMoveActions}");
        sb.AppendLine($"ai.enableMoveAndAction = {w.ai.enableMoveAndAction}");
        sb.AppendLine($"ai.enableBenchSwaps = {w.ai.enableBenchSwaps}");
        sb.AppendLine($"ai.enableResourceScoring = {w.ai.enableResourceScoring}");
        sb.AppendLine($"ai.enableAPCostPenalty = {w.ai.enableAPCostPenalty}");
        sb.AppendLine($"ai.APCostWeight = {w.ai.APCostWeight}");
        sb.AppendLine($"ai.aiLookaheadDepth = {w.ai.aiLookaheadDepth}");
        sb.AppendLine($"ai.enableIdlePenalty = {w.ai.enableIdlePenalty}");
        sb.AppendLine($"ai.idlePenalty = {w.ai.idlePenalty}");
        sb.AppendLine($"ai.enableAPOverflowPenalty = {w.ai.enableAPOverflowPenalty}");
        return sb.ToString();
    }

    #endregion

    #region Overlay

    void OnGUI()
    {
        if (state == TournamentState.Idle) return;

        EnsureOverlayStyle();

        DrawStandingsOverlay();
    }

    GUIStyle _labelStyle;
    GUIStyle _boxStyle;
    Texture2D _bgTex;
    float _cacheTimer = -1f;
    string _overlayCache = "";

    void EnsureOverlayStyle()
    {
        if (_bgTex == null)
        {
            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.7f));
            _bgTex.Apply();
        }
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, richText = true, wordWrap = true,
                normal = { textColor = Color.white, background = _bgTex }
            };
        }
        if (_boxStyle == null)
        {
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = _bgTex;
        }

        if (_cacheTimer < 0f || Time.unscaledTime - _cacheTimer > 0.25f)
        {
            _overlayCache = BuildOverlayText();
            _cacheTimer = Time.unscaledTime;
        }
    }

    void DrawStandingsOverlay()
    {
        float w = overlayWidth;
        float h = Mathf.Min(overlayHeight, Screen.height - overlayMargin * 2f);
        float x = Screen.width - w - overlayMargin;
        float y = overlayMargin;
        Rect area = new Rect(x, y, w, h);
        GUI.Box(area, GUIContent.none, _boxStyle);

        Rect inner = new Rect(area.x + 8f, area.y + 8f, area.width - 16f, area.height - 16f);
        GUILayout.BeginArea(inner);
        try
        {
            _standingsScrollPos = GUILayout.BeginScrollView(
                _standingsScrollPos,
                GUILayout.Width(inner.width),
                GUILayout.Height(inner.height));
            GUILayout.Label(_overlayCache, _labelStyle);
            GUILayout.EndScrollView();
        }
        finally
        {
            GUILayout.EndArea();
        }
    }

    string BuildOverlayText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<b>\u2550\u2550\u2550 FIGHT CLUB \u2550\u2550\u2550</b>");
        sb.AppendLine($"State: <color=#88ff88>{state}</color>");
        if (improveMode)
        {
            string poolLabel = onlyFightGenePool ? " [gene pool only]" : " [+ presets]";
            sb.AppendLine($"Mode: <color=#66ccff>Improve - cycle {currentCycle}/{totalCycles}</color>{poolLabel}");
        }
        sb.AppendLine($"Match: <b>{currentMatch}</b>/{totalMatches}");
        sb.AppendLine($"Matchup: <color=#ffcc66>{currentMatchup}</color>");
        if (!string.IsNullOrEmpty(lastResult))
            sb.AppendLine($"Last: <color=#88ff88>{lastResult}</color>");
        sb.AppendLine();
        sb.AppendLine("<b>STANDINGS</b>");
        sb.AppendLine(new string('-', 36));
        var sorted = new List<StandingEntry>(standings);
        sorted.Sort(CompareStandings);
        for (int i = 0; i < sorted.Count; i++)
        {
            var s = sorted[i];
            bool isChamp = !string.IsNullOrEmpty(champion) && champion == s.name;
            string marker = isChamp ? "<color=#ffcc66>\u2605</color>" : " ";
            sb.AppendLine($"{marker} {i+1,2}. {s.name,-20} {s.wins}W {s.losses}L {s.draws}D");
        }
        if (!string.IsNullOrEmpty(champion))
        {
            sb.AppendLine();
            sb.AppendLine($"<color=#ffcc66><b>CHAMPION: {champion}</b></color>");
        }

        if (showActionCounter)
        {
            sb.AppendLine();
            sb.Append(BuildActionCounterText());
        }

        return sb.ToString();
    }

    // Action counter text 
    string BuildActionCounterText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 44));
        sb.AppendLine("<b>ACTION COUNTER <color=#88ff88>for balancing</color></b>");
        sb.AppendLine($"matches counted: <b>{_actionMatchesCounted}</b>  total uses: <b>{_actionTotalUses}</b>");
        sb.AppendLine("<color=#666>counts include both teams (same unit picks)</color>");
        sb.AppendLine(new string('-', 44));

        if (_actionUsage.Count == 0)
        {
            sb.AppendLine("<color=#888>No actions recorded yet.</color>");
            sb.AppendLine("<color=#666>Run a tournament - usage is tallied at the end of each match.</color>");
            return sb.ToString();
        }

        var rows = new List<(string unit, int total, Dictionary<string, int> map)>();
        int maxActionUses = 1;
        foreach (var kv in _actionUsage)
        {
            int total = 0;
            foreach (var v in kv.Value.Values)
            {
                total += v;
                if (v > maxActionUses) maxActionUses = v;
            }
            rows.Add((kv.Key, total, kv.Value));
        }
        rows.Sort((a, b) => b.total.CompareTo(a.total));

        foreach (var row in rows)
        {
            sb.AppendLine();
            sb.AppendLine($"<color=#66ccff><b>{EscapeRich(row.unit)}</b></color>  <color=#888>({row.total} total uses)</color>");

            var actions = new List<KeyValuePair<string, int>>(row.map);
            actions.Sort((a, b) => b.Value.CompareTo(a.Value));

            foreach (var act in actions)
            {
                string bar = ActionUsageBar(act.Value, maxActionUses);
                sb.AppendLine($"  {bar} <color=#ddd>{EscapeRich(act.Key)}</color> <color=#ffcc66>\u00d7{act.Value}</color>");
            }
        }
        return sb.ToString();
    }

    // Build a fixed-width text bar (max 12 blocks) scaled to the global max.
    static string ActionUsageBar(int value, int maxUses)
    {
        const int width = 12;
        int filled = maxUses <= 0 ? 0 : Mathf.RoundToInt(Mathf.Clamp01((float)value / maxUses) * width);
        var sb = new StringBuilder();
        if (filled > 0)
            sb.Append("<color=#6cff8e>").Append('\u2588', filled).Append("</color>");
        if (width - filled > 0)
            sb.Append("<color=#33333a>").Append('\u2588', width - filled).Append("</color>");
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Action usage collection
    // -----------------------------------------------------------------------
    void CollectActionUsage()
    {
        if (combat == null) return;
        _actionMatchesCounted++;

        for (int team = 0; team < 2; team++)
        {
            for (int slot = 0; slot < 4; slot++)
                AddUnitActionUsage(combat.GetUnit(team, slot));

            if (runtime != null)
            {
                var bench = runtime.GetBench(team);
                if (bench != null)
                {
                    for (int i = 0; i < bench.Count; i++)
                        AddUnitActionUsage(bench[i]);
                }
            }
        }

        // Invalidate the combined overlay cache so the next OnGUI refreshes immediately.
        _cacheTimer = -1f;
    }

    void AddUnitActionUsage(CombatScript.UnitData u)
    {
        if (string.IsNullOrEmpty(u.name)) return;
        if (u.actions == null || u.ActionUseCounts == null) return;

        if (!_actionUsage.TryGetValue(u.name, out var ActionMap))
        {
            ActionMap = new Dictionary<string, int>();
            _actionUsage[u.name] = ActionMap;
        }

        for (int i = 0; i < u.actions.Count && i < u.ActionUseCounts.Count; i++)
        {
            string ActionName = u.actions[i].ActionName;
            if (string.IsNullOrEmpty(ActionName))
                ActionName = u.actions[i].fullName;
            if (string.IsNullOrEmpty(ActionName)) continue;

            int used = u.ActionUseCounts[i];
            if (used <= 0) continue;

            if (ActionMap.TryGetValue(ActionName, out int cur))
                ActionMap[ActionName] = cur + used;
            else
                ActionMap[ActionName] = used;

            _actionTotalUses += used;
        }
    }

    [ContextMenu("Reset Action Counter")]
    public void ResetActionCounter()
    {
        _actionUsage.Clear();
        _actionMatchesCounted = 0;
        _actionTotalUses = 0;
        _cacheTimer = -1f;
        if (CombatDebugHandler.UltraDebug) Debug.Log("[FightClub] Action counter reset.");
    }

    [ContextMenu("Toggle Action Counter Overlay")]
    public void ToggleActionCounter()
    {
        showActionCounter = !showActionCounter;
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[FightClub] Action counter overlay = {showActionCounter}");
    }

    static string EscapeRich(string s)
        => s == null ? "" : s.Replace("<", "&lt;").Replace(">", "&gt;");

    #endregion
}
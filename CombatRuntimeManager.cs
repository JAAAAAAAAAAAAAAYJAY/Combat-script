using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData    = CombatScript.UnitData;

[DefaultExecutionOrder(100)]
public class CombatRuntimeManager : MonoBehaviour
{
#region Config

[SerializeField] private sheetParser   sheetParser;
[SerializeField] private combatSetup   combatSetup;
[SerializeField] private CombatScript  combat;
[SerializeField] private CombatAi      combatAi;

[Header("Auto-start")]
[SerializeField] public bool autoStartOnStart = true;

[Header("Enemy AI auto-play")]
[SerializeField] private bool enemyAutoPlay = true;
[SerializeField] public float enemyAiActionDelay = 0.35f;

[Header("Teams (runtime owns all unit data)")]
public CombatScript.PlayerTeamData PlayerTeam;
public CombatScript.EnemyTeamData  EnemyTeam;

[Header("Fight Club (both teams AI)")]
[Tooltip("When true, the player team is also AI-controlled (for FightClub tournaments). Both teams auto-play.")]
public bool bothTeamsAi = false;
[Tooltip("Weights override for the player team's AI (set by FightClub). Null = use combatAi.weights.")]
public WeightsTemplate playerWeightsOverride;
[Tooltip("Weights override for the enemy team's AI (set by FightClub). Null = use combatAi.weights.")]
public WeightsTemplate enemyWeightsOverride;

[Header("Bench")]
[SerializeField] private List<UnitData> playerBench = new List<UnitData>();
[SerializeField] private List<UnitData> enemyBench  = new List<UnitData>();

#endregion

#region State

public enum BattleState { Idle, InProgress, Ended }
public enum TeamTurn    { None, Player, Enemy }

private BattleState battleState = BattleState.Idle;
private int         currentRound = 0;
private TeamTurn    activeTurn   = TeamTurn.None;

private readonly List<AISuggestion> currentSuggestions = new List<AISuggestion>();

public BattleSnapshot lastSnapshot;
private bool hasSnapshot = false;

private Coroutine aiRoutine;
private bool isAiRunning = false;

// baseline weights cached before any per-turn override is applied
private WeightsTemplate _baselineWeights;

WeightsTemplate GetWeightsForTeam(int team)
{
    if (team == CombatScript.TeamPlayer && playerWeightsOverride != null)
        return playerWeightsOverride;
    if (team == CombatScript.TeamEnemy && enemyWeightsOverride != null)
        return enemyWeightsOverride;
    return _baselineWeights;
}

public CombatScript.PlayerTeamData PlayerTeamData => PlayerTeam;
public CombatScript.EnemyTeamData  EnemyTeamData  => EnemyTeam;

[Header("AP Banks (runtime owns all AP state)")]
public CombatScript.APBank playerAPBank;
public CombatScript.APBank enemyAPBank;
[Tooltip("AP cap for the player team, computed by CombatScript during InitBattle. Read by CombatScript.GetTeamAPCap.")]
    public float fixedPlayerAPCap;
    [Tooltip("AP cap for the enemy team, computed by CombatScript during InitBattle.")]
    public float fixedEnemyAPCap;

#endregion

#region Bench access

public List<UnitData> PlayerBench => playerBench;
public List<UnitData> EnemyBench  => enemyBench;

public List<UnitData> GetBench(int team)
    => team == CombatScript.TeamPlayer ? playerBench : enemyBench;

public int BenchCount(int team) => GetBench(team).Count;

public int LivingBenchCount(int team)
{
    var bench = GetBench(team);
    int n = 0;
    for (int i = 0; i < bench.Count; i++)
        if (IsBenchUnitAlive(bench[i])) n++;
    return n;
}

public bool BenchHasLiving(int team) => LivingBenchCount(team) > 0;

public static bool IsBenchUnitAlive(UnitData u)
    => u.hp > 0 && !string.IsNullOrEmpty(u.name);

public int[] BenchSwapSlots(int team)
{
    if (combatAi != null && combatAi.weights != null && combatAi.weights.ai.benchSwapSlots != null && combatAi.weights.ai.benchSwapSlots.Length > 0)
        return combatAi.weights.ai.benchSwapSlots;
    return new int[] { 2, 3 };
}

public bool IsBenchSwapSlot(int team, int slot)
{
    int[] slots = BenchSwapSlots(team);
    for (int i = 0; i < slots.Length; i++)
        if (slots[i] == slot) return true;
    return false;
}

#endregion

#region Bench operations

public void BenchActiveUnit(int team, int slot)
{
    if (combat == null) return;
    if (slot < 0 || slot > 3) { Debug.LogWarning($"[Bench] bad slot {slot}"); return; }

    if (string.IsNullOrEmpty(combat.GetUnit(team, slot).name)) { Debug.LogWarning($"[Bench] slot {slot} empty"); return; }

    combat.DetachPassivesPublic(team, slot);
    CustomStatusRuntime.ClearForUnit(team, slot);
    UnitData u = combat.GetUnit(team, slot);
    ClearAllTemporaryEffects(ref u);

    u.PlayerUnit      = null;
    u.PlayerUnitSpace = null;

    var bench = GetBench(team);
    bench.Add(u);
    combat.SetUnit(team, slot, default(UnitData));

    if (CombatDebugHandler.UltraDebug) Debug.Log($"[Bench] {u.name} (team {team} slot {slot}) benched. HP {u.hp:0}/{u.maxHp:0}.");
}

public bool DeployBenchUnit(int team, int benchIndex, int slot)
{
    if (combat == null) return false;
    if (!IsBenchSwapSlot(team, slot)) return false;
    return DeployBenchUnitInternal(team, benchIndex, slot);
}

bool DeployBenchUnitInternal(int team, int benchIndex, int slot)
{
    if (combat == null) return false;
    var bench = GetBench(team);
    if (benchIndex < 0 || benchIndex >= bench.Count) return false;

    UnitData incoming = bench[benchIndex];
    if (string.IsNullOrEmpty(incoming.name)) return false;

    UnitData current = combat.GetUnit(team, slot);
    GameObject slotVisUnit  = current.PlayerUnit;
    GameObject slotVisSpace = current.PlayerUnitSpace;

    bool hadOccupant = !string.IsNullOrEmpty(current.name);
    if (hadOccupant)
    {
        combat.DetachPassivesPublic(team, slot);
        CustomStatusRuntime.ClearForUnit(team, slot);
        current = combat.GetUnit(team, slot);
        ClearAllTemporaryEffects(ref current);
    }

    incoming.PlayerUnit      = slotVisUnit;
    incoming.PlayerUnitSpace = slotVisSpace;
    combat.SetUnit(team, slot, incoming);

    bench.RemoveAt(benchIndex);
    if (hadOccupant)
    {
        current.PlayerUnit      = null;
        current.PlayerUnitSpace = null;
        bench.Add(current);
    }

    combat.ReinitializePassivesForSlot(team, slot);
    return true;
}

public bool AutoDeployBenchUnit(int team)
{
    if (combat == null) return false;
    if (team != CombatScript.TeamPlayer && team != CombatScript.TeamEnemy) return false;

    for (int slot = 0; slot < 4; slot++)
        if (IsUnitAlive(team, slot)) return false;

    var bench = GetBench(team);
    if (bench == null || bench.Count == 0) return false;

    int benchIndex = -1;
    for (int i = 0; i < bench.Count; i++)
        if (IsBenchUnitAlive(bench[i])) { benchIndex = i; break; }
    if (benchIndex < 0) return false;

    UnitData incoming = bench[benchIndex];
    // use configured bench swap slot fallback to 2
    int[] swapSlots = BenchSwapSlots(team);
    int ReinforceSlot = (swapSlots != null && swapSlots.Length > 0) ? swapSlots[0] : 2;

    bool ok = DeployBenchUnitInternal(team, benchIndex, ReinforceSlot);
    if (ok)
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Bench] Auto-reinforce: {incoming.name} deployed to team {team} slot {ReinforceSlot} (field was wiped).");
    return ok;
}

void AutoDeployBenchIfNeeded()
{
    try { AutoDeployBenchUnit(CombatScript.TeamPlayer); }
    catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] AutoDeployBenchUnit(player) threw: {e}"); }

    try { AutoDeployBenchUnit(CombatScript.TeamEnemy); }
    catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] AutoDeployBenchUnit(enemy) threw: {e}"); }
}

static void ClearAllTemporaryEffects(ref UnitData u)
{
    u.fireRemainingRounds          = 0;
    u.bleedRemainingRounds         = 0;
    u.poisonRemainingRounds        = 0;
    u.fireDamageNotation           = "";
    u.bleedDamageNotation          = "";
    u.poisonDamageNotation          = "";
    u.stunRemainingRounds          = 0;
    u.sleepRemainingRounds         = 0;
    u.confusionRemainingRounds     = 0;
    u.petrificationRemainingRounds = 0;
    u.tauntRemainingRounds         = 0;
    u.tauntTargetSlot             = -1;
    u.th = 0;
}

public void FillBenchFromSheet(combatSetup setup)
{
    if (setup == null) return;
    FillBenchListFromSheet(CombatScript.TeamPlayer, setup, playerBench);
    FillBenchListFromSheet(CombatScript.TeamEnemy,  setup, enemyBench);
}

void FillBenchListFromSheet(int team, combatSetup setup, List<UnitData> bench)
{
    if (bench == null) return;
    for (int i = 0; i < bench.Count; i++)
    {
        UnitData u = bench[i];
        if (string.IsNullOrWhiteSpace(u.unitcode)) continue;
        u = setup.fillemup(u);
        bench[i] = u;
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Bench] Loaded bench[{i}] team {team}: {u.name}");
    }
}

public void ClearAllBenches()
{
    if (playerBench != null) playerBench.Clear();
    if (enemyBench  != null) enemyBench.Clear();
}

public void DebugCloneToBench(int team, int fromSlot)
{
    if (combat == null) return;
    UnitData u = combat.GetUnit(team, fromSlot);
    if (string.IsNullOrEmpty(u.name)) return;
    var clone = u;
    if (u.actions != null) clone.actions = new List<ActionData>(u.actions);
    if (u.ActionUseCounts != null) clone.ActionUseCounts = new List<int>(u.ActionUseCounts);
    if (u.passiveKeys != null) clone.passiveKeys = new List<string>(u.passiveKeys);
    GetBench(team).Add(clone);
}

public string DumpBench(int team)
{
    var bench = GetBench(team);
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"--- BENCH (team {team})  count={bench.Count}  living={LivingBenchCount(team)} ---");
    for (int i = 0; i < bench.Count; i++)
    {
        var u = bench[i];
        string state = IsBenchUnitAlive(u) ? "alive" : "dead";
        sb.AppendLine($"  [{i}] {(string.IsNullOrEmpty(u.name) ? "<empty>" : u.name),-18} HP {u.hp:0}/{u.maxHp:0}  ({state})  code={u.unitcode}");
    }
    return sb.ToString();
}

#endregion

#region Public properties

public BattleState State         => battleState;
public int         CurrentRound  => currentRound;
public TeamTurn    ActiveTurn    => activeTurn;
public bool        IsPlayerTurn  => activeTurn == TeamTurn.Player;
public bool        IsEnemyTurn   => activeTurn == TeamTurn.Enemy;
public bool        HasSnapshot   => hasSnapshot;
public BattleSnapshot LastSnapshot => lastSnapshot;
public bool        IsAiRunning => isAiRunning;

public int ActiveTeamId =>
    activeTurn == TeamTurn.Player ? CombatScript.TeamPlayer :
    activeTurn == TeamTurn.Enemy  ? CombatScript.TeamEnemy  : -1;

public IReadOnlyList<AISuggestion> CurrentSuggestions => currentSuggestions;

#endregion

#region Unity lifecycle

void Start()
{
    if (combatAi != null && _baselineWeights == null)
        _baselineWeights = combatAi.weights;

    if (autoStartOnStart)
    {
        if (combat == null || sheetParser == null || combatSetup == null)
        {
            Debug.LogError("[CombatRuntimeManager] Dependencies not assigned in inspector!");
            return;
        }
        StartCoroutine(DeferredInitBattle());
    }
}

IEnumerator DeferredInitBattle()
{
    yield return null; // wait one frame
    InitBattle();
}

private bool _pendingBenchDeploy;

void Update()
{
    if (battleState != BattleState.InProgress) return;
    if (activeTurn == TeamTurn.None) return;
    if (combat == null) return;

    if (_pendingBenchDeploy && !combat.IsExecutingAction)
    {
        _pendingBenchDeploy = false;
        try { AutoDeployBenchIfNeeded(); }
        catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] Deferred AutoDeployBenchIfNeeded threw: {e}"); }
    }

    if (combat.IsExecutingAction) return;

    int team = ActiveTeamId;
    if (team < 0) return;

    combat.ClampAP(CombatScript.TeamPlayer);
    combat.ClampAP(CombatScript.TeamEnemy);

    if (isAiRunning) return;

    if (!TeamCanAct(team))
        EndTeamTurn();
}

#endregion

#region Bootstrap

[ContextMenu("Init Battle")]
public void InitBattle()
{
    if (combat == null) { Debug.LogError("[CombatRuntimeManager] CombatScript not assigned."); return; }

    battleState  = BattleState.InProgress;
    currentRound = 0;
    activeTurn   = TeamTurn.None;
    currentSuggestions.Clear();
    hasSnapshot = false;
    lastSnapshot = default;
    _pendingBenchDeploy = false;

    combat.BuildOverrideRegistryPublic();

    if (combatSetup != null)
    {
        combatSetup.LoadAllUnits();
    }
    else
    {
        Debug.LogWarning("[CombatRuntimeManager] combatSetup is null - units may not be loaded; AP cap could be 0.");
    }

    FillBenchFromSheet(combatSetup);

    if (combatAi != null) combatAi.ClearFatigue();

    combat.ClearAllDOTsForAllUnits();
    combat.InitAPBanks();
    combat.InitializePassives();

    combat.EnsurePlayableAPCap();

    combat.FillAPToCap(CombatScript.TeamPlayer);
    combat.FillAPToCap(CombatScript.TeamEnemy);

    StartRound();
}

[ContextMenu("Resume Battle")]
public void ResumeBattle()
{
    if (combat == null) return;
    battleState = BattleState.InProgress;
    currentSuggestions.Clear();
    StartTeamTurn(TeamTurn.Player);
}

#endregion


#region Auto-Wire Unit GameObjects

[Header("Auto-Wire (scene hierarchy)")]
[Tooltip("Root of the UI hierarchy holding TeamUnits/EnemyUnits. Leave empty to auto-find a GameObject named 'UnitUI'.")]
public Transform unitUiRoot;
[Tooltip("Name of the player team container under unitUiRoot (default 'TeamUnits').")]
public string playerUnitsName = "TeamUnits";
[Tooltip("Name of the enemy team container under unitUiRoot (default 'EnemyUnits').")]
public string enemyUnitsName  = "EnemyUnits";
[Tooltip("Name prefix for the space GameObjects that hold each unit (default 'UnitSpace'). Slot index is appended, e.g. UnitSpace0.")]
public string unitSpacePrefix = "UnitSpace";
[Tooltip("Name prefix for the unit image GameObjects (default 'UnitImage'). Slot index is appended, e.g. UnitImage0.")]
public string unitImagePrefix = "UnitImage";

[ContextMenu("Auto-Wire Unit GameObjects")]
public void AutoWireUnitGameObjects()
{
    ResolveUnitUiRoot();
    if (unitUiRoot == null)
    {
        Debug.LogWarning("[CombatRuntimeManager] Auto-wire failed: unitUiRoot not found (looked for a GameObject named 'UnitUI').");
        return;
    }

    Transform playerContainer = FindChild(unitUiRoot, playerUnitsName);
    Transform enemyContainer  = FindChild(unitUiRoot, enemyUnitsName);
    if (playerContainer == null)
        Debug.LogWarning($"[CombatRuntimeManager] Auto-wire: '{playerUnitsName}' not found under '{unitUiRoot.name}'.");
    if (enemyContainer == null)
        Debug.LogWarning($"[CombatRuntimeManager] Auto-wire: '{enemyUnitsName}' not found under '{unitUiRoot.name}'.");

    int wired = 0;
    wired += WireTeam(playerContainer, CombatScript.TeamPlayer);
    wired += WireTeam(enemyContainer,  CombatScript.TeamEnemy);

    if (CombatDebugHandler.UltraDebug) Debug.Log($"[CombatRuntimeManager] Auto-wire complete: {wired} slot GameObject refs assigned.");
}

void ResolveUnitUiRoot()
{
    if (unitUiRoot != null) return;
    var found = GameObject.Find("UnitUI");
    if (found != null) unitUiRoot = found.transform;
}

int WireTeam(Transform container, int team)
{
    if (container == null || combat == null) return 0;
    int count = 0;
    for (int slot = 0; slot < 4; slot++)
    {
        var space = FindChild(container, $"{unitSpacePrefix}{slot}");
        var unit  = FindChild(container, $"{unitImagePrefix}{slot}");
        if (space == null && unit == null) continue;

        var u = combat.GetUnit(team, slot);
        u.PlayerUnit      = unit  != null ? unit.gameObject  : (u.PlayerUnit);
        u.PlayerUnitSpace = space != null ? space.gameObject : (u.PlayerUnitSpace);
        combat.SetUnit(team, slot, u);
        count++;
    }
    return count;
}

static Transform FindChild(Transform parent, string name)
{
    if (parent == null || string.IsNullOrEmpty(name)) return null;
    var t = parent.Find(name);
    if (t != null) return t;
    for (int i = 0; i < parent.childCount; i++)
    {
        var r = FindChild(parent.GetChild(i), name);
        if (r != null) return r;
    }
    return null;
}

#endregion

#region Round flow

public void StartRound()
{
    currentRound++;
    combat.FireOnRoundStart();
    StartTeamTurn(TeamTurn.Player);
}

private void EndRound()
{
    try { combat.ProcessEndOfRound(); }
    catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] ProcessEndOfRound threw: {e}"); }

    // End-of-round DOTs may have wiped a field. Reinforce from the bench before
    // the win/lose check so reserves are accounted for.
    try { AutoDeployBenchIfNeeded(); }
    catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] AutoDeployBenchIfNeeded threw: {e}"); }

    try { CheckBattleOver(); }
    catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] CheckBattleOver threw: {e}"); }

    if (battleState == BattleState.InProgress)
    {
        try { StartRound(); }
        catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] StartRound threw: {e}"); activeTurn = TeamTurn.Player; }
    }
    else activeTurn = TeamTurn.None;
}

#endregion

#region Turn flow

private void StartTeamTurn(TeamTurn turn)
{
    activeTurn = turn;
    int team = (turn == TeamTurn.Player) ? CombatScript.TeamPlayer : CombatScript.TeamEnemy;

    combat.RecomputeAPCap();
    combat.RegenerateAP(team);

    if (combatAi != null) combatAi.OnTurnStart(team);

    try { combat.FireOnTurnStart(team); }
    catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] FireOnTurnStart threw: {e}"); }

    if (turn == TeamTurn.Enemy)
    {
        // Set the weights override BEFORE producing suggestions so the
        // displayed suggestion matches the actual AI decision.
        if (combatAi != null && enemyWeightsOverride != null)
            combatAi.weights = enemyWeightsOverride;
        ProduceAISuggestions();
    }

    bool shouldRunAi = (turn == TeamTurn.Enemy && enemyAutoPlay) || (turn == TeamTurn.Player && bothTeamsAi);
    if (shouldRunAi && combatAi != null)
    {
        int aiTeam = (turn == TeamTurn.Player) ? CombatScript.TeamPlayer : CombatScript.TeamEnemy;
        if (aiRoutine != null) StopCoroutine(aiRoutine);
        aiRoutine = StartCoroutine(RunAiTurn(aiTeam));
    }
}

public void EndTurnButton()
{
    if (battleState != BattleState.InProgress) return;
    if (activeTurn == TeamTurn.None) return;
    EndTeamTurn();
}

private void EndTeamTurn()
{
    if (activeTurn == TeamTurn.None) return;

    if (aiRoutine != null) { StopCoroutine(aiRoutine); aiRoutine = null; }
    isAiRunning = false;

    TeamTurn ending = activeTurn;
    int endingTeam = (ending == TeamTurn.Player) ? CombatScript.TeamPlayer : CombatScript.TeamEnemy;

    try { combat.FireOnTurnEnd(endingTeam); }
    catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] FireOnTurnEnd threw: {e}"); }

    if (ending == TeamTurn.Player) StartTeamTurn(TeamTurn.Enemy);
    else { activeTurn = TeamTurn.None; currentSuggestions.Clear(); EndRound(); }
}

#endregion

#region Action use

public bool RequestActionUse(int team, int slot, int ActionIndex)
{
    if (battleState != BattleState.InProgress) return false;
    if (!CombatDebugHandler.DebugMode && team != ActiveTeamId) return false;

    bool result = combat.TryUseAction(team, slot, ActionIndex);
    if (result)
    {
        if (combatAi != null) combatAi.OnActionUsed(team, slot, ActionIndex);
        _pendingBenchDeploy = true;
        if (combat.IsBattleOver()) CheckBattleOver();
    }
    return result;
}

public bool CanUseAction(int team, int slot, int ActionIndex)
{
    if (battleState != BattleState.InProgress) return false;
    if (!CombatDebugHandler.DebugMode && team != ActiveTeamId) return false;
    return combat.CanUseAction(team, slot, ActionIndex);
}

private bool TeamCanAct(int team)
{
    for (int slot = 0; slot < 4; slot++)
    {
        if (!IsUnitAlive(team, slot)) continue;
        UnitData unit = combat.GetUnit(team, slot);
        if (unit.actions == null) continue;
        for (int i = 0; i < unit.actions.Count; i++)
            if (combat.CanUseAction(team, slot, i)) return true;
    }

    if (CanAffordTeamPositionSwap(team))
        return true;

    if (CanDoAnyBenchSwap(team))
        return true;

    return false;
}

bool CanAffordTeamPositionSwap(int team)
{
    if (combat == null) return false;
    int livingCount = 0;
    for (int s = 0; s < 4; s++)
        if (IsUnitAlive(team, s)) livingCount++;
    if (livingCount < 2) return false;

    int cost = (combatAi != null && combatAi.weights != null)
             ? combatAi.weights.ai.teamPositionSwapAPCost
             : 2;
    return GetTeamAP(team) >= cost;
}

bool CanDoAnyBenchSwap(int team)
{
    if (combat == null) return false;
    if (!BenchHasLiving(team)) return false;

    int cost = (combatAi != null && combatAi.weights != null)
             ? combatAi.weights.ai.benchSwapAPCost
             : 4;
    if (GetTeamAP(team) < cost) return false;

    int[] slots = BenchSwapSlots(team);
    return slots != null && slots.Length > 0;
}

#endregion

#region AI suggestions

[System.Serializable]
public struct AISuggestion
{
    public int    unitSlot;
    public int    targetTeam;
    public int    targetSlot;
    public string targetName;
    public string description;
}

private void ProduceAISuggestions()
{
    currentSuggestions.Clear();
    if (combatAi == null) return;

    int enemyTeam = CombatScript.TeamEnemy;
    AiAction chosen = combatAi.DecideBestAction(enemyTeam);

    int tgtTeam = -1, tgtSlot = -1;
    string tgtName = "";
    if (chosen.effects != null)
    {
        for (int i = 0; i < chosen.effects.Count; i++)
        {
            var e = chosen.effects[i];
            if (e.team != enemyTeam) { tgtTeam = e.team; tgtSlot = e.slot; break; }
        }
    }
    if (tgtTeam >= 0) tgtName = combat.GetUnit(tgtTeam, tgtSlot).name;

    currentSuggestions.Add(new AISuggestion
    {
        unitSlot = chosen.casterSlot,
        targetTeam = tgtTeam,
        targetSlot = tgtSlot,
        targetName = tgtName,
        description = chosen.BuildDescription(),
    });
}

#endregion

#region Enemy AI auto-play

private IEnumerator RunAiTurn(int team)
{
    isAiRunning = true;
    TeamTurn myTurn = team == CombatScript.TeamPlayer ? TeamTurn.Player : TeamTurn.Enemy;

    var ai = combatAi;
    if (ai == null)
    {
        isAiRunning = false;
        aiRoutine = null;
        if (battleState == BattleState.InProgress && activeTurn == myTurn)
            EndTeamTurn();
        yield break;
    }

    if (ai.weights != null && _baselineWeights != ai.weights)
    {
        if (playerWeightsOverride == null && enemyWeightsOverride == null)
            _baselineWeights = ai.weights;
    }

    var weightsForTeam = GetWeightsForTeam(team);
    if (weightsForTeam != null)
        ai.weights = weightsForTeam;

    if (enemyAiActionDelay > 0f)
        yield return new WaitForSeconds(enemyAiActionDelay);
    else
        yield return null;

    int safetyCap = 64;
    int benchSwapCount = 0;
    const int maxBenchSwapsPerTurn = 2;
    while (battleState == BattleState.InProgress && activeTurn == myTurn && safetyCap-- > 0)
    {
        if (combat == null) break;
        if (combat.IsBattleOver()) break;
        if (ai == null) break;

        bool allowBenchSwaps = benchSwapCount < maxBenchSwapsPerTurn;
        AiAction action = ai.DecideBestAction(team, allowBenchSwaps);

        if (action.kind == AiActionKind.Idle) break;

        if (action.kind == AiActionKind.BenchSwap || action.kind == AiActionKind.MoveAndBenchSwap)
            benchSwapCount++;

        if (!TeamCanAct(team) && action.kind != AiActionKind.BenchSwap && action.kind != AiActionKind.MoveAndBenchSwap) break;

        yield return ExecuteAiAction(action);

        if (combat == null) break;
        if (combat.IsBattleOver()) break;

        if (enemyAiActionDelay > 0f) yield return new WaitForSeconds(enemyAiActionDelay);
    }

    isAiRunning = false;
    aiRoutine = null;

    // restore baseline not the override
    if (ai != null && _baselineWeights != null)
        ai.weights = _baselineWeights;

    if (battleState == BattleState.InProgress && activeTurn == myTurn)
        EndTeamTurn();
}

public IEnumerator ExecuteAiAction(AiAction action)
{
    if (combat == null) yield break;
    int team = action.team;

    switch (action.kind)
    {
        case AiActionKind.Idle:
            yield break;

        case AiActionKind.Move:
            RequestMove(team, action.casterSlot, action.moveDestination);
            yield return null;
            break;

        case AiActionKind.UseAction:
            {
                bool ok = RequestActionUse(team, action.casterSlot, action.ActionIndex);
                if (ok) yield return WaitForAction();
                break;
            }

        case AiActionKind.MoveAndUseAction:
            {
                bool moved = RequestMove(team, action.casterSlot, action.moveDestination);
                if (!moved) yield break;
                bool ok = RequestActionUse(team, action.moveDestination, action.ActionIndex);
                if (ok) yield return WaitForAction();
                break;
            }

        case AiActionKind.BenchSwap:
            RequestBenchSwap(team, action.casterSlot, action.benchIndex);
            yield return null;
            break;

        case AiActionKind.MoveAndBenchSwap:
            {
                bool moved = RequestMove(team, action.casterSlot, action.moveDestination);
                if (!moved) yield break;
                RequestBenchSwap(team, action.moveDestination, action.benchIndex);
                yield return null;
                break;
            }
    }
}

IEnumerator WaitForAction()
{
    yield return null;
    yield return null;
    int safety = 0;
    while (combat.IsExecutingAction && safety++ < 600)
        yield return null;

    // If we hit the safety cap, the Action coroutine likely hung.
    // Force-clear the flag so future actions aren't permanently blocked.
    if (combat.IsExecutingAction)
    {
        Debug.LogError($"[CombatRuntimeManager] WaitForAction timed out after 600 frames - force-clearing IsExecutingAction.");
        combat.ForceClearExecutingFlag();
    }
}

#endregion

#region Move + Bench swap APIs

public bool RequestMove(int team, int fromSlot, int toSlot)
{
    if (battleState != BattleState.InProgress) return false;
    if (combat == null) return false;
    if (!CombatDebugHandler.DebugMode && team != ActiveTeamId) return false;
    if (fromSlot < 0 || fromSlot > 3 || toSlot < 0 || toSlot > 3 || fromSlot == toSlot) return false;

    int cost = (combatAi != null && combatAi.weights != null) ? combatAi.weights.ai.teamPositionSwapAPCost : 2;

    if (cost > 0 && !combat.TrySpendAP(team, cost)) return false;

    SwapSlots(team, fromSlot, toSlot);
    if (combatAi != null) combatAi.OnSwapUsed(team);
    return true;
}

public bool RequestBenchSwap(int team, int slot, int benchIndex)
{
    if (battleState != BattleState.InProgress) return false;
    if (combat == null) return false;
    if (!CombatDebugHandler.DebugMode && team != ActiveTeamId) return false;

    int cost;
    bool slotAllowed;
    if (combatAi != null && combatAi.weights != null)
    {
        cost = combatAi.weights.ai.benchSwapAPCost;
        slotAllowed = combatAi.weights.ai.IsBenchSwapSlot(slot);
    }
    else
    {
        cost = 4;
        slotAllowed = IsBenchSwapSlot(team, slot);
    }
    if (!slotAllowed) return false;

    var bench = GetBench(team);
    if (benchIndex < 0 || benchIndex >= bench.Count) return false;
    if (!IsBenchUnitAlive(bench[benchIndex])) return false;

    if (cost > 0 && !combat.TrySpendAP(team, cost)) return false;

    bool deployed = DeployBenchUnit(team, benchIndex, slot);
    if (deployed && combatAi != null) combatAi.OnSwapUsed(team);
    return deployed;
}

void SwapSlots(int team, int slotA, int slotB)
{
    if (slotA == slotB) return;

    UnitData dataA = combat.GetUnit(team, slotA);
    UnitData dataB = combat.GetUnit(team, slotB);

    var visUnitA  = dataA.PlayerUnit;
    var visSpaceA = dataA.PlayerUnitSpace;
    var visUnitB  = dataB.PlayerUnit;
    var visSpaceB = dataB.PlayerUnitSpace;

    combat.SwapUnitsWithPassives(team, slotA, slotB);

    dataA = combat.GetUnit(team, slotA);
    dataA.PlayerUnit = visUnitA;
    dataA.PlayerUnitSpace = visSpaceA;
    combat.SetUnit(team, slotA, dataA);

    dataB = combat.GetUnit(team, slotB);
    dataB.PlayerUnit = visUnitB;
    dataB.PlayerUnitSpace = visSpaceB;
    combat.SetUnit(team, slotB, dataB);
}

public TeamWeightsBase GetTeamWeightsPublic(int team) => GetTeamWeights(team);

TeamWeightsBase GetTeamWeights(int team)
{
    if (combatAi == null || combatAi.weights == null) return null;
    // perspective-relative allies vs enemies
    int active = combatAi.ActivePerspectiveTeam;
    return team == active ? combatAi.weights.allies : combatAi.weights.enemies;
}

#endregion

#region AP

public float GetTeamAP(int team)
    => team == CombatScript.TeamPlayer ? combat.playerAPBank.current : combat.enemyAPBank.current;

public float GetTeamAPCap(int team) => combat.GetTeamAPCap(team);

#endregion

#region Helpers

public bool IsUnitAlive(int team, int slot)
{
    UnitData u = combat.GetUnit(team, slot);
    return !string.IsNullOrEmpty(u.name) && u.hp > 0;
}

private void CheckBattleOver()
{
    bool playerViable = combat.IsTeamViable(CombatScript.TeamPlayer);
    bool enemyViable  = combat.IsTeamViable(CombatScript.TeamEnemy);

    if (!playerViable || !enemyViable)
    {
        battleState = BattleState.Ended;
        activeTurn  = TeamTurn.None;
        if (aiRoutine != null) { StopCoroutine(aiRoutine); aiRoutine = null; }
        isAiRunning = false;

        try { combat.CleanupAllPassivesAndStatuses(); }
        catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] CleanupAllPassivesAndStatuses threw: {e}"); }

        string winner = !playerViable && !enemyViable ? "DRAW"
                      : !enemyViable                  ? "PLAYER"
                      :                                 "ENEMY";
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Battle] Match ended - winner: {winner}. All passives/statuses cleaned up for next match.");
    }
}

#endregion

#region Load / Unload / Snapshot

[System.Serializable]
public struct BattleSnapshot
{
    public UnitData[] playerUnits;
    public UnitData[] enemyUnits;
    public int        round;
    public TeamTurn   activeTurn;
    public float      playerAP;
    public float      enemyAP;
}

public BattleSnapshot CaptureSnapshot()
{
    var snap = new BattleSnapshot
    {
        playerUnits  = new UnitData[4],
        enemyUnits   = new UnitData[4],
        round        = currentRound,
        activeTurn   = activeTurn,
        playerAP = combat.playerAPBank.current,
        enemyAP  = combat.enemyAPBank.current
    };
    for (int s = 0; s < 4; s++)
    {
        snap.playerUnits[s] = DeepCloneUnit(combat.GetUnit(CombatScript.TeamPlayer, s));
        snap.enemyUnits[s]  = DeepCloneUnit(combat.GetUnit(CombatScript.TeamEnemy,  s));
    }
    return snap;
}

public BattleSnapshot CaptureCleanSnapshot()
{
    if (CustomStatusRuntime.Instance != null)
        CustomStatusRuntime.Instance.ClearAll();
    return CaptureSnapshot();
}

static UnitData DeepCloneUnit(UnitData u)
    {
        if (u.actions != null)
        {
            var act = new List<ActionData>(u.actions.Count);
            foreach (var a in u.actions) act.Add(DeepCloneAction(a));
            u.actions = act;
        }
        if (u.ActionUseCounts != null) { var uc = new List<int>(u.ActionUseCounts.Count); foreach (var c in u.ActionUseCounts) uc.Add(c); u.ActionUseCounts = uc; }
        if (u.passiveKeys != null) { var pk = new List<string>(u.passiveKeys.Count); foreach (var k in u.passiveKeys) pk.Add(k); u.passiveKeys = pk; }
        return u;
    }

    static ActionData DeepCloneAction(ActionData a)
    {
        a.physicalCoords         = CloneCasterPatterns(a.physicalCoords);
        a.fireCoords             = CloneCasterPatterns(a.fireCoords);
        a.bleedCoords            = CloneCasterPatterns(a.bleedCoords);
        a.magicCoords           = CloneCasterPatterns(a.magicCoords);
        a.poisonCoords           = CloneCasterPatterns(a.poisonCoords);
        a.recoverHealthCoords    = CloneCasterPatterns(a.recoverHealthCoords);
        a.addHealthCoords        = CloneCasterPatterns(a.addHealthCoords);
        a.addTempHealthCoords    = CloneCasterPatterns(a.addTempHealthCoords);
        a.recoverArmorCoords     = CloneCasterPatterns(a.recoverArmorCoords);
        a.addArmorCoords         = CloneCasterPatterns(a.addArmorCoords);
        a.recoverShieldCoords    = CloneCasterPatterns(a.recoverShieldCoords);
        a.addShieldCoords        = CloneCasterPatterns(a.addShieldCoords);
        a.stunCoords             = CloneCasterPatterns(a.stunCoords);
        a.sleepCoords            = CloneCasterPatterns(a.sleepCoords);
        a.confusionCoords        = CloneCasterPatterns(a.confusionCoords);
        a.petrificationCoords    = CloneCasterPatterns(a.petrificationCoords);
        return a;
    }

    static List<CombatScript.CasterPattern> CloneCasterPatterns(List<CombatScript.CasterPattern> src)
    {
        if (src == null) return null;
        var dst = new List<CombatScript.CasterPattern>(src.Count);
        for (int i = 0; i < src.Count; i++)
        {
            var p = src[i];
            p.targets = p.targets == null
                ? null
                : new List<CombatScript.TargetEntry>(p.targets);
            dst.Add(p);
        }
        return dst;
    }

[ContextMenu("ReloadFromSnapshot")]
public void TryReloadFromSnapshot()
{
    if (!hasSnapshot) { Debug.LogWarning("[CombatRuntimeManager] No snapshot to reload."); return; }
    ReloadFromSnapshot(lastSnapshot);
}

public void ReloadFromSnapshot(BattleSnapshot snap)
{
    if (combat == null) return;
    if (snap.playerUnits == null || snap.enemyUnits == null) return;

    for (int s = 0; s < 4; s++)
    {
        combat.SetUnit(CombatScript.TeamPlayer, s, snap.playerUnits[s]);
        combat.SetUnit(CombatScript.TeamEnemy,  s, snap.enemyUnits[s]);
    }

    combat.RecomputeAPCap();
    float pCap = combat.GetTeamAPCap(CombatScript.TeamPlayer);
    float eCap = combat.GetTeamAPCap(CombatScript.TeamEnemy);
    playerAPBank.current          = snap.playerAP;
    playerAPBank.startOfTurnTotal = pCap;
    enemyAPBank.current           = snap.enemyAP;
    enemyAPBank.startOfTurnTotal  = eCap;

    currentRound = snap.round;
    activeTurn   = snap.activeTurn;
    battleState  = BattleState.InProgress;

    combat.InitializePassives();
    currentSuggestions.Clear();
}

[ContextMenu("Unload Battle")]
public void UnloadBattle()
{
    if (combat == null) return;

    lastSnapshot = CaptureCleanSnapshot();
    hasSnapshot  = true;

    battleState  = BattleState.Idle;
    activeTurn   = TeamTurn.None;
    currentRound = 0;
    currentSuggestions.Clear();
    _pendingBenchDeploy = false;

    if (aiRoutine != null) { StopCoroutine(aiRoutine); aiRoutine = null; }
    isAiRunning = false;

    try { combat.CleanupAllPassivesAndStatuses(); }
    catch (System.Exception e) { Debug.LogError($"[CombatRuntimeManager] CleanupAllPassivesAndStatuses threw: {e}"); }

    for (int s = 0; s < 4; s++)
    {
        combat.SetUnit(CombatScript.TeamPlayer, s, default(UnitData));
        combat.SetUnit(CombatScript.TeamEnemy,  s, default(UnitData));
    }

    combat.InitializePassives();
    if (CustomStatusRuntime.Instance != null) CustomStatusRuntime.Instance.ClearAll();
    OverrideRegistry.ClearSceneHandlers();
    playerAPBank = default;
    enemyAPBank  = default;
    ClearAllBenches();
}

[ContextMenu("Reload From CSV")]
public void ReloadFromCSV()
{
    if (combatSetup != null) combatSetup.LoadAllUnits();
    hasSnapshot = false;
    lastSnapshot = default;
    InitBattle();
}

#endregion

}
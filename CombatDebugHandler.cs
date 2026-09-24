using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class CombatDebugHandler : MonoBehaviour
{

    #region Config

    [Header("Refs")]
    [Tooltip("Auto-found on Awake if not assigned.")]
    public CombatScript combat;

    [Tooltip("Combat AI used for the live score overlay (shift - F2). Auto-found on Awake if not assigned")]
    public CombatAi ai;

    [Tooltip("Combat runtime (owns the bench). Auto-found on Awake if not assigned.")]
    public CombatRuntimeManager runtime;

    [Header("Overlay")]
    public bool overlayOn = true;
    public bool overlayRichText = true;
    public int overlayFontSize = 12;
    public float overlayX = 10f;
    public float overlayY = 10f;
    public float overlayWidth = 600f;
    public float overlayHeight = 620f;
    public Color overlayText = new Color(1f, 1f, 1f, 0.95f);
    public Color overlayBackground = new Color(0.07f, 0.08f, 0.11f, 0.85f);

    [Header("AI Score Overlay (shift - F2)")]
    [Tooltip("World-space info cards float above each unit while on.")]
    public bool showAiScores = false;
    public bool[] showAiUnitScores = new bool[8]
    {
        true, true, true, true,
        true, true, true, true
    };
    [Tooltip("How often (seconds) the AI re-evaluates scores.")]
    public float aiEvalInterval = 0.2f;
    [Tooltip("World-space height (meters) above the unit's transform where the card sits.")]
    public float aiLabelWorldHeight = 1.5f;
    public float aiLabelWidth  = 210f;
    public float aiLabelHeight = 260f;
    public int   aiLabelFontSize  = 11;
    public int   aiHeaderFontSize = 13;
    public Color allyScoreColor  = new Color(0.55f, 0.85f, 1f, 1f);
    public Color enemyScoreColor = new Color(1f, 0.55f, 0.5f, 1f);
    public Color aiLabelBackground = new Color(0.05f, 0.06f, 0.09f, 0.82f);

    [Header("AI Action Evaluation Overlay (ctrl - F2)")]
    [Tooltip("Shows every evaluated action for the active team, grouped by category, with scores and the selected action's breakdown. Toggled with ctrl+F2.")]
    public bool showAiActionOverlay = true;
    [Tooltip("Show the full score breakdown for the selected action.")]
    public bool showBreakdownForSelected = true;
    [Tooltip("Show the bench contents for each team under its team block.")]
    public bool showBenchInOverlay = true;
    public float aiActionOverlayX = 620f;
    public float aiActionOverlayY = 10f;
    public float aiActionOverlayWidth = 580f;
    public float aiActionOverlayHeight = 820f;
    public int   aiActionFontSize = 12;
    public Color aiActionSelectedColor = new Color(0.55f, 1f, 0.55f, 1f);
    public Color aiActionCategoryColor = new Color(1f, 0.85f, 0.4f, 1f);
    public Color aiActionBackground = new Color(0.05f, 0.06f, 0.09f, 0.82f);

    [Header("Main Overlay - Layout")]
    [Tooltip("Scroll the main overlay content when it overflows the panel height.")]
    public bool overlayScroll = true;

    #endregion


    #region Debug State

    [Tooltip("Verbose combat logging. Toggled with F1.")]
    public static bool UltraDebug = false;

    // Cached Camera reference to avoid deprecated Camera.main scene scans.
    private Camera cachedCamera;

    [Tooltip("Player team takes no damage. Toggled with F6 (also toggles debugMode).")]
    public static bool GodModePlayer = false;

    [Tooltip("Bypass active-team/turn checks on all Request* APIs (actions, move, bench-swap on either team's turn). Toggled alongside GodMode via F6, or on its own via the context menu.")]
    [SerializeField] private bool debugMode = false;

    [Tooltip("Suppress AI debug overlays (action evaluation + AI score labels) and ignore their toggle keys. Set when the build is ready for playing. Toggled via shift+F6, and forced on by CombatRuntimeManager.InitBattle.")]
    [SerializeField] private bool readyForPlay = false;

    public static CombatDebugHandler Instance { get; private set; }

    public static bool DebugMode => Instance != null && Instance.debugMode;
    public static bool ReadyForPlay => Instance != null && Instance.readyForPlay;

    public static void SetDebugMode(bool v)    { if (Instance != null) Instance.debugMode    = v; }
    public static void SetReadyForPlay(bool v) { if (Instance != null) Instance.readyForPlay = v; }

    #endregion

    private GUIStyle _labelStyle;
    private GUIStyle _boxStyle;
    private Texture2D _bgTex;

    // AI overlay styles / cache
    private GUIStyle _aiLabelStyle;
    private GUIStyle _aiHeaderStyle;
    private Texture2D _aiBgTex;
    private float _aiEvalTimer = -1f;

    // AI action-panel styles / cache
    private GUIStyle _aiActionStyle;
    private GUIStyle _aiActionBoxStyle;
    private Texture2D _aiActionBgTex;
    private string _aiActionCache;
    private float _aiActionCacheTimer = -1f;
    private Vector2 _aiActionScrollPos;

    // Main-overlay interactive styles
    private GUIStyle _titleStyle;
    private GUIStyle _sectionHeaderStyle;
    private GUIStyle _smallButtonStyle;
    private GUIStyle _hintStyle;
    private GUIStyle _dividerStyle;
    private Texture2D _pillTex;
    private Vector2 _overlayScrollPos;

    private float _fps;
    private float _buttonRowWidth;
    private float _buttonX;

    #region Unity Lifecycle

    void Awake()
    {
        Instance = this;

        if (combat == null)
        {
            combat = FindFirstObjectByType<CombatScript>();
        }
        if (combat == null)
            Debug.LogError("[CombatDebugHandler] No CombatScript found in scene. Handler disabled.");

        if (ai == null)
        {
            ai = FindFirstObjectByType<CombatAi>();
        }
        if (ai == null)
            Debug.LogWarning("[CombatDebugHandler] No CombatAi found in scene. F2 live-score overlay will be unavailable until one is added.");

        if (runtime == null)
        {
            runtime = FindFirstObjectByType<CombatRuntimeManager>();
        }
        if (runtime == null)
            Debug.LogWarning("[CombatDebugHandler] No CombatRuntimeManager found in scene. Bench debug actions will be unavailable.");
    }

    void OnDestroy()
    {
        if (_bgTex != null) { Destroy(_bgTex); _bgTex = null; }
        if (_aiBgTex != null) { Destroy(_aiBgTex); _aiBgTex = null; }
        if (_aiActionBgTex != null) { Destroy(_aiActionBgTex); _aiActionBgTex = null; }
        if (_pillTex != null) { Destroy(_pillTex); _pillTex = null; }
        if (_dividerStyle != null && _dividerStyle.normal.background != null)
        {
            Destroy(_dividerStyle.normal.background);
            _dividerStyle.normal.background = null;
        }
        if (Instance == this) Instance = null;
    }

    // -----------------------------------------------------------------------
    // Keyboard shortcuts
    // -----------------------------------------------------------------------
    void Update()
    {
        if (combat == null) return;

        _fps = Mathf.Lerp(_fps, Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : _fps, 0.15f);

        if (Input.GetKeyDown(KeyCode.F1))
        {
            ToggleUltraDebug();
        }

        if (Input.GetKeyDown(KeyCode.F2))
        {
            bool shift =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);

            bool ctrl =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);

            bool alt =
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt);

            // ReadyForPlay suppresses all AI debug overlays + their toggles.
            if (!alt && !readyForPlay)
            {
                if (ctrl)
                {
                    ToggleAiActionOverlay();
                    Log($"AI Action Evaluation Overlay = {showAiActionOverlay}");
                }
                else if (shift)
                {
                    showAiScores = !showAiScores;
                    Log($"AI Score Overlay = {showAiScores}");
                }
                else
                {
                    overlayOn = !overlayOn;
                    Log($"Overlay = {overlayOn}");
                }
            }
            else if (!alt && readyForPlay)
            {
                // In ReadyForPlay mode only the basic overlay toggles; AI overlays stay hidden.
                if (!ctrl && !shift)
                {
                    overlayOn = !overlayOn;
                    Log($"Overlay = {overlayOn} (ReadyForPlay: AI overlays hidden)");
                }
            }
        }

        if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
            Input.GetKey(KeyCode.F2))
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) ToggleAiUnitScore(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) ToggleAiUnitScore(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) ToggleAiUnitScore(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) ToggleAiUnitScore(3);
            if (Input.GetKeyDown(KeyCode.Alpha5)) ToggleAiUnitScore(4);
            if (Input.GetKeyDown(KeyCode.Alpha6)) ToggleAiUnitScore(5);
            if (Input.GetKeyDown(KeyCode.Alpha7)) ToggleAiUnitScore(6);
            if (Input.GetKeyDown(KeyCode.Alpha8)) ToggleAiUnitScore(7);
        }

        if (Input.GetKeyDown(KeyCode.F3))
            DumpFullState();

#if UNITY_EDITOR || DEBUG_BUILD
        // cheat action keys gated to dev builds
        if (Input.GetKeyDown(KeyCode.F4))
        {
            if (combat.IsExecutingAction)
                Log("blocked - Action in flight");
            else
                ForceStartRound();
        }

        if (Input.GetKeyDown(KeyCode.F5))
        {
            if (combat.IsExecutingAction)
                Log("blocked - Action in flight");
            else
                ForceEndOfTurn();
        }

        if (Input.GetKeyDown(KeyCode.F6))
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift)
            {
                readyForPlay = !readyForPlay;
                if (readyForPlay) { showAiScores = false; showAiActionOverlay = false; }
                Log($"ReadyForPlay = {readyForPlay} (AI debug overlays {(readyForPlay ? "hidden" : "enabled")})");
            }
            else
            {
                ToggleGodMode();
            }
        }

        if (Input.GetKeyDown(KeyCode.F7))
            ResetActions();

        if (Input.GetKeyDown(KeyCode.F8))
            HealEveryone();

        if (Input.GetKeyDown(KeyCode.F9))
            DamageEnemies();

        if (Input.GetKeyDown(KeyCode.F10))
            EnemyBasicAttack();

        if (Input.GetKeyDown(KeyCode.F11))
            DamagePlayers();

        if (Input.GetKeyDown(KeyCode.F12))
            ForceSelectedAction();
#endif
    }
    // -----------------------------------------------------------------------
    // On-screen overlay
    // -----------------------------------------------------------------------
    void OnGUI()
    {
        if (combat == null) return;

        bool aiScoresAllowed = showAiScores && ai != null;
        bool aiActionsAllowed = showAiActionOverlay && ai != null;

        if ((overlayOn || aiScoresAllowed || aiActionsAllowed) && ai != null)
            RefreshAiEvaluation();

        if (aiScoresAllowed)
            DrawAiScoreOverlay();

        if (aiActionsAllowed)
            DrawAiActionOverlay();

        if (!overlayOn) return;

        EnsureStyles();

        Rect area = new Rect(overlayX, overlayY, overlayWidth, overlayHeight);
        GUI.Box(area, GUIContent.none, _boxStyle);

        Rect inner = new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f);
        GUILayout.BeginArea(inner);

        bool scrollThisFrame = overlayScroll;

        try
        {
            if (scrollThisFrame)
            {
                _overlayScrollPos = GUILayout.BeginScrollView(
                    _overlayScrollPos,
                    GUILayout.Width(inner.width),
                    GUILayout.Height(inner.height));
            }

            float rowWidth = inner.width - 18f; // leave room for the scrollbar

            DrawHeaderRow();
            GUILayout.Space(3f);
            GUILayout.Label("", _dividerStyle, GUILayout.Height(2f));
            GUILayout.Space(3f);

            DrawToggleButtons(rowWidth);
            DrawActionButtonRow(rowWidth);
            DrawUnitScoreToggles(rowWidth);

            GUILayout.Space(4f);
            GUILayout.Label("", _dividerStyle, GUILayout.Height(2f));
            GUILayout.Space(2f);

            DrawTeamSectionFlat("PLAYER", CombatScript.TeamPlayer, new Color(0.45f, 0.85f, 1f, 1f));
            GUILayout.Space(3f);
            DrawTeamSectionFlat("ENEMY", CombatScript.TeamEnemy, new Color(1f, 0.55f, 0.5f, 1f));
        }
        finally
        {
            if (scrollThisFrame)
                GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }

    void DrawHeaderRow()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>COMBAT DEBUG</b>", _titleStyle);
        GUILayout.FlexibleSpace();
        if (ai != null && ai.lastActionTeam >= 0)
        {
            string turnLabel = ai.lastActionTeam == CombatScript.TeamPlayer ? "PLAYER" : "ENEMY";
            GUILayout.Label($"<color=#888>turn</color> <b>{turnLabel}</b>", _hintStyle);
            GUILayout.Space(10f);
        }
        if (!Mathf.Approximately(Time.timeScale, 1f))
        {
            GUILayout.Label($"<color=#ffcc66>×{Time.timeScale:0.##}</color>", _hintStyle);
            GUILayout.Space(10f);
        }
        GUILayout.Label($"<color=#777>{_fps:0} fps</color>", _hintStyle);
        GUILayout.EndHorizontal();
    }

    void DrawToggleButtons(float rowWidth)
    {
        GUILayout.Label("<color=#888>FLAGS</color>", _hintStyle);
        Beginbutton(rowWidth);
        buttonToggle("Ultra",   () => UltraDebug,          v => UltraDebug = v);
        buttonToggle("God",     () => GodModePlayer,       v => GodModePlayer = v);
        buttonToggle("Debug",   () => debugMode,           v => debugMode = v);
        buttonToggle("Ready",   () => readyForPlay,        v => { readyForPlay = v; if (v) { showAiScores = false; showAiActionOverlay = false; } });
        buttonToggle("Scores",  () => showAiScores,        v => showAiScores = v);
        buttonToggle("Actions", () => showAiActionOverlay, v => showAiActionOverlay = v);
        buttonToggle("Bench",   () => showBenchInOverlay,  v => showBenchInOverlay = v);
        buttonToggle("Scroll",  () => overlayScroll,       v => overlayScroll = v);
        Endbutton();
        GUILayout.Space(3f);
    }

    void DrawActionButtonRow(float rowWidth)
    {
        GUILayout.Label("<color=#888>ACTIONS</color>", _hintStyle);
        Beginbutton(rowWidth);
        buttonAction("F3 Dump",       DumpFullState);
        buttonAction("F4 Round",      ForceStartRound);
        buttonAction("F5 EndTurn",    ForceEndOfTurn);
        buttonAction("F7 Reset",      ResetActions);
        buttonAction("F8 Heal",       HealEveryone);
        buttonAction("F9 DmgEnemy",   DamageEnemies);
        buttonAction("F10 EnemyAtk",  EnemyBasicAttack);
        buttonAction("F11 DmgPlayer", DamagePlayers);
        buttonAction("F12 PlyAtk",    ForceSelectedAction);
        Endbutton();
        GUILayout.Space(3f);
    }

    void DrawUnitScoreToggles(float rowWidth)
    {
        GUILayout.Label("<color=#888>PER-UNIT CARDS (shift+F2 · alt+F2+1-8 to pick)</color>", _hintStyle);
        Beginbutton(rowWidth);
        for (int i = 0; i < 8; i++)
        {
            int idx = i;
            string label = idx < 4 ? $"A{idx + 1}" : $"E{idx - 3}";
            buttonToggle(label, () => showAiUnitScores[idx], v => showAiUnitScores[idx] = v);
        }
        Endbutton();
        GUILayout.Space(3f);
    }

    void DrawSectionHeaderBar(string label, Color tint, int team)
    {
        int living = 0;
        for (int s = 0; s < 4; s++)
            if (!string.IsNullOrEmpty(combat.GetUnit(team, s).name) && combat.GetUnit(team, s).hp > 0) living++;

        string viAction = combat.IsTeamViable(team) ? "VIABLE" : "DOWN";
        string vHex = combat.IsTeamViable(team) ? "8effa0" : "ff6b6b";
        int benchLiving = runtime != null ? runtime.LivingBenchCount(team) : 0;

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(tint.r, tint.g, tint.b, 0.32f);
        GUILayout.Box(
            $"<b>{label}</b>  <color=#ccc>field {living}/4 · bench {benchLiving}</color>  <color=#{vHex}>{viAction}</color>",
            _sectionHeaderStyle, GUILayout.Height(20f));
        GUI.backgroundColor = prevBg;

        CombatScript.APBank bank = team == CombatScript.TeamPlayer
            ? combat.playerAPBank
            : combat.enemyAPBank;
        GUILayout.Label($"    AP: <b>{bank.current:0}/{bank.startOfTurnTotal:0}</b>{AiTeamTotalLine(team)}", _labelStyle);
    }

    void DrawTeamSectionFlat(string label, int team, Color titleColor)
    {
        DrawSectionHeaderBar(label, titleColor, team);

        Dictionary<int, CombatAi.UnitStrengthEntry> scoreBySlot = null;
        if (ai != null)
        {
            var src = (team == ai.ActivePerspectiveTeam) ? ai.lastActiveStrength : ai.lastOppositionStrength;
            scoreBySlot = new Dictionary<int, CombatAi.UnitStrengthEntry>(src.Count);
            for (int i = 0; i < src.Count; i++)
                scoreBySlot[src[i].slot] = src[i];
        }

        for (int s = 0; s < 4; s++)
        {
            var u = combat.GetUnit(team, s);
            if (string.IsNullOrEmpty(u.name))
            {
                GUILayout.Label($"  [{s}] <color=#555>- empty -</color>", _labelStyle);
                continue;
            }

            string hpColor = u.hp <= 0 ? "red" : (u.hp < u.maxHp * 0.5f ? "yellow" : "white");
            string hpStr = u.hp <= 0 ? "<color=red>DEAD</color>" : $"<color={hpColor}>{u.hp:0}/{u.maxHp:0}</color>";
            string hpBar = MiniBar(SafeDiv(u.hp, u.maxHp), 6, HpBarHex(u));

            string aiStr = "";
            if (scoreBySlot != null && scoreBySlot.TryGetValue(s, out var e))
                aiStr = $"  <color=#8effa0>AI {e.strength:0.00}</color>";

            GUILayout.Label(
                $"  [{s}] <b>{EscapeRich(u.name)}</b> {hpBar} HP {hpStr}  AC {u.ac:0}  AR {u.armor:0}/{u.armorBlock:0}  SH {u.shield:0}  TH {u.th:0}  AP {u.ap:0}{aiStr}",
                _labelStyle);

            var status = BuildStatusSummary(u, team, s);
            if (!string.IsNullOrEmpty(status))
                GUILayout.Label($"       <color=#ffcc66>{status}</color>", _labelStyle);

            if (u.actions != null && u.actions.Count > 0)
                GUILayout.Label($"       <color=#999>{BuildActionsLine(u)}</color>", _hintStyle);
        }

        DrawBenchFlat(team);
        GUILayout.Space(2f);
    }

    string BuildActionsLine(CombatScript.UnitData u)
    {
        var act = new StringBuilder();
        for (int i = 0; i < u.actions.Count; i++)
        {
            int used = (u.ActionUseCounts != null && i < u.ActionUseCounts.Count) ? u.ActionUseCounts[i] : 0;
            int limit = u.actions[i].useLimit;
            string usedStr = limit > 0 ? $"{used}/{limit}" : $"{used}";
            act.Append($"{u.actions[i].ActionName}({usedStr})");
            if (i < u.actions.Count - 1) act.Append(", ");
        }
        return act.ToString();
    }

    void DrawBenchFlat(int team)
    {
        if (runtime == null) return;
        if (!showBenchInOverlay) return;
        var bench = runtime.GetBench(team);
        int living = runtime.LivingBenchCount(team);

        GUILayout.Label($"    <color=#aaa>BENCH ({bench.Count}, {living} living):</color>", _labelStyle);
        if (bench.Count == 0)
        {
            GUILayout.Label($"          <color=#666>- empty -</color>", _labelStyle);
        }
        else
        {
            for (int i = 0; i < bench.Count; i++)
            {
                var b = bench[i];
                if (string.IsNullOrEmpty(b.name))
                {
                    GUILayout.Label($"          [{i}] <color=#666>- empty -</color>", _labelStyle);
                    continue;
                }
                string hpStr = b.hp <= 0 ? "<color=red>DEAD</color>" : $"<color=white>{b.hp:0}/{b.maxHp:0}</color>";
                string hpBar = MiniBar(SafeDiv(b.hp, b.maxHp), 6, HpBarHex(b));
                GUILayout.Label($"          [{i}] {EscapeRich(b.name)} {hpBar} HP {hpStr}  AR {b.armor:0}  SH {b.shield:0}  AP {b.ap:0}  <color=#888>{b.unitcode}</color>", _labelStyle);
            }
        }
        bool viable = combat.IsTeamViable(team);
        string vHex = ColorToHex(viable ? new Color(0.55f, 1f, 0.55f, 1f) : new Color(1f, 0.4f, 0.4f, 1f));
        GUILayout.Label($"          <color=#{vHex}>viable={viable}</color>", _labelStyle);
    }

    #endregion


    #region AI Action Evaluation Overlay

    [ContextMenu("Toggle AI Action Overlay (ctrl+F2)")]
    public void ToggleAiActionOverlay()
    {
        showAiActionOverlay = !showAiActionOverlay;
        Log($"AI Action Evaluation Overlay = {showAiActionOverlay}");
    }

    [ContextMenu("Evaluate AI Actions (active team)")]
    public void EvaluateAiActionsNow()
    {
        if (ai == null) return;
        int team = ai.ActivePerspectiveTeam;
        ai.DecideBestAction(team);
        _aiActionCache = null; // force refresh
    }

    void RefreshAiEvaluationForActions()
    {
        if (ai == null) return;
        if (ai.weights == null) return;

        if (runtime != null && runtime.IsAiRunning) return;

        if (_aiEvalTimer < 0f || Time.unscaledTime - _aiEvalTimer >= aiEvalInterval)
        {
            int team = ai.ActivePerspectiveTeam;
            ai.DecideBestAction(team);
            _aiEvalTimer = Time.unscaledTime;
            _aiActionCache = null; // invalidate cache
        }
    }

    void DrawAiActionOverlay()
    {
        if (combat == null) return;
        RefreshAiEvaluationForActions();

        EnsureAiActionStyles();

        if (_aiActionCache == null || Time.unscaledTime - _aiActionCacheTimer > 0.25f)
        {
            _aiActionCache = BuildAiActionPanelText();
            _aiActionCacheTimer = Time.unscaledTime;
        }

        Rect area = new Rect(aiActionOverlayX, aiActionOverlayY, aiActionOverlayWidth, aiActionOverlayHeight);
        GUI.Box(area, GUIContent.none, _aiActionBoxStyle);

        Rect inner = new Rect(area.x + 6f, area.y + 6f, area.width - 12f, area.height - 12f);
        GUILayout.BeginArea(inner);
        try
        {
            _aiActionScrollPos = GUILayout.BeginScrollView(
                _aiActionScrollPos,
                GUILayout.Width(inner.width),
                GUILayout.Height(inner.height));
            GUILayout.Label(_aiActionCache, _aiActionStyle);
            GUILayout.EndScrollView();
        }
        finally
        {
            GUILayout.EndArea();
        }
    }

    string BuildAiActionPanelText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<b>═══ AI ACTION EVALUATION ═══</b>");

        int team = ai != null ? ai.lastActionTeam : -1;
        string teamLabel = team == CombatScript.TeamPlayer ? "PLAYER" : (team == CombatScript.TeamEnemy ? "ENEMY" : "-");

        if (team >= 0)
            sb.AppendLine($"Team: <color=#{ColorToHex(aiActionCategoryColor)}><b>{teamLabel}</b></color>  " +
                          $"AP: <b>{combat.GetAPBank(team).current:0}/{combat.GetTeamAPCap(team):0}</b>");
        else
            sb.AppendLine($"Team: <color=#888>-</color>  AP: <color=#888>-</color>");

        if (ai == null || !ai.hasActionEvaluation || ai.lastActionEvaluation == null || ai.lastActionEvaluation.Count == 0)
        {
            sb.AppendLine("<color=#888>No evaluation yet.</color>");
            return sb.ToString();
        }

        var actions = ai.lastActionEvaluation;
        int sel = ai.lastChosenActionIndex;
        var chosen = ai.lastChosenAction;

        sb.AppendLine($"Actions evaluated: <b>{actions.Count}</b>  Fatigue: <color=#{ColorToHex((ai != null && ai.weights != null && ai.weights.ai.preferVariedActions) ? Color.green : Color.gray)}>{(ai != null && ai.weights != null && ai.weights.ai.preferVariedActions ? "ON" : "OFF")}</color>");

        var byCategory = new Dictionary<string, List<int>>();
        var order = new List<string>();
        for (int i = 0; i < actions.Count; i++)
        {
            string cat = actions[i].Category;
            if (!byCategory.ContainsKey(cat)) { byCategory[cat] = new List<int>(); order.Add(cat); }
            byCategory[cat].Add(i);
        }

        int maxPerCategory = 6;

        foreach (string cat in order)
        {
            var indices = byCategory[cat];
            indices.Sort((a, b) => actions[b].Score.CompareTo(actions[a].Score));
            sb.AppendLine();
            sb.AppendLine($"<color=#{ColorToHex(aiActionCategoryColor)}><b>{cat}</b></color> <color=#888>({indices.Count})</color>");
            int shown = Mathf.Min(indices.Count, maxPerCategory);
            for (int k = 0; k < shown; k++)
            {
                int i = indices[k];
                var a = actions[i];
                bool isSel = (i == sel);
                string marker = isSel ? "<color=#88ff88>►</color>" : " ";
                string scoreColor = ScoreColorHex(a.Score);
                string casterName = GetCasterShortName(a);
                string desc = EscapeRich(a.BuildDescription());
                string effects = BuildEffectSummary(a);
                string cost = BuildCostString(a);
                string fatigue = BuildFatigueString(a);
                sb.AppendLine($" {marker} <color=#cccccc>[{casterName}]</color> {desc}");
                if (!string.IsNullOrEmpty(effects))
                    sb.AppendLine($"     <color=#aaa>{effects}</color>");
                sb.AppendLine($"     score=<color=#{scoreColor}><b>{a.Score:0.0}</b></color>{cost}{fatigue}");
            }
            if (indices.Count > maxPerCategory)
                sb.AppendLine($"   <color=#666>+{indices.Count - maxPerCategory} more...</color>");
        }

        sb.AppendLine();
        sb.AppendLine(new string('=', 48));
        if (sel >= 0 && sel < actions.Count)
        {
            sb.AppendLine($"<color=#{ColorToHex(aiActionSelectedColor)}><b>► SELECTED</b></color>");
            string selCaster = GetCasterShortName(chosen);
            sb.AppendLine($"<color=#{ColorToHex(aiActionSelectedColor)}><b>[{selCaster}] {EscapeRich(chosen.BuildDescription())}</b></color>");
            sb.AppendLine($"  Score: <color=#{ScoreColorHex(chosen.Score)}><b>{chosen.Score:0.0}</b></color>  Cost: <color=#ffaa66>{ActionCostValue(chosen)}</color>");
            if (showBreakdownForSelected)
            {
                sb.AppendLine();
                sb.AppendLine(chosen.breakdown.BuildBreakdownString());
            }
        }
        else
        {
            sb.AppendLine("<color=#888>No selected action.</color>");
        }

        return sb.ToString();
    }

    TeamWeightsBase GetWeights(int team)
    {
        if (ai == null || ai.weights == null) return null;
        return team == CombatScript.TeamPlayer ? ai.weights.allies : ai.weights.enemies;
    }

    AiSettings GetAiSettings()
    {
        return ai != null && ai.weights != null ? ai.weights.ai : null;
    }

    string GetCasterShortName(AiAction a)
    {
        if (combat == null || a.casterSlot < 0) return "-";
        var u = combat.GetUnit(a.team, a.casterSlot);
        if (string.IsNullOrEmpty(u.name)) return "slot" + a.casterSlot;
        return Trunc(u.name, 8) + ":" + a.casterSlot;
    }

    string BuildEffectSummary(AiAction a)
    {
        if (a.effects == null || a.effects.Count == 0) return "";
        var parts = new List<string>();
        for (int i = 0; i < a.effects.Count; i++)
        {
            var e = a.effects[i];
            string who = e.team == CombatScript.TeamPlayer ? $"A{e.slot}" : $"E{e.slot}";
            if (AiAction.IsDamage(e.kind))
            {
                string kill = e.wouldKill ? " <color=#ff4444>KILL</color>" : "";
                parts.Add($"{e.expectedAmount:0.0} dmg → {who} ({e.expectedHitChance*100:0}% hit){kill}");
            }
            else if (AiAction.IsHeal(e.kind))
                parts.Add($"+{e.expectedAmount:0.0} hp → {who}");
            else if (AiAction.IsBuff(e.kind))
                parts.Add($"+{e.expectedAmount:0.0} buf → {who}");
            else if (AiAction.IsDebuff(e.kind))
                parts.Add($"{e.expectedAmount:0.0} turns → {who}");
        }
        return string.Join("  ·  ", parts);
    }

    string BuildCostString(AiAction a)
    {
        int cost = ActionCostValue(a);
        if (cost <= 0) return "  <color=#888>free</color>";
        return $"  cost=<color=#ffaa66>{cost}</color>";
    }

    int ActionCostValue(AiAction a)
    {
        var gs = GetAiSettings();
        if (gs == null) return 0;
        switch (a.kind)
        {
            case AiActionKind.UseAction:       return a.action.APCost;
            case AiActionKind.MoveAndUseAction: return gs.teamPositionSwapAPCost + a.action.APCost;
            case AiActionKind.Move:             return gs.teamPositionSwapAPCost;
            case AiActionKind.BenchSwap:         return gs.benchSwapAPCost;
            case AiActionKind.MoveAndBenchSwap:  return gs.teamPositionSwapAPCost + gs.benchSwapAPCost;
            default:                             return 0;        
        }
    }

    string BuildFatigueString(AiAction a)
    {
        if (a.breakdown.fatigueScore < 0f)
            return $"  <color=#ff6666>fatigue {a.breakdown.fatigueScore:0.0}</color>";
        return "";
    }

    string ScoreColorHex(float score)
    {
        if (score > 40f) return "88ff88";
        if (score > 20f) return "ccff88";
        if (score > 5f)  return "ffcc66";
        if (score > 0f)  return "cc8844";
        return "666666";
    }

    #endregion


    #region Debug Actions

    void ToggleAiUnitScore(int index)
    {
        if (index < 0 || index >= showAiUnitScores.Length)
            return;

        showAiUnitScores[index] = !showAiUnitScores[index];

        Log($"AI unit overlay {index + 1} = {showAiUnitScores[index]}");
    }

    [ContextMenu("Force StartRound (F4)")]
    public void ForceStartRound()
    {
        if (!combat) return;
        if (combat.IsExecutingAction) { Log("blocked - Action in flight"); return; }
        // Use the cached class field instead of a redundant scene scan.
        if (runtime == null) runtime = FindFirstObjectByType<CombatRuntimeManager>();
        if (runtime != null)
        {
            runtime.StartRound();
            Log("Runtime StartRound() forced.");
        }
        else
        {
            Log("No CombatRuntimeManager in scene - cannot force round start.");
        }
    }

    [ContextMenu("Force ProcessEndOfTurn (F5)")]
    public void ForceEndOfTurn()
    {
        if (!combat) return;
        if (combat.IsExecutingAction) { Log("blocked - Action in flight"); return; }
        combat.ProcessEndOfRound();
        Log("ProcessEndOfRound() forced.");
    }

    [ContextMenu("Toggle God Mode + Debug Mode (F6)")]
    public void ToggleGodMode()
    {
        GodModePlayer = !GodModePlayer;
        debugMode = GodModePlayer;
        Log($"GodMode = {GodModePlayer}  DebugMode = {debugMode}");
    }

    [ContextMenu("Toggle ReadyForPlay (shift+F6)")]
    public void ToggleReadyForPlay()
    {
        readyForPlay = !readyForPlay;
        if (readyForPlay) { showAiScores = false; showAiActionOverlay = false; }
        Log($"ReadyForPlay = {readyForPlay} (AI debug overlays {(readyForPlay ? "hidden" : "enabled")})");
    }

    [ContextMenu("Toggle Debug Mode (bypass turn checks)")]
    public void ToggleDebugMode()
    {
        debugMode = !debugMode;
        Log($"DebugMode = {debugMode} (all Request* APIs bypass active-team gate)");
    }

    [ContextMenu("Toggle UltraDebug (F1)")]
    public void ToggleUltraDebug() { UltraDebug = !UltraDebug; Log($"UltraDebug = {UltraDebug}"); }

    [ContextMenu("Toggle AI Score Overlay (Shift+F2)")]
    public void ToggleAiScoreOverlay() { showAiScores = !showAiScores; Log($"AI Score overlay = {showAiScores}"); }

    [ContextMenu("Reset Action Counts (F7)")]
    public void ResetActions()
    {
        if (!combat) return;
        combat.ResetActionCounts();
        Log("Action use counts reset.");
    }

    [ContextMenu("Heal Everyone (F8)")]
    public void HealEveryone()
    {
        HealEveryone(true);
        Log("Both teams healed to full.");

        // Use the cached class field instead of a redundant scene scan.
        if (runtime == null) runtime = FindFirstObjectByType<CombatRuntimeManager>();
        if (runtime != null && runtime.State == CombatRuntimeManager.BattleState.Ended)
        {
            runtime.ResumeBattle();
            Log("Battle resumed (was ended).");
        }
    }

    [ContextMenu("Damage All Enemies 50 (F9)")]
    public void DamageEnemies() { DamageTeam(CombatScript.TeamEnemy, 50); Log("All enemies took 50 damage."); }

    [ContextMenu("Damage All Players 50 (F11)")]
    public void DamagePlayers() { DamageTeam(CombatScript.TeamPlayer, 50); Log("All player units took 50 damage."); }

    [ContextMenu("Enemy slot 0 basic attack (F10)")]
    public void EnemyBasicAttack()
    {
        if (!combat) return;
        bool ok = combat.TryUseAction(CombatScript.TeamEnemy, 0, 0);
        Log($"Enemy slot 0 basic attack: {(ok ? "fired" : "blocked")}");
    }

    [ContextMenu("Player slot 0 basic attack (F12)")]
    public void ForceSelectedAction()
    {
        if (!combat) return;
        bool ok = combat.TryUseAction(CombatScript.TeamPlayer, 0, 0);
        Log($"Player slot 0 basic attack: {(ok ? "fired" : "blocked")}");
    }

    [ContextMenu("Bench: Clone Player slot 2 -> bench")]
    public void BenchClonePlayerSlot2()
    {
        if (runtime == null) { Log("no CombatRuntimeManager"); return; }
        runtime.DebugCloneToBench(CombatScript.TeamPlayer, 2);
        Log("Cloned player slot 2 -> player bench.");
    }

    [ContextMenu("Bench: Clone Enemy slot 2 -> bench")]
    public void BenchCloneEnemySlot2()
    {
        if (runtime == null) { Log("no CombatRuntimeManager"); return; }
        runtime.DebugCloneToBench(CombatScript.TeamEnemy, 2);
        Log("Cloned enemy slot 2 -> enemy bench.");
    }

    [ContextMenu("Bench: Force swap Player slot2 <-> bench[0]")]
    public void BenchForceSwapPlayer()
    {
        if (runtime == null) { Log("no CombatRuntimeManager"); return; }
        bool ok = runtime.RequestBenchSwap(CombatScript.TeamPlayer, 2, 0);
        Log($"Player bench swap slot2<->bench[0]: {(ok ? "ok" : "blocked")}");
    }

    #endregion

    [ContextMenu("Kill All Enemies")]
    public void KillEnemies()
    {
        DamageTeam(CombatScript.TeamEnemy, 999999);
        Log("All enemies force-killed.");
    }

    [ContextMenu("Kill All Players")]
    public void KillPlayers()
    {
        DamageTeam(CombatScript.TeamPlayer, 999999);
        Log("All player units force-killed.");
    }

    #region Team Manipulation Helpers

    void HealEveryone(bool clearStatuses)
    {
        if (combat == null) return;
        for (int team = 0; team < 2; team++)
        {
            for (int s = 0; s < 4; s++)
            {
                var u = combat.GetUnit(team, s);
                if (string.IsNullOrEmpty(u.name)) continue;
                u.hp = u.maxHp;
                u.shield = u.maxShield;
                u.armor = u.maxArmor;
                u.th = 0;
                if (clearStatuses)
                {
                    u.fireRemainingRounds = u.bleedRemainingRounds = u.poisonRemainingRounds = 0;
                    u.fireDamageNotation = u.bleedDamageNotation = u.poisonDamageNotation = "";
                    u.stunRemainingRounds = u.sleepRemainingRounds = u.confusionRemainingRounds = u.petrificationRemainingRounds = 0;
                    u.tauntRemainingRounds = 0;
                    u.tauntTargetSlot = -1;
                    // leave armorBlock alone
                }
                combat.SetUnit(team, s, u);
            }
        }
    }

    void DamageTeam(int team, int amount)
    {
        if (combat == null) return;
        for (int s = 0; s < 4; s++)
        {
            var u = combat.GetUnit(team, s);
            if (string.IsNullOrEmpty(u.name) || u.hp <= 0) continue;
            bool defeated = combat.ApplyDamageToTarget(
                team, s, amount,
                CombatScript.EffectKind.Physical,
                sourceTeam: -1, sourceSlot: -1);
            if (defeated) Log($"  {u.name} defeated by debug damage.");
        }
    }

    #endregion


    #region Console Dump

    [ContextMenu("Dump Full State (F3)")]
    public void DumpFullState()
    {
        if (combat == null) return;
        if (CombatDebugHandler.UltraDebug) Debug.Log(BuildFullStateDumpString());
    }

    string BuildFullStateDumpString()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("╔^___^══════════════════════════════════════════════════════^___^╗");
        sb.AppendLine("║o w o             CUMBAT FULL STATE DUMP                  o w o║");
        sb.AppendLine("╚════════════════════════════════════════════════════════════════╝");
        sb.AppendLine($"UltraDebug={UltraDebug}  GodModePlayer={GodModePlayer}  showAiScores={showAiScores}");
        sb.AppendLine($"Player AP: {combat.playerAPBank.current:0}/{combat.playerAPBank.startOfTurnTotal:0}   " +
                      $"Enemy AP: {combat.enemyAPBank.current:0}/{combat.enemyAPBank.startOfTurnTotal:0}");
        sb.AppendLine();
        sb.Append(DumpTeam("PLAYER", CombatScript.TeamPlayer));
        sb.AppendLine();
        sb.Append(DumpTeam("ENEMY", CombatScript.TeamEnemy));

        if (ai != null)
        {
            ai.EvaluateAllTeams();
            sb.AppendLine();
            sb.AppendLine("--- COMBAT AI SCORES ---");
            sb.AppendLine($"Active   total = {ai.lastActiveStrengthTotal:0.00}");
            sb.AppendLine($"Opposition total = {ai.lastOppositionStrengthTotal:0.00}  agression ×{ai.lastAgressionMultiplier:0.00}  -> final {ai.lastOppositionFinalStrength:0.00}");
            sb.Append(DumpAiTeam("ACTIVE", ai.lastActiveStrength));
            sb.Append(DumpAiTeam("OPPOSITION", ai.lastOppositionStrength));
        }

        // Bench + action-evaluation dump.
        if (runtime != null)
        {
            sb.AppendLine();
            sb.AppendLine("--- BENCH ---");
            sb.AppendLine($"Player viable = {combat.IsTeamViable(CombatScript.TeamPlayer)}");
            sb.AppendLine($"Enemy  viable = {combat.IsTeamViable(CombatScript.TeamEnemy)}");
            sb.Append(runtime.DumpBench(CombatScript.TeamPlayer));
            sb.Append(runtime.DumpBench(CombatScript.TeamEnemy));
        }

        if (ai != null && ai.hasActionEvaluation && ai.lastActionEvaluation != null)
        {
            sb.AppendLine();
            sb.AppendLine("--- AI ACTION EVALUATION (last) ---");
            sb.AppendLine($"Team={ai.lastActionTeam}  actions={ai.lastActionEvaluation.Count}  chosen=#{ai.lastChosenActionIndex}");
            for (int i = 0; i < ai.lastActionEvaluation.Count; i++)
            {
                var a = ai.lastActionEvaluation[i];
                string mark = (i == ai.lastChosenActionIndex) ? ">>" : "  ";
                sb.AppendLine($"  {mark}[{i}] {a.BuildDescription()}  score={a.Score:0.0}");
            }
            if (ai.lastChosenActionIndex >= 0 && ai.lastChosenActionIndex < ai.lastActionEvaluation.Count)
            {
                sb.AppendLine();
                sb.AppendLine("SELECTED BREAKDOWN:");
                sb.Append(ai.lastActionEvaluation[ai.lastChosenActionIndex].breakdown.BuildBreakdownString());
            }
        }

        return sb.ToString();
    }

    string DumpAiTeam(string label, List<CombatAi.UnitStrengthEntry> list)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"  {label}:");

        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];

            sb.AppendLine(
                $"    [slot {e.slot}] {e.unit.name,-18} " +
                $"HP miss {e.hpMissingFrac * 100f,5:0}% " +
                $"HP {e.hpScore,6:0.00}  " +

                $"AR miss {e.armorMissingFrac * 100f,5:0}% " +
                $"AR {e.armorScore,6:0.00}  " +

                $"SH miss {e.shieldMissingFrac * 100f,5:0}% " +
                $"SH {e.shieldScore,6:0.00}  " +

                $"sum {e.strength,6:0.00}"
            );
        }

        return sb.ToString();
    }
    string DumpTeam(string label, int team)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"--- {label} TEAM ---");
        string header = $"{"Slot",-4}{"Name",-20}{"HP",-10}{"AC",-4}{"AR",-6}{"SH",-4}{"TH",-4}{"AP",-4}{"Statuses"}";
        sb.AppendLine(header);
        sb.AppendLine(new string('-', Mathf.Max(header.Length, 70)));
        for (int s = 0; s < 4; s++)
        {
            var u = combat.GetUnit(team, s);
            if (string.IsNullOrEmpty(u.name))
            {
                sb.AppendLine($"{s,-4}{"<empty>"}");
                continue;
            }
            string hp = u.hp <= 0 ? "DEAD" : $"{u.hp:0}/{u.maxHp:0}";
            string ar = $"{u.armor:0}/{u.armorBlock:0}";
            string status = BuildStatusSummary(u, team, s);
            sb.AppendLine($"{s,-4}{Trunc(u.name, 20),-20}{hp,-10}{u.ac,-4:0}{ar,-6}{u.shield,-4:0}{u.th,-4:0}{u.ap,-4:0}{status}");

            if (u.actions != null)
            {
                for (int i = 0; i < u.actions.Count; i++)
                {
                    var a = u.actions[i];
                    int used = (u.ActionUseCounts != null && i < u.ActionUseCounts.Count) ? u.ActionUseCounts[i] : 0;
                    string limitStr = a.useLimit > 0 ? $"{used}/{a.useLimit}" : $"{used}";
                    string costStr = a.APCost == 0 ? "Free" : a.APCost.ToString();
                    string overrideStr = string.IsNullOrWhiteSpace(a.overrideKey) ? "" : $"  override={a.overrideKey}";
                    sb.AppendLine($"      [{i}] {a.fullName,-22} hit:{a.hitRoll,-8} dmg:{a.physicalDmg,-6} cost:{costStr,-5} used:{limitStr}{overrideStr}");
                }
            }
        }
        return sb.ToString();
    }

    #endregion


    #region Overlay Building

    string AiTeamTotalLine(int team)
    {
        if (ai == null) return "";
        if (team == ai.ActivePerspectiveTeam)
            return $"   AI total=<color=#88ff88><b>{ai.lastActiveStrengthTotal:0.00}</b></color>";
        return $"   AI total=<color=#88ff88><b>{ai.lastOppositionStrengthTotal:0.00}</b></color> " +
               $"×agression <color=#ffcc66>{ai.lastAgressionMultiplier:0.00}</color> " +
               $"= <color=#88ff88><b>{ai.lastOppositionFinalStrength:0.00}</b></color>";
    }

    string BuildStatusSummary(CombatScript.UnitData u, int team, int slot)
    {
        var parts = new List<string>();
        if (u.fireRemainingRounds   > 0) parts.Add($"Fire×{u.fireRemainingRounds}({u.fireDamageNotation})");
        if (u.bleedRemainingRounds  > 0) parts.Add($"Bleed×{u.bleedRemainingRounds}({u.bleedDamageNotation})");
        if (u.poisonRemainingRounds > 0) parts.Add($"Poison×{u.poisonRemainingRounds}({u.poisonDamageNotation})");
        if (u.stunRemainingRounds          > 0) parts.Add($"Stun×{u.stunRemainingRounds}");
        if (u.sleepRemainingRounds         > 0) parts.Add($"Sleep×{u.sleepRemainingRounds}");
        if (u.confusionRemainingRounds     > 0) parts.Add($"Confusion×{u.confusionRemainingRounds}");
        if (u.petrificationRemainingRounds > 0) parts.Add($"Petrified×{u.petrificationRemainingRounds}");
        if (u.tauntRemainingRounds > 0)     parts.Add($"Taunt→slot{u.tauntTargetSlot}×{u.tauntRemainingRounds}");
        if (u.passiveKeys != null && u.passiveKeys.Count > 0)
            parts.Add($"Passives:[{string.Join(",", u.passiveKeys)}]");
        var customs = CustomStatusRuntime.GetAll(team, slot);
        if (customs != null)
        {
            for (int i = 0; i < customs.Count; i++)
            {
                var cs = customs[i];
                if (cs == null || string.IsNullOrEmpty(cs.Key)) continue;
                parts.Add($"Custom:{cs.Key}×{cs.RemainingRounds}");
            }
        }
        return string.Join("  ", parts);
    }

    #endregion


    #region AI Score World-Space Overlay

    void RefreshAiEvaluation()
    {
        if (ai == null) return;
        if (_aiEvalTimer < 0f || Time.unscaledTime - _aiEvalTimer >= aiEvalInterval)
        {
            ai.EvaluateAllTeams();
            _aiEvalTimer = Time.unscaledTime;
        }
    }

    void DrawAiScoreOverlay()
    {
        EnsureAiStyles();

        Camera cam = cachedCamera != null ? cachedCamera : FindFirstObjectByType<Camera>();
        if (cam == null) cam = Camera.current;
        if (cam == null) return;

        DrawAiHeader();

        DrawTeamScoreLabels(
            ai.lastActiveStrength,
            allyScoreColor,
            cam,
            ai.ActivePerspectiveTeam
        );

        DrawTeamScoreLabels(
            ai.lastOppositionStrength,
            enemyScoreColor,
            cam,
            ai.OppositionTeam
        );
    }

    void DrawAiHeader()
    {
        string header =
            $"<b>ACTIVE</b> total=<color=#{ColorToHex(allyScoreColor)}>{ai.lastActiveStrengthTotal:0.00}</color>   ||   " +
            $"<b>OPPOSITION</b> total=<color=#{ColorToHex(enemyScoreColor)}>{ai.lastOppositionStrengthTotal:0.00}</color> " +
            $"×agression <color=#ffcc66>{ai.lastAgressionMultiplier:0.00}</color> " +
            $"-> <b>final {ai.lastOppositionFinalStrength:0.00}</b>";

        float w = Mathf.Min(760f, Screen.width - 20f);
        float h = aiHeaderFontSize + 14f;
        Rect r = new Rect((Screen.width - w) * 0.5f, 4f, w, h);
        GUI.Label(r, header, _aiHeaderStyle);
    }

    void DrawTeamScoreLabels(
        List<CombatAi.UnitStrengthEntry> list,
        Color tint,
        Camera cam,
        int team
    )
    {
        if (list == null) return;
        string hex = ColorToHex(tint);

        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];

        int overlayIndex =
            team == CombatScript.TeamPlayer
                ? e.slot
                : 4 + e.slot;

        if (overlayIndex < 0 || overlayIndex >= showAiUnitScores.Length)
            continue;

        if (!showAiUnitScores[overlayIndex])
            continue;

                GameObject go =
                e.unit.PlayerUnit != null
                    ? e.unit.PlayerUnit
                    : e.unit.PlayerUnitSpace;

            if (go == null)
                continue;

            Vector3 worldPos =
                go.transform.position +
                Vector3.up * aiLabelWorldHeight;

            Vector3 sp = cam.WorldToScreenPoint(worldPos);

            if (sp.z <= 0f)
                continue;

            float x = sp.x;
            float y = Screen.height - sp.y;

            float w = aiLabelWidth;
            float h = aiLabelHeight;

            Rect rect = new Rect(
                x - w * 0.5f,
                y - h,
                w,
                h
            );

            GUI.Label(
                rect,
                BuildUnitScoreText(e, hex, team),
                _aiLabelStyle
            );
        }
    }

    string BuildUnitScoreText(CombatAi.UnitStrengthEntry e, string hex, int team)
    {
        var u = e.unit;
        var sb = new StringBuilder();

        string teamTag = team == CombatScript.TeamPlayer ? "PLAYER" : "ENEMY";
        sb.AppendLine($"<color=#{hex}><b>{EscapeRich(u.name)}</b></color> <color=#888>#{e.slot} · {teamTag}</color>");

        sb.AppendLine($"HP {MiniBar(SafeDiv(u.hp, u.maxHp), 8, HpBarHex(u))} <color=#ddd>{Mathf.Max(0f, u.hp):0}/{u.maxHp:0}</color>");
        sb.AppendLine($"AR {MiniBar(SafeDiv(u.armor, u.maxArmor), 8, "6fb7ff")} <color=#ddd>{u.armor:0}/{u.maxArmor:0}</color> <color=#888>blk {u.armorBlock:0}</color>");
        sb.AppendLine($"SH {MiniBar(SafeDiv(u.shield, u.maxShield), 8, "ffe066")} <color=#ddd>{u.shield:0}/{u.maxShield:0}</color>");
        sb.AppendLine($"AP <b>{u.ap:0}</b>   TH <b>{u.th:0}</b>   AC <b>{u.ac:0}</b>");

        string status = BuildStatusSummary(u, team, e.slot);
        if (!string.IsNullOrEmpty(status))
            sb.AppendLine($"<color=#ffcc66>{status}</color>");

        if (u.actions != null && u.actions.Count > 0)
            sb.AppendLine($"<color=#999>{BuildActionsLine(u)}</color>");

        sb.Append($"<color=#{hex}>score <b>{e.strength:0.00}</b></color> " +
                  $"<color=#666>(hp {e.hpScore:0.0} ar {e.armorScore:0.0} sh {e.shieldScore:0.0})</color>");

        return sb.ToString();
    }
    #endregion


    #region Bar / Visual Helpers

    static string MiniBar(float frac01, int width, string hex)
    {
        int filled = Mathf.RoundToInt(Mathf.Clamp01(frac01) * width);
        var sb = new StringBuilder();
        if (filled > 0)
            sb.Append($"<color=#{hex}>").Append('█', filled).Append("</color>");
        if (width - filled > 0)
            sb.Append("<color=#3a3a3f>").Append('█', width - filled).Append("</color>");
        return sb.ToString();
    }

    static float SafeDiv(float a, float b) => b > 0f ? Mathf.Clamp01(a / b) : 0f;

    static string HpBarHex(CombatScript.UnitData u)
    {
        if (u.maxHp <= 0f) return "888888";
        float frac = u.hp / u.maxHp;
        if (frac > 0.5f)  return "6cff8e";
        if (frac > 0.25f) return "ffe066";
        return "ff6b6b";
    }

    #endregion


    #region button Layout Helpers

    void Beginbutton(float rowWidth)
    {
        _buttonRowWidth = Mathf.Max(40f, rowWidth);
        _buttonX = 0f;
        GUILayout.BeginHorizontal();
    }

    void Endbutton()
    {
        GUILayout.EndHorizontal();
    }

    void buttonWrapIfNeeded(float itemWidth)
    {
        if (_buttonX > 0f && _buttonX + itemWidth > _buttonRowWidth)
        {
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            _buttonX = 0f;
        }
    }

    float PillWidth(string plainLabel)
    {
        float w = _smallButtonStyle.CalcSize(new GUIContent(plainLabel)).x;
        return Mathf.Clamp(w + 8f, 32f, 140f);
    }

    void buttonToggle(string label, System.Func<bool> getter, System.Action<bool> setter)
    {
        bool on = getter();
        float w = PillWidth(label);
        buttonWrapIfNeeded(w);

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = on ? new Color(0.30f, 0.68f, 0.42f, 1f) : new Color(0.26f, 0.26f, 0.29f, 1f);
        string text = on ? $"<b>{label}</b>" : $"<color=#bbb>{label}</color>";
        if (GUILayout.Button(text, _smallButtonStyle, GUILayout.Width(w), GUILayout.Height(18f)))
            setter(!on);
        GUI.backgroundColor = prevBg;

        _buttonX += w + 3f;
    }

    void buttonAction(string label, System.Action action)
    {
        float w = PillWidth(label);
        buttonWrapIfNeeded(w);

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.22f, 0.24f, 0.30f, 1f);
        if (GUILayout.Button(label, _smallButtonStyle, GUILayout.Width(w), GUILayout.Height(18f)))
            action?.Invoke();
        GUI.backgroundColor = prevBg;

        _buttonX += w + 3f;
    }

    #endregion


    #region Style / Utility

    void EnsureStyles()
    {
        if (_bgTex == null)
        {
            _bgTex = MakeRoundedTex(overlayBackground, 10);
        }
        if (_boxStyle == null)
        {
            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = _bgTex;
            _boxStyle.border = new RectOffset(10, 10, 10, 10);
        }
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = overlayFontSize,
                richText = overlayRichText,
                wordWrap = true,
                normal = { textColor = overlayText }
            };
        }

        // Title bar.
        if (_titleStyle == null)
        {
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize   = overlayFontSize + 2,
                richText   = true,
                wordWrap   = false,
                alignment  = TextAnchor.MiddleLeft,
                fontStyle  = FontStyle.Bold,
                normal     = { textColor = new Color(1f, 1f, 1f, 0.95f) }
            };
        }

        // Hint / small text.
        if (_hintStyle == null)
        {
            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = overlayFontSize - 1,
                richText  = true,
                wordWrap  = true,
                alignment = TextAnchor.UpperLeft,
                normal    = { textColor = new Color(0.75f, 0.75f, 0.8f, 1f) }
            };
            _hintStyle.hover.textColor = new Color(1f, 0.9f, 0.6f, 1f);
        }

        if (_pillTex == null)
        {
            _pillTex = MakeRoundedTex(Color.white, 6);
        }

        if (_sectionHeaderStyle == null)
        {
            _sectionHeaderStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize   = overlayFontSize + 1,
                richText   = true,
                wordWrap   = false,
                alignment  = TextAnchor.MiddleLeft,
                fontStyle  = FontStyle.Bold,
                padding    = new RectOffset(10, 8, 3, 3),
                normal     = { textColor = Color.white }
            };
            _sectionHeaderStyle.normal.background = _pillTex;
            _sectionHeaderStyle.border = new RectOffset(6, 6, 6, 6);
        }

        // Small button-row buttons.
        if (_smallButtonStyle == null)
        {
            _smallButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize   = Mathf.Max(9, overlayFontSize - 2),
                richText   = true,
                alignment  = TextAnchor.MiddleCenter,
                padding    = new RectOffset(4, 4, 1, 1),
                margin     = new RectOffset(0, 3, 0, 3)
            };
            _smallButtonStyle.normal.background  = _pillTex;
            _smallButtonStyle.hover.background   = _pillTex;
            _smallButtonStyle.active.background  = _pillTex;
            _smallButtonStyle.focused.background = _pillTex;
            _smallButtonStyle.border = new RectOffset(6, 6, 6, 6);
        }

        // Divider line.
        if (_dividerStyle == null)
        {
            _dividerStyle = new GUIStyle(GUI.skin.box)
            {
                fixedHeight = 2f,
                margin = new RectOffset(0, 0, 2, 2)
            };
            _dividerStyle.normal.background = MakeSolidTex(new Color(1f, 1f, 1f, 0.14f));
        }
    }

    static Texture2D MakeSolidTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    static Texture2D MakeRoundedTex(Color32 fill, int radius)
    {
        int size = Mathf.Max(4, radius * 2 + 4);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        Color32 clear = new Color32(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inCornerZone = (x < radius || x >= size - radius) && (y < radius || y >= size - radius);
                Color32 c = fill;
                if (inCornerZone)
                {
                    float cx = x < radius ? radius - 0.5f : size - radius - 0.5f;
                    float cy = y < radius ? radius - 0.5f : size - radius - 0.5f;
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    c = dist <= radius ? fill : clear;
                }
                pixels[y * size + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    void EnsureAiStyles()
    {
        if (_aiBgTex == null)
        {
            _aiBgTex = MakeRoundedTex(aiLabelBackground, 8);
        }
        if (_aiLabelStyle == null)
        {
            _aiLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize   = aiLabelFontSize,
                richText    = true,
                wordWrap    = true,
                alignment   = TextAnchor.UpperLeft,
                padding     = new RectOffset(8, 8, 6, 6),
                normal      = { textColor = Color.white, background = _aiBgTex }
            };
            _aiLabelStyle.border = new RectOffset(8, 8, 8, 8);
        }
        if (_aiHeaderStyle == null)
        {
            _aiHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize   = aiHeaderFontSize,
                richText    = true,
                wordWrap    = false,
                alignment   = TextAnchor.MiddleCenter,
                padding     = new RectOffset(12, 12, 6, 6),
                normal      = { textColor = Color.white, background = _aiBgTex }
            };
            _aiHeaderStyle.border = new RectOffset(8, 8, 8, 8);
        }
    }

    void EnsureAiActionStyles()
    {
        if (_aiActionBgTex == null)
        {
            _aiActionBgTex = MakeRoundedTex(aiActionBackground, 10);
        }
        if (_aiActionStyle == null)
        {
            _aiActionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = aiActionFontSize,
                richText  = true,
                wordWrap  = true,
                alignment = TextAnchor.UpperLeft,
                padding   = new RectOffset(10, 10, 8, 8),
                normal    = { textColor = Color.white }
            };
        }
        if (_aiActionBoxStyle == null)
        {
            _aiActionBoxStyle = new GUIStyle(GUI.skin.box);
            _aiActionBoxStyle.normal.background = _aiActionBgTex;
            _aiActionBoxStyle.border = new RectOffset(10, 10, 10, 10);
        }
    }

    static string ColorToHex(Color c)
    {
        Color32 c32 = c;
        return $"{c32.r:X2}{c32.g:X2}{c32.b:X2}";
    }

    static string Trunc(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    static string EscapeRich(string s)
        => s == null ? "" : s.Replace("<", "&lt;").Replace(">", "&gt;");

    static void Log(string msg) => Debug.Log($"[CombatDebug] {msg}");

    #endregion
}
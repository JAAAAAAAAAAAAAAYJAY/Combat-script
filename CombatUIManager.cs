using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ActionData = CombatScript.ActionData;

[DefaultExecutionOrder(200)]
public class CombatUIManager : MonoBehaviour
{
#region Config

[SerializeField] private CombatRuntimeManager runtime;
[SerializeField] private CombatScript combat;

[Header("Buttons")]
[SerializeField] private Button[] ActionButtons = new Button[4];
[SerializeField] private Button swapUnitButton;
[SerializeField] private Button endTurnButton;

[Header("Display Texts")]
[SerializeField] private TMP_Text roundText;
[SerializeField] private TMP_Text turnText;
[SerializeField] private TMP_Text APText;
[SerializeField] private TMP_Text suggestionText;
[SerializeField] private TMP_Text selectedUnitText;
[SerializeField] private TMP_Text modeText;

[Header("Selection")]
[SerializeField] private float maxSelectionRange = 5f;
[SerializeField] private float refreshInterval = 0.05f;

private bool w;
private bool a;
private bool s;
private bool d;
private bool enter;
private bool esc;

[Header("Highlight Indicator")]
[SerializeField] private Vector2 indicatorSize = new Vector2(1.6f, 2.2f);
[SerializeField] private float indicatorZOffset = -1f;
[SerializeField] private int indicatorSortingOrder = 100;

[Header("Highlight Colors")]
[SerializeField] private Color selectedUnitColor  = new Color(1f, 0.85f, 0.2f, 0.8f);
[SerializeField] private Color hoveredUnitColor   = new Color(0.3f, 1f, 1f, 0.6f);
[SerializeField] private Color enemyTargetColor   = new Color(1f, 0.25f, 0.25f, 0.7f);
[SerializeField] private Color allyTargetColor    = new Color(0.3f, 1f, 0.3f, 0.7f);
[SerializeField] private Color swapSourceColor    = new Color(1f, 0.3f, 1f, 0.8f);
[SerializeField] private Color swapTargetColor    = new Color(1f, 0.6f, 1f, 0.8f);
[SerializeField] private Color focusedButtonColor = new Color(1f, 1f, 0.6f, 1f);
[SerializeField] private float focusedButtonScale = 1.12f;

[Header("Bench Panel (uGUI - wire up in inspector)")]
[Tooltip("REQUIRED. The bench panel RectTransform. Assign this in the inspector - it is no longer auto-created. The panel is only shown while a swap is in progress.")]
[SerializeField] private RectTransform benchPanel;

[Header("Bench Buttons (assign 4 per team in editor)")]
[Tooltip("Assign the 4 PLAYER-team bench button GameObjects you placed in the scene. Each must have a Button + an Image, plus children named, 'HPBar' (Image, filled), 'Name' (TMP_Text) and optionally 'Info' (TMP_Text). No prefab/instantiation - these are your own pre-placed buttons. Empty bench slots HIDE their button (not destroyed); occupied slots show + populate.")]
[SerializeField] private GameObject[] playerBenchButtons = new GameObject[4];
[Tooltip("Assign the 4 ENEMY-team bench button GameObjects (same child layout as the player buttons).")]
[SerializeField] private GameObject[] enemyBenchButtons  = new GameObject[4];

[Header("Bench Button Colors")]
[SerializeField] private Color benchBgColor       = new Color(0.12f, 0.13f, 0.16f, 0.92f);
[SerializeField] private Color benchBgHoverColor  = new Color(1f, 0.6f, 1f, 0.95f);
[SerializeField] private Color benchBgSourceColor  = new Color(1f, 0.3f, 1f, 0.95f);
[SerializeField] private Color benchBgDeadColor    = new Color(0.3f, 0.3f, 0.3f, 0.8f);
[SerializeField] private Color benchHpColor         = new Color(0.3f, 1f, 0.4f, 1f);
[SerializeField] private Color benchHpLowColor      = new Color(1f, 0.35f, 0.3f, 1f);
[SerializeField] private Color benchNameColor       = new Color(1f, 1f, 1f, 0.95f);

[Tooltip("Optional: world-space Transforms that mark where each bench slot is physically displayed in the scene. If assigned, swap-mode indicators are also drawn at these positions. Index 0..N maps to bench index.")]
[SerializeField] private Transform[] playerBenchMarkers = new Transform[0];
[SerializeField] private Transform[] enemyBenchMarkers  = new Transform[0];

[Header("Floating Text")]
[SerializeField] private GameObject floatingTextPrefab;
[SerializeField] private float floatingTextDuration = 1.2f;
[SerializeField] private float floatingTextRiseSpeed = 2f;
[SerializeField] private Vector3 floatingTextOffset = new Vector3(0, 2f, 0);
[SerializeField] private Color damageColor = new Color(1f, 0.3f, 0.2f, 1f);
[SerializeField] private Color healColor = new Color(0.3f, 1f, 0.4f, 1f);
[SerializeField] private Color dotColor = new Color(0.9f, 0.5f, 0.1f, 1f);
[SerializeField] private Color critColor = new Color(1f, 0.85f, 0.1f, 1f);
[SerializeField] private int floatingTextFontSize = 28;

#endregion


#region State

private enum FocusRow { Units, Buttons }
private enum SwapMode  { None, PickTarget, PickBackRow }

private int        selectedSlot = -1;
private GameObject selectedUnit = null;
private Camera     cachedCamera;
private float      lastRefreshTime;
private float      lastBenchRefreshTime;
private int        lastActiveTeam = -1;

private FocusRow focusRow = FocusRow.Units;
private int      focusUnitIndex = 0;
private int      focusButtonIndex = 0;
private int      hoveredButtonIndex = -1;
private int      hoveredUnitSlot = -1;
private int      hoveredBenchIndex = -1;

private bool     mouseSelectionLocked = false;
private Vector3  lastMouseScreenPos = new Vector3(-1f, -1f, -1f);

private SwapMode swapMode = SwapMode.None;
private int      swapTargetSlot = -1;
private int      swapBenchIndex = -1;
private int      swapHoverBenchIndex = -1;

private readonly List<Button> navigableButtons = new List<Button>();

#endregion


#region Bench Button UI (uGUI)

private class BenchButtonUI
{
    public GameObject   root;
    public Button       button;
    public Image        background;
    public Image        icon;        // child named "Icon" (Image) kid named fingers
    public Image        hpBarFill;   // child named "HPBar" (Image, filled)
    public TMP_Text     nameText;    // child named "Name" (TMP_Text)
    public TMP_Text     infoText;    // child named "Info" (TMP_Text, optional)
    public int          benchIndex;  // last bench index this button represented
}

private readonly List<BenchButtonUI> playerBenchButtonUIs = new List<BenchButtonUI>();
private readonly List<BenchButtonUI> enemyBenchButtonUIs  = new List<BenchButtonUI>();
private bool benchButtonsBuilt;

List<BenchButtonUI> ActiveBenchButtonUIs
{
    get
    {
        if (runtime == null) return null;
        int team = runtime.ActiveTeamId;
        if (team == CombatScript.TeamPlayer) return playerBenchButtonUIs;
        if (team == CombatScript.TeamEnemy)  return enemyBenchButtonUIs;
        return null;
    }
}

#endregion


#region Indicator Pool

private Sprite highlightSprite;
private readonly Dictionary<(int team, int slot), GameObject> indicators =
    new Dictionary<(int team, int slot), GameObject>();
private readonly HashSet<GameObject> activeIndicators = new HashSet<GameObject>();

private readonly Dictionary<Graphic, Color> originalGraphicColors =
    new Dictionary<Graphic, Color>();
private readonly Dictionary<Transform, Vector3> originalButtonScales =
    new Dictionary<Transform, Vector3>();
private readonly HashSet<Graphic> highlightedGraphics = new HashSet<Graphic>();
private readonly HashSet<Transform> scaledButtons = new HashSet<Transform>();

#endregion


#region Unity Lifecycle

void Start()
{
    cachedCamera = FindFirstObjectByType<Camera>();
    GenerateHighlightSprite();
    WireButtons();
    EnsureBenchPanel();
    CombatScript.OnFloatingText += HandleFloatingText;
}

void OnDestroy()
{
    CombatScript.OnFloatingText -= HandleFloatingText;
    foreach (var kvp in indicators)
        if (kvp.Value != null) Destroy(kvp.Value);
    indicators.Clear();
    if (highlightSprite != null)
    {
        if (highlightSprite.texture != null) Destroy(highlightSprite.texture);
        Destroy(highlightSprite);
    }
    // Bench buttons are assigned in the inspector (not instantiated by us), so we
    // must NOT destroy them - just drop our cached references.
    if (playerBenchButtonUIs != null) playerBenchButtonUIs.Clear();
    if (enemyBenchButtonUIs  != null) enemyBenchButtonUIs.Clear();
}

void Update()
{
    if (runtime == null || combat == null) return;

    if (cachedCamera == null) cachedCamera = FindFirstObjectByType<Camera>();

    w     = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
    a     = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow);
    s     = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
    d     = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow);
    enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
    esc   = Input.GetKeyDown(KeyCode.Escape);

    int activeTeam = runtime.ActiveTeamId;
    if (activeTeam != lastActiveTeam)
    {
        lastActiveTeam = activeTeam;
        selectedSlot = -1;
        selectedUnit = null;
        focusRow = FocusRow.Units;
        focusUnitIndex = 0;
        focusButtonIndex = 0;
        swapMode = SwapMode.None;
        swapTargetSlot = -1;
        swapBenchIndex = -1;
        swapHoverBenchIndex = -1;
        hoveredUnitSlot = -1;
        hoveredBenchIndex = -1;
        hoveredButtonIndex = -1;
        mouseSelectionLocked = false;
        AutoSelectFirstLivingUnit();
    }

    if (selectedSlot >= 0 && !runtime.IsUnitAlive(runtime.ActiveTeamId, selectedSlot))
        AutoSelectFirstLivingUnit();

    HandleKeyboardNavigation();
    HandleMouseHover();
    HandleMouseClick();
    DetectHoveredButton();

    if (Time.unscaledTime - lastBenchRefreshTime >= refreshInterval)
    {
        RefreshBenchPanel();
        lastBenchRefreshTime = Time.unscaledTime;
    }

    UpdateButtonInteractable();

    ApplyAllHighlights();

    if (Time.unscaledTime - lastRefreshTime > refreshInterval)
    {
        RebuildNavigableButtons();
        RefreshActionButtons();
        RefreshDisplay();
        lastRefreshTime = Time.unscaledTime;
    }
}

#endregion


#region Highlight Sprite Generation

void GenerateHighlightSprite()
{
    int texW = 64;
    int texH = 64;
    var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
    tex.filterMode = FilterMode.Bilinear;

    int borderPx = 6;

    var pixels = new Color32[texW * texH];
    for (int y = 0; y < texH; y++)
    {
        for (int x = 0; x < texW; x++)
        {
            int idx = y * texW + x;

            int distTop    = texH - 1 - y;
            int distBottom = y;
            int distLeft   = x;
            int distRight  = texW - 1 - x;

            int minDist = Mathf.Min(distTop, distBottom, distLeft, distRight);

            byte a;
            if (minDist < borderPx)
            {
                float edgeFade = Mathf.Clamp01((float)(minDist + 1) / 2f);
                a = (byte)(edgeFade * 255f);
            }
            else
            {
                a = 0;
            }

            pixels[idx] = new Color32(255, 255, 255, a);
        }
    }
    tex.SetPixels32(pixels);
    tex.Apply();
    highlightSprite = Sprite.Create(tex, new Rect(0, 0, texW, texH),
        new Vector2(0.5f, 0.5f), texW);
}

GameObject GetOrCreateIndicator(int team, int slot)
{
    var key = (team, slot);
    if (indicators.TryGetValue(key, out var go) && go != null)
        return go;

    go = new GameObject($"Highlight_{team}_{slot}");
    go.transform.SetParent(transform, false);
    var sr = go.AddComponent<SpriteRenderer>();
    sr.sprite = highlightSprite;
    sr.sortingOrder = indicatorSortingOrder;
    sr.color = Color.white;
    go.transform.localScale = new Vector3(indicatorSize.x, indicatorSize.y, 1f);
    go.SetActive(false);
    indicators[key] = go;
    return go;
}

void ShowIndicator(int team, int slot, Color color)
{
    var unit = combat.GetUnit(team, slot);
    if (unit.PlayerUnit == null) return;

    var go = GetOrCreateIndicator(team, slot);
    if (go == null) return;

    var pos = unit.PlayerUnit.transform.position;
    pos.z += indicatorZOffset;
    go.transform.position = pos;
    go.transform.localScale = new Vector3(indicatorSize.x, indicatorSize.y, 1f);

    var sr = go.GetComponent<SpriteRenderer>();
    if (sr != null) sr.color = color;

    go.SetActive(true);
    activeIndicators.Add(go);
}

void ShowBenchIndicator(int team, int benchIndex, Color color)
{
    if (cachedCamera == null) return;
    var markers = team == CombatScript.TeamPlayer ? playerBenchMarkers : enemyBenchMarkers;
    if (markers == null || benchIndex < 0 || benchIndex >= markers.Length) return;
    var marker = markers[benchIndex];
    if (marker == null) return;

    var key = (-team - 1, benchIndex); // negative team to namespace bench indicators
    if (!indicators.TryGetValue(key, out var go) || go == null)
    {
        go = new GameObject($"BenchHighlight_{team}_{benchIndex}");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = highlightSprite;
        sr.sortingOrder = indicatorSortingOrder;
        sr.color = Color.white;
        go.transform.localScale = new Vector3(indicatorSize.x, indicatorSize.y, 1f);
        go.SetActive(false);
        indicators[key] = go;
    }

    var pos = marker.position;
    pos.z += indicatorZOffset;
    go.transform.position = pos;
    go.transform.localScale = new Vector3(indicatorSize.x, indicatorSize.y, 1f);

    var sr2 = go.GetComponent<SpriteRenderer>();
    if (sr2 != null) sr2.color = color;

    go.SetActive(true);
    activeIndicators.Add(go);
}

void HideAllIndicators()
{
    foreach (var go in activeIndicators)
    {
        if (go != null) go.SetActive(false);
    }
    activeIndicators.Clear();
}

#endregion


#region Button Wiring

void WireButtons()
{
    if (ActionButtons != null)
    {
        for (int i = 0; i < ActionButtons.Length; i++)
        {
            if (ActionButtons[i] == null) continue;
            int idx = i;
            ActionButtons[i].onClick.RemoveAllListeners();
            ActionButtons[i].onClick.AddListener(() => OnActionButton(idx));
        }
    }
    if (swapUnitButton != null)
    {
        swapUnitButton.onClick.RemoveAllListeners();
        swapUnitButton.onClick.AddListener(OnSwapButton);
    }
    if (endTurnButton != null)
    {
        endTurnButton.onClick.RemoveAllListeners();
        endTurnButton.onClick.AddListener(OnEndTurn);
    }
}

void OnActionButton(int index)
{
    if (runtime == null || combat == null) return;
    if (selectedSlot < 0) return;
    if (swapMode != SwapMode.None) return;
    int team = runtime.ActiveTeamId;
    if (team < 0) return;
    runtime.RequestActionUse(team, selectedSlot, index);
}

void OnSwapButton()
{
    if (swapMode == SwapMode.None)
    {
        if (selectedSlot < 0) return;
        swapMode = SwapMode.PickTarget;
        swapTargetSlot = FindNextSwapSlot(selectedSlot, 1);
        swapBenchIndex = -1;
        swapHoverBenchIndex = -1;
    }
    else
    {
        CancelSwap();
    }
}

void OnEndTurn()
{
    if (runtime == null) return;
    if (swapMode != SwapMode.None) return;
    runtime.EndTurnButton();
}

#endregion


#region Keyboard Navigation

void HandleKeyboardNavigation()
{
    if (runtime == null) return;
    if (runtime.State != CombatRuntimeManager.BattleState.InProgress) return;
    if (runtime.ActiveTurn == CombatRuntimeManager.TeamTurn.None) return;

    if (swapMode == SwapMode.PickTarget || swapMode == SwapMode.PickBackRow)
    {
        HandleSwapModeInput();
        return;
    }

    if (esc)
    {
        if (focusRow == FocusRow.Buttons)
        {
            focusRow = FocusRow.Units;
        }
        else
        {
            mouseSelectionLocked = false;
        }
        return;
    }

    if (focusRow == FocusRow.Units)
    {
        if (a) CycleUnit(1);
        if (d) CycleUnit(-1);
        if (s || (enter && selectedSlot >= 0))
        {
            focusRow = FocusRow.Buttons;
            focusButtonIndex = Mathf.Clamp(focusButtonIndex, 0, Mathf.Max(0, navigableButtons.Count - 1));

            if (!IsButtonInteractable(focusButtonIndex))
            {
                int firstUsable = FindNextInteractableButton(focusButtonIndex, 1);
                if (firstUsable >= 0) focusButtonIndex = firstUsable;
            }
        }
    }
    else
    {
        if (a) CycleButton(-1);
        if (d) CycleButton(1);
        if (w) focusRow = FocusRow.Units;
        if (enter) ActivateFocusedButton();
    }
}

void CycleUnit(int dir)
{
    int team = runtime.ActiveTeamId;
    if (team < 0) return;
    int next = FindNextUnit(focusUnitIndex, dir);
    if (next >= 0)
    {
        focusUnitIndex = next;
        SelectUnit(team, next);
    }
}

void CycleButton(int dir)
{
    int next = FindNextInteractableButton(focusButtonIndex, dir);
    if (next >= 0)
        focusButtonIndex = next;
}

int FindNextInteractableButton(int fromIndex, int dir)
{
    int count = navigableButtons.Count;
    if (count == 0) return -1;

    int idx = fromIndex < 0 ? 0 : fromIndex;
    for (int i = 0; i < count; i++)
    {
        idx = (idx + dir + count) % count;
        if (IsButtonInteractable(idx))
            return idx;
    }
    return -1;
}

bool IsButtonInteractable(int idx)
{
    if (idx < 0 || idx >= navigableButtons.Count) return false;
    Button btn = navigableButtons[idx];
    return btn != null && btn.interactable;
}

void ActivateFocusedButton()
{
    if (focusButtonIndex < 0 || focusButtonIndex >= navigableButtons.Count) return;
    Button btn = navigableButtons[focusButtonIndex];
    if (btn == null || !btn.interactable) return;
    btn.onClick.Invoke();
}

void HandleSwapModeInput()
{
    if (swapMode == SwapMode.PickBackRow)
    {
        if (a) swapTargetSlot = 2;
        if (d) swapTargetSlot = 3;
        if (enter) ConfirmBackRowSwap();
        if (esc || w || s) CancelSwap();
        return;
    }

    if (a || d)
    {
        int dir = a ? 1 : -1;
        swapTargetSlot = FindNextSwapSlot(swapTargetSlot, dir);
        swapHoverBenchIndex = -1;
    }
    // Q/E cycle bench hover (so keyboard players can also pick a bench unit)
    if (Input.GetKeyDown(KeyCode.Q)) CycleBenchHover(-1);
    if (Input.GetKeyDown(KeyCode.E)) CycleBenchHover(1);
    if (enter)
    {
        if (swapHoverBenchIndex >= 0)
            ClickBenchUnit(swapHoverBenchIndex);
        else
            ConfirmSwap();
    }
    if (esc || w || s) CancelSwap();
}

void CycleBenchHover(int dir)
{
    int team = runtime.ActiveTeamId;
    if (team < 0) return;
    var bench = runtime.GetBench(team);
    if (bench == null || bench.Count == 0) return;

    var live = new List<int>(bench.Count);
    for (int i = 0; i < bench.Count; i++)
        if (CombatRuntimeManager.IsBenchUnitAlive(bench[i])) live.Add(i);
    if (live.Count == 0) return;

    int curIdx = live.IndexOf(swapHoverBenchIndex);
    int nextIdx = (curIdx < 0)
        ? 0
        : ((curIdx + dir + live.Count) % live.Count);
    swapHoverBenchIndex = live[nextIdx];

    swapTargetSlot = -1;
}

void ConfirmSwap()
{
    if (swapMode != SwapMode.PickTarget) return;
    int team = runtime.ActiveTeamId;
    if (team < 0) { CancelSwap(); return; }

    // Case A: a bench unit is currently hovered -> treat as a bench click.
    if (swapHoverBenchIndex >= 0)
    {
        ClickBenchUnit(swapHoverBenchIndex);
        return;
    }

    // Case B: an active slot is the target.
    if (selectedSlot < 0 || swapTargetSlot < 0 || selectedSlot == swapTargetSlot)
    {
        CancelSwap();
        return;
    }

    int slotA = selectedSlot;
    int slotB = swapTargetSlot;

    bool swapped = runtime.RequestMove(team, slotA, slotB);
    if (!swapped)
    {
        CancelSwap();
        return;
    }

    focusUnitIndex = selectedSlot;
    var u = combat.GetUnit(team, selectedSlot);
    selectedUnit = u.PlayerUnit;
    if (selectedUnit != null) combat.Jiggle(selectedUnit);

    swapMode = SwapMode.None;
    swapTargetSlot = -1;
    swapBenchIndex = -1;
    swapHoverBenchIndex = -1;
}

void ClickBenchUnit(int benchIndex)
{
    if (swapMode != SwapMode.PickTarget) return;
    int team = runtime.ActiveTeamId;
    if (team < 0) { CancelSwap(); return; }
    if (selectedSlot < 0) { CancelSwap(); return; }

    var bench = runtime.GetBench(team);
    if (bench == null || benchIndex < 0 || benchIndex >= bench.Count) { CancelSwap(); return; }
    if (!CombatRuntimeManager.IsBenchUnitAlive(bench[benchIndex])) return;

    bool sourceIsBackRow = IsBackRowSlot(selectedSlot);

    if (sourceIsBackRow)
    {
        bool ok = runtime.RequestBenchSwap(team, selectedSlot, benchIndex);
        if (!ok)
        {
            CancelSwap();
            return;
        }
        // Refresh selection visual after the swap.
        var u = combat.GetUnit(team, selectedSlot);
        selectedUnit = u.PlayerUnit;
        if (selectedUnit != null) combat.Jiggle(selectedUnit);
        swapMode = SwapMode.None;
        swapTargetSlot = -1;
        swapBenchIndex = -1;
        swapHoverBenchIndex = -1;
    }
    else
    {
        swapMode = SwapMode.PickBackRow;
        swapBenchIndex = benchIndex;
        swapHoverBenchIndex = -1;
        swapTargetSlot = 2; // default to slot 2
    }
}

void ConfirmBackRowSwap()
{
    if (swapMode != SwapMode.PickBackRow) return;
    int team = runtime.ActiveTeamId;
    if (team < 0) { CancelSwap(); return; }
    if (swapBenchIndex < 0) { CancelSwap(); return; }
    if (swapTargetSlot != 2 && swapTargetSlot != 3) { CancelSwap(); return; }

    bool ok = runtime.RequestBenchSwap(team, swapTargetSlot, swapBenchIndex);
    if (!ok)
    {
        CancelSwap();
        return;
    }

    var u = combat.GetUnit(team, selectedSlot);
    selectedUnit = u.PlayerUnit;
    // jiggle the back row slot that actually changed
    var back = combat.GetUnit(team, swapTargetSlot);
    var backUnit = back.PlayerUnit;
    if (backUnit != null) combat.Jiggle(backUnit);

    swapMode = SwapMode.None;
    swapTargetSlot = -1;
    swapBenchIndex = -1;
    swapHoverBenchIndex = -1;
}

void CancelSwap()
{
    swapMode = SwapMode.None;
    swapTargetSlot = -1;
    swapBenchIndex = -1;
    swapHoverBenchIndex = -1;
}

static bool IsBackRowSlot(int slot)
{
    return slot == 2 || slot == 3;
}

int FindNextUnit(int fromSlot, int dir)
{
    int team = runtime.ActiveTeamId;
    if (team < 0) return -1;

    int idx = fromSlot < 0 ? 0 : fromSlot;
    for (int i = 0; i < 4; i++)
    {
        idx = ((idx + dir) % 4 + 4) % 4;
        if (runtime.IsUnitAlive(team, idx))
            return idx;
    }
    return -1;
}

int FindNextSwapSlot(int fromSlot, int dir)
{
    int start = fromSlot < 0 ? 0 : fromSlot;
    return ((start + dir) % 4 + 4) % 4;
}

void AutoSelectFirstLivingUnit()
{
    int team = runtime.ActiveTeamId;
    if (team < 0) return;
    for (int slot = 0; slot < 4; slot++)
    {
        if (runtime.IsUnitAlive(team, slot))
        {
            focusUnitIndex = slot;
            SelectUnit(team, slot);
            return;
        }
    }
}

void SelectUnit(int team, int slot)
{
    if (!runtime.IsUnitAlive(team, slot)) return;
    var unit = combat.GetUnit(team, slot);
    selectedSlot = slot;
    selectedUnit = unit.PlayerUnit;
    focusUnitIndex = slot;
    if (selectedUnit != null) combat.Jiggle(selectedUnit);
    RefreshActionButtons();
    RefreshDisplay();
    ApplyAllHighlights();
}

#endregion


#region Mouse Input

void HandleMouseHover()
{
    hoveredUnitSlot = -1;

    Vector3 currentMousePos = Input.mousePosition;
    bool mouseMoved = (currentMousePos - lastMouseScreenPos).sqrMagnitude > 0.01f;
    lastMouseScreenPos = currentMousePos;

    if (runtime == null || combat == null) return;
    if (runtime.State != CombatRuntimeManager.BattleState.InProgress) return;
    if (cachedCamera == null) return;

    int team = runtime.ActiveTeamId;
    if (team < 0) return;

    bool inSwap = swapMode == SwapMode.PickTarget || swapMode == SwapMode.PickBackRow;

    if (inSwap && hoveredBenchIndex >= 0)
    {
        // Keep the active-slot target stable while the user is on the bench
        return;
    }

    Vector2 mousePos = cachedCamera.ScreenToWorldPoint(currentMousePos);
    float minDist = float.MaxValue;
    int bestSlot = -1;

    for (int i = 0; i < 4; i++)
    {
        // In PickBackRow mode, only slots 2 and 3 are valid hover targets
        if (swapMode == SwapMode.PickBackRow && i != 2 && i != 3) continue;

        // Swap targets can be dead units; normal unit selection cannot
        if (!inSwap && !runtime.IsUnitAlive(team, i)) continue;
        var unit = combat.GetUnit(team, i);
        if (unit.PlayerUnitSpace == null) continue;
        float dist = Vector2.Distance(unit.PlayerUnitSpace.transform.position, mousePos);
        if (dist < minDist)
        {
            minDist = dist;
            bestSlot = i;
        }
    }

    if (minDist <= maxSelectionRange && bestSlot >= 0)
        hoveredUnitSlot = bestSlot;

    if (swapMode == SwapMode.PickBackRow)
    {
        if (mouseMoved && hoveredUnitSlot >= 0)
            swapTargetSlot = hoveredUnitSlot;
        return;
    }

    if (inSwap)
    {
        if (mouseMoved && hoveredUnitSlot >= 0)
            swapTargetSlot = hoveredUnitSlot;
        return;
    }

    if (mouseMoved
        && !mouseSelectionLocked
        && hoveredUnitSlot >= 0
        && hoveredUnitSlot != selectedSlot
        && runtime.IsUnitAlive(team, hoveredUnitSlot))
    {
        SelectUnit(team, hoveredUnitSlot);
        focusRow = FocusRow.Units;
    }
}

void HandleMouseClick()
{
    if (runtime == null || combat == null) return;
    if (runtime.State != CombatRuntimeManager.BattleState.InProgress) return;
    if (!Input.GetMouseButtonDown(0)) return;
    if (cachedCamera == null) return;

    int team = runtime.ActiveTeamId;
    if (team < 0) return;

    if (swapMode == SwapMode.PickTarget && hoveredBenchIndex >= 0)
    {
        return;
    }

    if (swapMode == SwapMode.PickBackRow)
    {
        // Bench markers aren't re-clickable in this mode; only slot 2/3.
        if (hoveredUnitSlot == 2 || hoveredUnitSlot == 3)
        {
            swapTargetSlot = hoveredUnitSlot;
            ConfirmBackRowSwap();
        }
        return;
    }

    if (swapMode == SwapMode.PickTarget)
    {
        if (hoveredUnitSlot >= 0)
        {
            swapHoverBenchIndex = -1;
            swapTargetSlot = hoveredUnitSlot;
            ConfirmSwap();
        }
        return;
    }

    if (hoveredUnitSlot < 0)
    {
        mouseSelectionLocked = false;
        return;
    }

    if (mouseSelectionLocked && hoveredUnitSlot == selectedSlot)
    {
        mouseSelectionLocked = false;
        return;
    }

    if (hoveredUnitSlot != selectedSlot)
        SelectUnit(team, hoveredUnitSlot);

    mouseSelectionLocked = true;
    focusRow = FocusRow.Units;
}

void DetectHoveredButton()
{
    hoveredButtonIndex = -1;
    for (int i = 0; i < navigableButtons.Count; i++)
    {
        Button btn = navigableButtons[i];
        if (btn == null) continue;
        if (IsMouseOverButton(btn))
        {
            hoveredButtonIndex = i;
            break;
        }
    }
}

void DetectHoveredBenchButton()
{
    if (runtime == null) { hoveredBenchIndex = -1; return; }
    if (swapMode == SwapMode.None) { hoveredBenchIndex = -1; return; }
    if (swapMode == SwapMode.PickBackRow) { hoveredBenchIndex = -1; return; }
    if (runtime.State != CombatRuntimeManager.BattleState.InProgress) { hoveredBenchIndex = -1; return; }

    var list = ActiveBenchButtonUIs;
    if (list == null || list.Count == 0)
    {
        hoveredBenchIndex = -1;
        return;
    }

    int team = runtime.ActiveTeamId;
    var bench = team >= 0 ? runtime.GetBench(team) : null;

    int newHover = -1;
    for (int i = 0; i < list.Count; i++)
    {
        var b = list[i];
        if (b == null || b.root == null || !b.root.activeSelf) continue;
        if (b.button == null) continue;
        if (b.benchIndex < 0) continue;
        // Skip dead bench units - they can't be swap targets.
        if (bench != null && b.benchIndex < bench.Count
            && !CombatRuntimeManager.IsBenchUnitAlive(bench[b.benchIndex]))
            continue;

        var rt = b.button.GetComponent<RectTransform>();
        if (rt == null) continue;
        if (RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cachedCamera))
        {
            newHover = b.benchIndex;
            break;
        }
    }
    hoveredBenchIndex = newHover;
}

bool IsMouseOverButton(Button btn)
{
    if (btn == null) return false;
    var rt = btn.GetComponent<RectTransform>();
    if (rt == null) return false;
    if (RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null))
        return true;
    if (cachedCamera != null &&
        RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cachedCamera))
        return true;
    return false;
}

#endregion


#region Button Management

void RebuildNavigableButtons()
{
    navigableButtons.Clear();
    if (ActionButtons != null)
        foreach (var b in ActionButtons)
            if (b != null) navigableButtons.Add(b);
    if (swapUnitButton != null) navigableButtons.Add(swapUnitButton);
    if (endTurnButton != null) navigableButtons.Add(endTurnButton);

    if (focusButtonIndex >= navigableButtons.Count)
        focusButtonIndex = Mathf.Max(0, navigableButtons.Count - 1);
}

void UpdateButtonInteractable()
{
    if (endTurnButton != null)
    {
        endTurnButton.interactable =
            runtime != null &&
            runtime.State == CombatRuntimeManager.BattleState.InProgress &&
            runtime.ActiveTurn != CombatRuntimeManager.TeamTurn.None &&
            swapMode == SwapMode.None;
    }
    if (swapUnitButton != null)
    {
        bool canConfirmTarget = swapTargetSlot >= 0
                                || swapHoverBenchIndex >= 0
                                || hoveredBenchIndex >= 0;
        bool canConfirm = (swapMode == SwapMode.PickTarget && canConfirmTarget)
                          || swapMode == SwapMode.PickBackRow;
        swapUnitButton.interactable =
            runtime != null &&
            runtime.State == CombatRuntimeManager.BattleState.InProgress &&
            runtime.ActiveTurn != CombatRuntimeManager.TeamTurn.None &&
            (canConfirm || (swapMode == SwapMode.None && selectedSlot >= 0));
    }
}

void RefreshActionButtons()
{
    if (ActionButtons == null) return;
    if (combat == null) return;

    int team = runtime != null ? runtime.ActiveTeamId : -1;
    bool executing = combat != null && combat.IsExecutingAction;
    bool inSwap = swapMode != SwapMode.None;

    List<ActionData> actions = null;
    if (selectedSlot >= 0 && team >= 0)
    {
        var unit = combat.GetUnit(team, selectedSlot);
        actions = unit.actions;
    }

    for (int i = 0; i < ActionButtons.Length; i++)
    {
        Button btn = ActionButtons[i];
        if (btn == null) continue;

        bool hasAction = actions != null && i < actions.Count;
        bool canUse = hasAction
                     && team >= 0
                     && !executing
                     && !inSwap
                     && runtime.CanUseAction(team, selectedSlot, i);

        btn.interactable = canUse;

        if (hasAction)
            SetButtonText(btn, actions[i].ActionName);
        else
            SetButtonText(btn, "");
    }
}

void SetButtonText(Button btn, string text)
{
    var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
    if (tmp != null) { tmp.text = text; return; }
    var legacy = btn.GetComponentInChildren<Text>();
    if (legacy != null) legacy.text = text;
}

#endregion


#region Display Refresh

void RefreshDisplay()
{
    if (runtime == null) return;

    if (roundText != null)
        roundText.text = $"Round {runtime.CurrentRound}";

    if (turnText != null)
    {
        turnText.text = runtime.ActiveTurn switch
        {
            CombatRuntimeManager.TeamTurn.Player => "Player Turn",
            CombatRuntimeManager.TeamTurn.Enemy  => "Enemy Turn",
            _                                     => "---",
        };
    }

    if (APText != null)
    {
        int team = runtime.ActiveTeamId;
        if (team >= 0)
        {
            float cur = runtime.GetTeamAP(team);
            float cap = runtime.GetTeamAPCap(team);
            APText.text = $"AP: {cur:0} / {cap:0}";
        }
        else
            APText.text = "";
    }

    if (selectedUnitText != null)
    {
        int team = runtime.ActiveTeamId;
        if (team >= 0 && selectedSlot >= 0)
        {
            var u = combat.GetUnit(team, selectedSlot);
            selectedUnitText.text = string.IsNullOrEmpty(u.name) ? "" : u.name;
        }
        else
            selectedUnitText.text = "";
    }

    if (modeText != null)
    {
        int modeTeam = runtime.ActiveTeamId;
        if (swapMode == SwapMode.PickBackRow)
        {
            string benchName = "?";
            if (modeTeam >= 0 && swapBenchIndex >= 0)
            {
                var bench = runtime.GetBench(modeTeam);
                if (bench != null && swapBenchIndex < bench.Count)
                {
                    var b = bench[swapBenchIndex];
                    benchName = string.IsNullOrEmpty(b.name) ? "?" : b.name;
                }
            }
            string slotName = (swapTargetSlot == 3) ? "slot 3" : "slot 2";
            modeText.text = $"BENCH IN: {benchName} -> {slotName}\n[A/D] pick 2 or 3  [Enter] confirm  [Esc] cancel";
        }
        else if (swapMode == SwapMode.PickTarget)
        {
            int swapTeam = modeTeam;
            string tname = "?";

            int benchHover = swapHoverBenchIndex >= 0 ? swapHoverBenchIndex : hoveredBenchIndex;
            if (benchHover >= 0 && swapTeam >= 0)
            {
                var bench = runtime.GetBench(swapTeam);
                if (bench != null && benchHover < bench.Count)
                {
                    var b = bench[benchHover];
                    tname = string.IsNullOrEmpty(b.name) ? "?" : b.name;
                    bool sourceIsBackRow = IsBackRowSlot(selectedSlot);
                    modeText.text = sourceIsBackRow
                        ? $"BENCH SWAP: {tname} in (slot {selectedSlot} out)\n[Enter] confirm  [Esc] cancel  [Q/E] cycle bench"
                        : $"BENCH PICK: {tname} -> pick slot 2 or 3\n[Enter] to redirect  [Esc] cancel  [Q/E] cycle bench";
                }
            }
            else if (swapTargetSlot >= 0 && swapTargetSlot < 4)
            {
                var target = combat.GetUnit(swapTeam, swapTargetSlot);
                bool targetAlive = runtime.IsUnitAlive(swapTeam, swapTargetSlot);
                if (!targetAlive)
                    tname = string.IsNullOrEmpty(target.name) ? "(fainted)" : $"{target.name} (fainted)";
                else
                    tname = string.IsNullOrEmpty(target.name) ? "?" : target.name;
                modeText.text = $"SWAP: {tname}\n[A/D] pick  [Q/E] bench  [Enter] confirm  [Esc] cancel";
            }
            else
            {
                modeText.text = $"SWAP: pick a target\n[A/D] pick  [Q/E] bench  [Enter] confirm  [Esc] cancel";
            }
        }
        else if (focusRow == FocusRow.Buttons)
            modeText.text = "[A/D] buttons  [W] units  [Enter] activate";
        else
        {
            string mouseHint = mouseSelectionLocked
                ? "Mouse locked - click again to release"
                : "Hover a unit to select";
            modeText.text = $"[A/D] units  [S] buttons\n{mouseHint}";
        }
    }

    if (suggestionText != null)
    {
        if (runtime.IsEnemyTurn && runtime.CurrentSuggestions.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("AI Suggestions:");
            foreach (var s in runtime.CurrentSuggestions)
            {
                string unitName;
                if (s.unitSlot >= 0 && s.unitSlot < 4)
                {
                    var unit = combat.GetUnit(CombatScript.TeamEnemy, s.unitSlot);
                    unitName = string.IsNullOrEmpty(unit.name) ? $"slot {s.unitSlot}" : unit.name;
                }
                else
                    unitName = "-";
                sb.AppendLine($"  [{unitName}] {s.description}");
            }
            suggestionText.text = sb.ToString();
        }
        else
            suggestionText.text = "";
    }
}

#endregion


#region Highlights

void ApplyAllHighlights()
{
    HideAllIndicators();
    ClearButtonHighlights();

    int team = runtime.ActiveTeamId;
    if (team < 0) return;
    if (runtime.State != CombatRuntimeManager.BattleState.InProgress) return;

    if (swapMode == SwapMode.PickTarget)
    {
        if (selectedSlot >= 0)
            ShowIndicator(team, selectedSlot, swapSourceColor);

        if (swapHoverBenchIndex < 0 && hoveredBenchIndex < 0
            && swapTargetSlot >= 0 && swapTargetSlot != selectedSlot)
        {
            ShowIndicator(team, swapTargetSlot, swapTargetColor);
        }

        int highlightBench = swapHoverBenchIndex >= 0 ? swapHoverBenchIndex : hoveredBenchIndex;
        if (highlightBench >= 0)
            ShowBenchIndicator(team, highlightBench, swapTargetColor);

        int activeButtonIdx = GetActiveButtonIndex();
        if (activeButtonIdx >= 0 && activeButtonIdx < navigableButtons.Count)
        {
            Button btn = navigableButtons[activeButtonIdx];
            if (btn != null && btn.interactable)
            {
                HighlightButton(btn, focusedButtonColor);
                ScaleButton(btn, focusedButtonScale);
            }
        }
        return;
    }

    if (swapMode == SwapMode.PickBackRow)
    {
        if (swapBenchIndex >= 0)
            ShowBenchIndicator(team, swapBenchIndex, swapSourceColor);

        ShowIndicator(team, 2, swapTargetColor);
        ShowIndicator(team, 3, swapTargetColor);

        // Emphasise the currently-picked target.
        if (swapTargetSlot == 2 || swapTargetSlot == 3)
            ShowIndicator(team, swapTargetSlot, selectedUnitColor);

        int activeButtonIdx = GetActiveButtonIndex();
        if (activeButtonIdx >= 0 && activeButtonIdx < navigableButtons.Count)
        {
            Button btn = navigableButtons[activeButtonIdx];
            if (btn != null && btn.interactable)
            {
                HighlightButton(btn, focusedButtonColor);
                ScaleButton(btn, focusedButtonScale);
            }
        }
        return;
    }

    var preview = GetSelectedActionPreview();
    if (preview.HasTargets)
    {
        foreach (var t in preview.targets)
        {
            if (!runtime.IsUnitAlive(t.team, t.slot)) continue;
            Color c = t.isOffensive ? enemyTargetColor : allyTargetColor;
            ShowIndicator(t.team, t.slot, c);
        }
    }

    if (selectedSlot >= 0 && team >= 0)
        ShowIndicator(team, selectedSlot, selectedUnitColor);

    if (hoveredUnitSlot >= 0
        && hoveredUnitSlot != selectedSlot
        && runtime.IsUnitAlive(team, hoveredUnitSlot))
    {
        ShowIndicator(team, hoveredUnitSlot, hoveredUnitColor);
    }

    int defaultActiveButtonIdx = GetActiveButtonIndex();
    if (defaultActiveButtonIdx >= 0
        && defaultActiveButtonIdx < navigableButtons.Count)
    {
        Button btn = navigableButtons[defaultActiveButtonIdx];
        if (btn != null && btn.interactable)
        {
            HighlightButton(btn, focusedButtonColor);
            ScaleButton(btn, focusedButtonScale);
        }
    }
}

int GetActiveButtonIndex()
{
    if (hoveredButtonIndex >= 0) return hoveredButtonIndex;
    if (focusRow == FocusRow.Buttons) return focusButtonIndex;
    return -1;
}

CombatScript.ActionTargetPreview GetSelectedActionPreview()
{
    if (selectedSlot < 0) return default;

    int team = runtime.ActiveTeamId;
    if (team < 0) return default;

    int buttonIdx = GetActiveButtonIndex();
    if (buttonIdx < 0) return default;

    int ActionIndex = NavigableIndexToActionIndex(buttonIdx);
    if (ActionIndex < 0) return default;

    return combat.GetCurrentTargetPreview(team, selectedSlot, ActionIndex);
}

int NavigableIndexToActionIndex(int navIdx)
{
    if (ActionButtons == null) return -1;
    int count = 0;
    for (int i = 0; i < ActionButtons.Length; i++)
    {
        if (ActionButtons[i] == null) continue;
        if (count == navIdx) return i;
        count++;
    }
    return -1;
}

void HighlightButton(Button btn, Color color)
{
    if (btn == null) return;
    var g = btn.targetGraphic as Graphic;
    if (g == null) g = btn.GetComponentInChildren<Graphic>();
    if (g == null) return;

    if (!originalGraphicColors.ContainsKey(g))
        originalGraphicColors[g] = g.color;

    g.color = color;
    highlightedGraphics.Add(g);
}

void ScaleButton(Button btn, float scale)
{
    if (btn == null) return;
    var t = btn.transform;
    if (!originalButtonScales.ContainsKey(t))
        originalButtonScales[t] = t.localScale;
    t.localScale = originalButtonScales[t] * scale;
    scaledButtons.Add(t);
}

void ClearButtonHighlights()
{
    foreach (var g in highlightedGraphics)
    {
        if (g == null) continue;
        if (originalGraphicColors.TryGetValue(g, out Color c))
            g.color = c;
    }
    highlightedGraphics.Clear();

    foreach (var t in scaledButtons)
    {
        if (t == null) continue;
        if (originalButtonScales.TryGetValue(t, out Vector3 s))
            t.localScale = s;
    }
    scaledButtons.Clear();

    var deadGraphics = new List<Graphic>();
    foreach (var kvp in originalGraphicColors)
        if (kvp.Key == null) deadGraphics.Add(kvp.Key);
    foreach (var g in deadGraphics) originalGraphicColors.Remove(g);

    var deadTransforms = new List<Transform>();
    foreach (var kvp in originalButtonScales)
        if (kvp.Key == null) deadTransforms.Add(kvp.Key);
    foreach (var t in deadTransforms) originalButtonScales.Remove(t);
}

#endregion

#region Bench Panel (uGUI)

void EnsureBenchPanel()
{
    if (benchPanel == null)
    {
        Debug.LogWarning("[CombatUIManager] Bench Panel not assigned in inspector - bench swap UI disabled. Assign benchPanel to enable it.");
        return;
    }

    if (!benchButtonsBuilt)
        BuildBenchButtonUIs();

    HideAllBenchButtons(playerBenchButtonUIs);
    HideAllBenchButtons(enemyBenchButtonUIs);
    benchPanel.gameObject.SetActive(false);
}

void BuildBenchButtonUIs()
{
    playerBenchButtonUIs.Clear();
    enemyBenchButtonUIs.Clear();

    BuildTeamBenchButtonUIs(playerBenchButtons, playerBenchButtonUIs);
    BuildTeamBenchButtonUIs(enemyBenchButtons,  enemyBenchButtonUIs);

    benchButtonsBuilt = true;
}

void BuildTeamBenchButtonUIs(GameObject[] roots, List<BenchButtonUI> output)
{
    if (roots == null) return;
    for (int i = 0; i < roots.Length; i++)
    {
        var go = roots[i];
        if (go == null) continue;

        var ui = new BenchButtonUI { benchIndex = -1 };
        ui.root = go;

        ui.button = go.GetComponent<Button>();
        if (ui.button == null)
        {
            Debug.LogWarning($"[CombatUIManager] Bench button '{go.name}' has no Button component - clicks will be ignored.");
        }

        ui.background = go.GetComponent<Image>();
        if (ui.background == null) ui.background = go.GetComponentInChildren<Image>();

        ui.icon      = FindChildImage(go, "Icon");
        ui.hpBarFill = FindChildImage(go, "HPBar");
        ui.nameText  = FindChildText(go, "Name");
        ui.infoText  = FindChildText(go, "Info");

        if (ui.button != null)
        {
            ui.button.onClick.RemoveAllListeners();
            var captured = ui;
            ui.button.onClick.AddListener(() =>
            {
                if (captured.benchIndex >= 0)
                    ClickBenchUnit(captured.benchIndex);
            });
        }

        go.SetActive(false);
        output.Add(ui);
    }
}

void HideAllBenchButtons(List<BenchButtonUI> list)
{
    if (list == null) return;
    for (int i = 0; i < list.Count; i++)
    {
        var b = list[i];
        if (b == null) continue;
        if (b.root != null && b.root.activeSelf) b.root.SetActive(false);
        b.benchIndex = -1;
    }
}

static Image FindChildImage(GameObject root, string name)
{
    if (root == null) return null;
    var t = root.transform.Find(name);
    if (t != null) return t.GetComponent<Image>();
    // Recursive fallback.
    foreach (var img in root.GetComponentsInChildren<Image>(true))
        if (img.gameObject.name == name) return img;
    return null;
}

static TMP_Text FindChildText(GameObject root, string name)
{
    if (root == null) return null;
    var t = root.transform.Find(name);
    if (t != null) return t.GetComponent<TMP_Text>();
    foreach (var txt in root.GetComponentsInChildren<TMP_Text>(true))
        if (txt.gameObject.name == name) return txt;
    return null;
}

void RefreshBenchPanel()
{
    if (benchPanel == null) EnsureBenchPanel();
    if (benchPanel == null) { hoveredBenchIndex = -1; return; }
    if (!benchButtonsBuilt) BuildBenchButtonUIs();

    bool inProgress = runtime != null
                      && runtime.State == CombatRuntimeManager.BattleState.InProgress;
    bool show = inProgress && swapMode != SwapMode.None;

    int team = runtime != null ? runtime.ActiveTeamId : -1;
    var activeList = ActiveBenchButtonUIs;
    var inactiveList = (team == CombatScript.TeamPlayer) ? enemyBenchButtonUIs
                     : (team == CombatScript.TeamEnemy)  ? playerBenchButtonUIs
                     : null;

    HideAllBenchButtons(inactiveList);

    if (!show)
    {
        if (benchPanel.gameObject.activeSelf)
            benchPanel.gameObject.SetActive(false);
        HideAllBenchButtons(activeList);
        hoveredBenchIndex = -1;
        return;
    }

    if (!benchPanel.gameObject.activeSelf)
        benchPanel.gameObject.SetActive(true);

    var bench = team >= 0 ? runtime.GetBench(team) : null;

    int shown = 0;
    if (bench != null)
    {
        for (int i = 0; i < bench.Count && shown < (activeList != null ? activeList.Count : 0); i++)
        {
            var u = bench[i];
            if (string.IsNullOrEmpty(u.name)) continue; // skip empty bench entries
            ApplyBenchButton(activeList[shown], i, u, team);
            shown++;
        }
    }

    if (activeList != null)
    {
        for (int i = shown; i < activeList.Count; i++)
        {
            var b = activeList[i];
            if (b == null) continue;
            if (b.root != null && b.root.activeSelf) b.root.SetActive(false);
            b.benchIndex = -1;
        }
    }

    DetectHoveredBenchButton();
}

void ApplyBenchButton(BenchButtonUI b, int benchIndex, CombatScript.UnitData u, int team)
{
    b.benchIndex = benchIndex;
    if (b.root != null && !b.root.activeSelf) b.root.SetActive(true);

    bool living = CombatRuntimeManager.IsBenchUnitAlive(u);
    bool isSource = (swapMode == SwapMode.PickBackRow && swapBenchIndex == benchIndex);
    bool isHovered = (hoveredBenchIndex == benchIndex) || (swapHoverBenchIndex == benchIndex);

    // Background colour reflects the button's role.
    Color bg;
    if (!living)            bg = benchBgDeadColor;
    else if (isSource)       bg = benchBgSourceColor;
    else if (isHovered && swapMode == SwapMode.PickTarget) bg = benchBgHoverColor;
    else                    bg = benchBgColor;
    if (b.background != null) b.background.color = bg;

    if (b.button != null)
        b.button.interactable = living && swapMode == SwapMode.PickTarget;

    if (b.hpBarFill != null)
    {
        float frac = u.maxHp > 0f ? Mathf.Clamp01(u.hp / u.maxHp) : 0f;
        b.hpBarFill.fillAmount = frac;
        b.hpBarFill.color = frac > 0.5f ? benchHpColor
                          : frac > 0.25f ? new Color(1f, 0.75f, 0.3f, 1f)
                          : benchHpLowColor;
    }

    if (b.nameText != null)
    {
        string nm = string.IsNullOrEmpty(u.name) ? "(empty)" : u.name;
        b.nameText.text = living ? nm : $"{nm}\n(fainted)";
        b.nameText.color = living ? benchNameColor : new Color(0.6f, 0.6f, 0.6f, 1f);
    }

    if (b.infoText != null)
    {
        if (!living)
        {
            b.infoText.text = "FAINTED";
            b.infoText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        }
        else
        {
            b.infoText.text = $"AR {u.armor:0}  SH {u.shield:0}  AP {u.ap:0}";
            b.infoText.color = new Color(0.8f, 0.8f, 0.85f, 1f);
        }
    }
}

#endregion


#region Public

public int SelectedSlot => selectedSlot;

public void RefreshNow()
{
    if (combat == null) return;
    RebuildNavigableButtons();
    RefreshActionButtons();
    RefreshDisplay();
    ApplyAllHighlights();
}

#endregion


#region Floating Text

static readonly System.Random _visualRng = new System.Random(0);

// spawn popup at the unit that took damage or got healed
void HandleFloatingText(CombatScript.FloatingTextInfo info)
{
    if (combat == null) return;
    var unit = combat.GetUnit(info.team, info.slot);
    if (unit.PlayerUnit == null) return;

    // random horizontal offset so multiple popups dont overlap
    float xOff = (float)(_visualRng.NextDouble() * 1.6 - 0.8);
    float yOff = (float)(_visualRng.NextDouble() * 0.6 - 0.3);
    Vector3 worldPos = unit.PlayerUnit.transform.position + floatingTextOffset + new Vector3(xOff, yOff, 0);
    SpawnFloatingText(worldPos, info);
}

void SpawnFloatingText(Vector3 worldPos, CombatScript.FloatingTextInfo info)
{
    GameObject go;
    TMPro.TextMeshPro tmpro = null;

    if (floatingTextPrefab != null)
    {
        go = Instantiate(floatingTextPrefab, worldPos, Quaternion.identity);
        tmpro = go.GetComponentInChildren<TMPro.TextMeshPro>();
    }
    else
    {
        go = new GameObject("FloatingText");
        go.transform.position = worldPos;
        tmpro = go.AddComponent<TMPro.TextMeshPro>();
    }

    if (tmpro == null)
    {
        Destroy(go);
        return;
    }

    Color c = damageColor;
    string text = "";
    int fontSize = floatingTextFontSize;

    switch (info.type)
    {
        case CombatScript.FloatingTextType.Damage:
            c = damageColor;
            text = "-" + info.amount;
            break;
        case CombatScript.FloatingTextType.Heal:
            c = healColor;
            text = "+" + info.amount;
            break;
        case CombatScript.FloatingTextType.Dot:
            c = dotColor;
            text = "-" + info.amount;
            break;
        case CombatScript.FloatingTextType.Buff:
            c = new Color(0.4f, 0.8f, 1f, 1f); // cyan
            text = "+" + info.amount + " " + GetBuffLabel(info.kind);
            break;
        case CombatScript.FloatingTextType.Status:
            c = new Color(0.9f, 0.3f, 0.9f, 1f); // purple
            text = info.text;
            break;
        case CombatScript.FloatingTextType.Miss:
            c = new Color(0.7f, 0.7f, 0.7f, 1f); // gray
            text = "MISS";
            break;
        case CombatScript.FloatingTextType.Fumble:
            c = new Color(0.5f, 0.5f, 0.5f, 1f); // dark gray
            text = "FUMBLE!";
            fontSize = floatingTextFontSize + 4;
            break;
        case CombatScript.FloatingTextType.HalfMiss:
            c = new Color(0.8f, 0.6f, 0.2f, 1f); // orange
            text = "GLANCING";
            break;
        case CombatScript.FloatingTextType.Crit:
            c = new Color(1f, 0.85f, 0.1f, 1f); // gold
            text = "CRIT! -" + info.amount;
            fontSize = floatingTextFontSize + 6;
            break;
        case CombatScript.FloatingTextType.SuperCrit:
            c = new Color(1f, 0.4f, 0.1f, 1f); // bright orange
            text = "SUPER CRIT! -" + info.amount;
            fontSize = floatingTextFontSize + 10;
            break;
        case CombatScript.FloatingTextType.Info:
            c = Color.white;
            text = info.text;
            break;
    }

    tmpro.text = text;
    tmpro.fontSize = fontSize;
    tmpro.enableAutoSizing = false;
    tmpro.color = c;
    tmpro.alignment = TMPro.TextAlignmentOptions.Center;
    tmpro.sortingOrder = 200;

    // force color on material too tmp ignores .color on instantiated prefabs
    tmpro.ForceMeshUpdate();
    tmpro.fontMaterial.color = c;
    tmpro.fontMaterial.SetColor("_FaceColor", c);

    StartCoroutine(AnimateFloatingText(go, worldPos));
}

string GetBuffLabel(CombatScript.EffectKind kind)
{
    switch (kind)
    {
        case CombatScript.EffectKind.AddShield:
        case CombatScript.EffectKind.RecoverShield:
            return "SH";
        case CombatScript.EffectKind.AddArmor:
        case CombatScript.EffectKind.RecoverArmor:
            return "AR";
        case CombatScript.EffectKind.AddTempHealth:
            return "TH";
        case CombatScript.EffectKind.AddHealth:
        case CombatScript.EffectKind.RecoverHealth:
            return "HP";
        default:
            return "";
    }
}

IEnumerator AnimateFloatingText(GameObject go, Vector3 startPos)
{
    if (go == null) yield break;
    float elapsed = 0f;
    var tmpro = go.GetComponentInChildren<TMPro.TextMeshPro>();

    while (elapsed < floatingTextDuration && go != null)
    {
        elapsed += Time.deltaTime;
        float t = elapsed / floatingTextDuration;
        go.transform.position = startPos + Vector3.up * floatingTextRiseSpeed * elapsed;
        if (tmpro != null)
        {
            var col = tmpro.color;
            col.a = Mathf.Lerp(1f, 0f, t);
            tmpro.color = col;
            // fade material too
            var faceCol = tmpro.fontMaterial.GetColor("_FaceColor");
            faceCol.a = col.a;
            tmpro.fontMaterial.SetColor("_FaceColor", faceCol);
        }
        yield return null;
    }
    if (go != null) Destroy(go);
}

#endregion

}
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(0)]
public class CombatScript : MonoBehaviour
{
    #region All the nested data 

    // -------------------------------------------------------------------------
    // Targeting structs
    // -------------------------------------------------------------------------
    [System.Serializable]
    public struct TargetEntry
    {
        public int   slot;
        public float portion;
    }

    [System.Serializable]
    public struct CasterPattern
    {
        public int               casterSlot;
        public List<TargetEntry> targets;
    }

    // -------------------------------------------------------------------------
    // Action data
    // -------------------------------------------------------------------------
    [System.Serializable]
    public struct ActionData
    {
        public string fullName;
        public string ActionName;
        public string overrideKey;
        public string overrideCategory; // "basic", "skill", "ability", or "" for default executor
        public string hitRoll;
        public int    APCost;
        public int    useLimit;
        public bool   hitAlive;          // make this Action autotarget the nearest alive target if the attack is lined up for a dead unit

        public string physicalDmg;
        public List<CasterPattern> physicalCoords;
        public string fireDmg;
        public List<CasterPattern> fireCoords;
        public int fireDuration;
        public string bleedDmg;
        public List<CasterPattern> bleedCoords;
        public int bleedDuration;
        public string magicDmg;
        public List<CasterPattern> magicCoords;
        public string poisonDmg;
        public List<CasterPattern> poisonCoords;
        public int poisonDuration;

        public string stunTime;
        public List<CasterPattern> stunCoords;
        public string sleepTime;
        public List<CasterPattern> sleepCoords;
        public string confusionTime;
        public List<CasterPattern> confusionCoords;
        public string petrificationTime;
        public List<CasterPattern> petrificationCoords;

        public string recoverHealth;
        public List<CasterPattern> recoverHealthCoords;
        public string addTempHealth;
        public List<CasterPattern> addTempHealthCoords;
        public string addHealth;
        public List<CasterPattern> addHealthCoords;
        public string recoverArmor;
        public List<CasterPattern> recoverArmorCoords;
        public string recoverShield;
        public List<CasterPattern> recoverShieldCoords;
        public string addArmor;
        public List<CasterPattern> addArmorCoords;
        public string addShield;
        public List<CasterPattern> addShieldCoords;
    }

    // -------------------------------------------------------------------------
    // Unit data
    // -------------------------------------------------------------------------
    // struct with list fields copies share the same lists
    // always use SetUnit after mutating scalar fields
    [System.Serializable]
    public struct UnitData
    {
        public string     unitcode;
        public GameObject PlayerUnit;
        public GameObject PlayerUnitSpace;
        public string     name;
        public float      hp;
        public float      maxHp;
        public float      ac;
        public float      armor;
        public float      maxArmor;
        public float      armorBlock;
        public float      shield;
        public float      maxShield;
        public float      th;
        public float      ap;     // action points (formerly "en" / energy)
        public List<ActionData> actions;
        public List<int> ActionUseCounts;

        public int   fireRemainingRounds;
        public string fireDamageNotation;
        public int   bleedRemainingRounds;
        public string bleedDamageNotation;
        public int   poisonRemainingRounds;
        public string poisonDamageNotation;

        public int   stunRemainingRounds;
        public int   sleepRemainingRounds;
        public int   confusionRemainingRounds;
        public int   petrificationRemainingRounds;

        // ---- Custom statuses ----
        public int   tauntTargetSlot;         // -1 = no taunt 0-7 = the taunters slot
        public int   tauntRemainingRounds;

        // ---- Passives ----
        public List<string> passiveKeys;
    }

    // -------------------------------------------------------------------------
    // Teams
    // -------------------------------------------------------------------------
    [System.Serializable]
    public struct PlayerTeamData
    {
        public UnitData unit_0, unit_1, unit_2, unit_3;
    }

    [System.Serializable]
    public struct EnemyTeamData
    {
        public UnitData unit_0, unit_1, unit_2, unit_3;
    }

    // -------------------------------------------------------------------------
    // AP bank
    // -------------------------------------------------------------------------
    [System.Serializable]
    public struct APBank
    {
        public float current;
        public float startOfTurnTotal;

        public void Spend(float cost)
        {
            current = Mathf.Max(0, current - cost);
        }

        public void Add(float amount)
        {
            current += amount;
        }

        public void Clamp(float cap)
        {
            if (current > cap) current = cap;
            if (startOfTurnTotal > cap) startOfTurnTotal = cap;
        }
    }

    // -------------------------------------------------------------------------
    // Effect kinds (what an Action does to a target)
    // -------------------------------------------------------------------------
    public enum EffectKind
    {
        Physical,
        Magic,
        FireDmg,
        BleedDmg,
        PoisonDmg,
        RecoverHealth,
        AddHealth,
        AddTempHealth,
        RecoverShield,
        AddShield,
        RecoverArmor,
        AddArmor,
        Stun,
        Sleep,
        Confusion,
        Petrification,
        Unsupported
    }

    #endregion


    #region Constants & Team Instances

    public const int BaseAPCap = 4;
    public const int TeamPlayer = 0;
    public const int TeamEnemy  = 1;

    public PlayerTeamData PlayerTeam
    {
        get => Runtime != null ? Runtime.PlayerTeam : default;
        set { if (Runtime != null) Runtime.PlayerTeam = value; }
    }
    public EnemyTeamData EnemyTeam
    {
        get => Runtime != null ? Runtime.EnemyTeam : default;
        set { if (Runtime != null) Runtime.EnemyTeam = value; }
    }

    #endregion


    #region Config

    [SerializeField] private diceSystem diceRoller;

    #endregion


    #region Runtime State

    private Dictionary<(int team, int slot), List<iPassive>> passiveInstances =
        new Dictionary<(int team, int slot), List<iPassive>>();

    public APBank playerAPBank
    {
        get => Runtime != null ? Runtime.playerAPBank : default;
        set { if (Runtime != null) Runtime.playerAPBank = value; }
    }
    public APBank enemyAPBank
    {
        get => Runtime != null ? Runtime.enemyAPBank : default;
        set { if (Runtime != null) Runtime.enemyAPBank = value; }
    }

    private bool isExecutingAction;
    private Coroutine executingActionCoroutine;
    private int _executionGeneration;

    private bool isProcessingPassiveReaction;

    private readonly Queue<(int team, int slot, int killerTeam, int killerSlot)> pendingDeaths =
        new Queue<(int, int, int, int)>();
    private bool isProcessingDeathFanout;

    private readonly HashSet<(int, int)> _computingACSet = new HashSet<(int, int)>();

    public bool IsExecutingAction => isExecutingAction;

    public void ForceClearExecutingFlag()
    {
        if (isExecutingAction)
        {
            Debug.LogWarning("[CombatScript] ForceClearExecutingFlag called - an Action coroutine may have hung.");
            if (executingActionCoroutine != null)
            {
                StopCoroutine(executingActionCoroutine);
                executingActionCoroutine = null;
            }
            isExecutingAction = false;
            _executionGeneration++;
        }
    }

    public diceSystem DiceRollerPublic => diceRoller;

    // floating text event for damage heal buff status miss crit everything
    public enum FloatingTextType { Damage, Heal, Dot, Buff, Status, Miss, Crit, Info, Fumble, HalfMiss, SuperCrit }
    public struct FloatingTextInfo
    {
        public int team;
        public int slot;
        public int amount;
        public EffectKind kind;
        public bool isHeal;
        public bool isDot;
        public bool isCrit;
        public FloatingTextType type;
        public string text;
    }
    public static event System.Action<FloatingTextInfo> OnFloatingText;
    public static void FireFloatingText(int team, int slot, int amount, EffectKind kind, bool isHeal, bool isDot, bool isCrit = false)
    {
        if (amount == 0) return;
        OnFloatingText?.Invoke(new FloatingTextInfo { team = team, slot = slot, amount = amount, kind = kind, isHeal = isHeal, isDot = isDot, isCrit = isCrit, type = isHeal ? FloatingTextType.Heal : (isDot ? FloatingTextType.Dot : (isCrit ? FloatingTextType.Crit : FloatingTextType.Damage)) });
    }
    // generic text popup for status buff miss info etc
    public static void FireFloatingText(int team, int slot, string text, FloatingTextType type = FloatingTextType.Info)
    {
        OnFloatingText?.Invoke(new FloatingTextInfo { team = team, slot = slot, amount = 0, kind = EffectKind.Unsupported, isHeal = false, isDot = false, isCrit = false, type = type, text = text });
    }
    // buff popup for shield armor temp hp gains
    public static void FireBuffText(int team, int slot, int amount, EffectKind kind)
    {
        if (amount == 0) return;
        OnFloatingText?.Invoke(new FloatingTextInfo { team = team, slot = slot, amount = amount, kind = kind, isHeal = false, isDot = false, isCrit = false, type = FloatingTextType.Buff });
    }
    // status popup for stun sleep confusion petrify taunt
    public static void FireStatusText(int team, int slot, string statusName)
    {
        OnFloatingText?.Invoke(new FloatingTextInfo { team = team, slot = slot, amount = 0, kind = EffectKind.Unsupported, isHeal = false, isDot = false, isCrit = false, type = FloatingTextType.Status, text = statusName });
    }

    public bool IsTeamAlive(int team)
    {
        for (int s = 0; s < 4; s++)
        {
            UnitData u = GetUnit(team, s);
            if (!string.IsNullOrEmpty(u.name) && u.hp > 0) return true;
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // Bench integration either place the unit codes in runtime or in combat 
    // -------------------------------------------------------------------------
    public CombatRuntimeManager Runtime { get; private set; }

    public bool IsTeamViable(int team)
    {
        // Active deployed living units count.
        if (IsTeamAlive(team)) return true;
        // Benched living units also count.
        if (Runtime != null && Runtime.BenchHasLiving(team)) return true;
        return false;
    }

    public bool IsBattleOver()
    {
        return !IsTeamViable(TeamPlayer) || !IsTeamViable(TeamEnemy);
    }

    public bool IsUnitActiveAndAlive(int team, int slot)
    {
        UnitData u = GetUnit(team, slot);
        return !string.IsNullOrEmpty(u.name) && u.hp > 0;
    }

    public bool IsUnitVanished(int team, int slot)
    {
        return CustomStatusRuntime.IsActive(team, slot, "VanishStatus");
    }

    public void CancelExecutingAction()
    {
        if (executingActionCoroutine != null)
        {
            StopCoroutine(executingActionCoroutine);
            executingActionCoroutine = null;
        }
        isExecutingAction = false;
    }

    #endregion


    #region Unity Lifecycle

    void Start()
    {
        BuildOverrideRegistry();
        Runtime = FindFirstObjectByType<CombatRuntimeManager>();
    }

    void OnDisable()
    {
        isExecutingAction = false;
        executingActionCoroutine = null;
        pendingDeaths.Clear();
        isProcessingDeathFanout = false;

        foreach (var kvp in _runningJiggles)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        }
        _runningJiggles.Clear();

        OnFloatingText = null;
    }

    #endregion


    #region Battle Setup

    void BuildOverrideRegistry()
    {
        OverrideRegistry.ClearSceneHandlers();

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        if (behaviours == null) return;

        foreach (var behaviour in behaviours)
        {
            if (behaviour is iBasic b && !string.IsNullOrWhiteSpace(b.OverrideKey))
                OverrideRegistry.RegisterSceneBasic(b.OverrideKey, b);
            if (behaviour is iSkill s && !string.IsNullOrWhiteSpace(s.OverrideKey))
                OverrideRegistry.RegisterSceneSkill(s.OverrideKey, s);
            if (behaviour is iAbility a && !string.IsNullOrWhiteSpace(a.OverrideKey))
                OverrideRegistry.RegisterSceneAbility(a.OverrideKey, a);
        }
    }

    public void BuildOverrideRegistryPublic() => BuildOverrideRegistry();

    #endregion


    #region Round / Turn Lifecycle

    public void ProcessEndOfRound()
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log("[End of Round] Ticking all DOTs/statuses.");
        for (int team = 0; team < 2; team++)
            for (int slot = 0; slot < 4; slot++)
                ProcessUnitStatusEffects(team, slot);

        if (CustomStatusRuntime.Instance != null)
            CustomStatusRuntime.Instance.TickAll();

        FireOnRoundEnd();
    }
    private void ProcessUnitStatusEffects(int team, int slot)
    {
        UnitData unit = GetUnit(team, slot);
        if (string.IsNullOrEmpty(unit.name) || unit.hp <= 0)
            return;

        var u = unit;

        if (u.fireRemainingRounds > 0)
        {
            bool rolled = false;
            if (!string.IsNullOrEmpty(u.fireDamageNotation))
            {
                var roll = TryRoll(u.fireDamageNotation);
                if (roll.HasValue)
                {
                    rolled = true;
                    int damage = roll.Value.total;
                    ApplyDamageWithHooks(team, slot, ref damage, EffectKind.FireDmg, 0, null,
                        -1, -1, isDamageKind: true, godModeCheck: true,
                        isDotTick: true);
                    u = GetUnit(team, slot);
                }
                else
                {
                    Debug.LogWarning($"[Fire DOT] Malformed notation '{u.fireDamageNotation}' on {u.name} - skipping damage but still expiring the burn.");
                }
            }
            if (!rolled)
            {
                u.fireRemainingRounds = Mathf.Max(0, u.fireRemainingRounds - 1);
                SetUnit(team, slot, u);
            }
            if (u.hp <= 0) return;
        }

        if (u.bleedRemainingRounds > 0)
        {
            bool rolled = false;
            if (!string.IsNullOrEmpty(u.bleedDamageNotation))
            {
                var roll = TryRoll(u.bleedDamageNotation);
                if (roll.HasValue)
                {
                    rolled = true;
                    int damage = roll.Value.total;
                    ApplyDamageWithHooks(team, slot, ref damage, EffectKind.BleedDmg, 0, null,
                        -1, -1, isDamageKind: true, godModeCheck: true,
                        isDotTick: true);
                    u = GetUnit(team, slot);
                }
                else
                {
                    Debug.LogWarning($"[Bleed DOT] Malformed notation '{u.bleedDamageNotation}' on {u.name} - skipping damage but still expiring the bleed.");
                }
            }
            if (!rolled)
            {
                u.bleedRemainingRounds = Mathf.Max(0, u.bleedRemainingRounds - 1);
                SetUnit(team, slot, u);
            }
            if (u.hp <= 0) return;
        }

        if (u.poisonRemainingRounds > 0)
        {
            bool rolled = false;
            if (!string.IsNullOrEmpty(u.poisonDamageNotation))
            {
                var roll = TryRoll(u.poisonDamageNotation);
                if (roll.HasValue)
                {
                    rolled = true;
                    int damage = roll.Value.total;
                    ApplyDamageWithHooks(team, slot, ref damage, EffectKind.PoisonDmg, 0, null,
                        -1, -1, isDamageKind: true, godModeCheck: true,
                        isDotTick: true);
                    u = GetUnit(team, slot);
                }
                else
                {
                    Debug.LogWarning($"[Poison DOT] Malformed notation '{u.poisonDamageNotation}' on {u.name} - skipping damage but still expiring the poison.");
                }
            }
            if (!rolled)
            {
                u.poisonRemainingRounds = Mathf.Max(0, u.poisonRemainingRounds - 1);
                SetUnit(team, slot, u);
            }
            if (u.hp <= 0) return;
        }

        u = GetUnit(team, slot);
        bool updated = false;
        if (u.stunRemainingRounds > 0) { u.stunRemainingRounds--; updated = true; }
        if (u.sleepRemainingRounds > 0) { u.sleepRemainingRounds--; updated = true; }
        if (u.confusionRemainingRounds > 0) { u.confusionRemainingRounds--; updated = true; }
        if (u.petrificationRemainingRounds > 0) { u.petrificationRemainingRounds--; updated = true; }
        if (u.tauntRemainingRounds > 0)
        {
            u.tauntRemainingRounds--;
            if (u.tauntRemainingRounds <= 0)
                u.tauntTargetSlot = -1;
            updated = true;
        }
        if (u.th > 0) { u.th = Mathf.Max(0, u.th - 1); updated = true; }

        if (updated)
            SetUnit(team, slot, u);
    }

    // -------------------------------------------------------------------------
    // ResetActionCounts
    // -------------------------------------------------------------------------
    public void ResetActionCounts()
    {
        for (int team = 0; team < 2; team++)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                UnitData u = GetUnit(team, slot);
                if (u.ActionUseCounts != null)
                {
                    for (int i = 0; i < u.ActionUseCounts.Count; i++)
                        u.ActionUseCounts[i] = 0;
                    SetUnit(team, slot, u);
                }
            }
        }
        if (CombatDebugHandler.UltraDebug) Debug.Log("Action use counts reset.");
    }

    public void ClearAllDOTsForAllUnits()
    {
        for (int team = 0; team < 2; team++)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                UnitData u = GetUnit(team, slot);
                if (string.IsNullOrEmpty(u.name)) continue;
                u.fireRemainingRounds = 0;
                u.bleedRemainingRounds = 0;
                u.poisonRemainingRounds = 0;
                u.fireDamageNotation = "";
                u.bleedDamageNotation = "";
                u.poisonDamageNotation = "";
                u.stunRemainingRounds = 0;
                u.sleepRemainingRounds = 0;
                u.confusionRemainingRounds = 0;
                u.petrificationRemainingRounds = 0;
                u.tauntRemainingRounds = 0;
                u.tauntTargetSlot = -1;
                u.th = 0;
                SetUnit(team, slot, u);
            }
        }
    }

    #endregion


    #region Action Gating

    public bool CanUseAction(int team, int slot, int ActionIndex)
    {
        return CanUseAction(GetUnit(team, slot), team, ActionIndex);
    }

    public bool CanUseAction(UnitData caster, int team, int ActionIndex)
    {
        if (string.IsNullOrEmpty(caster.name) || caster.hp <= 0) return false;

        // Status blockers
        if (caster.stunRemainingRounds > 0) return false;
        if (caster.sleepRemainingRounds > 0) return false;
        if (caster.petrificationRemainingRounds > 0) return false;

        if (caster.actions == null || ActionIndex < 0 || ActionIndex >= caster.actions.Count)
            return false;

        ActionData action = caster.actions[ActionIndex];

        if (!string.IsNullOrWhiteSpace(action.overrideKey) &&
            !string.IsNullOrWhiteSpace(action.overrideCategory))
        {
            string cat = action.overrideCategory.Trim().ToLowerInvariant();
            bool found = cat switch
            {
                "basic"   => OverrideRegistry.GetBasic(action.overrideKey) != null,
                "skill"   => OverrideRegistry.GetSkill(action.overrideKey) != null,
                "ability" => OverrideRegistry.GetAbility(action.overrideKey) != null,
                _          => false,
            };
            if (!found) return false;
        }

        // Use limit
        if (action.useLimit > 0)
        {
            if (caster.ActionUseCounts == null || ActionIndex >= caster.ActionUseCounts.Count)
                return false;
            if (caster.ActionUseCounts[ActionIndex] >= action.useLimit)
                return false;
        }

        // AP cost
        if (action.APCost > 0)
        {
            ref APBank bank = ref GetAPBank(team);
            if (bank.current < action.APCost) return false;
        }

        return true;
    }

    // -------------------------------------------------------------------------
    // TryUseAction with status checks
    // -------------------------------------------------------------------------
    public bool TryUseAction(int team, int slot, int ActionIndex)
    {
        if (isExecutingAction)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log("[CombatScript] Another Action is already executing.");
            return false;
        }

        if (!CanUseAction(team, slot, ActionIndex))
            return false;

        UnitData caster = GetUnit(team, slot);
        ActionData action = caster.actions[ActionIndex];

        if (!TrySpendAP(team, action.APCost))
            return false;

        if (action.useLimit > 0)
        {
            caster.ActionUseCounts[ActionIndex]++;
            SetUnit(team, slot, caster);
        }

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Team {team}] {caster.name} uses {action.ActionName} (slot {ActionIndex})");

        executingActionCoroutine = StartCoroutine(ExecuteAction(team, slot, action));

        return true;
    }

    #endregion


    #region Action Execution Pipeline

    public IEnumerator ExecuteAction(int casterTeam, int casterSlot, ActionData action)
    {
        if (isExecutingAction)
        {
            Debug.LogWarning("[CombatScript] ExecuteAction called while another Action is already executing - ignoring.");
            yield break;
        }

        UnitData caster = GetUnit(casterTeam, casterSlot);
        if (string.IsNullOrEmpty(caster.name) || caster.hp <= 0)
        {
            Debug.LogWarning($"[CombatScript] ExecuteAction called on dead/empty caster (team {casterTeam}, slot {casterSlot}) - ignoring.");
            yield break;
        }

        isExecutingAction = true;
        ClearProcs();

        int myGeneration = ++_executionGeneration;

        try
        {
            ActionData modifiedAction = ApplyTauntRedirect(casterTeam, casterSlot, action);
            modifiedAction = ApplyConfusionRedirect(casterTeam, casterSlot, modifiedAction);

            FireOnActionCast(casterTeam, casterSlot, modifiedAction);

            if (string.IsNullOrWhiteSpace(modifiedAction.overrideKey))
            {
                yield return DefaultExecuteAction(casterTeam, casterSlot, modifiedAction);
            }
            else
            {
                string cat = (modifiedAction.overrideCategory ?? "").Trim().ToLowerInvariant();
                bool dispatched = false;

                switch (cat)
                {
                    case "basic":
                        var basic = OverrideRegistry.GetBasic(modifiedAction.overrideKey);
                        if (basic != null)
                        {
                            yield return basic.Execute(this, casterTeam, casterSlot, modifiedAction);
                            dispatched = true;
                        }
                        break;

                    case "skill":
                        var skill = OverrideRegistry.GetSkill(modifiedAction.overrideKey);
                        if (skill != null)
                        {
                            yield return skill.Execute(this, casterTeam, casterSlot, modifiedAction);
                            dispatched = true;
                        }
                        break;

                    case "ability":
                        var abil = OverrideRegistry.GetAbility(modifiedAction.overrideKey);
                        if (abil != null)
                        {
                            yield return abil.Execute(this, casterTeam, casterSlot, modifiedAction);
                            dispatched = true;
                        }
                        break;

                    default:
                        Debug.LogWarning($"Action '{modifiedAction.fullName}' has override key '{modifiedAction.overrideKey}' but no recognised category. Falling back to default executor.");
                        yield return DefaultExecuteAction(casterTeam, casterSlot, modifiedAction);
                        break;
                }

                if (!dispatched &&
                    (cat == "basic" || cat == "skill" || cat == "ability"))
                {
                    Debug.LogWarning($"Action '{modifiedAction.fullName}' has override key '{modifiedAction.overrideKey}' (category {cat}) but no handler is registered.");
                }
            }
        }
        finally
        {
            if (myGeneration == _executionGeneration)
            {
                isExecutingAction = false;
                executingActionCoroutine = null;
            }
        }
    }

    ActionData ApplyConfusionRedirect(int casterTeam, int casterSlot, ActionData action)
    {
        UnitData caster = GetUnit(casterTeam, casterSlot);
        if (caster.confusionRemainingRounds <= 0) return action;
        if (diceRoller == null) return action;

        var slotRoll = diceRoller.Roll("1d8", -1, quiet: true);
        int randomSlot = slotRoll.total - 1;
        if (randomSlot < 0) randomSlot = 0;

        // skip empty or dead slots so confusion never fizzles
        var (tt, ts) = ResolveTarget(casterTeam, randomSlot);
        UnitData tu = GetUnit(tt, ts);
        if (string.IsNullOrEmpty(tu.name) || tu.hp <= 0)
        {
            int redirect = FindNearestAliveSlot(tt, ts);
            if (redirect >= 0)
                randomSlot = tt == casterTeam ? redirect : redirect + 4;
        }

        if (CombatDebugHandler.UltraDebug) Debug.Log($"{caster.name} is confused targeting slot {randomSlot}");

        return ForceSingleTarget(action, randomSlot);
    }

    ActionData ApplyTauntRedirect(int casterTeam, int casterSlot, ActionData action)
    {
        UnitData caster = GetUnit(casterTeam, casterSlot);
        if (caster.tauntRemainingRounds <= 0) return action;
        if (caster.tauntTargetSlot < 0) return action;

        int forcedSlot = caster.tauntTargetSlot;

        var (targetTeam, targetSlot) = ResolveTarget(casterTeam, forcedSlot);
        UnitData target = GetUnit(targetTeam, targetSlot);
        if (string.IsNullOrEmpty(target.name) || target.hp <= 0)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"{caster.name}'s taunt target (slot {forcedSlot}) is dead - attacking normally.");
            return action;
        }

        if (CombatDebugHandler.UltraDebug) Debug.Log($"{caster.name} is taunted! Forced to target slot {forcedSlot} ({target.name}) for {caster.tauntRemainingRounds} more round(s).");

        return ForceSingleTarget(action, forcedSlot);
    }

    static ActionData ForceSingleTarget(ActionData action, int forcedSlot)
    {
        var forcedPattern = new List<CasterPattern>
        {
            new CasterPattern
            {
                casterSlot = -1,
                targets = new List<TargetEntry>
                {
                    new TargetEntry { slot = forcedSlot, portion = 1f }
                }
            }
        };

        ActionData modified = action;
        modified.physicalCoords          = forcedPattern;
        modified.fireCoords              = forcedPattern;
        modified.bleedCoords             = forcedPattern;
        modified.magicCoords            = forcedPattern;
        modified.poisonCoords            = forcedPattern;
        modified.recoverHealthCoords     = forcedPattern;
        modified.addHealthCoords         = forcedPattern;
        modified.addTempHealthCoords     = forcedPattern;
        modified.recoverShieldCoords     = forcedPattern;
        modified.addShieldCoords         = forcedPattern;
        modified.recoverArmorCoords      = forcedPattern;
        modified.addArmorCoords          = forcedPattern;
        modified.stunCoords              = forcedPattern;
        modified.sleepCoords             = forcedPattern;
        modified.confusionCoords         = forcedPattern;
        modified.petrificationCoords     = forcedPattern;
        return modified;
    }

    IEnumerator DefaultExecuteAction(int casterTeam, int casterSlot, ActionData action)
    {
        var rollsToShow = new List<(string label, diceSystem.RollResult)>();

        // no shared hit roll anymore each target rolls independently
        bool useHitRoll = !string.IsNullOrWhiteSpace(action.hitRoll) && IsDiceNotation(action.hitRoll);

        // --- Apply effects ---
        ApplyEffect(casterTeam, casterSlot, EffectKind.Physical,
                    action.physicalDmg, action.physicalCoords,
                    useHitRoll ? action.hitRoll : null, null,
                    rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.Magic,
                    action.magicDmg, action.magicCoords,
                    useHitRoll ? action.hitRoll : null, null,
                    rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.FireDmg,
                    action.fireDmg, action.fireCoords,
                    useHitRoll ? action.hitRoll : null, null,
                    duration: action.fireDuration, rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.BleedDmg,
                    action.bleedDmg, action.bleedCoords,
                    useHitRoll ? action.hitRoll : null, null,
                    duration: action.bleedDuration, rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.PoisonDmg,
                    action.poisonDmg, action.poisonCoords,
                    useHitRoll ? action.hitRoll : null, null,
                    duration: action.poisonDuration, rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.RecoverHealth,
                    action.recoverHealth, action.recoverHealthCoords,
                    null, null,
                    rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.AddHealth,
                    action.addHealth, action.addHealthCoords,
                    null, null,
                    rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.AddTempHealth,
                    action.addTempHealth, action.addTempHealthCoords,
                    null, null,
                    rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.RecoverShield,
                    action.recoverShield, action.recoverShieldCoords,
                    null, null,
                    rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.AddShield,
                    action.addShield, action.addShieldCoords,
                    null, null,
                    rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.RecoverArmor,
                    action.recoverArmor, action.recoverArmorCoords,
                    null, null,
                    rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        ApplyEffect(casterTeam, casterSlot, EffectKind.AddArmor,
                    action.addArmor, action.addArmorCoords,
                    null, null,
                    rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        if (int.TryParse(action.stunTime, out int stunDur) && stunDur > 0)
            ApplyEffect(casterTeam, casterSlot, EffectKind.Stun,
                        action.stunTime, action.stunCoords,
                        useHitRoll ? action.hitRoll : null, null, duration: stunDur, rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        if (int.TryParse(action.sleepTime, out int sleepDur) && sleepDur > 0)
            ApplyEffect(casterTeam, casterSlot, EffectKind.Sleep,
                        action.sleepTime, action.sleepCoords,
                        useHitRoll ? action.hitRoll : null, null, duration: sleepDur, rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        if (int.TryParse(action.confusionTime, out int confDur) && confDur > 0)
            ApplyEffect(casterTeam, casterSlot, EffectKind.Confusion,
                        action.confusionTime, action.confusionCoords,
                        useHitRoll ? action.hitRoll : null, null, duration: confDur, rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        if (int.TryParse(action.petrificationTime, out int petrDur) && petrDur > 0)
            ApplyEffect(casterTeam, casterSlot, EffectKind.Petrification,
                        action.petrificationTime, action.petrificationCoords,
                        useHitRoll ? action.hitRoll : null, null, duration: petrDur, rollsToShow: rollsToShow, hitAlive: action.hitAlive);

        if (rollsToShow.Count > 0)
        {
            //<- ui stuff goes here  (flush the accumulated rolls to the dice popup)
            var viz = diceRoller != null ? diceRoller.GetComponent<diceVizualizer>() : null;
            if (viz != null)
                viz.ShowRolls(rollsToShow);
            else if (CombatDebugHandler.UltraDebug)
                Debug.LogWarning("No diceVizualizer on diceRoller - rolls won't be shown.");
        }

        yield return null;
    }

    // -------------------------------------------------------------------------
    // ApplyEffect
    // -------------------------------------------------------------------------
    void ApplyEffect(
        int casterTeam, int casterSlot,
        EffectKind kind,
        string valueExpr,
        List<CasterPattern> coords,
        string hitRollNotation = null,
        diceSystem.RollResult? preRolled = null,
        int duration = 0,
        List<(string label, diceSystem.RollResult)> rollsToShow = null,
        bool hitAlive = false)
    {
        if (coords == null || coords.Count == 0)
            return;

        var targets = GetEffectiveTargets(coords, casterSlot);
        if (targets.Count == 0) return;

        bool needsRoll = kind != EffectKind.Stun && kind != EffectKind.Sleep &&
                         kind != EffectKind.Confusion && kind != EffectKind.Petrification;

        bool isDamageKind = kind == EffectKind.Physical ||
                            kind == EffectKind.Magic ||
                            kind == EffectKind.FireDmg ||
                            kind == EffectKind.BleedDmg ||
                            kind == EffectKind.PoisonDmg;

        bool isStatusKind = kind == EffectKind.Stun ||
                            kind == EffectKind.Sleep ||
                            kind == EffectKind.Confusion ||
                            kind == EffectKind.Petrification;

        bool isHealKind = kind == EffectKind.RecoverHealth ||
                          kind == EffectKind.AddHealth ||
                          kind == EffectKind.AddTempHealth;

        bool useHitRoll = !string.IsNullOrWhiteSpace(hitRollNotation) && IsDiceNotation(hitRollNotation);

        for (int targetIdx = 0; targetIdx < targets.Count; targetIdx++)
        {
            var targetEntry = targets[targetIdx];
            var (targetTeam, targetSlot) = ResolveTarget(casterTeam, targetEntry.slot);
            UnitData target = GetUnit(targetTeam, targetSlot);

            // dead or empty target handling
            if (string.IsNullOrEmpty(target.name) || target.hp <= 0)
            {
                if (!hitAlive) continue;

                if (isHealKind)
                {
                    // heals retarget to neediest ally when hitAlive
                    int redirectSlot = FindLowestHpPercentAliveSlot(targetTeam);
                    if (redirectSlot < 0) continue;
                    targetSlot = redirectSlot;
                    target = GetUnit(targetTeam, targetSlot);
                }
                else
                {
                    // damage and debuffs retarget to nearest alive
                    int redirectSlot = FindNearestAliveSlot(targetTeam, targetSlot);
                    if (redirectSlot < 0) continue;
                    targetSlot = redirectSlot;
                    target = GetUnit(targetTeam, targetSlot);
                }

                if (CombatDebugHandler.UltraDebug) Debug.Log($"[hitAlive] {kind} retarget to slot {targetSlot}");
            }

            int amount = 0;
            diceSystem.RollResult? rolled = null;
            if (needsRoll)
            {
                rolled = TryRoll(valueExpr);
                if (rolled.HasValue && rollsToShow != null)
                    rollsToShow.Add(($"{kind} Roll→{target.name}", rolled.Value));
            }
            if (rolled.HasValue)
                amount = Mathf.CeilToInt(rolled.Value.total * targetEntry.portion);

            // independent hit roll per target
            if (useHitRoll && (isDamageKind || isStatusKind))
            {
                float effectiveAC = GetEffectiveAC(targetTeam, targetSlot);
                var hr = diceRoller.RollHit(hitRollNotation, Mathf.RoundToInt(effectiveAC), quiet: true);
                if (rollsToShow != null)
                    rollsToShow.Add(($"Attack→{target.name}", hr));

                int hitTotal = hr.total;
                int halfAC = Mathf.CeilToInt(effectiveAC * 0.5f);
                bool nat20 = hr.isCrunchyCrit;
                bool softCrit = hr.isCrit;
                bool fumble = hr.isFumble;

                // fumble nat 1
                if (fumble)
                {
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"{kind} FUMBLE on {target.name}");
                    FireFloatingText(targetTeam, targetSlot, "FUMBLE!", FloatingTextType.Fumble);
                    continue;
                }

                if (!nat20)
                {
                    // miss total below half ac
                    if (hitTotal < halfAC)
                    {
                        if (CombatDebugHandler.UltraDebug) Debug.Log($"{kind} missed {target.name}");
                        FireFloatingText(targetTeam, targetSlot, "MISS", FloatingTextType.Miss);
                        continue;
                    }
                    else if (hitTotal < effectiveAC)
                    {
                        if (isDamageKind)
                        {
                            amount = Mathf.CeilToInt(amount / 2f);
                            FireFloatingText(targetTeam, targetSlot, "GLANCING", FloatingTextType.HalfMiss);
                        }
                    }
                }

                // super crit nat 20 = max + bonus
                if (nat20 && isDamageKind && diceRoller != null)
                {
                    int maxDmg = diceRoller.MaxPossible(valueExpr);
                    amount = Mathf.CeilToInt(maxDmg * targetEntry.portion);
                    var bonus = TryRoll(valueExpr);
                    if (bonus.HasValue)
                    {
                        int bonusAmount = Mathf.CeilToInt(bonus.Value.total * targetEntry.portion);
                        amount += bonusAmount;
                        if (rollsToShow != null)
                            rollsToShow.Add(($"{kind} CRIT Bonus", bonus.Value));
                    }
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"[NAT20] {kind} max+bonus = {amount}");
                    FireFloatingText(targetTeam, targetSlot, "SUPER CRIT! -" + amount, FloatingTextType.SuperCrit);
                }
                // soft crit total >= 20 = max only
                else if (softCrit && isDamageKind && diceRoller != null)
                {
                    int maxDmg = diceRoller.MaxPossible(valueExpr);
                    amount = Mathf.CeilToInt(maxDmg * targetEntry.portion);
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"[SOFTCRIT] {kind} max = {amount}");
                    FireFloatingText(targetTeam, targetSlot, "CRIT! -" + amount, FloatingTextType.Crit);
                }
            }

            int srcTeam = casterTeam;
            int srcSlot = casterSlot;
            if (valueExpr == null && !useHitRoll) { srcTeam = -1; srcSlot = -1; }

            ApplyDamageWithHooks(
                targetTeam, targetSlot, ref amount, kind, duration, valueExpr,
                srcTeam, srcSlot, isDamageKind, godModeCheck: true);
        }
    }

    // find lowest hp percent alive slot on a team
    int FindLowestHpPercentAliveSlot(int team)
    {
        int best = -1;
        float bestFrac = 2f;
        for (int s = 0; s < 4; s++)
        {
            UnitData u = GetUnit(team, s);
            if (string.IsNullOrEmpty(u.name) || u.hp <= 0) continue;
            float frac = u.maxHp > 0 ? u.hp / u.maxHp : 1f;
            if (frac < bestFrac) { bestFrac = frac; best = s; }
        }
        return best;
    }

    #endregion


    #region Damage Resolution

    bool ApplyDamageWithHooks(
        int targetTeam, int targetSlot,
        ref int amount, EffectKind kind, int duration, string valueExpr,
        int sourceTeam, int sourceSlot,
        bool isDamageKind, bool godModeCheck,
        bool isDotTick = false)
    {
        // God mode: the player team takes no damage at all.
        if (godModeCheck && CombatDebugHandler.GodModePlayer && targetTeam == TeamPlayer && isDamageKind)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[GodMode] {GetUnit(targetTeam, targetSlot).name} is immune - damage negated.");
            return false;
        }

        if (isDamageKind && amount > 0 && !isProcessingPassiveReaction)
        {
            FireModifyDamageDealt(sourceTeam, sourceSlot, targetTeam, targetSlot, ref amount, kind);
            FireOnDamageTaken(targetTeam, targetSlot, ref amount, kind, sourceTeam, sourceSlot);
        }

        // If passives reduced amount to 0 or below skip application
        if (isDamageKind && amount <= 0)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"{kind}: damage reduced to {amount} by passives - no effect on {GetUnit(targetTeam, targetSlot).name}.");
            return false;
        }

        var updated = GetUnit(targetTeam, targetSlot);
        float hpBefore = updated.hp;
        bool defeated = ApplyEffectToTarget(ref updated, amount, kind, duration, valueExpr, isDotTick, targetTeam, targetSlot);

        SetUnit(targetTeam, targetSlot, updated);

        // floating text for damage and heals
        float hpDelta = updated.hp - hpBefore;
        if (Mathf.Abs(hpDelta) >= 1f)
        {
            bool isHeal = hpDelta > 0;
            FireFloatingText(targetTeam, targetSlot, Mathf.Abs(Mathf.CeilToInt(hpDelta)), kind, isHeal, isDotTick);
        }

        if (isDamageKind && sourceTeam >= 0 && sourceSlot >= 0 && !isProcessingPassiveReaction)
        {
            int hpDamage = Mathf.Max(0, Mathf.CeilToInt(hpBefore - updated.hp));
            if (hpDamage > 0)
                FireOnPostDamage(sourceTeam, sourceSlot, targetTeam, targetSlot, hpDamage, kind);
        }

        if (defeated)
        {
            bool prevented = FireOnDefeat(targetTeam, targetSlot, ref updated);
            if (prevented)
            {
                defeated = false;
                if (CombatDebugHandler.UltraDebug) Debug.Log($"{updated.name} cheated death via a passive! (survived at {updated.hp:0} HP)");
            }
            else
            {
                if (CombatDebugHandler.UltraDebug) Debug.Log($"{updated.name} has been defeated!");

                pendingDeaths.Enqueue((targetTeam, targetSlot, sourceTeam, sourceSlot));
                if (!isProcessingDeathFanout)
                    DrainPendingDeaths();
                return true;
            }
        }

        SetUnit(targetTeam, targetSlot, updated);
        return defeated;
    }

    void DrainPendingDeaths()
    {
        isProcessingDeathFanout = true;
        try
        {
            while (pendingDeaths.Count > 0)
            {
                var (team, slot, killerTeam, killerSlot) = pendingDeaths.Dequeue();
                try
                {
                    DetachPassives(team, slot);
                }
                catch (System.Exception e) { Debug.LogError($"[DrainPendingDeaths] DetachPassives threw: {e}"); }
                try
                {
                    CustomStatusRuntime.NotifyUnitDefeated(team, slot);
                }
                catch (System.Exception e) { Debug.LogError($"[DrainPendingDeaths] NotifyUnitDefeated threw: {e}"); }
                try
                {
                    FireOnAnyUnitDefeated(team, slot, killerTeam, killerSlot);
                }
                catch (System.Exception e) { Debug.LogError($"[DrainPendingDeaths] FireOnAnyUnitDefeated threw: {e}"); }
            }
        }
        finally
        {
            isProcessingDeathFanout = false;
        }
    }

    void DetachPassives(int team, int slot)
    {
        if (!passiveInstances.TryGetValue((team, slot), out List<iPassive> list))
            return;
        if (list == null)
        {
            passiveInstances.Remove((team, slot));
            return;
        }
        var snap = GetSnapshot(list);
        foreach (var p in snap)
        {
            try { p.OnDetach(this, team, slot); }
            catch (System.Exception e) { Debug.LogError($"[CombatScript] OnDetach threw on {p}: {e}"); }
            if (p is MonoBehaviour mb && mb != null)
                Destroy(mb.gameObject);
        }
        list.Clear();
        passiveInstances.Remove((team, slot));
    }

    // -------------------------------------------------------------------------
    // Public passive-slot helpers (used by the bench system)
    // -------------------------------------------------------------------------

    public void DetachPassivesPublic(int team, int slot) => DetachPassives(team, slot);

    public void ReinitializePassivesForSlot(int team, int slot)
    {
        DetachPassives(team, slot);

        UnitData u = GetUnit(team, slot);
        if (string.IsNullOrEmpty(u.name) || u.passiveKeys == null || u.passiveKeys.Count == 0)
            return;

        var list = new List<iPassive>();
        passiveInstances[(team, slot)] = list;

        foreach (string key in u.passiveKeys)
        {
            iPassive instance = OverrideRegistry.InstantiatePassive(key, team, slot);
            if (instance == null)
            {
                Debug.LogWarning($"[CombatScript] ReinitializePassivesForSlot: passive '{key}' on {u.name} has no handler - skipping.");
                continue;
            }
            list.Add(instance);
            instance.OnAttach(this, team, slot);
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[Passive] Re-attached '{key}' to {u.name} (team {team} slot {slot}).");
        }
    }

    private bool ApplyEffectToTarget(
        ref UnitData unit,
        int amount,
        EffectKind kind,
        int duration,
        string valueExpr,
        bool isDotTick = false,
        int targetTeam = -1,
        int targetSlot = -1)
    {
        bool defeated = false;

        switch (kind)
        {
            // --- DAMAGE ---
            case EffectKind.Physical:
                {
                    int remaining = AbsorbDamage(ref unit, amount, true, true, true);
                    unit.hp = Mathf.Max(0, unit.hp - remaining);
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"{kind}: {amount} damage → {unit.name} HP={unit.hp}");
                    defeated = unit.hp <= 0;
                    if (remaining > 0) WakeOnDamage(ref unit);
                    break;
                }

            case EffectKind.FireDmg:
                {
                    int remaining = AbsorbDamage(ref unit, amount, true, true, true);
                    unit.hp = Mathf.Max(0, unit.hp - remaining);
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"Fire: {amount} damage → {unit.name} HP={unit.hp}");
                    defeated = unit.hp <= 0;
                    if (remaining > 0) WakeOnDamage(ref unit);

                    if (duration > 0)
                    {
                        string resolved = ResolveDotNotation(unit.fireRemainingRounds, unit.fireDamageNotation, valueExpr);
                        unit.fireRemainingRounds = Mathf.Max(unit.fireRemainingRounds, duration);
                        unit.fireDamageNotation = resolved;
                    }
                    else if (isDotTick && unit.fireRemainingRounds > 0)
                    {
                        unit.fireRemainingRounds--;
                        if (diceRoller != null)
                        {
                            var roll = diceRoller.Roll("1d2", target: -1, quiet: true);
                            if (roll.total == 2)
                            {
                                unit.fireRemainingRounds = 0;
                                if (CombatDebugHandler.UltraDebug) Debug.Log($"Fire removed from {unit.name} by dice roll (1d2 = {roll.total}).");
                            }
                        }
                    }
                    break;
                }

            case EffectKind.BleedDmg:
                {
                    int remaining = AbsorbDamage(ref unit, amount, false, true, true);
                    unit.hp = Mathf.Max(0, unit.hp - remaining);
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"Bleed: {amount} damage → {unit.name} HP={unit.hp}");
                    defeated = unit.hp <= 0;
                    if (remaining > 0) WakeOnDamage(ref unit);

                    if (duration > 0)
                    {
                        string resolved = ResolveDotNotation(unit.bleedRemainingRounds, unit.bleedDamageNotation, valueExpr);
                        unit.bleedRemainingRounds = Mathf.Max(unit.bleedRemainingRounds, duration);
                        unit.bleedDamageNotation = resolved;
                    }
                    else if (isDotTick && unit.bleedRemainingRounds > 0)
                    {
                        unit.bleedRemainingRounds--;
                    }
                    break;
                }

            case EffectKind.Magic:
            case EffectKind.PoisonDmg:
                {
                    int remaining = AbsorbDamage(ref unit, amount, false, false, true);
                    unit.hp = Mathf.Max(0, unit.hp - remaining);
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"{kind}: {amount} damage → {unit.name} HP={unit.hp}");
                    defeated = unit.hp <= 0;
                    if (remaining > 0) WakeOnDamage(ref unit);

                    if (kind == EffectKind.PoisonDmg)
                    {
                        if (duration > 0)
                        {
                            string resolved = ResolveDotNotation(unit.poisonRemainingRounds, unit.poisonDamageNotation, valueExpr);
                            unit.poisonRemainingRounds = Mathf.Max(unit.poisonRemainingRounds, duration);
                            unit.poisonDamageNotation = resolved;
                        }
                        else if (isDotTick && unit.poisonRemainingRounds > 0)
                        {
                            unit.poisonRemainingRounds--;
                        }
                    }
                    break;
                }

            // ---HEALING / BUFFS---
            case EffectKind.RecoverHealth:
                unit.hp = Mathf.Min(unit.maxHp, unit.hp + amount);
                if (CombatDebugHandler.UltraDebug) Debug.Log($"RecoverHealth: +{amount} HP → {unit.hp}/{unit.maxHp}");
                break;

            case EffectKind.AddHealth:
                {
                    float space = unit.maxHp > 0 ? (unit.maxHp - unit.hp) : 0f;
                    if (space > 0f)
                    {
                        int toHp = Mathf.FloorToInt(Mathf.Min(space, amount));
                        unit.hp += toHp;
                        int overflow = amount - toHp;
                        if (overflow > 0)
                        {
                            unit.th += overflow;
                            if (CombatDebugHandler.UltraDebug) Debug.Log($"AddHealth: +{toHp} HP (cap), +{overflow} overflow → temp HP (th={unit.th})");
                        }
                        else if (CombatDebugHandler.UltraDebug) Debug.Log($"AddHealth: +{amount} HP → {unit.hp}/{unit.maxHp}");
                    }
                    else
                    {
                        // already at or above max - all of it becomes temp HP
                        unit.th += amount;
                        if (CombatDebugHandler.UltraDebug) Debug.Log($"AddHealth: at max, +{amount} → temp HP (th={unit.th})");
                    }
                    break;
                }

            case EffectKind.AddTempHealth:
                unit.th += amount;
                if (CombatDebugHandler.UltraDebug) Debug.Log($"AddTempHealth: +{amount} temp HP → {unit.th}");
                if (targetTeam >= 0) FireBuffText(targetTeam, targetSlot, amount, kind);
                break;

            case EffectKind.RecoverShield:
                unit.shield = Mathf.Min(unit.maxShield, unit.shield + amount);
                if (CombatDebugHandler.UltraDebug) Debug.Log($"RecoverShield: +{amount} shield → {unit.shield}/{unit.maxShield}");
                if (targetTeam >= 0) FireBuffText(targetTeam, targetSlot, amount, kind);
                break;

            case EffectKind.AddShield:
                unit.shield += amount;
                if (CombatDebugHandler.UltraDebug) Debug.Log($"AddShield: +{amount} shield → {unit.shield}/{unit.maxShield}");
                if (targetTeam >= 0) FireBuffText(targetTeam, targetSlot, amount, kind);
                break;

            case EffectKind.RecoverArmor:
                unit.armor = Mathf.Min(unit.maxArmor, unit.armor + amount);
                if (CombatDebugHandler.UltraDebug) Debug.Log($"RecoverArmor: +{amount} armor → {unit.armor}/{unit.maxArmor}");
                if (targetTeam >= 0) FireBuffText(targetTeam, targetSlot, amount, kind);
                break;

            case EffectKind.AddArmor:
                unit.armor += amount;
                if (CombatDebugHandler.UltraDebug) Debug.Log($"AddArmor: +{amount} armor → {unit.armor}/{unit.maxArmor}");
                if (targetTeam >= 0) FireBuffText(targetTeam, targetSlot, amount, kind);
                break;

            // --- STATUSES ---
            case EffectKind.Stun:
                unit.stunRemainingRounds = Mathf.Max(unit.stunRemainingRounds, duration);
                if (CombatDebugHandler.UltraDebug) Debug.Log($"{kind}: {duration} turns");
                if (targetTeam >= 0) FireStatusText(targetTeam, targetSlot, "STUN");
                break;
            case EffectKind.Sleep:
                unit.sleepRemainingRounds = Mathf.Max(unit.sleepRemainingRounds, duration);
                if (targetTeam >= 0) FireStatusText(targetTeam, targetSlot, "SLEEP");
                break;
            case EffectKind.Confusion:
                unit.confusionRemainingRounds = Mathf.Max(unit.confusionRemainingRounds, duration);
                if (targetTeam >= 0) FireStatusText(targetTeam, targetSlot, "CONFUSE");
                break;
            case EffectKind.Petrification:
                unit.petrificationRemainingRounds = Mathf.Max(unit.petrificationRemainingRounds, duration);
                if (targetTeam >= 0) FireStatusText(targetTeam, targetSlot, "PETRIFY");
                break;

            default:
                Debug.LogWarning($"Unhandled effect: {kind}");
                break;
        }

        return defeated;
    }

    public string ResolveDotNotationPublic(int existingRounds, string existingNotation, string newNotation)
        => ResolveDotNotation(existingRounds, existingNotation, newNotation);

    string ResolveDotNotation(int existingRounds, string existingNotation, string newNotation)
    {
        if (existingRounds <= 0) return newNotation ?? "";
        if (string.IsNullOrWhiteSpace(existingNotation)) return newNotation ?? "";
        if (string.IsNullOrWhiteSpace(newNotation)) return existingNotation;
        if (diceRoller == null) return existingNotation;
        float existing = diceRoller.ExpectedValue(existingNotation);
        float newer = diceRoller.ExpectedValue(newNotation);
        return newer > existing ? newNotation : existingNotation;
    }

    public bool ApplyDamageToTarget(int team, int slot, int amount, EffectKind kind,
        int duration = 0, int sourceTeam = -1, int sourceSlot = -1,
        bool isDotTick = false)
    {
        UnitData unit = GetUnit(team, slot);
        if (string.IsNullOrEmpty(unit.name) || unit.hp <= 0) return false;

        // Determine if this is a damage kind (fires hooks) or a heal/buff (doesn't).
        bool isDamageKind = kind == EffectKind.Physical ||
                            kind == EffectKind.Magic ||
                            kind == EffectKind.FireDmg ||
                            kind == EffectKind.BleedDmg ||
                            kind == EffectKind.PoisonDmg;

        string valueExpr = null;

        return ApplyDamageWithHooks(
            team, slot, ref amount, kind, duration, valueExpr,
            sourceTeam, sourceSlot, isDamageKind, godModeCheck: true,
            isDotTick: isDotTick);
    }

    public bool ApplyProcDamage(int team, int slot, int amount, EffectKind kind,
        int sourceTeam = -1, int sourceSlot = -1)
    {
        UnitData unit = GetUnit(team, slot);
        if (string.IsNullOrEmpty(unit.name) || unit.hp <= 0) return false;
        if (amount <= 0) return false;

        bool isDamageKind = kind == EffectKind.Physical ||
                            kind == EffectKind.Magic ||
                            kind == EffectKind.FireDmg ||
                            kind == EffectKind.BleedDmg ||
                            kind == EffectKind.PoisonDmg;
        if (!isDamageKind) return false;

        // Fire ONLY the defensive hook (OnDamageTaken), not ModifyDamageDealt.
        // We temporarily clear isProcessingPassiveReaction so FireOnDamageTaken
        // actually runs, then restore it.
        bool wasProcessing = isProcessingPassiveReaction;
        isProcessingPassiveReaction = false;
        try
        {
            FireOnDamageTaken(team, slot, ref amount, kind, sourceTeam, sourceSlot);
        }
        finally
        {
            isProcessingPassiveReaction = wasProcessing;
        }

        if (amount <= 0) return false;

        // Apply the damage directly without re-firing hooks.
        var updated = GetUnit(team, slot);
        float hpBefore = updated.hp;
        bool defeated = ApplyEffectToTarget(ref updated, amount, kind, 0, null, false, team, slot);
        SetUnit(team, slot, updated);

        float hpDelta = updated.hp - hpBefore;
        if (Mathf.Abs(hpDelta) >= 1f)
            FireFloatingText(team, slot, Mathf.Abs(Mathf.CeilToInt(hpDelta)), kind, false, false);

        if (sourceTeam >= 0 && sourceSlot >= 0 && hpBefore > updated.hp)
        {
            int hpDamage = Mathf.Max(0, Mathf.CeilToInt(hpBefore - updated.hp));
            if (hpDamage > 0)
                FireOnPostDamage(sourceTeam, sourceSlot, team, slot, hpDamage, kind);
        }

        if (defeated)
        {
            bool prevented = FireOnDefeat(team, slot, ref updated);
            if (prevented)
            {
                defeated = false;
            }
            else
            {
                pendingDeaths.Enqueue((team, slot, sourceTeam, sourceSlot));
                if (!isProcessingDeathFanout)
                    DrainPendingDeaths();
            }
            SetUnit(team, slot, updated);
        }

        return defeated;
    }

    int AbsorbDamage(ref UnitData unit, int amount, bool absorbShield, bool absorbArmor, bool absorbTemp)
    {
        float remaining = amount;

        if (absorbShield && unit.shield > 0 && remaining > 0f)
        {
            float absorbed = Mathf.Min(unit.shield, remaining);
            unit.shield = Mathf.Max(0f, unit.shield - absorbed);
            remaining = Mathf.Max(0f, remaining - absorbed);
        }
        if (absorbArmor && unit.armor > 0 && unit.armorBlock > 0 && remaining > 0f)
        {
            float reduction = Mathf.Min(remaining, Mathf.Min(unit.armor, unit.armorBlock));
            remaining = Mathf.Max(0f, remaining - reduction);
            unit.armor = Mathf.Max(0f, unit.armor - reduction);
        }
        if (absorbTemp && unit.th > 0 && remaining > 0f)
        {
            float absorbed = Mathf.Min(unit.th, remaining);
            unit.th = Mathf.Max(0f, unit.th - absorbed);
            remaining = Mathf.Max(0f, remaining - absorbed);
        }

        return Mathf.Max(0, Mathf.CeilToInt(remaining));
    }

    static void WakeOnDamage(ref UnitData unit)
    {
        if (unit.sleepRemainingRounds > 0)
        {
            unit.sleepRemainingRounds = 0;
            if (CombatDebugHandler.UltraDebug) Debug.Log($"{unit.name} woke up from damage!");
        }
    }

    #endregion


    #region Passive System

    public void InitializePassives()
    {
        foreach (var kvp in passiveInstances)
        {
            if (kvp.Value == null) continue;
            foreach (var p in kvp.Value)
            {
                if (p is MonoBehaviour mb && mb != null)
                    Destroy(mb.gameObject);
            }
        }
        passiveInstances.Clear();
        for (int team = 0; team < 2; team++)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                UnitData u = GetUnit(team, slot);
                if (string.IsNullOrEmpty(u.name) || u.passiveKeys == null || u.passiveKeys.Count == 0)
                    continue;

                var list = new List<iPassive>();
                passiveInstances[(team, slot)] = list;

                foreach (string key in u.passiveKeys)
                {
                    iPassive instance = OverrideRegistry.InstantiatePassive(key, team, slot);
                    if (instance == null)
                    {
                        Debug.LogWarning($"[CombatScript] Passive '{key}' on {u.name} (team {team} slot {slot}) has no registered handler - skipping.");
                        continue;
                    }
                    list.Add(instance);
                    instance.OnAttach(this, team, slot);
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"[Passive] Attached '{key}' to {u.name} (team {team} slot {slot}).");
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Passive hook dispatchers
    // -------------------------------------------------------------------------

    static bool IsDead(iPassive p)
    {
        if (p == null) return true;
        if (p is MonoBehaviour mb && mb == null) return true;
        return false;
    }

    static void PruneDead(List<iPassive> list)
    {
        if (list == null) return;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (IsDead(list[i]))
                list.RemoveAt(i);
        }
    }

    static List<iPassive> GetSnapshot(List<iPassive> list)
    {
        if (list == null) return _emptyPassiveList;
        var snap = new List<iPassive>(list.Count);
        foreach (var p in list)
        {
            if (!IsDead(p))
                snap.Add(p);
        }
        return snap;
    }

    static readonly List<iPassive> _emptyPassiveList = new List<iPassive>();

    static List<(int team, int slot, List<iPassive> snap)> SnapshotAll(
        Dictionary<(int team, int slot), List<iPassive>> source)
    {
        var result = new List<(int, int, List<iPassive>)>(source.Count);
        foreach (var kvp in source)
        {
            if (kvp.Value == null) continue;
            var snap = GetSnapshot(kvp.Value);
            if (snap.Count > 0)
                result.Add((kvp.Key.team, kvp.Key.slot, snap));
        }
        return result;
    }

    public void FireOnTurnStart(int team)
        => FireTeamHook(team, (p, c, t) => p.OnTurnStart(c, t));

    public void FireOnTurnEnd(int team)
        => FireTeamHook(team, (p, c, t) => p.OnTurnEnd(c, t));

    public void FireOnRoundStart()
        => FireGlobalHook(p => p.OnRoundStart(this));

    public void FireOnRoundEnd()
        => FireGlobalHook(p => p.OnRoundEnd(this));

    void FireTeamHook(int team, System.Action<iPassive, CombatScript, int> invoke)
    {
        for (int slot = 0; slot < 4; slot++)
        {
            if (!passiveInstances.TryGetValue((team, slot), out var list) || list == null) continue;
            var snap = GetSnapshot(list);
            foreach (var p in snap)
            {
                try { invoke(p, this, team); }
                catch (System.Exception e) { Debug.LogError($"[FireTeamHook] {e}"); }
            }
            PruneDead(list);
        }
    }

    void FireGlobalHook(System.Action<iPassive> invoke)
    {
        var snapshot = SnapshotAll(passiveInstances);
        foreach (var (team, slot, snap) in snapshot)
        {
            foreach (var p in snap)
            {
                try { invoke(p); }
                catch (System.Exception e) { Debug.LogError($"[FireGlobalHook] {e}"); }
            }
            if (passiveInstances.TryGetValue((team, slot), out var list))
                PruneDead(list);
        }
    }

    void FireOnDamageTaken(int targetTeam, int targetSlot, ref int amount,
        EffectKind kind, int sourceTeam, int sourceSlot)
    {
        if (!passiveInstances.TryGetValue((targetTeam, targetSlot), out var list) || list == null) return;

        var snap = GetSnapshot(list);

        bool wasProcessing = isProcessingPassiveReaction;
        isProcessingPassiveReaction = true;
        try
        {
            foreach (var p in snap)
            {
                try { p.OnDamageTaken(this, ref amount, kind, sourceTeam, sourceSlot); }
                catch (System.Exception e) { Debug.LogError($"[FireOnDamageTaken] {e}"); }
            }
        }
        finally
        {
            isProcessingPassiveReaction = wasProcessing;
        }
        PruneDead(list);
    }

    // proc tracking so each passive can only proc once per triggering event
    readonly HashSet<(iPassive, string)> _procTracker = new HashSet<(iPassive, string)>();
    public bool TryProc(iPassive p, string procKey)
    {
        var key = (p, procKey);
        if (_procTracker.Contains(key)) return false;
        _procTracker.Add(key);
        return true;
    }
    public void ClearProcs() { _procTracker.Clear(); }

    bool FireOnDefeat(int team, int slot, ref UnitData unit)
    {
        bool prevented = false;
        float survivalPercent = 0f;

        if (passiveInstances.TryGetValue((team, slot), out var list) && list != null)
        {
            var snap = GetSnapshot(list);

            bool wasProcessing = isProcessingPassiveReaction;
            isProcessingPassiveReaction = true;
            try
            {
                foreach (var p in snap)
                {
                    try { p.OnDefeat(this, ref prevented, ref survivalPercent); }
                    catch (System.Exception e) { Debug.LogError($"[FireOnDefeat] {e}"); }
                }
            }
            finally
            {
                isProcessingPassiveReaction = wasProcessing;
            }
            PruneDead(list);
        }

        if (prevented)
        {
            float surviveHp = survivalPercent > 0f
                ? unit.maxHp * Mathf.Clamp01(survivalPercent)
                : 1f;
            unit.hp = Mathf.Max(1f, Mathf.CeilToInt(surviveHp));
            unit.shield = 0;
            unit.th = 0;
            unit.fireRemainingRounds = 0;
            unit.bleedRemainingRounds = 0;
            unit.poisonRemainingRounds = 0;
            unit.fireDamageNotation = "";
            unit.bleedDamageNotation = "";
            unit.poisonDamageNotation = "";
            unit.stunRemainingRounds = 0;
            unit.sleepRemainingRounds = 0;
            unit.confusionRemainingRounds = 0;
            unit.petrificationRemainingRounds = 0;
            unit.tauntRemainingRounds = 0;
            unit.tauntTargetSlot = -1;
            // armor and armorBlock stay unchanged
        }
        return prevented;
    }

    void FireOnAnyUnitDefeated(int defTeam, int defSlot, int killerTeam, int killerSlot)
        => FireGlobalHook(p => p.OnAnyUnitDefeated(this, defTeam, defSlot, killerTeam, killerSlot));

    void FireOnActionCast(int casterTeam, int casterSlot, ActionData action)
        => FireGlobalHook(p => p.OnActionCast(this, casterTeam, casterSlot, action));

    private readonly Dictionary<(int, int), float> _computingACValues = new Dictionary<(int, int), float>(8);

    public float GetEffectiveAC(int team, int slot)
    {
        var key = (team, slot);
        if (_computingACSet.Contains(key))
            return _computingACValues.TryGetValue(key, out float inProgress) ? inProgress : GetUnit(team, slot).ac;

        _computingACSet.Add(key);
        try
        {
            UnitData u = GetUnit(team, slot);
            float ac = u.ac;
            _computingACValues[key] = ac;

            if (passiveInstances.TryGetValue((team, slot), out var selfList) && selfList != null)
            {
                var snap = GetSnapshot(selfList);
                foreach (var p in snap)
                {
                    _computingACValues[key] = ac;
                    try { p.ModifyAC(this, team, slot, ref ac); }
                    catch (System.Exception e) { Debug.LogError($"[GetEffectiveAC] {e}"); }
                    _computingACValues[key] = ac;
                }
                PruneDead(selfList);
            }

            var snapshot = SnapshotAll(passiveInstances);
            foreach (var (t, s, snap) in snapshot)
            {
                if (t == team && s == slot) continue;
                foreach (var p in snap)
                {
                    _computingACValues[key] = ac;
                    try { p.ModifyAC(this, team, slot, ref ac); }
                    catch (System.Exception e) { Debug.LogError($"[GetEffectiveAC] {e}"); }
                    _computingACValues[key] = ac;
                }
            }
            return ac;
        }
        finally
        {
            _computingACSet.Remove(key);
            _computingACValues.Remove(key);
        }
    }

    void FireModifyDamageDealt(int attTeam, int attSlot, int tgtTeam, int tgtSlot,
        ref int amount, EffectKind kind)
    {
        bool wasProcessing = isProcessingPassiveReaction;
        isProcessingPassiveReaction = true;
        try
        {
            var snapshot = SnapshotAll(passiveInstances);
            foreach (var (team, slot, snap) in snapshot)
            {
                foreach (var p in snap)
                {
                    try { p.ModifyDamageDealt(this, attTeam, attSlot, tgtTeam, tgtSlot, ref amount, kind); }
                    catch (System.Exception e) { Debug.LogError($"[FireModifyDamageDealt] {e}"); }
                }
                if (passiveInstances.TryGetValue((team, slot), out var list))
                    PruneDead(list);
            }

            // Fire custom-status OnDealDamage hooks while the reentry guard is still active,
            // so a status that deals bonus damage doesn't trigger unguarded recursion.
            CustomStatusRuntime.FireOnDealDamage(attTeam, attSlot, ref amount, kind, tgtTeam, tgtSlot);
        }
        finally
        {
            isProcessingPassiveReaction = wasProcessing;
        }
    }

    void FireOnPostDamage(int attTeam, int attSlot, int tgtTeam, int tgtSlot,
        int hpDamage, EffectKind kind)
    {
        if (attTeam < 0 || attSlot < 0) return;
        if (!passiveInstances.TryGetValue((attTeam, attSlot), out var list) || list == null) return;

        var snap = GetSnapshot(list);
        bool wasProcessing = isProcessingPassiveReaction;
        isProcessingPassiveReaction = true;
        try
        {
            foreach (var p in snap)
            {
                try { p.OnPostDamage(this, attTeam, attSlot, tgtTeam, tgtSlot, hpDamage, kind); }
                catch (System.Exception e) { Debug.LogError($"[FireOnPostDamage] {e}"); }
            }
        }
        finally
        {
            isProcessingPassiveReaction = wasProcessing;
        }
        PruneDead(list);
    }

    #endregion


    #region AP Bank

    public ref APBank GetAPBank(int team)
    {
        if (Runtime == null) throw new System.InvalidOperationException("Runtime not set - cannot access AP banks.");
        if (team == TeamPlayer) return ref Runtime.playerAPBank;
        if (team == TeamEnemy) return ref Runtime.enemyAPBank;
        throw new System.ArgumentException($"Invalid team {team}", nameof(team));
    }

    public float GetTeamAPCap(int team)
    {
        if (Runtime == null) return 0f;
        if (team == TeamPlayer) return Runtime.fixedPlayerAPCap;
        if (team == TeamEnemy) return Runtime.fixedEnemyAPCap;
        return 0f;
    }

    public void InitAPBanks()
    {
        if (Runtime == null) return;
        Runtime.fixedPlayerAPCap = ComputeAPCap(TeamPlayer);
        Runtime.fixedEnemyAPCap  = ComputeAPCap(TeamEnemy);
        Runtime.playerAPBank = new APBank { current = Runtime.fixedPlayerAPCap, startOfTurnTotal = Runtime.fixedPlayerAPCap };
        Runtime.enemyAPBank  = new APBank { current = Runtime.fixedEnemyAPCap,  startOfTurnTotal = Runtime.fixedEnemyAPCap  };
    }

    float ComputeAPCap(int team)
    {
        float cap = (float)BaseAPCap;
        for (int slot = 0; slot < 4; slot++)
        {
            UnitData u = GetUnit(team, slot);
            if (!string.IsNullOrEmpty(u.name))
                cap += Mathf.Max(0f, u.ap);
        }
        return Mathf.Max(cap, 4f);
    }

    public void RecomputeAPCap()
    {
        if (Runtime == null) return;
        Runtime.fixedPlayerAPCap = ComputeAPCap(TeamPlayer);
        Runtime.fixedEnemyAPCap  = ComputeAPCap(TeamEnemy);
    }

    public void RegenerateAP(int team)
    {
        float cap = GetTeamAPCap(team);
        if (cap <= 0f) return;
        ref APBank bank = ref GetAPBank(team);
        bank.current = cap;
        bank.startOfTurnTotal = cap;
    }

    public void ClampAP(int team)
    {
        ref APBank bank = ref GetAPBank(team);
        bank.Clamp(GetTeamAPCap(team));
    }

    public void EnsurePlayableAPCap(float minimumCap = 6f)
    {
        for (int team = 0; team < 2; team++)
        {
            float cap = GetTeamAPCap(team);
            if (cap <= 0f)
            {
                if (team == TeamPlayer) Runtime.fixedPlayerAPCap = minimumCap;
                else                    Runtime.fixedEnemyAPCap  = minimumCap;

                ref APBank bank = ref GetAPBank(team);
                bank.current          = Mathf.Max(bank.current, minimumCap);
                bank.startOfTurnTotal = minimumCap;

                Debug.LogWarning($"[CombatScript] Team {team} had AP cap 0 - forced to {minimumCap}. Make sure combat units are loaded (combatSetup.LoadAllUnits) and have AP > 0.");
            }
        }
    }

    public void FillAPToCap(int team)
    {
        if (Runtime == null) return;
        float cap = GetTeamAPCap(team);
        if (cap <= 0f) return;
        ref APBank bank = ref GetAPBank(team);
        bank.current = cap;
        bank.startOfTurnTotal = cap;
    }

    public bool TrySpendAP(int team, int cost)
    {
        if (cost <= 0) return true;

        ref APBank bank = ref GetAPBank(team);

        if (bank.current < cost)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[AP] Team {team} needs {cost} but only has {bank.current:0} - can't afford it.");
            return false;
        }

        bank.Spend(cost);
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[AP] Team {team} spent {cost} AP - {bank.current:0}/{bank.startOfTurnTotal:0} remaining.");
        return true;
    }

    #endregion


    #region Battle-End Cleanup

    public void CleanupAllPassivesAndStatuses()
    {
        for (int team = 0; team < 2; team++)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                DetachPassivesPublic(team, slot);

                UnitData u = GetUnit(team, slot);
                if (string.IsNullOrEmpty(u.name)) continue;

                u.fireRemainingRounds          = 0;
                u.bleedRemainingRounds         = 0;
                u.poisonRemainingRounds        = 0;
                u.fireDamageNotation           = "";
                u.bleedDamageNotation          = "";
                u.poisonDamageNotation         = "";
                u.stunRemainingRounds          = 0;
                u.sleepRemainingRounds         = 0;
                u.confusionRemainingRounds     = 0;
                u.petrificationRemainingRounds = 0;
                u.tauntRemainingRounds         = 0;
                u.tauntTargetSlot             = -1;
                u.th                           = 0;
                SetUnit(team, slot, u);
            }
        }

        if (CustomStatusRuntime.Instance != null)
            CustomStatusRuntime.Instance.ClearAll();
    }

    #endregion


    #region Unit Access

    // -------------------------------------------------------------------------
    // Get and Set units
    // -------------------------------------------------------------------------
    public UnitData GetUnit(int team, int slot)
    {
        if (slot < 0 || slot > 3)
        {
            Debug.LogError($"[CombatScript] GetUnit(team={team}, slot={slot}) - slot out of range (0-3). Returning default.");
            return default;
        }
        if (team == TeamPlayer)
        {
            switch (slot)
            {
                case 0: return PlayerTeam.unit_0;
                case 1: return PlayerTeam.unit_1;
                case 2: return PlayerTeam.unit_2;
                case 3: return PlayerTeam.unit_3;
            }
        }
        else if (team == TeamEnemy)
        {
            switch (slot)
            {
                case 0: return EnemyTeam.unit_0;
                case 1: return EnemyTeam.unit_1;
                case 2: return EnemyTeam.unit_2;
                case 3: return EnemyTeam.unit_3;
            }
        }
        else
        {
            Debug.LogError($"[CombatScript] GetUnit(team={team}, slot={slot}) - unknown team. Returning default.");
        }
        return default;
    }

    public void SetUnit(int team, int slot, UnitData data)
    {
        if (slot < 0 || slot > 3)
        {
            Debug.LogError($"[CombatScript] SetUnit(team={team}, slot={slot}) - slot out of range (0-3). Data discarded.");
            return;
        }
        if (team == TeamPlayer)
        {
            var t = PlayerTeam;
            switch (slot)
            {
                case 0: t.unit_0 = data; break;
                case 1: t.unit_1 = data; break;
                case 2: t.unit_2 = data; break;
                case 3: t.unit_3 = data; break;
            }
            PlayerTeam = t;
        }
        else if (team == TeamEnemy)
        {
            var t = EnemyTeam;
            switch (slot)
            {
                case 0: t.unit_0 = data; break;
                case 1: t.unit_1 = data; break;
                case 2: t.unit_2 = data; break;
                case 3: t.unit_3 = data; break;
            }
            EnemyTeam = t;
        }
        else
        {
            Debug.LogError($"[CombatScript] SetUnit(team={team}, slot={slot}) - unknown team. Data discarded.");
        }
    }

    public int FindNearestAliveSlot(int team, int originSlot)
    {
        for (int offset = 1; offset <= 3; offset++)
        {
            int lower = originSlot - offset;
            if (lower >= 0)
            {
                UnitData u = GetUnit(team, lower);
                if (!string.IsNullOrEmpty(u.name) && u.hp > 0) return lower;
            }

            int upper = originSlot + offset;
            if (upper <= 3)
            {
                UnitData u = GetUnit(team, upper);
                if (!string.IsNullOrEmpty(u.name) && u.hp > 0) return upper;
            }
        }
        return -1;
    }

    public (int team, int slot) ResolveTarget(int casterTeam, int rawSlot)
    {
        if (rawSlot >= 0 && rawSlot <= 3)
            return (casterTeam, rawSlot);

        if (rawSlot >= 4 && rawSlot <= 7)
        {
            int otherTeam = casterTeam == TeamPlayer ? TeamEnemy : TeamPlayer;
            return (otherTeam, rawSlot - 4);
        }

        Debug.LogError($"[CombatScript] ResolveTarget got out-of-range slot {rawSlot}; returning (-1, -1) (invalid). Caller should skip this target.");
        return (-1, -1);
    }

    public void SwapUnitsWithPassives(int team, int slotA, int slotB)
    {
        if (slotA == slotB) return;
        if (slotA < 0 || slotA > 3 || slotB < 0 || slotB > 3)
        {
            Debug.LogError($"[CombatScript] SwapUnitsWithPassives slots out of range: {slotA}, {slotB}");
            return;
        }

        // Swap the UnitData.
        UnitData a = GetUnit(team, slotA);
        UnitData b = GetUnit(team, slotB);
        SetUnit(team, slotA, b);
        SetUnit(team, slotB, a);

        passiveInstances.TryGetValue((team, slotA), out List<iPassive> listA);
        passiveInstances.TryGetValue((team, slotB), out List<iPassive> listB);
        passiveInstances.Remove((team, slotA));
        passiveInstances.Remove((team, slotB));
        if (listB != null && listB.Count > 0) passiveInstances[(team, slotA)] = listB;
        if (listA != null && listA.Count > 0) passiveInstances[(team, slotB)] = listA;

        // Reassign owners so passives know where they live now.
        if (listA != null)
            foreach (var p in listA)
                if (p is PassiveBase pb) pb.ReassignOwner(team, slotB);
        if (listB != null)
            foreach (var p in listB)
                if (p is PassiveBase pb) pb.ReassignOwner(team, slotA);

        CustomStatusRuntime.SwapStatuses(team, slotA, slotB);

        if (CombatDebugHandler.UltraDebug) Debug.Log($"[SwapUnitsWithPassives] team {team}: slot {slotA} ↔ slot {slotB} (units + passives + statuses).");
    }

    public void SetTaunt(int team, int slot, int targetRawSlot, int duration)
    {
        if (targetRawSlot < 0 || targetRawSlot > 7)
        {
            Debug.LogWarning($"[CombatScript] SetTaunt invalid targetRawSlot {targetRawSlot} (must be 0-7).");
            return;
        }
        UnitData u = GetUnit(team, slot);
        u.tauntTargetSlot = targetRawSlot;
        u.tauntRemainingRounds = Mathf.Max(1, duration);
        SetUnit(team, slot, u);
    }

    #endregion


    #region Visual Feedback

    private readonly Dictionary<GameObject, Coroutine> _runningJiggles =
        new Dictionary<GameObject, Coroutine>();

    public void Jiggle(GameObject target)
    {
        if (target == null) return;

        if (_runningJiggles.TryGetValue(target, out Coroutine existing) && existing != null)
            StopCoroutine(existing);

        _runningJiggles[target] = StartCoroutine(JiggleRoutine(target));
    }

    IEnumerator JiggleRoutine(GameObject target)
    {
        if (target == null) { _runningJiggles.Remove(target); yield break; }

        Vector3 startPos = target.transform.localPosition;
        target.transform.localPosition = startPos + Vector3.up * 10f;
        yield return new WaitForSeconds(0.05f);

        if (target == null) { _runningJiggles.Remove(target); yield break; }
        target.transform.localPosition = startPos + Vector3.up * -10f;
        yield return new WaitForSeconds(0.05f);

        if (target == null) { _runningJiggles.Remove(target); yield break; }
        target.transform.localPosition = startPos;

        _runningJiggles.Remove(target);
    }

    #endregion


    #region Action Target Preview

    [System.Flags]
    public enum PreviewEffectCategory
    {
        None   = 0,
        Attack = 1 << 0, // direct damage: physical/fire/bleed/AP/poison
        Heal   = 1 << 1, // health restore/protect: recoverHealth/addHealth/addTempHealth
        Buff   = 1 << 2, // misc. team benefit: armor/shield restore or add
        Debuff = 1 << 3, // non-damage hostile effect: stun/sleep/confusion/petrification
    }

    [System.Serializable]
    public struct PreviewTarget
    {
        public int   team;
        public int   slot;
        public float portion;
        public bool  isOffensive; // true if Attack and/or Debuff is present; kept for back-compat
        public PreviewEffectCategory categories;
    }

    [System.Serializable]
    public struct ActionTargetPreview
    {
        public int casterTeam;
        public int casterSlot;
        public List<PreviewTarget> targets;

        public bool HasTargets => targets != null && targets.Count > 0;
    }

    public ActionTargetPreview GetActionTargetPreview(int casterTeam, int casterSlot, ActionData action)
    {
        var preview = new ActionTargetPreview
        {
            casterTeam = casterTeam,
            casterSlot = casterSlot,
            targets = new List<PreviewTarget>()
        };

        var coordLists = new List<(List<CasterPattern> coords, PreviewEffectCategory category)>
        {
            (action.physicalCoords,       PreviewEffectCategory.Attack),
            (action.fireCoords,           PreviewEffectCategory.Attack),
            (action.bleedCoords,          PreviewEffectCategory.Attack),
            (action.magicCoords,         PreviewEffectCategory.Attack),
            (action.poisonCoords,         PreviewEffectCategory.Attack),
            (action.stunCoords,           PreviewEffectCategory.Debuff),
            (action.sleepCoords,          PreviewEffectCategory.Debuff),
            (action.confusionCoords,      PreviewEffectCategory.Debuff),
            (action.petrificationCoords,  PreviewEffectCategory.Debuff),
            (action.recoverHealthCoords,  PreviewEffectCategory.Heal),
            (action.addHealthCoords,      PreviewEffectCategory.Heal),
            (action.addTempHealthCoords,  PreviewEffectCategory.Heal),
            (action.recoverArmorCoords,   PreviewEffectCategory.Buff),
            (action.addArmorCoords,       PreviewEffectCategory.Buff),
            (action.recoverShieldCoords,  PreviewEffectCategory.Buff),
            (action.addShieldCoords,      PreviewEffectCategory.Buff),
        };

        var seen = new Dictionary<(int team, int slot), PreviewTarget>();

        foreach (var (coords, category) in coordLists)
        {
            if (coords == null || coords.Count == 0) continue;
            var targets = GetEffectiveTargets(coords, casterSlot);

            bool isOffensiveCategory = category == PreviewEffectCategory.Attack
                                     || category == PreviewEffectCategory.Debuff;

            foreach (var te in targets)
            {
                var (tTeam, tSlot) = ResolveTarget(casterTeam, te.slot);
                if (tTeam < 0 || tSlot < 0) continue;

                int finalSlot = tSlot;
                if (action.hitAlive)
                {
                    UnitData targetUnit = GetUnit(tTeam, tSlot);
                    if (string.IsNullOrEmpty(targetUnit.name) || targetUnit.hp <= 0)
                    {
                        int aliveSlot = FindNearestAliveSlot(tTeam, tSlot);
                        if (aliveSlot >= 0) finalSlot = aliveSlot;
                    }
                }

                var key = (tTeam, finalSlot);
                if (seen.TryGetValue(key, out var existing))
                {
                    if (te.portion > existing.portion) existing.portion = te.portion;
                    existing.categories |= category;
                    existing.isOffensive |= isOffensiveCategory;
                    seen[key] = existing;
                }
                else
                {
                    seen[key] = new PreviewTarget
                    {
                        team = tTeam,
                        slot = finalSlot,
                        portion = te.portion,
                        isOffensive = isOffensiveCategory,
                        categories = category
                    };
                }
            }
        }

        preview.targets = new List<PreviewTarget>(seen.Values);
        return preview;
    }

    public ActionTargetPreview GetCurrentTargetPreview(int casterTeam, int casterSlot, ActionData action)
    {
        ActionData resolved = ApplyTauntRedirect(casterTeam, casterSlot, action);
        resolved = ApplyConfusionRedirect(casterTeam, casterSlot, resolved);
        return GetActionTargetPreview(casterTeam, casterSlot, resolved);
    }

    public ActionData ResolveActionForCaster(int casterTeam, int casterSlot, ActionData action)
    {
        ActionData resolved = ApplyTauntRedirect(casterTeam, casterSlot, action);
        resolved = ApplyConfusionRedirect(casterTeam, casterSlot, resolved);
        return resolved;
    }

    public ActionTargetPreview GetCurrentTargetPreview(int casterTeam, int casterSlot, int ActionIndex)
    {
        UnitData caster = GetUnit(casterTeam, casterSlot);
        if (caster.actions == null || ActionIndex < 0 || ActionIndex >= caster.actions.Count)
            return default;

        return GetCurrentTargetPreview(casterTeam, casterSlot, caster.actions[ActionIndex]);
    }

    #endregion


    #region Dice Helpers

    List<TargetEntry> GetEffectiveTargets(List<CasterPattern> patterns, int casterSlot)
    {
        var result = new List<TargetEntry>();
        if (patterns == null) return result;
        foreach (CasterPattern pattern in patterns)
        {
            if (pattern.casterSlot == -1 || pattern.casterSlot == casterSlot)
            {
                if (pattern.targets != null)
                {
                    // -1 means self resolve to caster slot
                    for (int i = 0; i < pattern.targets.Count; i++)
                    {
                        var te = pattern.targets[i];
                        if (te.slot < 0) te.slot = casterSlot;
                        result.Add(te);
                    }
                }
            }
        }
        return result;
    }

    public List<TargetEntry> GetEffectiveTargetsPublic(List<CasterPattern> patterns, int casterSlot)
        => GetEffectiveTargets(patterns, casterSlot);

    int FindFirstTargetAC(List<CasterPattern> coords, int casterTeam, int casterSlot)
    {
        if (coords == null || coords.Count == 0) return -1;
        var targets = GetEffectiveTargets(coords, casterSlot);
        if (targets.Count == 0) return -1;
        var (tTeam, tSlot) = ResolveTarget(casterTeam, targets[0].slot);
        if (tTeam < 0 || tSlot < 0) return -1;
        UnitData target = GetUnit(tTeam, tSlot);
        if (string.IsNullOrEmpty(target.name)) return -1;
        return Mathf.RoundToInt(GetEffectiveAC(tTeam, tSlot));
    }

    int FindFirstActionTargetAC(ActionData action, int casterTeam, int casterSlot)
    {
        var coordLists = new List<List<CasterPattern>>
        {
            action.physicalCoords, action.fireCoords, action.bleedCoords,
            action.magicCoords, action.poisonCoords,
            action.recoverHealthCoords, action.addHealthCoords,
            action.addTempHealthCoords, action.recoverShieldCoords,
            action.addShieldCoords, action.recoverArmorCoords,
            action.addArmorCoords, action.stunCoords, action.sleepCoords,
            action.confusionCoords, action.petrificationCoords
        };

        foreach (var coords in coordLists)
        {
            int ac = FindFirstTargetAC(coords, casterTeam, casterSlot);
            if (ac >= 0) return ac;
        }
        return -1;
    }

    public diceSystem.RollResult? TryRoll(string notation)
    {
        if (!IsDiceNotation(notation)) return null;
        if (diceRoller == null)
        {
            Debug.LogWarning("CombatScript has no diceSystem assigned - can't roll " + notation);
            return null;
        }
        return diceRoller.Roll(notation, target: -1, quiet: true);
    }

    static bool IsDiceNotation(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        string trimmed = s.Trim();

        if (int.TryParse(trimmed, out _)) return true;

        return diceSystem.IsValidNotation(trimmed);
    }

    public bool IsDiceNotationPublic(string s) => IsDiceNotation(s);

    #endregion
}
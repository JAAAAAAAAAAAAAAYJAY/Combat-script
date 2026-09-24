using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnitData       = CombatScript.UnitData;
using ActionData    = CombatScript.ActionData;
using CasterPattern  = CombatScript.CasterPattern;
using TargetEntry    = CombatScript.TargetEntry;

[DefaultExecutionOrder(50)]
public class combatSetup : MonoBehaviour
{

    #region Config
    [SerializeField] private sheetParser  sheetParser;
    [SerializeField] private CombatScript combat;

    #endregion


    #region Sheet Constants

    const int Plane1 = 0;
    const int Plane2 = 1;

    const int ColArmy       = 0;
    const int ColUnitNumber = 1;
    const int ColDetails    = 2;

    const int MaxStatRows = 8;

    const string ActionsMarker = "Actions";

    private bool _columnsResolved = false;

    #endregion


    #region Fallback Columns

    const int FallbackColPhysical          = 3;
    const int FallbackColHitChance         = 4;
    const int FallbackColCost              = 5;
    const int FallbackColLimit             = 6;
    const int FallbackColOverride          = 7;
    const int FallbackColHitAlive          = 8;
    const int FallbackColFireDmg           = 9;
    const int FallbackColFireTime          = 10;
    const int FallbackColBleedDmg          = 11;
    const int FallbackColBleedTime         = 12;
    const int FallbackColMagicDmg         = 13;
    const int FallbackColPoisonDmg         = 14;
    const int FallbackColPoisonTime        = 15;
    const int FallbackColStunTime          = 16;
    const int FallbackColSleepTime         = 17;
    const int FallbackColConfusionTime     = 18;
    const int FallbackColPetrificationTime = 19;
    const int FallbackColRecoverHealth     = 20;
    const int FallbackColAddTempHealth     = 21;
    const int FallbackColAddHealth         = 22;
    const int FallbackColRecoverArmor      = 23;
    const int FallbackColRecoverShield     = 24;
    const int FallbackColAddArmor          = 25;
    const int FallbackColAddShield         = 26;

    #endregion


    #region Header Aliases

    static readonly string[] HPhysical          = { "Physical" };
    static readonly string[] HHitChance         = { "HitChance"};
    static readonly string[] HCost              = { "Cost" };
    static readonly string[] HLimit             = { "Limit" };
    static readonly string[] HOverride          = { "Override" };
    static readonly string[] HHitAlive          = { "HitAlive" };
    static readonly string[] HFireDmg           = { "Fire DMG", "Fire Damage" };
    static readonly string[] HFireTime          = { "Fire Time"};
    static readonly string[] HBleedDmg          = { "Bleed DMG", "Bleed Damage" };
    static readonly string[] HBleedTime         = { "Bleed Time"};
    static readonly string[] HMagicDmg         = { "Magic Damage", "Energy DMG", "AP DMG"};
    static readonly string[] HPoisonDmg         = { "Poison DMG", "Poision DMG"};
    static readonly string[] HPoisonTime        = { "Poison Time", "Poision Time"};
    static readonly string[] HStunTime          = { "Stun Time"};
    static readonly string[] HSleepTime         = { "Sleep Time"};
    static readonly string[] HConfusionTime     = { "Confusion Time"};
    static readonly string[] HPetrificationTime = { "Petrification Time",};
    static readonly string[] HRecoverHealth     = { "RecoverHealth" };
    static readonly string[] HAddTempHealth     = { "AddTempHealth" };
    static readonly string[] HAddHealth         = { "AddHealth" };
    static readonly string[] HRecoverArmor      = { "RecoverArmor" };
    static readonly string[] HRecoverShield     = { "RecoverShield" };
    static readonly string[] HAddArmor          = { "AddArmor" };
    static readonly string[] HAddShield         = { "AddShield" };

    static readonly string[] OverrideCategoryPrefixes = { "basic", "skill", "ability", "passive" };

    #endregion


    #region Resolved Columns

    int ColPhysical, ColHitChance, ColCost, ColLimit, ColOverride, ColHitAlive,
        ColFireDmg, ColFireTime, ColBleedDmg, ColBleedTime, ColMagicDmg,
        ColPoisonDmg, ColPoisonTime, ColStunTime, ColSleepTime,
        ColConfusionTime, ColPetrificationTime, ColRecoverHealth,
        ColAddTempHealth, ColAddHealth, ColRecoverArmor, ColRecoverShield,
        ColAddArmor, ColAddShield;

    #endregion


    #region Unity Lifecycle

    void Start()
    {
    }

    public void LoadAllUnits()
    {
        _columnsResolved = false;
        if (sheetParser == null || combat == null)
        {
            Debug.LogError("[combatSetup] sheetParser or CombatScript not assigned - aborting setup.");
            return;
        }

        if (sheetParser.grid == null || sheetParser.grid.Count < 2)
        {
            Debug.LogError("[combatSetup] sheetParser grid not ready (need both Plane1 and Plane2). Aborting setup.");
            return;
        }
        int rows1 = sheetParser.grid[Plane1] != null ? sheetParser.grid[Plane1].Count : 0;
        int rows2 = sheetParser.grid[Plane2] != null ? sheetParser.grid[Plane2].Count : 0;
        if (rows1 != rows2)
        {
            Debug.LogWarning($"[combatSetup] Plane1 ({rows1} rows) and Plane2 ({rows2} rows) have different row counts. Cross-sheet Action lookups may misalign - verify both CSVs share the same row layout.");
        }

        ResolveColumns();

        combat.SetUnit(CombatScript.TeamPlayer, 0, fillemup(combat.GetUnit(CombatScript.TeamPlayer, 0)));
        combat.SetUnit(CombatScript.TeamPlayer, 1, fillemup(combat.GetUnit(CombatScript.TeamPlayer, 1)));
        combat.SetUnit(CombatScript.TeamPlayer, 2, fillemup(combat.GetUnit(CombatScript.TeamPlayer, 2)));
        combat.SetUnit(CombatScript.TeamPlayer, 3, fillemup(combat.GetUnit(CombatScript.TeamPlayer, 3)));
        combat.SetUnit(CombatScript.TeamEnemy,  0, fillemup(combat.GetUnit(CombatScript.TeamEnemy,  0)));
        combat.SetUnit(CombatScript.TeamEnemy,  1, fillemup(combat.GetUnit(CombatScript.TeamEnemy,  1)));
        combat.SetUnit(CombatScript.TeamEnemy,  2, fillemup(combat.GetUnit(CombatScript.TeamEnemy,  2)));
        combat.SetUnit(CombatScript.TeamEnemy,  3, fillemup(combat.GetUnit(CombatScript.TeamEnemy,  3)));

        combat.ResetActionCounts();


        if (CombatDebugHandler.UltraDebug)
        {
            //debug dump for unit_0
            UnitData u = combat.GetUnit(CombatScript.TeamPlayer, 0);
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[{u.name}]  HP:{u.hp}  AC:{u.ac}  AR:{u.armor}  AP:{u.ap}");
            if (u.actions != null)
            {
                foreach (var a in u.actions)
                {
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"  [{a.fullName}]  hit:{a.hitRoll}  phys:{a.physicalDmg}  cost:{a.APCost}  limit:{a.useLimit}");
                    LogPatterns("    physCoords",      a.physicalCoords);
                    LogPatterns("    addHealthCoords", a.addHealthCoords);
                }
            }
        }
    }

    #endregion


    #region Debug Logging

    void LogPatterns(string label, List<CasterPattern> patterns)
    {
        if (patterns == null || patterns.Count == 0) return;
        foreach (var p in patterns)
        {
            string caster = p.casterSlot < 0 ? "any" : p.casterSlot.ToString();
            var parts = new System.Text.StringBuilder();
            if (p.targets != null)
            {
                foreach (var t in p.targets)
                    parts.Append($"{t.slot}({t.portion * 100f:0}%) ");
            }
            if (CombatDebugHandler.UltraDebug) Debug.Log($"{label}  caster:{caster}  targets:[{parts.ToString().Trim()}]");
        }
    }

    #endregion


    #region Main Fillemup Flow

    // -------------------------------------------------------------------------
    // Main fill method
    // -------------------------------------------------------------------------
    public UnitData fillemup(UnitData data)
    {
        if (sheetParser == null || sheetParser.grid == null || sheetParser.grid.Count < 2)
        {
            Debug.LogWarning("[combatSetup] sheetParser grid not ready.");
            return data;
        }

        // validate lookups before wiping actions and passives
        List<string> codeValues = codeSeperator(data.unitcode);
        if (codeValues.Count < 2)
        {
            if (!string.IsNullOrWhiteSpace(data.unitcode))
                Debug.LogWarning($"[combatSetup] unitcode '{data.unitcode}' did not split into at least 2 parts (group_unit).");
            return data;
        }

        List<(int row, int col)> groupMatch =
            sheetParser.FindInColumn(codeValues[0], Plane1, ColArmy, true);
        if (groupMatch.Count == 0)
        {
            Debug.LogWarning($"[combatSetup] Army group '{codeValues[0]}' not found in Plane1 col {ColArmy}.");
            return data;
        }

        int groupRow = groupMatch[0].row;

        List<List<string>> sheet1 = sheetParser.grid[Plane1];
        if (sheet1 == null)
        {
            Debug.LogWarning("[combatSetup] Plane1 grid is null.");
            return data;
        }

        int groupBlockEnd = sheet1.Count - 1;
        for (int r = groupRow + 1; r < sheet1.Count; r++)
        {
            if (ColArmy < sheet1[r].Count &&
                !string.IsNullOrWhiteSpace(sheet1[r][ColArmy]))
            {
                groupBlockEnd = r - 1;
                break;
            }
        }

        List<(int row, int col)> unitMatch = sheetParser.Find(
            codeValues[1], Plane1,
            groupRow + 1, groupBlockEnd,
            ColUnitNumber, ColUnitNumber,
            true, true);
        if (unitMatch.Count == 0)
        {
            Debug.LogWarning($"[combatSetup] Unit '{codeValues[1]}' not found in group '{codeValues[0]}' (col {ColUnitNumber}, rows {groupRow + 1}-{groupBlockEnd}).");
            return data;
        }

        int unitRow = unitMatch[0].row;

        int unitBlockEnd = groupBlockEnd;
        for (int r = unitRow + 1; r <= groupBlockEnd; r++)
        {
            if (ColUnitNumber < sheet1[r].Count &&
                !string.IsNullOrWhiteSpace(sheet1[r][ColUnitNumber]))
            {
                unitBlockEnd = r - 1;
                break;
            }
        }

        List<(int row, int col)> actionsMarker = sheetParser.Find(
            ActionsMarker, Plane1,
            unitRow + 1, unitBlockEnd,
            ColDetails, ColDetails,
            true, true);

        int actionsStartRow = actionsMarker.Count > 0
            ? actionsMarker[0].row + 1 : -1;

        int statBlockEnd = actionsMarker.Count > 0
            ? actionsMarker[0].row - 1
            : Mathf.Min(unitRow + MaxStatRows, unitBlockEnd);

        data.name   = SafeCell(sheet1[unitRow], ColDetails);
        data.hp     = ParseFloat(FindLabelValue("HP", unitRow + 1, statBlockEnd, Plane1));
        data.maxHp  = data.hp;
        data.ac     = ParseFloat(FindLabelValue("AC", unitRow + 1, statBlockEnd, Plane1));
        var armorValues = SimpleSplit(FindLabelValue("AR", unitRow + 1, statBlockEnd, Plane1));
        data.armor     = armorValues.value1;
        data.armorBlock  = armorValues.value2;
        data.maxArmor = data.armor;
        data.shield = ParseFloat(FindLabelValue("SH", unitRow + 1, statBlockEnd, Plane1));
        data.maxShield = data.shield;
        string apValue = FindLabelValue("AP", unitRow + 1, statBlockEnd, Plane1);
        if (string.IsNullOrWhiteSpace(apValue))
            apValue = FindLabelValue("EN", unitRow + 1, statBlockEnd, Plane1);
        data.ap     = ParseFloat(apValue);

        data.actions = new List<ActionData>();
        data.ActionUseCounts = new List<int>();
        data.passiveKeys = new List<string>();
        data.fireRemainingRounds = 0;
        data.bleedRemainingRounds = 0;
        data.poisonRemainingRounds = 0;
        data.fireDamageNotation = "";
        data.bleedDamageNotation = "";
        data.poisonDamageNotation = "";
        data.stunRemainingRounds = 0;
        data.sleepRemainingRounds = 0;
        data.confusionRemainingRounds = 0;
        data.petrificationRemainingRounds = 0;
        data.tauntRemainingRounds = 0;
        data.tauntTargetSlot = -1;
        data.th = 0;

        if (data.hp <= 0 || data.maxHp <= 0 || data.ac < 0 || data.armor < 0 ||
            data.maxArmor < 0 || data.shield < 0 || data.maxShield < 0 || data.ap < 0)
        {
            Debug.LogError($"[combatSetup] Unit '{data.name}' has invalid stats - refusing to load actions/passives. " +
                $"hp={data.hp}, maxHp={data.maxHp}, ac={data.ac}, armor={data.armor}/{data.maxArmor}, " +
                $"shield={data.shield}/{data.maxShield}, ap={data.ap}. Returning data with cleared actions.");
            return data;
        }

        if (actionsStartRow < 0)
        {
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[combatSetup] No '{ActionsMarker}' marker for unit '{data.name}' - loaded with stats only.");
            return data;
        }

        List<List<string>> sheet2 = sheetParser.grid[Plane2];

        for (int r = actionsStartRow; r <= unitBlockEnd; r++)
        {
            List<string> rowP1 = sheet1[r];
            List<string> rowP2 = r < sheet2.Count ? sheet2[r] : new List<string>();

            string labelP1 = SafeCell(rowP1, ColDetails);
            string labelP2 = SafeCell(rowP2, ColDetails);

            string label   = !string.IsNullOrWhiteSpace(labelP1) ? labelP1 : labelP2;

            string detailsP1Trim = labelP1.Trim();
            string detailsP2Trim = labelP2.Trim();
            if (!string.IsNullOrEmpty(detailsP1Trim) && !string.IsNullOrEmpty(detailsP2Trim) &&
                !detailsP1Trim.Equals(detailsP2Trim, System.StringComparison.Ordinal))
            {
                Debug.LogWarning($"[combatSetup] Plane1/Plane2 misalignment at row {r}: " +
                    $"Plane1='{detailsP1Trim}', Plane2='{detailsP2Trim}'.");
            }

            if (string.IsNullOrWhiteSpace(label)) continue;

            string ActionName = label;
            if      (label.StartsWith("basic_",   System.StringComparison.OrdinalIgnoreCase))
                ActionName = label.Substring("basic_".Length).Trim();
            else if (label.StartsWith("skill_",   System.StringComparison.OrdinalIgnoreCase))
                ActionName = label.Substring("skill_".Length).Trim();
            else if (label.StartsWith("ability_", System.StringComparison.OrdinalIgnoreCase))
                ActionName = label.Substring("ability_".Length).Trim();
            else if (label.StartsWith("passive_", System.StringComparison.OrdinalIgnoreCase))
                ActionName = label.Substring("passive_".Length).Trim();

            string overrideRaw = SafeCell(rowP1, ColOverride).Trim();
            string overrideKey = "";
            string overrideCategory = "";

            if (!string.IsNullOrWhiteSpace(overrideRaw))
            {
                overrideKey = overrideRaw;
                overrideCategory = ClassifyOverrideKey(overrideRaw);

                if (string.IsNullOrEmpty(overrideCategory))
                {
                    Debug.LogWarning($"[combatSetup] Override key '{overrideRaw}' (label '{label}') doesn't start " +
                        $"with a known classifier prefix (basic_/skill_/ability_/passive_) - it will fall back to " +
                        $"the default executor.");
                }
                if (overrideCategory == "basic")
                {
                    if (OverrideRegistry.GetBasic(overrideKey) == null)
                        Debug.LogWarning($"[combatSetup] OverrideRegistry has no basic handler for key '{overrideKey}' (label '{label}').");
                }
                else if (overrideCategory == "skill")
                {
                    if (OverrideRegistry.GetSkill(overrideKey) == null)
                        Debug.LogWarning($"[combatSetup] OverrideRegistry has no skill handler for key '{overrideKey}' (label '{label}').");
                }
                else if (overrideCategory == "ability")
                {
                    if (OverrideRegistry.GetAbility(overrideKey) == null)
                        Debug.LogWarning($"[combatSetup] OverrideRegistry has no ability handler for key '{overrideKey}' (label '{label}').");
                }
                else if (overrideCategory == "passive")
                {
                    if (OverrideRegistry.GetPassiveType(overrideKey) == null)
                        Debug.LogWarning($"[combatSetup] OverrideRegistry has no passive type for key '{overrideKey}' (label '{label}').");
                }
            }

            if (overrideCategory == "passive")
            {
                if (!string.IsNullOrWhiteSpace(overrideKey))
                {
                    data.passiveKeys.Add(overrideKey);
                    if (CombatDebugHandler.UltraDebug) Debug.Log($"[combatSetup] {data.name}: loaded passive '{overrideKey}' (label '{label}').");
                }
                continue;
            }

            ActionData act = new ActionData();
            act.fullName    = label;
            act.ActionName = ActionName;
            act.overrideKey = overrideKey;
            act.overrideCategory = overrideCategory;
            act.APCost  = ParseCost(SafeCell(rowP1, ColCost));
            act.useLimit    = ParseInt(SafeCell(rowP1, ColLimit));
            act.hitAlive    = ParseBool(SafeCell(rowP1, ColHitAlive));

            act.hitRoll = SafeCell(rowP1, ColHitChance);

            act.physicalDmg          = SafeCell(rowP1, ColPhysical);
            act.physicalCoords       = ParseCoords(SafeCell(rowP2, ColPhysical));

            act.fireDmg              = SafeCell(rowP1, ColFireDmg);
            act.fireCoords           = ParseCoords(SafeCell(rowP2, ColFireDmg));
            act.fireDuration         = ParseInt(SafeCell(rowP1, ColFireTime));

            act.bleedDmg             = SafeCell(rowP1, ColBleedDmg);
            act.bleedCoords          = ParseCoords(SafeCell(rowP2, ColBleedDmg));
            act.bleedDuration        = ParseInt(SafeCell(rowP1, ColBleedTime));

            act.magicDmg            = SafeCell(rowP1, ColMagicDmg);
            act.magicCoords         = ParseCoords(SafeCell(rowP2, ColMagicDmg));

            act.poisonDmg            = SafeCell(rowP1, ColPoisonDmg);
            act.poisonCoords         = ParseCoords(SafeCell(rowP2, ColPoisonDmg));
            act.poisonDuration       = ParseInt(SafeCell(rowP1, ColPoisonTime));

            act.stunTime             = SafeCell(rowP1, ColStunTime);
            act.stunCoords           = ParseCoords(SafeCell(rowP2, ColStunTime));

            act.sleepTime            = SafeCell(rowP1, ColSleepTime);
            act.sleepCoords          = ParseCoords(SafeCell(rowP2, ColSleepTime));

            act.confusionTime        = SafeCell(rowP1, ColConfusionTime);
            act.confusionCoords      = ParseCoords(SafeCell(rowP2, ColConfusionTime));

            act.petrificationTime    = SafeCell(rowP1, ColPetrificationTime);
            act.petrificationCoords  = ParseCoords(SafeCell(rowP2, ColPetrificationTime));

            act.recoverHealth        = SafeCell(rowP1, ColRecoverHealth);
            act.recoverHealthCoords  = ParseCoords(SafeCell(rowP2, ColRecoverHealth));

            act.addTempHealth        = SafeCell(rowP1, ColAddTempHealth);
            act.addTempHealthCoords  = ParseCoords(SafeCell(rowP2, ColAddTempHealth));

            act.addHealth            = SafeCell(rowP1, ColAddHealth);
            act.addHealthCoords      = ParseCoords(SafeCell(rowP2, ColAddHealth));

            act.recoverArmor         = SafeCell(rowP1, ColRecoverArmor);
            act.recoverArmorCoords   = ParseCoords(SafeCell(rowP2, ColRecoverArmor));

            act.recoverShield        = SafeCell(rowP1, ColRecoverShield);
            act.recoverShieldCoords  = ParseCoords(SafeCell(rowP2, ColRecoverShield));

            act.addArmor             = SafeCell(rowP1, ColAddArmor);
            act.addArmorCoords       = ParseCoords(SafeCell(rowP2, ColAddArmor));

            act.addShield            = SafeCell(rowP1, ColAddShield);
            act.addShieldCoords      = ParseCoords(SafeCell(rowP2, ColAddShield));

            AddAction(ref data, act);
        }

        return data;
    }

    void AddAction(ref UnitData data, ActionData act)
    {
        data.actions.Add(act);
        data.ActionUseCounts.Add(0);
    }

    #endregion


    #region Column Resolution

    void ResolveColumns()
    {
        if (_columnsResolved) return;

        if (sheetParser.grid == null || sheetParser.grid.Count < 1) { UseFallbackColumns(); _columnsResolved = true; return; }

        List<List<string>> sheet1 = sheetParser.grid[Plane1];
        if (sheet1 == null || sheet1.Count == 0) { UseFallbackColumns(); _columnsResolved = true; return; }

        List<string> header = sheet1[0];

        ColPhysical          = ResolveCol(header, HPhysical,          FallbackColPhysical);
        ColHitChance         = ResolveCol(header, HHitChance,         FallbackColHitChance);
        ColCost              = ResolveCol(header, HCost,              FallbackColCost);
        ColLimit             = ResolveCol(header, HLimit,             FallbackColLimit);
        ColOverride          = ResolveCol(header, HOverride,          FallbackColOverride);
        ColHitAlive          = ResolveCol(header, HHitAlive,          FallbackColHitAlive);
        ColFireDmg           = ResolveCol(header, HFireDmg,           FallbackColFireDmg);
        ColFireTime          = ResolveCol(header, HFireTime,          FallbackColFireTime);
        ColBleedDmg          = ResolveCol(header, HBleedDmg,          FallbackColBleedDmg);
        ColBleedTime         = ResolveCol(header, HBleedTime,         FallbackColBleedTime);
        ColMagicDmg         = ResolveCol(header, HMagicDmg,         FallbackColMagicDmg);
        ColPoisonDmg         = ResolveCol(header, HPoisonDmg,         FallbackColPoisonDmg);
        ColPoisonTime        = ResolveCol(header, HPoisonTime,        FallbackColPoisonTime);
        ColStunTime          = ResolveCol(header, HStunTime,          FallbackColStunTime);
        ColSleepTime         = ResolveCol(header, HSleepTime,         FallbackColSleepTime);
        ColConfusionTime     = ResolveCol(header, HConfusionTime,     FallbackColConfusionTime);
        ColPetrificationTime = ResolveCol(header, HPetrificationTime, FallbackColPetrificationTime);
        ColRecoverHealth     = ResolveCol(header, HRecoverHealth,     FallbackColRecoverHealth);
        ColAddTempHealth     = ResolveCol(header, HAddTempHealth,     FallbackColAddTempHealth);
        ColAddHealth         = ResolveCol(header, HAddHealth,         FallbackColAddHealth);
        ColRecoverArmor      = ResolveCol(header, HRecoverArmor,      FallbackColRecoverArmor);
        ColRecoverShield     = ResolveCol(header, HRecoverShield,     FallbackColRecoverShield);
        ColAddArmor          = ResolveCol(header, HAddArmor,          FallbackColAddArmor);
        ColAddShield         = ResolveCol(header, HAddShield,         FallbackColAddShield);

        _columnsResolved = true;
    }

    static int ResolveCol(List<string> header, string[] aliases, int fallback)
    {
        if (header == null) return fallback;
        foreach (string alias in aliases)
        {
            string normAlias = NormalizeHeader(alias);
            for (int i = 0; i < header.Count; i++)
            {
                if (NormalizeHeader(header[i]) == normAlias)
                    return i;
            }
        }
        Debug.LogWarning($"[combatSetup] Header '{aliases[0]}' not found - using fallback col {fallback}.");
        return fallback;
    }

    static string NormalizeHeader(string s)
        => s == null ? "" : s.Replace(" ", "").Trim().ToLowerInvariant();

    void UseFallbackColumns()
    {
        ColPhysical          = FallbackColPhysical;
        ColHitChance         = FallbackColHitChance;
        ColCost              = FallbackColCost;
        ColLimit             = FallbackColLimit;
        ColOverride          = FallbackColOverride;
        ColHitAlive          = FallbackColHitAlive;
        ColFireDmg           = FallbackColFireDmg;
        ColFireTime          = FallbackColFireTime;
        ColBleedDmg          = FallbackColBleedDmg;
        ColBleedTime         = FallbackColBleedTime;
        ColMagicDmg         = FallbackColMagicDmg;
        ColPoisonDmg         = FallbackColPoisonDmg;
        ColPoisonTime        = FallbackColPoisonTime;
        ColStunTime          = FallbackColStunTime;
        ColSleepTime         = FallbackColSleepTime;
        ColConfusionTime     = FallbackColConfusionTime;
        ColPetrificationTime = FallbackColPetrificationTime;
        ColRecoverHealth     = FallbackColRecoverHealth;
        ColAddTempHealth     = FallbackColAddTempHealth;
        ColAddHealth         = FallbackColAddHealth;
        ColRecoverArmor      = FallbackColRecoverArmor;
        ColRecoverShield     = FallbackColRecoverShield;
        ColAddArmor          = FallbackColAddArmor;
        ColAddShield         = FallbackColAddShield;
    }

    #endregion


    #region Coord Parsing

    // -------------------------------------------------------------------------
    // Coord parsing
    // -------------------------------------------------------------------------
    public List<CasterPattern> ParseCoords(string raw)
    {
        var result = new List<CasterPattern>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        string[] segments = raw.Split('|');

        foreach (string seg in segments)
        {
            string trimmed = seg.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            int casterSlot = -1;
            string targetPart = trimmed;

            int xIdx = trimmed.IndexOf('x', System.StringComparison.OrdinalIgnoreCase);
            if (xIdx == 0)
            {
                Debug.LogWarning($"[combatSetup] Malformed coord segment '{trimmed}' - 'x' with no caster, treating as no-caster.");
            }
            else if (xIdx > 0 && int.TryParse(trimmed.Substring(0, xIdx), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCaster))
            {
                casterSlot = parsedCaster;
                targetPart = trimmed.Substring(xIdx + 1);
            }

            var pattern = new CasterPattern
            {
                casterSlot = casterSlot,
                targets    = ParseTargetList(targetPart)
            };

            result.Add(pattern);
        }

        return result;
    }

    List<TargetEntry> ParseTargetList(string targetPart)
    {
        var targets = new List<TargetEntry>();
        if (string.IsNullOrWhiteSpace(targetPart)) return targets;

        string[] slots = targetPart.Split('_');

        foreach (string slotRaw in slots)
        {
            string s = slotRaw.Trim();
            if (string.IsNullOrWhiteSpace(s)) continue;

            // sentinel so parse failure stays distinct from explicit -1 self
            const int NoSlot = int.MinValue;
            int    slot    = NoSlot;
            float  portion = 1f;

            int parenOpen  = s.IndexOf('(');
            int parenClose = s.IndexOf(')');

            if (parenOpen > 0 && parenClose > parenOpen)
            {
                string slotStr    = s.Substring(0, parenOpen).Trim();
                string percentStr = s.Substring(parenOpen + 1, parenClose - parenOpen - 1)
                                     .Replace("%", "").Trim();

                if (int.TryParse(slotStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ps))       slot    = ps;
                if (float.TryParse(percentStr, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out float pc))
                {
                    if (pc < 0f || pc > 100f)
                        Debug.LogWarning($"[combatSetup] Target portion {pc}% out of range [0-100] for slot '{slotStr}', clamping.");
                    portion = Mathf.Clamp01(pc / 100f);
                }
            }
            else
            {
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ps)) slot = ps;
            }

            if (slot == NoSlot) continue;
            // -1 means self flows through resolved to caster slot later
            if (slot < -1 || slot > 7)
            {
                Debug.LogWarning($"[combatSetup] Target slot {slot} out of range (-1 for self, 0-7), skipping.");
                continue;
            }
            targets.Add(new TargetEntry { slot = slot, portion = portion });
        }

        return targets;
    }

    #endregion


    #region Parse / Lookup Helpers

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    string SafeCell(List<string> row, int col)
        => (row != null && col >= 0 && col < row.Count) ? row[col] : "";

    string ClassifyOverrideKey(string overrideKey)
    {
        if (string.IsNullOrWhiteSpace(overrideKey)) return "";

        int underscoreIdx = overrideKey.IndexOf('_');
        if (underscoreIdx <= 0) return "";

        string prefix = overrideKey.Substring(0, underscoreIdx).Trim().ToLowerInvariant();
        foreach (string known in OverrideCategoryPrefixes)
        {
            if (prefix == known) return known;
        }
        return "";
    }

    string FindLabelValue(string label, int rowStart, int rowEnd, int sheetIndex)
    {
        if (sheetParser == null || sheetParser.grid == null) return "";
        if (sheetIndex < 0 || sheetIndex >= sheetParser.grid.Count) return "";

        var match = sheetParser.Find(
            label, sheetIndex, rowStart, rowEnd,
            ColDetails, ColDetails, true, true);

        if (match.Count > 0)
        {
            int valueCol = match[0].col + 1;
            List<string> row = sheetParser.grid[sheetIndex][match[0].row];
            if (valueCol < row.Count) return row[valueCol];
        }
        return "";
    }

    float ParseFloat(string value)
        => float.TryParse(value, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out float result) ? result : 0f;

    int ParseInt(string value)
        => int.TryParse(value == null ? "" : value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;

    int ParseCost(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        if (value.Trim().Equals("Free", System.StringComparison.OrdinalIgnoreCase)) return 0;
        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return parsed;
        Debug.LogWarning($"[combatSetup] ParseCost could not parse '{value}' as int - defaulting to 0.");
        return 0;
    }

    bool ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string v = value.Trim();
        if (string.Equals(v, "true",  System.StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(v, "false", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(v, "yes",   System.StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(v, "no",    System.StringComparison.OrdinalIgnoreCase)) return false;
        if (v == "1") return true;
        if (v == "0") return false;
        return false;
    }

    public List<string> codeSeperator(string code)
    {
        var values = new List<string>();
        if (string.IsNullOrEmpty(code))
        {
            return values;
        }
        foreach (string part in code.Split('_'))
            values.Add(part.Trim());
        return values;
    }

    public (float value1, float value2) SimpleSplit(string code)
    {
        float value1 = 0f;
        float value2 = 0f;

        // An empty/null/whitespace code is a VALID "no value" case, not an error.
        // The only caller (fillemup, line 304) feeds us FindLabelValue("AR", ...),
        // which returns "" when:
        //   * the "AR" label is absent for a unit, OR
        //   * the "AR" label exists but its value cell is blank (e.g. ",,AR,,,").
        // Both legitimately mean "this unit has no armor" => 0 / 0. Warning here
        // just spammed the console for every such unit, so we return silently.
        if (string.IsNullOrWhiteSpace(code))
            return (value1, value2);

        string[] parts = code.Split('-');

        if (parts.Length > 0)
        {
            string p0 = parts[0].Trim();
            if (!string.IsNullOrEmpty(p0) &&
                !float.TryParse(p0, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value1))
                Debug.LogWarning($"[combatSetup] SimpleSplit could not parse part 0 '{p0}' (code='{code}') as float - defaulting to 0.");
        }
        if (parts.Length > 1)
        {
            string p1 = parts[1].Trim();
            if (!string.IsNullOrEmpty(p1) &&
                !float.TryParse(p1, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value2))
                Debug.LogWarning($"[combatSetup] SimpleSplit could not parse part 1 '{p1}' (code='{code}') as float - defaulting to 0.");
        }

        if (parts.Length == 1 && value1 > 0)
            value2 = value1;

        return (value1, value2);
    }

    #endregion
}
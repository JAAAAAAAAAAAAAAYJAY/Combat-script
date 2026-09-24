using UnityEngine;

[CreateAssetMenu(fileName = "WeightsTemplate", menuName = "Combat/Weights Template")]
public class WeightsTemplate : ScriptableObject
{
    [Tooltip("How the AI values effects on its OWN team. Used when scoring heals, buffs, and survival for friendly units. Both player AI and enemy AI use this tab for their own units (symmetric).")]
    public AllyWeights allies  = new AllyWeights();
    [Tooltip("How the AI values effects on the OPPOSING team. Used when scoring damage, kills, and debuffs on hostile units. Both player AI and enemy AI use this tab for the enemy they're fighting (symmetric).")]
    public EnemyWeights enemies = new EnemyWeights();
    public AiSettings ai = new AiSettings();

    void OnValidate()
    {
        if (allies != null) { ValidateStatWeights(allies); ValidateTeamCurves(allies); allies.scoring.OnValidate(); }
        if (enemies != null) { ValidateStatWeights(enemies); ValidateTeamCurves(enemies); enemies.scoring.OnValidate(); }
        ai.OnValidate();
    }

    static void ValidateStatWeights(TeamWeightsBase w)
    {
        w.hpWeight = Mathf.Max(0, w.hpWeight);
        w.armorWeight = Mathf.Max(0, w.armorWeight);
        w.shieldWeight = Mathf.Max(0, w.shieldWeight);
        w.totalHpAgressionWeight = Mathf.Max(0, w.totalHpAgressionWeight);
    }

    // Ensure curves are never null and clamp negative Y values to 0 so a
    // designer can't accidentally produce negative scores via the inspector.
    static void ValidateTeamCurves(TeamWeightsBase w)
    {
        w.hpCurve          = SanitizeCurve(w.hpCurve);
        w.armorCurve       = SanitizeCurve(w.armorCurve);
        w.shieldCurve      = SanitizeCurve(w.shieldCurve);
        w.agressionHpCurve = SanitizeCurve(w.agressionHpCurve);
    }

    static AnimationCurve SanitizeCurve(AnimationCurve c)
    {
        if (c == null) return AnimationCurve.Linear(0f, 0f, 1f, 1f);
        if (c.length == 0) return AnimationCurve.Linear(0f, 0f, 1f, 1f);
        var keys = c.keys;
        bool changed = false;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i].value < 0f) { keys[i].value = 0f; changed = true; }
        }
        return changed ? new AnimationCurve(keys) : c;
    }
}

[System.Serializable]
public abstract class TeamWeightsBase
{
    [Header("Stat weights (team-strength evaluation - how valuable is each unit's current state)")]
    [Tooltip("Weight × hpCurve(hpMissingFrac) for HP scoring")]
    public float hpWeight     = 0.1f;
    [Tooltip("Weight × armorCurve(armorMissingFrac) for armor scoring")]
    public float armorWeight  = 0.1f;
    [Tooltip("Weight × shieldCurve(shieldMissingFrac) for shield scoring")]
    public float shieldWeight = 0.1f;
    [Tooltip("Multiplier applied to the team total; raises aggression when the team is badly hurt (heal/defend more)")]
    public float totalHpAgressionWeight = 1f;

    [Header("Stat curves")]
    public AnimationCurve hpCurve     = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public AnimationCurve armorCurve  = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public AnimationCurve shieldCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public AnimationCurve agressionHpCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
}

[System.Serializable]
public class AllyWeights : TeamWeightsBase
{
    [Header("How the AI scores effects on its own units")]
    public AllyScoring scoring = new AllyScoring();
}

[System.Serializable]
public class EnemyWeights : TeamWeightsBase
{
    [Header("How the AI scores effects on the opposing team")]
    public EnemyScoring scoring = new EnemyScoring();
}

[System.Serializable]
public class AllyScoring
{
    #region Toggles

    [Tooltip("When true, the AI evaluates whether a heal would prevent an ally from dying to incoming threat and adds a survival bonus.")]
    public bool enableSurvival = true;
    [Tooltip("When true, heals that prevent death get an extra multiplier.")]
    public bool enableHealPreventsDeathBonus = true;
    [Tooltip("When true, buffs on low-HP allies get an extra multiplier (defensive buffing on dying allies).")]
    public bool enableBuffLowHealthBonus = true;
    [Tooltip("When true, heals that would overheal (waste healing beyond max HP) get a penalty.")]
    public bool enableOverhealPenalty = true;
    [Tooltip("Per-point of overhealing (heal amount beyond missing HP) penalty. Only used when enableOverhealPenalty is on.")]
    public float overhealPenaltyWeight = 0.3f;

    #endregion

    #region Target importance

    [Tooltip("X = unit importance (0-1, based on HP/armor/shield), Y = multiplier. Strong allies are worth more to heal/buff.")]
    public AnimationCurve targetImportanceCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);

    #endregion

    #region Survival / prevent death

    [Tooltip("Bonus for an action that prevents an ally from dying this turn. Scaled by lowHealthCurve × importance. Only when enableSurvival is on.")]
    public float survivalWeight = 30.0f;

    #endregion

    #region Healing

    [Tooltip("Per-point-of-effective-healing score (effective = min(heal, missingHp)). Low-HP allies naturally score higher via healMissingCurve.")]
    public float healWeight = 0.8f;
    [Tooltip("X = ally HP-fraction-missing (0-1, 1 = nearly dead), Y = multiplier. Higher at low HP = heal weak allies first.")]
    public AnimationCurve healMissingCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);
    [Tooltip("Multiplier on heal value when the heal also prevents death. Only when enableHealPreventsDeathBonus is on.")]
    public float healPreventsDeathBonus = 1.5f;

    #endregion

    #region Buffs

    [Tooltip("Per-point-of-buff-amount score (armor/shield/temp-hp restored or added).")]
    public float buffEffectValueWeight = 0.5f;
    [Tooltip("Multiplier based on the receiving unit's current strength.")]
    public float buffTargetStrengthWeight = 1.0f;
    [Tooltip("Multiplier based on how likely the receiving unit is to attack soon.")]
    public float buffLikelyToAttackWeight = 1.0f;
    [Tooltip("Extra multiplier when the buff target is low on health. Only when enableBuffLowHealthBonus is on.")]
    public float buffLowHealthWeight = 0.75f;
    [Tooltip("X = unit importance (0-1), Y = multiplier for the buff target.")]
    public AnimationCurve buffTargetStrengthCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);

    #endregion

    public void OnValidate()
    {
        survivalWeight             = Mathf.Max(0, survivalWeight);
        healWeight                 = Mathf.Max(0, healWeight);
        healPreventsDeathBonus     = Mathf.Max(0, healPreventsDeathBonus);
        buffEffectValueWeight      = Mathf.Max(0, buffEffectValueWeight);
        buffTargetStrengthWeight   = Mathf.Max(0, buffTargetStrengthWeight);
        buffLikelyToAttackWeight   = Mathf.Max(0, buffLikelyToAttackWeight);
        buffLowHealthWeight        = Mathf.Max(0, buffLowHealthWeight);
        overhealPenaltyWeight      = Mathf.Max(0, overhealPenaltyWeight);
    }
}

[System.Serializable]
public class EnemyScoring
{
    #region Toggles

    [Tooltip("When true, damage to low-HP enemies scores higher (focus fire to finish them off). When false, all enemies are valued equally regardless of HP.")]
    public bool enableFocusFire = true;
    [Tooltip("When true, expected kills get a flat bonus on top of damage score.")]
    public bool enableKillBonus = true;
    [Tooltip("When true, target importance (unit strength) affects how much damage is worth dealing to that target.")]
    public bool enableDamageImportance = false;
    [Tooltip("When true, high-importance enemies get extra debuff score (prioritise neutralising dangerous enemies).")]
    public bool enableDebuffHighThreat = true;
    [Tooltip("When true, the AI adds bonus score for attacking/debuffing enemies that pose the highest threat (most expected outgoing damage next turn). Makes the AI prioritise neutralising dangerous enemies.")]
    public bool enableThreatTargeting = true;

    #endregion

    #region Target importance

    [Tooltip("X = unit importance (0-1, based on HP/armor/shield), Y = multiplier. Strong enemies are worth more to kill/debuff.")]
    public AnimationCurve targetImportanceCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);

    #endregion

    #region Damage

    [Tooltip("Per-point-of-expected-damage score.")]
    public float damageWeight = 1.0f;
    [Tooltip("X = target HP fraction (1 = full, 0 = near death), Y = multiplier. Only used when enableFocusFire is on.")]
    public AnimationCurve damageFocusFireCurve = new AnimationCurve(
        new Keyframe(0.00f, 1.5f),
        new Keyframe(0.25f, 1.25f),
        new Keyframe(0.50f, 1.00f),
        new Keyframe(1.00f, 0.80f));

    #endregion

    #region Kills

    [Tooltip("Flat bonus when an action is expected to defeat a target. Only when enableKillBonus is on.")]
    public float killWeight = 25.0f;
    [Tooltip("X = target HP fraction (1 = full, 0 = dead), Y = multiplier. Leans toward finishing low-HP targets.")]
    public AnimationCurve killHpFractionCurve = new AnimationCurve(
        new Keyframe(0.00f, 1.25f),
        new Keyframe(0.25f, 1.10f),
        new Keyframe(0.50f, 1.00f),
        new Keyframe(1.00f, 0.85f));

    #endregion

    #region Threat targeting

    [Tooltip("Bonus score added for attacking/debuffing high-threat enemies. Scaled by the enemy's expected outgoing damage. Only when enableThreatTargeting is on.")]
    public float threatTargetingWeight = 8.0f;
    [Tooltip("X = normalised threat (0 = harmless, 1 = most dangerous enemy on the field), Y = multiplier.")]
    public AnimationCurve threatTargetingCurve = AnimationCurve.Linear(0f, 0f, 1f, 1.0f);

    #endregion

    #region Debuffs

    [Tooltip("Per-turn-of-applied-debuff score, scaled by target importance.")]
    public float debuffWeight = 6.0f;
    [Tooltip("X = target importance, Y = multiplier for the debuff target.")]
    public AnimationCurve debuffTargetCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);
    [Tooltip("Extra multiplier when debuffing a high-threat enemy. Only when enableDebuffHighThreat is on.")]
    public float debuffHighThreatBonus = 1.25f;

    #endregion

    public void OnValidate()
    {
        damageWeight               = Mathf.Max(0, damageWeight);
        killWeight                 = Mathf.Max(0, killWeight);
        debuffWeight               = Mathf.Max(0, debuffWeight);
        debuffHighThreatBonus      = Mathf.Max(0, debuffHighThreatBonus);
        threatTargetingWeight      = Mathf.Max(0, threatTargetingWeight);
    }
}

[System.Serializable]
public class AiSettings
{
    #region Action generation toggles

    [Tooltip("When true, the AI considers pure move actions (reposition without attacking).")]
    public bool enableMoveActions = true;
    [Tooltip("When true, the AI considers move + Action combinations (reposition then attack from the new slot).")]
    public bool enableMoveAndAction = true;
    [Tooltip("When true, the AI considers bench swap actions (replace active unit with a benched one).")]
    public bool enableBenchSwaps = true;

    #endregion

    #region Scoring toggles

    [Tooltip("When true, remaining-use abundance affects Action scoring (using an Action with many uses left is 'cheaper').")]
    public bool enableResourceScoring = true;
    [Tooltip("When true, the AI penalises AP costs so expensive actions are less attractive when AP is low.")]
    public bool enableAPCostPenalty = true;

    #endregion

    #region Action variety / fatigue

    [Tooltip("When true, actions accumulate fatigue each time they are used (+1), and units accumulate fatigue each time THEY act with any Action (+1). Each turn something is not used, its fatigue recovers by fatigueRecoveryPerTurn. The AI cycles through both its moves and its units over multiple turns.")]
    public bool preferVariedActions = true;
    [Tooltip("Flat penalty added when an Action has been used this turn (only when preferVariedActions is on). Each use adds +1 to the fatigue counter, and the penalty is -(actionFatigueWeight + fatigue). Lower = less steep.")]
    public float actionFatigueWeight = 3.0f;
    [Tooltip("Flat penalty added when the ACTING UNIT (regardless of which Action) has acted recently (only when preferVariedActions is on). Each action by that unit adds +1 to its fatigue counter, and the penalty is -(unitFatigueWeight + fatigue), on top of any Action-specific fatigue. Encourages spreading actions across different units instead of repeatedly acting with the same one. Lower = less steep; 0 disables the unit-variety nudge while keeping Action variety.")]
    public float unitFatigueWeight = 2.0f;
    [Tooltip("How much fatigue recovers each turn an Action or unit is NOT used.")]
    public float fatigueRecoveryPerTurn = 0.5f;

    #endregion

    #region Positioning / movement

    [Tooltip("Score per net new reachable target gained by moving to a new slot.")]
    public float positionWeight = 2.0f;
    [Tooltip("Bonus for moving a nearly-dead unit out of a frontline slot. Scaled by lowHealthCurve.")]
    public float positionSafetyWeight = 1.5f;
    [Tooltip("X = slot index (0 = frontline, 3 = back), Y = positional danger multiplier.")]
    public AnimationCurve slotDangerCurve = new AnimationCurve(
        new Keyframe(0f, 1.0f),
        new Keyframe(1f, 0.85f),
        new Keyframe(2f, 0.6f),
        new Keyframe(3f, 0.4f));
    [Tooltip("X = HP fraction (0 = nearly dead, 1 = full), Y = multiplier. Used by survival, buff, move-safety, and bench-swap scoring.")]
    public AnimationCurve lowHealthCurve = new AnimationCurve(
        new Keyframe(0.00f, 2.0f),
        new Keyframe(0.25f, 1.0f),
        new Keyframe(0.50f, 0.3f),
        new Keyframe(1.00f, 0.0f));

    #endregion

    #region Resources / remaining uses

    [Tooltip("Score proportional to remaining-use abundance.")]
    public float resourceWeight = 1.0f;
    [Tooltip("X = fraction of uses remaining (0-1), Y = multiplier.")]
    public AnimationCurve resourceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    #endregion

    #region AP

    [Tooltip("Per-point AP-cost penalty. Only when enableAPCostPenalty is on.")]
    public float APCostWeight = 1.0f;
    [Tooltip("X = team AP fraction remaining (0 = empty), Y = scarcity multiplier.")]
    public AnimationCurve APScarcityCurve = new AnimationCurve(
        new Keyframe(0.00f, 2.0f),
        new Keyframe(0.25f, 1.5f),
        new Keyframe(0.50f, 1.0f),
        new Keyframe(1.00f, 0.5f));

    #endregion

    #region Idle / AP overflow

    [Tooltip("When true, Idle gets a flat penalty so the AI prefers doing at least one action before ending the turn.")]
    public bool enableIdlePenalty = true;
    [Tooltip("Flat penalty on Idle. Higher = AI is more reluctant to skip its turn.")]
    public float idlePenalty = 10.0f;
    [Tooltip("When true, actions that don't spend AP get penalised if the team is at max AP (regen would be wasted).")]
    public bool enableAPOverflowPenalty = true;
    [Tooltip("Penalty on 0-cost actions when AP is at cap. Pushes the AI to spend AP before the turn ends.")]
    public float APOverflowPenalty = 5.0f;

    #endregion

    #region Future / lookahead

    [Tooltip("0 = immediate action only (default). 1 = one level of prediction. 2+ = deeper.")]
    [Range(0, 3)] public int aiLookaheadDepth = 0;
    [Tooltip("Weight on estimated enemy counter-damage when lookahead >= 1.")]
    public float futureWeight = 0.5f;

    #endregion

    #region Randomness

    [Tooltip("Softmax temperature. 0 = always highest score. Higher = may pick lower-scoring actions.")]
    [Range(0f, 2f)] public float aiDecisionRandomness = 0.0f;

    #endregion

    #region Bench swap scoring

    [Tooltip("Bonus when the active unit being replaced is nearly dead. × lowHealthCurve.")]
    public float swapLowHpWeight = 12.0f;
    [Tooltip("Flat bonus when swapping OUT a dead active unit.")]
    public float swapDeadUnitWeight = 20.0f;
    [Tooltip("Bonus when the active unit has exhausted most of its useful attack uses.")]
    public float swapLowRemainingUsesWeight = 6.0f;
    [Tooltip("Bonus when the active unit has no useful usable attacks left this turn.")]
    public float swapNoUsefulAttacksWeight = 8.0f;
    [Tooltip("Bonus based on the replacement unit's strength/importance.")]
    public float swapReplacementValueWeight = 4.0f;
    [Tooltip("AP-cost penalty specific to bench swaps (in addition to APCostWeight). Now scaled by APScarcityCurve, same as the general penalty, so it also gets harsher as team AP runs low.")]
    public float swapAPCostWeight = 3.0f;
    [Tooltip("X = replacement unit importance (0-1), Y = multiplier on replacement value.")]
    public AnimationCurve swapReplacementCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);
    [Tooltip("Discount on the replacement's predicted attack value when scoring a swap, since that attack hasn't actually happened yet this action (mirrors the 0.5x discount future-turn attack gains get elsewhere). 1 = no discount, 0 = ignore predicted attack value entirely.")]
    [Range(0f, 1f)] public float swapFutureAttackDiscount = 0.5f;

    #endregion

    #region Repeat-swap punishment (per turn)

    [Tooltip("When true, doing more than one swap-type action (plain move, bench swap, or move+bench swap) in the same turn is punished with an escalating penalty, so the AI stops endlessly repositioning instead of attacking. The first swap-type action each turn is always free.")]
    public bool enableRepeatSwapPenalty = true;
    [Tooltip("Penalty applied starting on the 2nd swap-type action in a turn. Grows exponentially with each further swap via repeatSwapPenaltyGrowth: e.g. with weight=6 and growth=2, the 2nd swap costs -6, the 3rd costs -12, the 4th costs -24, etc.")]
    public float repeatSwapPenaltyWeight = 6.0f;
    [Tooltip("Exponential growth factor applied per additional swap-type action beyond the first free one. 1 = no growth (flat penalty on every extra swap). Higher = punishment escalates faster.")]
    public float repeatSwapPenaltyGrowth = 3.0f;

    #endregion

    #region Gameplay AP costs

    [Tooltip("AP cost of swapping a bench unit into an active slot (slots 2/3 only).")]
    public int benchSwapAPCost = 4;
    [Tooltip("AP cost of moving/swapping a unit within the active team (slots 0-3).")]
    public int teamPositionSwapAPCost = 2;

    #endregion

    #region Bench slot policy

    [Tooltip("Team-local slots that may directly interact with the bench. Symmetric for both teams.")]
    public int[] benchSwapSlots = new int[] { 2, 3 };

    public bool IsBenchSwapSlot(int slot)
    {
        if (benchSwapSlots == null) return slot == 2 || slot == 3;
        for (int i = 0; i < benchSwapSlots.Length; i++)
            if (benchSwapSlots[i] == slot) return true;
        return false;
    }

    #endregion

    public void OnValidate()
    {
        positionWeight             = Mathf.Max(0, positionWeight);
        positionSafetyWeight       = Mathf.Max(0, positionSafetyWeight);
        resourceWeight             = Mathf.Max(0, resourceWeight);
        APCostWeight           = Mathf.Max(0, APCostWeight);
        idlePenalty                = Mathf.Max(0, idlePenalty);
        APOverflowPenalty      = Mathf.Max(0, APOverflowPenalty);
        futureWeight               = Mathf.Max(0, futureWeight);
        aiDecisionRandomness       = Mathf.Max(0, aiDecisionRandomness);
        swapLowHpWeight            = Mathf.Max(0, swapLowHpWeight);
        swapDeadUnitWeight         = Mathf.Max(0, swapDeadUnitWeight);
        swapLowRemainingUsesWeight = Mathf.Max(0, swapLowRemainingUsesWeight);
        swapNoUsefulAttacksWeight  = Mathf.Max(0, swapNoUsefulAttacksWeight);
        swapReplacementValueWeight = Mathf.Max(0, swapReplacementValueWeight);
        swapAPCostWeight       = Mathf.Max(0, swapAPCostWeight);
        swapFutureAttackDiscount   = Mathf.Clamp01(swapFutureAttackDiscount);
        repeatSwapPenaltyWeight    = Mathf.Max(0, repeatSwapPenaltyWeight);
        repeatSwapPenaltyGrowth    = Mathf.Max(1f, repeatSwapPenaltyGrowth);
        benchSwapAPCost        = Mathf.Max(0, benchSwapAPCost);
        teamPositionSwapAPCost = Mathf.Max(0, teamPositionSwapAPCost);
        actionFatigueWeight       = Mathf.Max(0, actionFatigueWeight);
        unitFatigueWeight          = Mathf.Max(0, unitFatigueWeight);
        fatigueRecoveryPerTurn     = Mathf.Max(0, fatigueRecoveryPerTurn);
        aiLookaheadDepth           = Mathf.Clamp(aiLookaheadDepth, 0, 3);
    }
}
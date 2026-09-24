using System.Collections.Generic;
using UnityEngine;
using UnitData = CombatScript.UnitData;

public abstract partial class CombatHandlerBase
{
    // -----------------------------------------------------------------------
    // Unit liveness
    // -----------------------------------------------------------------------

    protected static bool IsUnitAlive(UnitData u)
        => !string.IsNullOrEmpty(u.name) && u.hp > 0;

    protected bool IsUnitAlive(int team, int slot) => IsUnitAlive(GetUnit(team, slot));

    // CONFIRM-2: A unit is only targetable if it's alive AND not Vanished.
    // Vanish grants stealth via the VanishStatus custom status; without this
    // check, Vanished units were still fully targetable despite the Action's
    // documented "AI will skip targeting them" behavior.
    protected bool IsUnitTargetable(int team, int slot)
        => IsUnitAlive(team, slot) && !Combat.IsUnitVanished(team, slot);

    // Instance (not static) because Combat is an instance property.
    protected bool IsUnitTargetable(UnitData u, int team, int slot)
        => IsUnitAlive(u) && !Combat.IsUnitVanished(team, slot);

    // -----------------------------------------------------------------------
    // Effect-kind classification
    // -----------------------------------------------------------------------

    protected static bool IsDamageKind(CombatScript.EffectKind kind)
        => kind == CombatScript.EffectKind.Physical ||
           kind == CombatScript.EffectKind.Magic   ||
           kind == CombatScript.EffectKind.FireDmg  ||
           kind == CombatScript.EffectKind.BleedDmg ||
           kind == CombatScript.EffectKind.PoisonDmg;

    protected static bool IsHealKind(CombatScript.EffectKind kind)
        => kind == CombatScript.EffectKind.RecoverHealth ||
           kind == CombatScript.EffectKind.AddHealth     ||
           kind == CombatScript.EffectKind.AddTempHealth ||
           kind == CombatScript.EffectKind.RecoverShield ||
           kind == CombatScript.EffectKind.AddShield     ||
           kind == CombatScript.EffectKind.RecoverArmor  ||
           kind == CombatScript.EffectKind.AddArmor;

    protected static bool IsStatusKind(CombatScript.EffectKind kind)
        => kind == CombatScript.EffectKind.Stun          ||
           kind == CombatScript.EffectKind.Sleep         ||
           kind == CombatScript.EffectKind.Confusion     ||
           kind == CombatScript.EffectKind.Petrification;

    protected static bool IsDotKind(CombatScript.EffectKind kind)
        => kind == CombatScript.EffectKind.FireDmg  ||
           kind == CombatScript.EffectKind.BleedDmg ||
           kind == CombatScript.EffectKind.PoisonDmg;

    // -----------------------------------------------------------------------
    // Team plumbing
    // -----------------------------------------------------------------------
    protected static string GetArmy(string unitCode)
    {
        if (string.IsNullOrWhiteSpace(unitCode)) return string.Empty;

        int idx = unitCode.LastIndexOf('_');
        if (idx < 0) return unitCode;
        if (idx == 0) return string.Empty;

        return unitCode.Substring(0, idx);
    }

    protected static string GetUnitNumber(string unitCode)
    {
        if (string.IsNullOrWhiteSpace(unitCode)) return string.Empty;

        int idx = unitCode.LastIndexOf('_');

        if (idx < 0) return unitCode;
        if (idx == unitCode.Length - 1) return string.Empty;

        return unitCode.Substring(idx + 1);
    }
    protected static int GetAllyTeam(int casterTeam) => casterTeam;

    protected static int GetEnemyTeam(int casterTeam)
        => casterTeam == CombatScript.TeamPlayer ? CombatScript.TeamEnemy : CombatScript.TeamPlayer;

    protected static bool IsAllyTeam(int team, int casterTeam) => team == casterTeam;

    protected static bool IsEnemyTeam(int team, int casterTeam) => team != casterTeam;

    protected static bool RawSlotIsAlly(int rawSlot) => rawSlot >= 0 && rawSlot <= 3;

    protected static int ToRawSlot(int casterTeam, int targetTeam, int targetSlot)
        => targetTeam == casterTeam ? targetSlot : targetSlot + 4;

    // -----------------------------------------------------------------------
    // Debuff bookkeeping
    // -----------------------------------------------------------------------

    protected static int CountActiveDebuffs(UnitData u)
    {
        int n = 0;
        if (u.fireRemainingRounds          > 0) n++;
        if (u.bleedRemainingRounds         > 0) n++;
        if (u.poisonRemainingRounds        > 0) n++;
        if (u.stunRemainingRounds          > 0) n++;
        if (u.sleepRemainingRounds         > 0) n++;
        if (u.confusionRemainingRounds     > 0) n++;
        if (u.petrificationRemainingRounds > 0) n++;
        return n;
    }

    // clears every harmful status not just DOTs
    protected static void ClearAllStatuses(ref UnitData u)
    {
        u.fireRemainingRounds          = 0;
        u.bleedRemainingRounds         = 0;
        u.poisonRemainingRounds        = 0;
        u.fireDamageNotation          = "";
        u.bleedDamageNotation         = "";
        u.poisonDamageNotation        = "";
        u.stunRemainingRounds          = 0;
        u.sleepRemainingRounds         = 0;
        u.confusionRemainingRounds     = 0;
        u.petrificationRemainingRounds = 0;
        u.tauntRemainingRounds         = 0;
        u.tauntTargetSlot             = -1;
    }

    // hitAlive retarget for override actions
    // heals retarget to neediest ally  damage and debuffs to nearest alive
    protected ResolvedTarget? ResolveHitAliveTarget(int casterTeam, int targetTeam, int targetSlot, bool isHeal)
    {
        if (IsUnitAlive(targetTeam, targetSlot)) return null;

        if (isHeal)
        {
            int best = -1;
            float bestFrac = 2f;
            for (int s = 0; s < 4; s++)
            {
                UnitData u = GetUnit(targetTeam, s);
                if (!IsUnitAlive(u)) continue;
                float frac = u.maxHp > 0 ? u.hp / u.maxHp : 1f;
                if (frac < bestFrac) { bestFrac = frac; best = s; }
            }
            if (best < 0) return null;
            return new ResolvedTarget { team = targetTeam, slot = best, portion = 1f, unit = GetUnit(targetTeam, best) };
        }
        else
        {
            int redirect = Combat.FindNearestAliveSlot(targetTeam, targetSlot);
            if (redirect < 0) return null;
            return new ResolvedTarget { team = targetTeam, slot = redirect, portion = 1f, unit = GetUnit(targetTeam, redirect) };
        }
    }
}

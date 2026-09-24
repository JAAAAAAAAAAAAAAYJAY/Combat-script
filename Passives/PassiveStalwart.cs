using UnityEngine;
using CombatOverride;

[Passive("passive_Stalwart")]
public class PassiveStalwart : PassiveBase
{
    public override string OverrideKey => "passive_Stalwart";

    bool usedThisBattle = false;
    const float SurvivalPercent = 0.3f;

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Stalwart] Attached to {GetOwnerUnit().name}.");
    }

    public override void OnDefeat(CombatScript combat, ref bool preventDeath, ref float survivalPercent)
    {
        if (usedThisBattle) return;
        preventDeath = true;
        survivalPercent = SurvivalPercent;
        usedThisBattle = true;
        var unit = GetOwnerUnit();
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[Stalwart] {unit.name} refuses to fall! Survives at {SurvivalPercent*100:0}% HP.");
    }
}

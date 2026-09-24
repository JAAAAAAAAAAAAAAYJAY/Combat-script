using UnityEngine;
using ActionData = CombatScript.ActionData;
using UnitData = CombatScript.UnitData;
using CombatOverride;

[Passive("passive_ThickSkin")]
public class PassiveThickSkin : PassiveBase
{
    public override string OverrideKey => "passive_ThickSkin";

    const float DamageReduction = 0.2f;

    public override void OnAttach(CombatScript combat, int team, int slot)
    {
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[ThickSkin] Attached to {GetOwnerUnit().name}.");
    }

    public override void OnDamageTaken(CombatScript combat, ref int amount,
        CombatScript.EffectKind kind, int sourceTeam, int sourceSlot)
    {
        if (amount <= 0) return;
        if (kind != CombatScript.EffectKind.Physical && kind != CombatScript.EffectKind.Magic) return;
        int reduced = Mathf.CeilToInt(amount * (1f - DamageReduction));
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[ThickSkin] {GetOwnerUnit().name} reduces {amount} → {reduced} ({DamageReduction*100:0}% reduction, {kind}).");
        amount = reduced;
    }
}

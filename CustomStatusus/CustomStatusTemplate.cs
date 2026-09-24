using UnityEngine;

public class MyCustomStatusTemplate : CustomStatus
{
    const string StatusKey = "MyCustomStatus";   // unique key for refresh/stacking

    // Config
    public int AcPenalty    = 0;     // example:-2 AC while active
    public string TickNotation = "1d4";  //Example: damage rolled each turn end

    public MyCustomStatusTemplate() { Key = StatusKey; }

    // OnApply runs ONCE the first time this status lands on a target
    // Use it for instant one-shot mutations: stat debuffs, visual cues, etc
    public override void OnApply()
    {
        if (AcPenalty != 0)
        {
            ModifyTarget((ref CombatScript.UnitData u) =>
            {
                u.ac += AcPenalty;   // AcPenalty is negative for a debuff (AC can go negative)
            });
            if (CombatDebugHandler.UltraDebug) Debug.Log($"[MyCustomStatus] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s AC changed by {AcPenalty} (now {Combat.GetUnit(TargetTeam, TargetSlot).ac:0}).");
        }
    }

    public override void OnTurnEndTick()
    {
        if (!IsTargetAlive()) return;

        // Example: roll and apply per-turn damage.
        if (!string.IsNullOrWhiteSpace(TickNotation))
        {
            var roll = Roll(TickNotation);
            if (roll.HasValue)
            {
                int dmg = roll.Value.total;
                Combat.ApplyDamageToTarget(TargetTeam, TargetSlot, dmg,
                    CombatScript.EffectKind.Magic, 0, SourceTeam, SourceSlot);
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[MyCustomStatus] {Combat.GetUnit(TargetTeam, TargetSlot).name} takes {dmg} ({RemainingRounds - 1} turn(s) left).");
            }
        }
    }

    // OnExpire runs when RemainingRounds hits 0 (natural expiry) OR when the
    // status is manually removed via CustomStatusRuntime.Remove
    // Use it to REVERSE anything OnApply did (restore AC, remove visuals, etc)
    public override void OnExpire()
    {
        // Reverse the OnApply mutation safe to run on a dead target the
        // restore is a no-op on a corpse and correct if they revive later
        if (AcPenalty != 0)
        {
            ModifyTarget((ref CombatScript.UnitData u) => u.ac -= AcPenalty);
            if (CombatDebugHandler.UltraDebug && IsTargetAlive())
                if (CombatDebugHandler.UltraDebug) Debug.Log($"[MyCustomStatus] {Combat.GetUnit(TargetTeam, TargetSlot).name}'s AC restored (status faded).");
        }
    }

    // OnTargetDefeated runs if the target DIES while the status is active
    // The base impl just calls OnExpire() so DELETE this override if you want
    // the default clean up the same way on death as on expiry behaviour
    // Override ONLY if death needs different cleanup
    public override void OnTargetDefeated()
    {
        // Default: treat death like expiry (restore AC, etc.).
        base.OnTargetDefeated();
        if (CombatDebugHandler.UltraDebug) Debug.Log($"[MyCustomStatus] Target defeated - status cleaned up.");
    }
}

using CombatOverride;

public interface iAbility : ICombatOverride { }

public abstract class AbilityBase : CombatOverrideBase, iAbility { }

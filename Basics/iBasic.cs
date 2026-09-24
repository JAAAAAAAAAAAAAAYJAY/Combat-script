using CombatOverride;

public interface iBasic : ICombatOverride { }

public abstract class BasicBase : CombatOverrideBase, iBasic { }

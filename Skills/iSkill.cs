using CombatOverride;

public interface iSkill : ICombatOverride { }

public abstract class SkillBase : CombatOverrideBase, iSkill { }

using System.Collections;

namespace CombatOverride
{
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class CombatOverrideAttribute : System.Attribute
    {
        public string Key { get; }
        public CombatOverrideAttribute(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new System.ArgumentException("Override key must be non-empty.", nameof(key));
            Key = key;
        }
    }
}

public interface ICombatOverride
{
    string OverrideKey { get; }

    IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, CombatScript.ActionData action);
}

public abstract class CombatOverrideBase : CombatHandlerBase, ICombatOverride
{
    public abstract string OverrideKey { get; }
    public abstract IEnumerator Execute(CombatScript combat, int casterTeam, int casterSlot, CombatScript.ActionData action);
}

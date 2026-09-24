using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using CombatOverride;


public static class OverrideRegistry
{
    static readonly Dictionary<string, Type> _basicTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Type> _skillTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Type> _abilityTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Type> _passiveTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, iBasic> _basicInstances = new Dictionary<string, iBasic>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, iSkill> _skillInstances = new Dictionary<string, iSkill>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, iAbility> _abilityInstances = new Dictionary<string, iAbility>(StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, iBasic> _sceneBasics = new Dictionary<string, iBasic>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, iSkill> _sceneSkills = new Dictionary<string, iSkill>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, iAbility> _sceneAbilities = new Dictionary<string, iAbility>(StringComparer.OrdinalIgnoreCase);

    static bool _scanDone;

    private static CombatScript _cachedCombat;
    private static bool _sceneHookInstalled;

    static void EnsureScanDone()
    {
        if (_scanDone) return;
        _scanDone = true;

        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }

            foreach (Type t in types)
            {
                if (t.IsAbstract) continue;

                var attr = t.GetCustomAttribute<CombatOverrideAttribute>();
                if (attr == null) continue;

                if (!typeof(MonoBehaviour).IsAssignableFrom(t))
                {
                    Debug.LogWarning($"[OverrideRegistry] {t.Name} has [CombatOverride] but isn't a MonoBehaviour. Skipping.");
                    continue;
                }

                if (typeof(iBasic).IsAssignableFrom(t))
                    RegisterKey(_basicTypes, attr.Key, t);
                else if (typeof(iSkill).IsAssignableFrom(t))
                    RegisterKey(_skillTypes, attr.Key, t);
                else if (typeof(iAbility).IsAssignableFrom(t))
                    RegisterKey(_abilityTypes, attr.Key, t);
                else if (typeof(iPassive).IsAssignableFrom(t))
                    RegisterKey(_passiveTypes, attr.Key, t);
            }
        }
    }

    static void RegisterKey(Dictionary<string, Type> dict, string key, Type type)
    {
        if (dict.TryGetValue(key, out Type existing))
        {
            Debug.LogWarning($"[OverrideRegistry] Duplicate key '{key}' on {type.Name} - keeping the first ({existing.Name}).");
            return;
        }
        dict[key] = type;
    }

    public static CombatScript GetCombat()
    {
        if (_cachedCombat != null && _cachedCombat.gameObject != null)
            return _cachedCombat;

        if (!_sceneHookInstalled)
        {
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _sceneHookInstalled = true;
        }

        _cachedCombat = UnityEngine.Object.FindFirstObjectByType<CombatScript>();
        return _cachedCombat;
    }

    static void OnSceneUnloaded(Scene scene)
    {
        _cachedCombat = null;
    }

    public static void InvalidateCombatCache()
    {
        _cachedCombat = null;
        if (_sceneHookInstalled)
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _sceneHookInstalled = false;
        }
    }

    public static void ClearSceneHandlers()
    {
        _sceneBasics.Clear();
        _sceneSkills.Clear();
        _sceneAbilities.Clear();

        DestroyInstanceCache(_basicInstances);
        DestroyInstanceCache(_skillInstances);
        DestroyInstanceCache(_abilityInstances);

        InvalidateCombatCache();
    }

    static void DestroyInstanceCache<T>(Dictionary<string, T> cache) where T : class
    {
        foreach (var kvp in cache)
        {
            if (kvp.Value is MonoBehaviour mb && mb != null && mb.gameObject != null)
                UnityEngine.Object.Destroy(mb.gameObject);
        }
        cache.Clear();
    }

    public static void RegisterSceneBasic(string key, iBasic handler)
    {
        if (!string.IsNullOrWhiteSpace(key) && handler != null)
            _sceneBasics[key] = handler;
    }

    public static void RegisterSceneSkill(string key, iSkill handler)
    {
        if (!string.IsNullOrWhiteSpace(key) && handler != null)
            _sceneSkills[key] = handler;
    }

    public static void RegisterSceneAbility(string key, iAbility handler)
    {
        if (!string.IsNullOrWhiteSpace(key) && handler != null)
            _sceneAbilities[key] = handler;
    }

    public static iBasic GetBasic(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        EnsureScanDone();

        if (_sceneBasics.TryGetValue(key, out iBasic scene)) return scene;
        if (_basicInstances.TryGetValue(key, out iBasic cached)) return cached;

        if (_basicTypes.TryGetValue(key, out Type type))
        {
            var instance = InstantiateShared<iBasic>(type);
            if (instance != null) _basicInstances[key] = instance;
            return instance;
        }
        return null;
    }

    public static iSkill GetSkill(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        EnsureScanDone();

        if (_sceneSkills.TryGetValue(key, out iSkill scene)) return scene;
        if (_skillInstances.TryGetValue(key, out iSkill cached)) return cached;

        if (_skillTypes.TryGetValue(key, out Type type))
        {
            var instance = InstantiateShared<iSkill>(type);
            if (instance != null) _skillInstances[key] = instance;
            return instance;
        }
        return null;
    }

    public static iAbility GetAbility(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        EnsureScanDone();

        if (_sceneAbilities.TryGetValue(key, out iAbility scene)) return scene;
        if (_abilityInstances.TryGetValue(key, out iAbility cached)) return cached;

        if (_abilityTypes.TryGetValue(key, out Type type))
        {
            var instance = InstantiateShared<iAbility>(type);
            if (instance != null) _abilityInstances[key] = instance;
            return instance;
        }
        return null;
    }

    public static Type GetPassiveType(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        EnsureScanDone();
        return _passiveTypes.TryGetValue(key, out Type type) ? type : null;
    }

    static T InstantiateShared<T>(Type type) where T : class
    {
        var go = new GameObject($"__Override_{type.Name}");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        var instance = go.AddComponent(type) as T;
        if (instance == null)
        {
            UnityEngine.Object.Destroy(go);
            Debug.LogError($"[OverrideRegistry] Failed to instantiate {type.FullName} as {typeof(T).Name}.");
        }
        return instance;
    }

    public static iPassive InstantiatePassive(string key, int team, int slot, Transform parent = null)
    {
        Type type = GetPassiveType(key);
        if (type == null) return null;

        var go = new GameObject($"__Passive_{key}_{team}_{slot}");
        if (parent != null) go.transform.SetParent(parent, false);
        else
        {
            var combat = GetCombat();
            if (combat != null) go.transform.SetParent(combat.transform, false);
            else UnityEngine.Object.DontDestroyOnLoad(go);
        }
        var instance = go.AddComponent(type) as iPassive;
        if (instance == null)
        {
            UnityEngine.Object.Destroy(go);
            Debug.LogError($"[OverrideRegistry] Failed to instantiate passive {type.FullName}.");
            return null;
        }
        if (instance is PassiveBase pb)
            pb.Initialize(team, slot);
        return instance;
    }
}

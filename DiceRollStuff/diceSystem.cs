using System.Collections.Generic;
using UnityEngine;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine.UI;

public class diceSystem : MonoBehaviour
{
    diceVizualizer animator;

    void Awake() {
        animator = GetComponent<diceVizualizer>();
        if (animator == null)
            Debug.LogWarning("[diceSystem] no diceVizualizer dice wont show");
    }

    public Button rollButton;
    public string rollNotation = "1d20+5";
    public int rollTarget = -1;

    // bag rng 4 copies of each face per bag
    // prevents rolling same value more than 4 times in a row
    class BagRng
    {
        System.Random rng;
        readonly Dictionary<int, List<int>> bags = new Dictionary<int, List<int>>();

        public BagRng(int seed) { rng = new System.Random(seed); }

        public int Next(int faces)
        {
            if (faces < 1) return 1;
            if (!bags.TryGetValue(faces, out var bag) || bag.Count == 0)
            {
                bag = CreateBag(faces);
                bags[faces] = bag;
            }
            int idx = rng.Next(bag.Count);
            int val = bag[idx];
            bag.RemoveAt(idx);
            return val;
        }

        // 4 copies of each face shuffled
        List<int> CreateBag(int faces)
        {
            var bag = new List<int>(4 * faces);
            for (int copy = 0; copy < 4; copy++)
                for (int f = 1; f <= faces; f++)
                    bag.Add(f);
            for (int j = bag.Count - 1; j > 0; j--)
            {
                int k = rng.Next(j + 1);
                (bag[j], bag[k]) = (bag[k], bag[j]);
            }
            return bag;
        }

        public float NextFloat()
        {
            return (float)rng.NextDouble();
        }
    }

    BagRng _bagRng;

    static readonly Regex _splitter = new Regex(@"(?=[+-])", RegexOptions.Compiled);

    // supports adv dis ad prefixes and High Low suffix
    static readonly Regex _validator = new Regex(
        @"^\s*(?:adv|dis|ad)?[+-]?(?:\d*d\d+(?:(?:kh|kl|r)\d*|!|High|Low)?|\d+)(?:\s*[+-]\s*(?:\d*d\d+(?:(?:kh|kl|r)\d*|!|High|Low)?|\d+))*\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public void SetSeed(int seed) { _bagRng = new BagRng(seed); }

    public enum DiceModifier {
        None,
        KeepHighest,
        KeepLowest,
        Explode,
        Reroll
    }

    public struct DiceGroup {
        public int amount;
        public int faces;
        public int sign;
        public DiceModifier modifier;
        public int modifierCount;
    }

    public struct DieRoll {
        public int faces;
        public int rolled;
        public int sign;
    }

    public struct ParsedRoll {
        public List<DiceGroup> groups;
        public int flat;
        public bool advantage;
        public bool disadvantage;
    }

    public struct RollResult {
        public List<DieRoll> dice;
        public int flat;
        public int total;
        public bool isCrunchyCrit;
        public bool isCrit;
        public bool isFumble;
        public bool isHit;
        public bool isHalfMiss;
        public bool isMiss;
        public int target;
        public List<DieRoll> droppedDice;
    }

    public static bool IsValidNotation(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        return _validator.IsMatch(s);
    }

    ParsedRoll Parse(string notation) {
        var result = new ParsedRoll {
            groups = new List<DiceGroup>(),
            flat = 0,
            advantage = false,
            disadvantage = false
        };

        if (string.IsNullOrWhiteSpace(notation)) return result;

        string clean = notation.Replace(" ", "").ToLower();

        // check adv dis ad prefix
        if (clean.StartsWith("adv")) { result.advantage = true; clean = clean.Substring(3); }
        else if (clean.StartsWith("dis")) { result.disadvantage = true; clean = clean.Substring(3); }
        else if (clean.StartsWith("ad")) { result.advantage = true; clean = clean.Substring(2); }

        string[] tokens = _splitter.Split(clean);

        foreach (string token in tokens) {
            if (string.IsNullOrEmpty(token)) continue;

            if (token.Contains("d")) {
                int sign = token.StartsWith("-") ? -1 : 1;
                string body = token.TrimStart('+', '-');
                string[] parts = body.Split('d');

                if (parts.Length < 2) { Debug.LogWarning($"[diceSystem] bad token {token}"); continue; }

                string amountStr = parts[0];
                string facesStr  = parts[1];

                int amount;
                if (string.IsNullOrEmpty(amountStr)) amount = 1;
                else if (!int.TryParse(amountStr, out amount)) { Debug.LogWarning($"[diceSystem] bad amount {token}"); continue; }

                int idx = 0;
                while (idx < facesStr.Length && char.IsDigit(facesStr[idx])) idx++;
                string facesNumStr = facesStr.Substring(0, idx);
                string modStr      = facesStr.Substring(idx);

                if (!int.TryParse(facesNumStr, out int faces) || faces < 1) { Debug.LogWarning($"[diceSystem] bad faces {token}"); continue; }

                DiceModifier modifier = DiceModifier.None;
                int modifierCount = 0;
                if (modStr.Length > 0)
                {
                    if (modStr.StartsWith("kh"))
                    {
                        modifier = DiceModifier.KeepHighest;
                        string countStr = modStr.Substring(2);
                        modifierCount = string.IsNullOrEmpty(countStr) ? 1 : (int.TryParse(countStr, out int c) ? c : 1);
                    }
                    else if (modStr.StartsWith("kl"))
                    {
                        modifier = DiceModifier.KeepLowest;
                        string countStr = modStr.Substring(2);
                        modifierCount = string.IsNullOrEmpty(countStr) ? 1 : (int.TryParse(countStr, out int c) ? c : 1);
                    }
                    else if (modStr.StartsWith("r"))
                    {
                        modifier = DiceModifier.Reroll;
                        string countStr = modStr.Substring(1);
                        modifierCount = string.IsNullOrEmpty(countStr) ? 1 : (int.TryParse(countStr, out int c) ? c : 1);
                    }
                    else if (modStr == "!")
                    {
                        modifier = DiceModifier.Explode;
                        modifierCount = 10;
                    }
                    // High = keep highest 1  Low = keep lowest 1
                    else if (modStr == "high")
                    {
                        modifier = DiceModifier.KeepHighest;
                        modifierCount = 1;
                    }
                    else if (modStr == "low")
                    {
                        modifier = DiceModifier.KeepLowest;
                        modifierCount = 1;
                    }
                    else
                    {
                        Debug.LogWarning($"[diceSystem] unknown modifier {modStr} in {token}");
                        continue;
                    }
                }

                if (amount < 1) amount = 1;
                if (amount > 1000) amount = 1000;

                result.groups.Add(new DiceGroup {
                    amount = amount,
                    faces = faces,
                    sign = sign,
                    modifier = modifier,
                    modifierCount = modifierCount
                });
            }
            else {
                if (int.TryParse(token, out int flat))
                    result.flat += flat;
                else
                    Debug.LogWarning($"[diceSystem] bad token {token}");
            }
        }

        return result;
    }

    int RollDie(int faces)
    {
        if (_bagRng != null) return _bagRng.Next(faces);
        return Random.Range(1, faces + 1);
    }

    RollResult Evaluate(ParsedRoll parsed, int target = -1) {
        var result = new RollResult();
        result.dice   = new List<DieRoll>();
        result.flat   = parsed.flat;
        result.target = target;
        result.total  = parsed.flat;

        bool nat20 = false;
        bool nat1  = false;
        bool hasD20 = false;

        foreach (DiceGroup group in parsed.groups) {
            if (group.faces == 20) hasD20 = true;

            var groupRolls = new List<DieRoll>(group.amount);
            List<DieRoll> dropped = null;

            for (int i = 0; i < group.amount; i++) {
                int rolled = RollDie(group.faces);

                if (group.modifier == DiceModifier.Reroll && rolled <= group.modifierCount) {
                    int replacement = RollDie(group.faces);
                    if (dropped == null) dropped = new List<DieRoll>();
                    dropped.Add(new DieRoll { faces = group.faces, rolled = rolled, sign = group.sign });
                    rolled = replacement;
                }

                if (group.faces == 20 && rolled == 20) nat20 = true;
                if (group.faces == 20 && rolled == 1)  nat1  = true;

                groupRolls.Add(new DieRoll { faces = group.faces, rolled = rolled, sign = group.sign });
            }

            if (group.modifier == DiceModifier.Explode) {
                int explodeCap = group.modifierCount;
                int extrasAdded = 0;
                for (int i = 0; i < groupRolls.Count && extrasAdded < explodeCap; i++) {
                    var r = groupRolls[i];
                    if (r.rolled == group.faces) {
                        int extra = RollDie(group.faces);
                        groupRolls.Add(new DieRoll { faces = group.faces, rolled = extra, sign = group.sign });
                        extrasAdded++;
                        if (group.faces == 20 && extra == 20) nat20 = true;
                        if (group.faces == 20 && extra == 1)  nat1  = true;
                    }
                }
            }

            if (group.modifier == DiceModifier.KeepHighest ||
                group.modifier == DiceModifier.KeepLowest)
            {
                int keepCount = Mathf.Clamp(group.modifierCount, 1, groupRolls.Count);
                var indices = new List<int>(groupRolls.Count);
                for (int i = 0; i < groupRolls.Count; i++) indices.Add(i);
                indices.Sort((a, b) => groupRolls[a].rolled.CompareTo(groupRolls[b].rolled));

                var keepSet = new HashSet<int>();
                int startIdx = (group.modifier == DiceModifier.KeepHighest) ? indices.Count - keepCount : 0;
                for (int i = 0; i < keepCount; i++) keepSet.Add(indices[startIdx + i]);

                var kept = new List<DieRoll>(keepCount);
                for (int i = 0; i < groupRolls.Count; i++) {
                    if (keepSet.Contains(i)) kept.Add(groupRolls[i]);
                    else { if (dropped == null) dropped = new List<DieRoll>(); dropped.Add(groupRolls[i]); }
                }
                groupRolls = kept;
            }

            foreach (var roll in groupRolls) {
                result.dice.Add(roll);
                result.total += roll.rolled * roll.sign;
            }

            if (dropped != null) {
                if (result.droppedDice == null) result.droppedDice = new List<DieRoll>();
                result.droppedDice.AddRange(dropped);
            }
        }

        if (nat20) result.isCrunchyCrit = true;
        else if (!nat1 && hasD20 && result.total >= 20) result.isCrit = true;
        if (nat1) result.isFumble = true;

        if (target != -1) {
            int halfTarget = Mathf.CeilToInt(target * 0.5f);
            if (nat20 || (hasD20 && result.total >= 20))
            {
                result.isHit = true;
            }
            else if (nat1)
            {
                result.isMiss = true;
            }
            else
            {
                if (result.total >= target) result.isHit = true;
                else if (result.total >= halfTarget) result.isHalfMiss = true;
                else result.isMiss = true;
            }
        }

        return result;
    }

    string DiceBreakdown(RollResult result) {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var die in result.dice) {
            if (!first) sb.Append("  ");
            first = false;
            string signSymbol = die.sign == 1 ? "+" : "-";
            sb.Append("d").Append(die.faces).Append("=").Append(die.rolled).Append(signSymbol);
        }
        return sb.ToString();
    }

    // main roll used by everything
    public RollResult Roll(string notation, int target = -1, bool advantage = false, bool disadvantage = false, bool quiet = false) {
        ParsedRoll parsed = Parse(notation);

        // notation-level adv/dis overrides parameter
        if (parsed.advantage) advantage = true;
        if (parsed.disadvantage) disadvantage = true;

        RollResult result;
        bool cancelled = advantage && disadvantage;
        bool useAdvantage = advantage && !cancelled;
        bool useDisadvantage = disadvantage && !cancelled;

        if (useAdvantage || useDisadvantage) {
            RollResult rollA = Evaluate(parsed, target);
            RollResult rollB = Evaluate(parsed, target);

            bool pickA = useAdvantage ? rollA.total >= rollB.total : rollA.total <= rollB.total;
            result = pickA ? rollA : rollB;

            // merge dropped dice instead of overwriting
            var mergedDropped = new List<DieRoll>();
            var droppedRoll = pickA ? rollB : rollA;
            if (droppedRoll.dice != null) mergedDropped.AddRange(droppedRoll.dice);
            if (result.droppedDice != null) mergedDropped.AddRange(result.droppedDice);
            if (mergedDropped.Count > 0) result.droppedDice = mergedDropped;

            if (useAdvantage)
            {
                result.isCrunchyCrit = (pickA ? rollA : rollB).isCrunchyCrit;
                result.isCrit        = (pickA ? rollA : rollB).isCrit;
                result.isFumble      = (pickA ? rollA : rollB).isFumble;
            }
            else
            {
                result.isCrunchyCrit = (pickA ? rollA : rollB).isCrunchyCrit;
                result.isCrit        = (pickA ? rollA : rollB).isCrit;
                result.isFumble      = (pickA ? rollA : rollB).isFumble;
            }
        } else {
            result = Evaluate(parsed, target);
        }

        if (!quiet && CombatDebugHandler.UltraDebug)
        {
            Debug.Log($"[dice] {notation} = {result.total} dice:{DiceBreakdown(result)}");
        }

        if (!quiet && animator != null)
            animator.ShowRoll(result);

        return result;
    }

    // hit roll function no fumble or crit auto miss/hit
    // total vs ac only  caller checks dice for nat20
    public RollResult RollHit(string notation, int targetAC, bool quiet = true)
    {
        if (string.IsNullOrWhiteSpace(notation) || !IsValidNotation(notation))
            return default;

        RollResult result = Roll(notation, targetAC, false, false, quiet);

        if (targetAC >= 0 && !result.isCrunchyCrit && !result.isCrit && !result.isFumble)
        {
            int half = Mathf.CeilToInt(targetAC * 0.5f);
            result.isHit = result.total >= targetAC;
            result.isHalfMiss = !result.isHit && result.total >= half;
            result.isMiss = !result.isHit && !result.isHalfMiss;
        }

        return result;
    }

    // damage roll function no target no fumble no crit
    public RollResult RollDamage(string notation, bool quiet = true)
    {
        if (string.IsNullOrWhiteSpace(notation) || !IsValidNotation(notation))
            return default;

        return Roll(notation, -1, false, false, quiet);
    }

    // seeded chance roll for passives
    public bool RollChance(float pct)
    {
        if (_bagRng != null) return _bagRng.NextFloat() < pct;
        return Random.value < pct;
    }

    // seeded int range for fallbacks
    public int RangeInt(int min, int maxExclusive)
    {
        if (_bagRng != null) return _bagRng.Next(maxExclusive - min) + min;
        return Random.Range(min, maxExclusive);
    }

    public List<RollResult> RollMultiple(List<(string notation, int target)> rolls)
    {
        var results = new List<RollResult>();
        foreach (var (notation, target) in rolls)
            results.Add(Roll(notation, target, false, false, quiet: true));
        return results;
    }

    public int MaxPossible(string notation)
    {
        ParsedRoll parsed = Parse(notation);
        int max = parsed.flat;
        foreach (DiceGroup group in parsed.groups)
        {
            int perDie = group.faces;
            int effectiveAmount = group.amount;
            if (group.modifier == DiceModifier.Explode)
                perDie = group.faces * (1 + group.modifierCount);
            else if (group.modifier == DiceModifier.KeepLowest ||
                     group.modifier == DiceModifier.KeepHighest)
                effectiveAmount = Mathf.Clamp(group.modifierCount, 1, group.amount);

            if (group.sign >= 0)
                max += effectiveAmount * perDie;
            else
                max += effectiveAmount * 1 * group.sign;
        }
        return max;
    }

    // analytic avg for ai scoring
    public float ExpectedValue(string notation)
    {
        if (!IsValidNotation(notation)) return 0f;
        ParsedRoll parsed = Parse(notation);

        float total = parsed.flat;
        foreach (DiceGroup group in parsed.groups)
        {
            if (group.faces < 1) continue;
            int amount = Mathf.Max(1, group.amount);
            float meanPerDie = (group.faces + 1) * 0.5f;
            float groupMean = amount * meanPerDie;

            switch (group.modifier)
            {
                case DiceModifier.KeepHighest:
                    {
                        int keep = Mathf.Clamp(group.modifierCount, 1, amount);
                        float dropFrac = 1f - ((float)keep / amount);
                        groupMean = amount * meanPerDie * (1f + 0.5f * dropFrac) * ((float)keep / amount);
                        break;
                    }
                case DiceModifier.KeepLowest:
                    {
                        int keep = Mathf.Clamp(group.modifierCount, 1, amount);
                        float dropFrac = 1f - ((float)keep / amount);
                        groupMean = amount * meanPerDie * (1f - 0.5f * dropFrac) * ((float)keep / amount);
                        break;
                    }
                case DiceModifier.Explode:
                    {
                        float explodeBonus = group.faces > 1 ? 1f / (group.faces - 1) : 0f;
                        groupMean = amount * meanPerDie * (1f + explodeBonus);
                        break;
                    }
                case DiceModifier.Reroll:
                    {
                        float rerollThreshold = group.modifierCount;
                        if (group.faces > 0 && rerollThreshold < group.faces)
                        {
                            float rerollProb = (float)rerollThreshold / group.faces;
                            float eDie = meanPerDie;
                            float eNotRerolled = (rerollThreshold + 1f + group.faces) / 2f;
                            float effective = rerollProb * eDie + (1f - rerollProb) * eNotRerolled;
                            groupMean = amount * effective;
                        }
                        else groupMean = amount * meanPerDie;
                        break;
                    }
            }

            total += groupMean * group.sign;
        }

        // advantage averages higher disadvantage lower
        if (parsed.advantage) total *= 1.15f;
        if (parsed.disadvantage) total *= 0.85f;

        return total;
    }

    public ParsedRoll ParsePublic(string notation) => Parse(notation);

    void Start()
    {
        if (rollButton != null)
            rollButton.onClick.AddListener(OnRollButtonPressed);
    }

    void OnDestroy()
    {
        if (rollButton != null)
            rollButton.onClick.RemoveListener(OnRollButtonPressed);
    }

    void OnRollButtonPressed() {
        Roll(rollNotation, rollTarget);
    }
}

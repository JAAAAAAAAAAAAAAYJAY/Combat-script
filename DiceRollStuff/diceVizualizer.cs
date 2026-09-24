using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

public class diceVizualizer : MonoBehaviour
{
    public GameObject diePrefab;
    public TextMeshPro totalText;
    public float moveSpeed = 20f;
    public float spacing = 1.5f;
    public float verticalGap = 2.5f;

    private List<GameObject> activeDice = new List<GameObject>();
    private List<Coroutine> activeMoveCoroutines = new List<Coroutine>();

    private Coroutine runningAnimation;

    private readonly StringBuilder _sb = new StringBuilder();
    private readonly List<(string label, diceSystem.RollResult result)> _snapshotScratch
        = new List<(string label, diceSystem.RollResult result)>();

    public void ShowRoll(diceSystem.RollResult result)
    {
        ShowRolls(new List<(string label, diceSystem.RollResult result)>
        {
            ("", result)
        });
    }

    public void ShowRolls(List<(string label, diceSystem.RollResult result)> rolls)
    {
        if (runningAnimation != null)
            StopCoroutine(runningAnimation);

        runningAnimation = StartCoroutine(AnimateRolls(rolls));
    }

    public void Clear()
    {
        if (runningAnimation != null)
        {
            StopCoroutine(runningAnimation);
            runningAnimation = null;
        }
        foreach (var c in activeMoveCoroutines)
            if (c != null) StopCoroutine(c);
        activeMoveCoroutines.Clear();
        foreach (GameObject go in activeDice)
            if (go != null) Destroy(go);
        activeDice.Clear();
        if (totalText != null) totalText.text = "";
    }

    void OnDestroy()
    {
        foreach (var go in activeDice)
        {
            if (go != null) Destroy(go);
        }
        activeDice.Clear();
        if (runningAnimation != null)
        {
            StopCoroutine(runningAnimation);
            runningAnimation = null;
        }
        foreach (var c in activeMoveCoroutines)
        {
            if (c != null) StopCoroutine(c);
        }
        activeMoveCoroutines.Clear();
    }

    IEnumerator AnimateRolls(List<(string label, diceSystem.RollResult result)> rolls)
    {
        _snapshotScratch.Clear();
        foreach (var item in rolls) _snapshotScratch.Add(item);

        foreach (var c in activeMoveCoroutines)
            if (c != null) StopCoroutine(c);
        activeMoveCoroutines.Clear();

        foreach (GameObject go in activeDice)
            if (go != null) Destroy(go);
        activeDice.Clear();

        if (totalText != null)
            totalText.text = "";

        if (diePrefab == null)
        {
            Debug.LogWarning("[diceVizualizer] diePrefab is not assigned - cannot render dice.");
            runningAnimation = null;
            yield break;
        }

        float yOffset = 0f;
        _sb.Clear();
        bool firstRoll = true;

        foreach (var (label, result) in _snapshotScratch)
        {
            int diceCount = result.dice != null ? result.dice.Count : 0;
            float totalWidth = (diceCount - 1) * spacing;
            float startX = -totalWidth / 2f;

            List<GameObject> rollDice = new List<GameObject>();

            for (int i = 0; i < diceCount; i++)
            {
                diceSystem.DieRoll die = result.dice[i];

                Vector3 targetPos = new Vector3(startX + i * spacing, yOffset, 0f);
                Vector3 spawnPos = targetPos + new Vector3(0f, -verticalGap, 0f); // spawn just below its row

                GameObject dieObj = Instantiate(diePrefab, spawnPos, Quaternion.identity);
                activeDice.Add(dieObj);
                rollDice.Add(dieObj);

                TextMeshPro text = dieObj.GetComponentInChildren<TextMeshPro>();
                if (text != null)
                {
                    text.text = die.rolled.ToString();
                    text.color = die.sign == -1 ? Color.red : Color.white;
                }
                else
                {
                    Debug.LogWarning("[diceVizualizer] diePrefab has no TextMeshPro - cannot show die value.");
                }

                SpriteRenderer sr = dieObj.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    if (die.faces == 20) sr.color = new Color(1f, 0.8f, 0.2f); // gold for d20
                    else if (die.faces == 6) sr.color = new Color(0.7f, 0.9f, 0.7f); // green for d6
                    else sr.color = Color.white;
                }
                else
                {
                    Debug.LogWarning("[diceVizualizer] diePrefab has no SpriteRenderer - cannot tint die.");
                }

                Coroutine c = StartCoroutine(MoveDie(dieObj, targetPos, i * 0.1f)); // staggered start
                activeMoveCoroutines.Add(c);
            }

            int droppedCount = result.droppedDice != null ? result.droppedDice.Count : 0;
            if (droppedCount > 0)
            {
                float droppedWidth = (droppedCount - 1) * spacing;
                float droppedStartX = -droppedWidth / 2f;
                float droppedY = yOffset - verticalGap * 0.6f;
                for (int i = 0; i < droppedCount; i++)
                {
                    diceSystem.DieRoll die = result.droppedDice[i];

                    Vector3 targetPos = new Vector3(droppedStartX + i * spacing, droppedY, 0f);
                    Vector3 spawnPos = targetPos + new Vector3(0f, -verticalGap, 0f);

                    GameObject dieObj = Instantiate(diePrefab, spawnPos, Quaternion.identity);
                    activeDice.Add(dieObj);
                    rollDice.Add(dieObj);

                    TextMeshPro text = dieObj.GetComponentInChildren<TextMeshPro>();
                    if (text != null)
                    {
                        text.text = die.rolled.ToString();
                        text.color = new Color(0.5f, 0.5f, 0.5f, 0.6f); // dimmed
                    }
                    else
                    {
                        Debug.LogWarning("[diceVizualizer] diePrefab has no TextMeshPro - cannot show dropped die value.");
                    }

                    SpriteRenderer sr = dieObj.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        Color dimColor = sr.color;
                        dimColor.a = 0.5f;
                        sr.color = dimColor;
                    }
                    else
                    {
                        Debug.LogWarning("[diceVizualizer] diePrefab has no SpriteRenderer - cannot dim dropped die.");
                    }

                    Coroutine coroutine = StartCoroutine(MoveDie(dieObj, targetPos, i * 0.1f));
                    activeMoveCoroutines.Add(coroutine);
                }
            }

            yield return new WaitForSeconds(0.4f + diceCount * 0.05f);

            string rollLabel = string.IsNullOrEmpty(label) ? "Roll" : label;
            string outcomeText = BuildOutcomeText(result);
            string totalLine = BuildTotalLine(result);

            if (_snapshotScratch.Count == 1)
            {
                if (totalText != null)
                    totalText.text = $"{rollLabel}: {totalLine}\n{outcomeText}";
            }
            else
            {
                if (!firstRoll) _sb.Append("\n");
                _sb.Append($"{rollLabel}: {totalLine}  {outcomeText}");
                firstRoll = false;
                if (totalText != null)
                    totalText.text = _sb.ToString();
            }

            yOffset -= verticalGap;
        }

        runningAnimation = null;
    }

    IEnumerator MoveDie(GameObject dieObj, Vector3 target, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (dieObj == null) yield break;
        while (Vector3.Distance(dieObj.transform.position, target) > 0.01f)
        {
            if (dieObj == null) yield break; // destroyed mid-move
            dieObj.transform.position = Vector3.MoveTowards(
                dieObj.transform.position,
                target,
                moveSpeed * Time.deltaTime
            );
            yield return null;
        }
        if (dieObj != null)
            dieObj.transform.position = target;
    }

    string BuildTotalLine(diceSystem.RollResult result)
    {
        var sb = new StringBuilder();
        if (result.dice == null) return "";
        for (int i = 0; i < result.dice.Count; i++)
        {
            diceSystem.DieRoll die = result.dice[i];
            string signSymbol = die.sign == 1 ? " + " : " - ";
            if (i == 0)
            {
                sb.Append(die.sign == -1 ? "-" : "");
                sb.Append("d" + die.faces + "=" + die.rolled);
            }
            else
            {
                sb.Append(signSymbol + "d" + die.faces + "=" + die.rolled);
            }
        }
        if (result.flat != 0)
        {
            sb.Append(result.flat > 0 ? " + " + result.flat : " - " + Mathf.Abs(result.flat));
        }
        sb.Append(" = " + result.total);
        return sb.ToString();
    }

    string BuildOutcomeText(diceSystem.RollResult result)
    {
        if (result.target == -1)
            return "";

        string outcome = "";
        if (result.isFumble)      outcome = "FUMBLE";
        else if (result.isCrunchyCrit) outcome = "CRUNCHY CRIT";
        else if (result.isCrit)   outcome = "CRIT";
        else if (result.isHit)    outcome = "HIT";
        else if (result.isHalfMiss) outcome = "HALF MISS";
        else if (result.isMiss)   outcome = "MISS";

        return $"(vs AC {result.target})  {outcome}";
    }
}
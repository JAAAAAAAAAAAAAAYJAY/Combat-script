using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public class sheetParser : MonoBehaviour
{

    #region Config

    [Tooltip("Each TextAsset becomes one sheet: grid[sheetIndex][row][col]")]
    public TextAsset csvFile1;
    public TextAsset csvFile2;

    [Header("Debug")]
    public bool logTableOnLoad = true;

    #endregion


    #region State

    // grid[sheet][row][col]
    public List<List<List<string>>> grid = new List<List<List<string>>>();

    private TextAsset[] _csvFiles;
    TextAsset[] CsvFiles
    {
        get
        {
            if (_csvFiles == null)
                _csvFiles = new TextAsset[] { csvFile1, csvFile2 };
            else
            {
                _csvFiles[0] = csvFile1;
                _csvFiles[1] = csvFile2;
            }
            return _csvFiles;
        }
    }

    #endregion


    #region Unity Lifecycle

    void Awake()
    {
        Load();
    }

    void Start()
    {
        if (logTableOnLoad)
            LogGrid();
    }

    #endregion


    #region Loading

    void Load()
    {
        grid.Clear();

        // abort only if nothing to load at all
        if (csvFile1 == null && csvFile2 == null)
        {
            Debug.LogError("No CSV files assigned!");
            return;
        }

        if (csvFile1 == null || csvFile2 == null)
            Debug.LogWarning("Only one CSV file assigned - loading single sheet");

        TextAsset[] csvFiles = CsvFiles;

        for (int s = 0; s < csvFiles.Length; s++)
        {
            TextAsset csvFile = csvFiles[s];

            if (csvFile == null)
            {
                Debug.LogError($"CSV file at index {s} is not assigned!");
                grid.Add(new List<List<string>>());
                continue;
            }

            grid.Add(ParseCsv(csvFile.text));
        }
    }

    List<List<string>> ParseCsv(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<List<string>>();

        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text.Substring(1);
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        int maxCols = 0;
        List<List<string>> sheet = new List<List<string>>();
        List<string> row = new List<string>();
        StringBuilder current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        current.Append('"'); // escaped quote ("")
                        i++;
                    }
                    else
                    {
                        inQuotes = false; // closing quote
                    }
                }
                else
                {
                    current.Append(c); // includes embedded '\n'
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    row.Add(current.ToString().Trim());
                    current.Clear();
                }
                else if (c == '\n')
                {
                    row.Add(current.ToString().Trim());
                    current.Clear();
                    CommitRow(row, sheet, ref maxCols);
                    row = new List<string>();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        // Flush any trailing cell / row
        if (current.Length > 0 || row.Count > 0 || inQuotes)
        {
            row.Add(current.ToString().Trim());
            current.Clear();
            CommitRow(row, sheet, ref maxCols);
        }

        // Pad every committed row out to maxCols so the grid is rectangular
        foreach (var r in sheet)
        {
            while (r.Count < maxCols)
                r.Add("");
        }

        return sheet;
    }

    static void CommitRow(List<string> row, List<List<string>> sheet, ref int maxCols)
    {
        bool anyNonEmpty = false;
        for (int i = 0; i < row.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(row[i])) { anyNonEmpty = true; break; }
        }
        if (!anyNonEmpty)
            return;

        sheet.Add(row);
        if (row.Count > maxCols)
            maxCols = row.Count;
    }

    #endregion


    #region Logging

    public void LogGrid()
    {
        if (grid.Count == 0)
        {
            Debug.LogWarning("Grid is empty - nothing to log.");
            return;
        }

        for (int s = 0; s < grid.Count; s++)
        {
            LogSheet(s);
        }
    }

    public void LogSheet(int sheetIndex)
    {
        if (sheetIndex < 0 || sheetIndex >= grid.Count)
        {
            Debug.LogWarning($"Sheet index {sheetIndex} is out of range.");
            return;
        }

        List<List<string>> sheet = grid[sheetIndex];

        if (sheet.Count == 0)
        {
            Debug.LogWarning($"Sheet {sheetIndex} is empty - nothing to log.");
            return;
        }

        int colCount = sheet[0].Count;
        int[] colWidths = new int[colCount];

        for (int c = 0; c < colCount; c++)
        {
            int widest = 0;
            for (int r = 0; r < sheet.Count; r++)
            {
                if (c < sheet[r].Count)
                    widest = Mathf.Max(widest, sheet[r][c].Length);
            }
            colWidths[c] = widest;
        }

        TextAsset[] csvFiles = CsvFiles;
        string sheetName = (sheetIndex < csvFiles.Length && csvFiles[sheetIndex] != null)
            ? csvFiles[sheetIndex].name
            : sheetIndex.ToString();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Sheet [{sheetIndex}] \"{sheetName}\" ({sheet.Count} rows x {colCount} cols)");

        for (int r = 0; r < sheet.Count; r++)
        {
            sb.Append($"[{r}] ");
            for (int c = 0; c < sheet[r].Count; c++)
            {
                sb.Append(sheet[r][c].PadRight(colWidths[c] + 2));
            }
            sb.AppendLine();
        }

        if (CombatDebugHandler.UltraDebug) Debug.Log(sb.ToString());
    }

    #endregion


    #region Find

    public List<(int row, int col)> Find(
        string value,
        int sheetIndex,
        int rowStart = -1,
        int rowEnd = -1,
        int colStart = -1,
        int colEnd = -1,
        bool exactMatch = false,
        bool ignoreCase = true)
    {
        List<(int row, int col)> results = new List<(int row, int col)>();

        if (string.IsNullOrEmpty(value) || grid == null || grid.Count == 0)
            return results;

        if (sheetIndex < 0 || sheetIndex >= grid.Count)
            return results;

        List<List<string>> sheet = grid[sheetIndex];
        if (sheet == null || sheet.Count == 0)
            return results;

        StringComparison comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        int rFrom = rowStart < 0 ? 0 : rowStart;
        int rTo = rowEnd < 0 ? sheet.Count - 1 : Mathf.Min(rowEnd, sheet.Count - 1);

        for (int r = rFrom; r <= rTo; r++)
        {
            if (r < 0 || r >= sheet.Count) continue;

            int colCount = sheet[r].Count;
            int cFrom = colStart < 0 ? 0 : colStart;
            int cTo = colEnd < 0 ? colCount - 1 : Mathf.Min(colEnd, colCount - 1);

            for (int c = cFrom; c <= cTo; c++)
            {
                if (c < 0 || c >= colCount) continue;

                string cell = sheet[r][c];
                bool isMatch = exactMatch
                    ? string.Equals(cell, value, comparison)
                    : cell.IndexOf(value, comparison) >= 0;

                if (isMatch)
                    results.Add((r, c));
            }
        }

        return results;
    }

    public List<(int row, int col)> FindInColumn(string value, int sheetIndex, int col, bool exactMatch = false, bool ignoreCase = true)
        => Find(value, sheetIndex, -1, -1, col, col, exactMatch, ignoreCase);

    #endregion
}

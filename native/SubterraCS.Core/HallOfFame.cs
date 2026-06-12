namespace SubterraCS.Core;

/// <summary>
/// The HALL OF FAME — port of the cassette's idle-title screen at
/// <c>$FCDB</c> (see docs/disasm/title-menu.md).  Eight entries,
/// seeded with the original's default table: scores from
/// <c>$FDF5</c> and names from <c>$FE0F</c> — which turn out to be
/// Star Wars Red Squadron pilots, a 1985 easter egg.
///
/// Port-only addition on top of the cassette: scores persist to a
/// plain-text file (<c>hiscores.cfg</c> at the repo root, one
/// <c>name=score</c> line per entry) and a finished game's score is
/// inserted into the table.  The cassette never wrote its table
/// back (no writable storage on tape!), so persistence is ours.
/// </summary>
public sealed class HallOfFame
{
    public const int Entries = 8;

    public readonly record struct Entry(string Name, int Score);

    private readonly List<Entry> _table = new();
    private string _path = "";

    /// <summary>The cassette's default table — $FDF5 scores (LE
    /// 16-bit) + $FE0F names (8 chars each), in rank order.  Verified
    /// byte-for-byte from the snapshot: four Star Wars Red Squadron
    /// pilots, plus "Timothy" and "Gof" — almost certainly Tim Follin
    /// (music) and Peter Gough (code) signing their work.</summary>
    private static readonly Entry[] Defaults =
    {
        new("somebody", 2900),
        new("Wedge",    2820),
        new("Biggs",    2422),
        new("John D.",  1402),
        new("Luke",      488),
        new("Porkins",   487),
        new("Timothy",   442),
        new("Gof",       240),
    };

    public IReadOnlyList<Entry> Table => _table;

    public static HallOfFame Load(string path)
    {
        var hof = new HallOfFame { _path = path };
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.LastIndexOf('=');
                if (eq <= 0) continue;
                if (int.TryParse(line[(eq + 1)..].Trim(), out int score))
                    hof._table.Add(new Entry(line[..eq].Trim(), score));
            }
        }
        if (hof._table.Count == 0) hof._table.AddRange(Defaults);
        hof._table.Sort((a, b) => b.Score.CompareTo(a.Score));
        while (hof._table.Count > Entries) hof._table.RemoveAt(hof._table.Count - 1);
        return hof;
    }

    /// <summary>Would <paramref name="score"/> enter the table?
    /// Returns the 0-based rank it would take, or -1.  No insert,
    /// no persistence — used to decide whether to ask for a name.</summary>
    public int WouldPlace(int score)
    {
        if (score <= 0) return -1;
        int rank = _table.FindIndex(e => score > e.Score);
        if (rank >= 0) return rank;
        return _table.Count < Entries ? _table.Count : -1;
    }

    /// <summary>Submit a finished game's score.  Returns the 0-based
    /// rank if it entered the table, or -1.  Persists on insert.</summary>
    public int Submit(string name, int score)
    {
        if (score <= 0) return -1;
        int rank = _table.FindIndex(e => score > e.Score);
        if (rank < 0)
        {
            if (_table.Count >= Entries) return -1;
            rank = _table.Count;
        }
        _table.Insert(rank, new Entry(name, score));
        while (_table.Count > Entries) _table.RemoveAt(_table.Count - 1);
        Save();
        return rank;
    }

    private void Save()
    {
        if (_path.Length == 0) return;
        try
        {
            File.WriteAllLines(_path, _table.Select(e => $"{e.Name}={e.Score}"));
        }
        catch
        {
            // Read-only checkout — table still works for this session.
        }
    }
}

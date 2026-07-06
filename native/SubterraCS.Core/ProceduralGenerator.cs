namespace SubterraCS.Core;

/// <summary>
/// One generated page for the MODERN endless mode (depth 6+), emitted
/// entirely in the CASSETTE'S OWN data formats so the faithful
/// subsystems consume it unchanged:
///   MiniMapBuffer   — 4096 bytes (16 rows × 256 cols of tile indices),
///                     same shape as the $60F4.. per-level buffers.
///   WorkerSchedule  — 32 bytes: 8 × (worldX, row, cycle, status),
///                     the $E69D record format (workers.md).
///   ShipInit        — 32 bytes: 8 × (X, Y, status, sub), the $E48D
///                     record format ($E319 loader).
///   StationX/Y      — the $E58B fuel-station pair ($DFCD compare).
///   LevelColour     — the $E57B attribute byte.
///   Decor           — System-A-style eternal entity records.
/// </summary>
public sealed class GeneratedLevel
{
    public byte[] MiniMapBuffer = new byte[MiniMap.BufferSize];
    public byte[] WorkerSchedule = new byte[32];
    public byte[] ShipInit = new byte[32];
    public byte StationX;
    public byte StationY;
    public byte LevelColour = 0x04;
    public readonly List<(int TypeId, int WorldX, int Y, int Hp)> Decor = new();
}

/// <summary>
/// Generates endless-mode pages so the MODERN game can keep going past
/// the cassette's five levels.  Historic mode never calls this — it
/// wraps 5 → 1 like the (bug-fixed) original.
///
/// Each page picks a <see cref="Theme"/> which drives its cave
/// colour, terrain roughness, decor mix, and enemy pressure — so a
/// "Drone Swarm" page feels different from a "Lava Chamber".
/// Difficulty rises with depth: more live ships, rougher terrain,
/// more stalagmite obstacles.
///
/// Deterministic: seeded from (baseSeed, depth), so the same run
/// reproduces the same caves.
///
/// Playability guarantees, checked constructively:
///   - the spawn area (cols 8..24, rows 0..2) is always open;
///   - every column keeps an air channel of ≥ 4 char-rows;
///   - 3–4 full-depth shafts reach row 15 so the $F868 dive gate
///     (altitude ≥ $75) is passable;
///   - all 8 workers stand on reachable floor;
///   - the fuel station sits in an open pocket.
/// Cave interiors use tile $01 (the byte the $DFAF wall-death probe
/// checks for); edge cells use decorative tile indices that block
/// ships/bullets but only graze the player — matching the cassette,
/// where only a full two-column $01 overlap kills.
/// </summary>
public sealed class ProceduralGenerator
{
    private readonly int _baseSeed;

    public ProceduralGenerator(int baseSeed) => _baseSeed = baseSeed;

    public enum Theme
    {
        RockFall,        // baseline cave
        LavaChamber,     // lava + drips along the ceiling
        DroneSwarm,      // drone decor, heavy ship pressure
        DeepCreatures,   // dense stalactites, rough terrain
        Caverns,         // tall open rooms, sparse hazards
        MineYard,        // stalagmite slalom
    }

    public Theme ThemeForDepth(int depth)
    {
        // Depths 6..11 tour each theme once; after that, seeded pick.
        Theme[] intro =
        [
            Theme.RockFall, Theme.LavaChamber, Theme.DroneSwarm,
            Theme.Caverns, Theme.MineYard, Theme.DeepCreatures,
        ];
        if (depth - 6 < intro.Length && depth >= 6) return intro[depth - 6];
        var rng = new Random(HashCode.Combine(_baseSeed, depth, 0xBEEF));
        return (Theme)rng.Next(0, 6);
    }

    public GeneratedLevel Generate(int depth)
    {
        var rng = new Random(HashCode.Combine(_baseSeed, depth));
        var theme = ThemeForDepth(depth);
        var page = new GeneratedLevel
        {
            LevelColour = theme switch
            {
                Theme.RockFall      => 0x04,   // green
                Theme.LavaChamber   => 0x02,   // red
                Theme.DroneSwarm    => 0x05,   // cyan
                Theme.DeepCreatures => 0x03,   // magenta
                Theme.Caverns       => 0x06,   // yellow
                _                   => 0x07,   // white
            },
        };

        int difficulty = Math.Min(10, depth - 5);          // 1..10
        int roughness = theme == Theme.DeepCreatures ? 3
                       : theme == Theme.Caverns ? 1 : 2;

        // ---- Terrain: ceiling/floor random walks, wrap-matched ----
        var ceil = new int[256];    // first open row (0 = no ceiling)
        var floor = new int[256];   // first solid row from the top (16 = no floor)
        int c0 = 1, f0 = 13;
        int cCur = c0, fCur = f0;
        for (int x = 0; x < 256; x++)
        {
            if (x % (8 - roughness * 2 + 6) == 0)   // step cadence by roughness
            {
                cCur = Math.Clamp(cCur + rng.Next(-1, 2), 0, 3 + roughness);
                fCur = Math.Clamp(fCur + rng.Next(-1, 2), 10 - roughness, 15);
            }
            if (fCur - cCur < 5) { cCur = Math.Max(0, fCur - 5); }
            ceil[x] = cCur;
            floor[x] = fCur;
        }
        // Blend the last 16 columns back toward the start values so
        // the 256-wide world wraps seamlessly.
        for (int x = 240; x < 256; x++)
        {
            int t = x - 240;
            ceil[x] = (ceil[x] * (15 - t) + c0 * (t + 1)) / 16;
            floor[x] = (floor[x] * (15 - t) + f0 * (t + 1)) / 16;
        }
        // Spawn area always open (player boots at cols 15/16, row 0).
        for (int x = 8; x <= 24; x++) { ceil[x] = 0; floor[x] = Math.Max(floor[x], 12); }

        // ---- Dive shafts: full-depth openings for the $F868 gate ----
        int shaftCount = 3 + (difficulty >= 5 ? 1 : 0);
        var shaftCols = new List<int>();
        for (int s = 0; s < shaftCount; s++)
        {
            int start = 32 + s * (200 / shaftCount) + rng.Next(0, 24);
            int width = 4 + rng.Next(0, 3);
            for (int x = start; x < start + width && x < 256; x++)
            {
                floor[x] = 16;                   // no floor — open to row 15
                shaftCols.Add(x);
            }
        }

        // ---- Stalagmite slalom obstacles (MineYard/deep pages) ----
        int obstacles = theme == Theme.MineYard ? 8 + difficulty
                       : theme == Theme.Caverns ? 2 : 4 + difficulty / 2;
        for (int o = 0; o < obstacles; o++)
        {
            int x = 32 + rng.Next(0, 220);
            if (x is >= 8 and <= 24) continue;
            if (shaftCols.Contains(x)) continue;
            if (floor[x] >= 16) continue;
            // Raise a 1-col spike, keeping ≥ 4 rows of air.
            int spikeTop = Math.Max(ceil[x] + 4, floor[x] - 3);
            floor[x] = spikeTop;
        }

        // ---- Fill the tile buffer ----
        // Interior = $01 (the wall-death byte); edges = decorative
        // theme tiles (solid to ships/bullets, survivable to graze).
        byte edgeTile = theme switch
        {
            Theme.LavaChamber   => 0x06,
            Theme.DroneSwarm    => 0x04,
            Theme.DeepCreatures => 0x08,
            Theme.Caverns       => 0x03,
            Theme.MineYard      => 0x05,
            _                   => 0x02,
        };
        var buf = page.MiniMapBuffer;
        for (int x = 0; x < 256; x++)
        {
            for (int row = 0; row < 16; row++)
            {
                bool solid = row < ceil[x] || row >= floor[x];
                if (!solid) continue;
                bool isEdge = (row == ceil[x] - 1) || (row == floor[x]);
                buf[row * 256 + x] = isEdge ? edgeTile : (byte)0x01;
            }
        }

        // ---- Workers: 8, standing on real floor, spread out ----
        for (int i = 0; i < 8; i++)
        {
            int x;
            int guard = 0;
            do { x = 24 + (i * 29 + rng.Next(0, 20)) % 224; }
            while ((floor[x] >= 16 || floor[x] - ceil[x] < 4) && ++guard < 64);
            if (floor[x] >= 16) x = 16;          // fall back to spawn ledge
            int row = Math.Clamp(floor[x] - 1, 1, 15);
            page.WorkerSchedule[i * 4 + 0] = (byte)x;
            page.WorkerSchedule[i * 4 + 1] = (byte)row;
            page.WorkerSchedule[i * 4 + 2] = (byte)rng.Next(0, 4);
            page.WorkerSchedule[i * 4 + 3] = 0x00;
        }

        // ---- Ships: difficulty-scaled live count in the $E48D format ----
        int shipsAlive = Math.Min(7, 3 + difficulty / 2);
        for (int i = 0; i < 8; i++)
        {
            bool alive = i < shipsAlive;
            int x = 40 + i * 27 + rng.Next(0, 16);
            int y = 0x10 + rng.Next(0, 0x50);
            page.ShipInit[i * 4 + 0] = (byte)(x & 0xFF);
            page.ShipInit[i * 4 + 1] = (byte)y;
            page.ShipInit[i * 4 + 2] = (byte)(alive ? 0x80 : 0x00);
            page.ShipInit[i * 4 + 3] = (byte)(0x40 | (rng.Next(0, 4) << 5));
        }

        // ---- Fuel station: open pocket, marked by an arc decor ----
        {
            int x;
            int guard = 0;
            do { x = 48 + rng.Next(0, 180); }
            while ((floor[x] >= 16 || floor[x] - ceil[x] < 4) && ++guard < 64);
            page.StationX = (byte)x;
            page.StationY = (byte)Math.Clamp((floor[x] - 2) * 8, 8, 0x78);
            // Electric-arc decor ($12 — bulletproof) as the visual
            // beacon at the station.
            page.Decor.Add((0x12, x, Math.Max(0, page.StationY - 8), 1));
        }

        // ---- Theme decor: eternal System-A-style records ----
        (int typeId, bool ceiling)[] mix = theme switch
        {
            Theme.LavaChamber   => [(0x08, false), (0x09, true), (0x09, true)],
            Theme.DroneSwarm    => [(0x0A, true), (0x0A, false), (0x01, false)],
            Theme.DeepCreatures => [(0x02, true), (0x02, true), (0x01, false)],
            Theme.Caverns       => [(0x01, false), (0x09, true)],
            Theme.MineYard      => [(0x01, false), (0x02, true), (0x08, false)],
            _                   => [(0x02, true), (0x01, false), (0x08, false)],
        };
        int decorCount = Math.Min(10, 4 + difficulty);
        for (int i = 0; i < decorCount; i++)
        {
            var (typeId, onCeiling) = mix[rng.Next(0, mix.Length)];
            int x = 28 + rng.Next(0, 220);
            if (floor[x] - ceil[x] < 5) continue;
            int row = onCeiling ? ceil[x] : Math.Max(ceil[x] + 1, floor[x] - 2);
            int y = Math.Clamp(row * 8, 0, 112);
            int hp = typeId == 0x0A ? 2 : 1;
            page.Decor.Add((typeId, x, y, hp));
        }

        return page;
    }
}

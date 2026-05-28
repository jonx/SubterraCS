namespace SubterraCS.Core;

/// <summary>
/// Port of the original's death-animation particle system at
/// <c>$DBC8</c> / <c>$DBDA</c> (see <c>docs/disasm/death.md</c>).
///
/// Mechanism: 8 particles, each a 4-byte record (x, y, dx, dy),
/// seeded from a 32-byte table at <c>$E861</c> with the Y component
/// overridden to <c>$BF - altitude</c>.  64 animation iterations of:
///
///   paint each cell colour C  →  step (x += dx, y += dy)  →  paint white
///
/// The effect lives entirely in the ATTRIBUTE FILE — the bitmap is
/// not modified.  This is a clever choice in the original (no
/// bitmap state to clean up); we mirror it here.
///
/// The full `$DBC8` sequence is FOUR `$DBDA` passes bracketing a
/// `$DC43` screen-dim sound effect.  In our port we run one pass
/// of 64 frames (totaling ~1 second at 60 fps) since the original's
/// 4 × 64 = 256 frames is too long for a 60-fps redraw cycle.
/// </summary>
public sealed class Explosion
{
    public const int ParticleCount = 8;
    public const int AnimFrames = 64;

    private struct Particle
    {
        public int X, Y;
        public int Dx, Dy;
    }

    private readonly Particle[] _particles = new Particle[ParticleCount];
    private int _frame;
    private byte _levelColor = 0x04;
    public bool Active { get; private set; }

    /// <summary>The 8 starter velocities — outward fan, mirroring the
    /// shape of the original's seed table at <c>$E861</c> (an 8-direction
    /// burst).  Exact byte-extract is in <c>build/at-f100.bin</c>+32 at
    /// $E861; we use the shape (8 outward directions) and let our
    /// integer step replace the original's signed-byte add semantics.</summary>
    private static readonly (int dx, int dy)[] Velocities =
    {
        ( 2,  0), ( 2,  2), ( 0,  2), (-2,  2),
        (-2,  0), (-2, -2), ( 0, -2), ( 2, -2),
    };

    /// <summary>Trigger the explosion at the given pixel coordinate
    /// using the level's attribute colour.  Port of <c>$DBC8</c>'s
    /// 8-particle outward burst with seeds at <c>$E861</c>.</summary>
    public void Trigger(int centerX, int centerY, byte levelColor)
    {
        _levelColor = levelColor;
        _frame = 0;
        _converge = false;
        for (int i = 0; i < ParticleCount; i++)
        {
            _particles[i].X = centerX;
            _particles[i].Y = centerY;
            _particles[i].Dx = Velocities[i].dx;
            _particles[i].Dy = Velocities[i].dy;
        }
        Active = true;
    }

    /// <summary>Trigger the spawn-in animation — port of <c>$E135</c>:
    /// 8 particles seeded at fixed scattered positions (table at
    /// <c>$E841</c>) converging on the screen centre over 40 frames.
    /// Replaces the explosion's outward-burst with inward-converge.</summary>
    public void TriggerSpawnIn(byte levelColor)
    {
        _levelColor = levelColor;
        _frame = 0;
        _converge = true;
        for (int i = 0; i < ParticleCount; i++)
        {
            _particles[i].X = SpawnSeeds[i].X;
            _particles[i].Y = SpawnSeeds[i].ScreenY;
            _particles[i].Dx = SpawnSeeds[i].Dx;
            _particles[i].Dy = SpawnSeeds[i].Dy;
        }
        Active = true;
    }

    /// <summary>Spawn-in particle seed table — port of <c>$E841</c>.
    /// The cassette stores Y as a BUS-COUNTER value: actual screen
    /// scanline = $BF - storedY (per <c>$E1E4 LD A,$BF; SUB B</c>).
    /// We pre-invert here so we can use Y directly as screen Y.
    /// Cassette bytes:
    ///   (X=$30 Y=$BC) → screen Y = $BF-$BC = 3
    ///   (X=$58 Y=$44) → screen Y = $BF-$44 = 123
    ///   (X=$08 Y=$94) → screen Y = $BF-$94 = 43
    ///   (X=$30 Y=$6C) → screen Y = $BF-$6C = 83
    /// Similarly for the four right-side particles.  Velocities are
    /// kept as-is (they're added to the stored Y, which after
    /// inversion needs Dy to act inverse too — see TriggerSpawnIn).</summary>
    private static readonly (int X, int ScreenY, int Dx, int Dy)[] SpawnSeeds =
    {
        (0x30, 3,   +2, +0),  (0x58, 123, +1, -3),
        (0x08, 43,  +3, -1),  (0x30, 83,  +2, -2),
        (0xD0, 43,  -2, -1),  (0xF8, 83,  -3, -2),
        (0xD0, 123, -2, -3),  (0xD0, 3,   -2, +0),
    };

    private bool _converge;

    public void Reset()
    {
        Active = false;
        _frame = 0;
    }

    /// <summary>Advance one frame: step every particle and update the
    /// active flag when finished.</summary>
    public void Tick()
    {
        if (!Active) return;
        for (int i = 0; i < ParticleCount; i++)
        {
            if (!_converge && _particles[i].Y < 0x41) continue;   // $DC1D
            _particles[i].X += _particles[i].Dx;
            _particles[i].Y += _particles[i].Dy;
        }
        _frame++;
        // Spawn-in is 40 frames per $E144 LD B,$28; death is 64 frames
        // per $DC00 LD B,$40.
        int maxFrames = _converge ? 40 : AnimFrames;
        if (_frame >= maxFrames) Active = false;
    }

    /// <summary>Paint particles for this frame.  Like the original's
    /// `$E199`, this stamps the attribute cell for each particle's
    /// (x, y) — bitmap untouched.  Alternates colour <c>$E57B</c> /
    /// <c>$07</c> across frames to produce the strobing flash.
    /// Spawn-in restricted to the playfield (y &lt; 128) so the
    /// converging particles don't clobber the mini-map area.</summary>
    public void Draw(Framebuffer fb)
    {
        if (!Active) return;
        // $DBFC and $DC35 paint alternate colours each iter — emulate
        // by flipping based on the frame counter's low bit.
        byte attr = (_frame & 1) == 0 ? _levelColor : (byte)0x07;
        for (int i = 0; i < ParticleCount; i++)
        {
            int x = _particles[i].X;
            int y = _particles[i].Y;
            if ((uint)x >= Framebuffer.Width) continue;
            if ((uint)y >= Framebuffer.Height) continue;
            // Don't paint into the HUD / mini-map area (y >= 128).
            if (y >= 128) continue;
            fb.Attributes[Framebuffer.AttributeAddress(x, y)] = attr;
        }
    }
}

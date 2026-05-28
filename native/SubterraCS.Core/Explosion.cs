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
    /// using the level's attribute colour.</summary>
    public void Trigger(int centerX, int centerY, byte levelColor)
    {
        _levelColor = levelColor;
        _frame = 0;
        for (int i = 0; i < ParticleCount; i++)
        {
            _particles[i].X = centerX;
            _particles[i].Y = centerY;
            _particles[i].Dx = Velocities[i].dx;
            _particles[i].Dy = Velocities[i].dy;
        }
        Active = true;
    }

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
            // Match $DC1D: skip particles with Y < $41 (offscreen-ish).
            if (_particles[i].Y < 0x41) continue;
            _particles[i].X += _particles[i].Dx;
            _particles[i].Y += _particles[i].Dy;
        }
        _frame++;
        if (_frame >= AnimFrames) Active = false;
    }

    /// <summary>Paint particles for this frame.  Like the original's
    /// `$E199`, this stamps the attribute cell for each particle's
    /// (x, y) — bitmap untouched.  Alternates colour <c>$E57B</c> /
    /// <c>$07</c> across frames to produce the strobing flash.</summary>
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
            fb.Attributes[Framebuffer.AttributeAddress(x, y)] = attr;
        }
    }
}

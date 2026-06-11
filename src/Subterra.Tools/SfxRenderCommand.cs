using System.Globalization;
using Subterra.Spectrum;

namespace Subterra.Tools;

/// <summary>
/// Renders each of the cassette's sound routines to a WAV file by
/// running the ORIGINAL Z80 code in isolation inside our emulator and
/// capturing the beeper output — the same capture/resample pipeline as
/// <c>run-emu -wav</c>, but per-effect instead of per-run.
///
/// Harness: boot the game to its initialised state (the Follin player
/// relocates to <c>$5E88</c> during init), then for each effect, push a
/// sentinel return address on a scratch stack, point PC at the routine,
/// and single-step until PC hits the sentinel.  "Queued" effects
/// (dispatched through <c>$FA0A</c> into the <c>$FF51</c> buffer) are
/// then driven by repeated calls to the <c>$FA32</c> player tick until
/// the beeper goes quiet.  See docs/disasm/sound.md.
///
/// Output: <c>assets/extracted/sfx/&lt;name&gt;.wav</c>, mono 16-bit PCM
/// at 22 050 Hz (matching the native runner's audio device rate so the
/// samples can be played back 1:1 with no resampling).
/// </summary>
internal static class SfxRenderCommand
{
    private const ushort Sentinel = 0x1FFF;       // mid-ROM, never executed by these routines
    private const ushort ScratchStack = 0xFF40;   // below the $FF51 message buffer
    private const long MaxCyclesPerEffect = 60_000_000;   // ~17 s hard cap (single CALL)
    private const long MaxQueuedCycles = 14_000_000;      // ~4 s — Follin messages LOOP forever by design
    private const long QuietCycles = 140_000;             // ~2 frames of silence = done

    private sealed record Effect(string Name, ushort Entry, bool Queued, byte? Level = null);

    private static readonly Effect[] Effects =
    {
        // Direct OUT-loop routines (sound plays inside the call).
        // ($DC43 "death whine" turned out to have NO sound — it's the
        // screen-dim SRL loop only; see sound.md.)
        new("hit",        0xDDC4, Queued: false),
        new("barfill",    0xE419, Queued: false),
        new("spawnin",    0xE135, Queued: false),
        // Follin-queued messages: entry queues via $FA0A, then the
        // $FA32 player tick consumes the $FF51 buffer.
        new("bossalert",  0xF8F9, Queued: true),
        // Ship/boss laser-kill jingle ($F958 entry has a 50% random
        // gate at $F95B; enter at $F962 to capture deterministically).
        new("shipkill",   0xF962, Queued: true),
        new("pickup",     0xF90E, Queued: true),
        new("warning",    0xF93A, Queued: true),
        new("fuellow",    0xF8B4, Queued: true),
        new("shieldlow",  0xF8D8, Queued: true),
        new("gameover",   0xF974, Queued: true),
        new("fanfare1",   0xF99F, Queued: true, Level: 1),
        new("fanfare2",   0xF99F, Queued: true, Level: 2),
        new("fanfare3",   0xF99F, Queued: true, Level: 3),
        new("fanfare4",   0xF99F, Queued: true, Level: 4),
        new("fanfare5",   0xF99F, Queued: true, Level: 5),
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "usage: sfx-render <48k.rom> <snapshot.z80> [-rate=22050]\n" +
                "  Renders every known cassette sound routine to\n" +
                "  assets/extracted/sfx/<name>.wav via the emulator.");
            return 2;
        }
        int rate = 22050;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i].StartsWith("-rate=", StringComparison.Ordinal))
                rate = int.Parse(args[i].Substring("-rate=".Length), CultureInfo.InvariantCulture);
        }

        var rom = File.ReadAllBytes(args[0]);
        var snap = Z80SnapshotReader.Load(args[1]);

        var repoRoot = RenderTarget.FindRepoRoot(AppContext.BaseDirectory);
        var outDir = Path.Combine(repoRoot, "assets", "extracted", "sfx");
        Directory.CreateDirectory(outDir);

        foreach (var fx in Effects)
        {
            // FRESH boot per effect: the Follin player's saved-register
            // block ($FA2A..$FA30) and the $FF51 message state are left
            // mid-flight when we hard-cap a looping tune; reusing the
            // machine makes later entries resume into garbage.  Booting
            // 600 frames (past the menu into gameplay) takes well under
            // a second in our emulator.
            var sys = new Spectrum48(rom);
            sys.LoadSnapshot(snap);
            for (int f = 0; f < 600; f++)
            {
                ApplyBootKeys(sys, f);
                sys.RunFrame();
            }
            // Healthy shield/fuel so the hit chain doesn't reach death,
            // and the requested level for the per-level fanfares.
            sys.WriteMemory(0xE463, 0xFF);
            sys.WriteMemory(0xE464, 0x5F);
            sys.WriteMemory(0xE465, 0xFF);
            sys.WriteMemory(0xE466, 0x5F);
            if (fx.Level is { } lvl) sys.WriteMemory(0xE587, lvl);
            // Mark the $FF51 message buffer idle (($FF54) = $FF51) so
            // the entry's $F8A8 pending-check doesn't bail because a
            // previous capture was cut mid-loop.
            sys.WriteMemory(0xFF54, 0x51);
            sys.WriteMemory(0xFF55, 0xFF);

            long startCycle = sys.Cpu.Cycles;
            CallRoutine(sys, fx.Entry);
            if (fx.Queued)
            {
                // Drive the player until the beeper stays quiet — or
                // until the loop cap: Follin messages repeat forever
                // (the same player loops the title tune), so ~4 s
                // captures at least one full pass of every message.
                // Count the quiet window from the EFFECT start, not
                // from boot leftovers — otherwise entries that queue
                // without sounding immediately (e.g. $F974) get cut
                // off after a single $FA32 tick.
                long lastEdgeCycle = Math.Max(LastEdgeCycle(sys), startCycle);
                while (sys.Cpu.Cycles - startCycle < MaxQueuedCycles)
                {
                    CallRoutine(sys, 0xFA32);
                    long newest = LastEdgeCycle(sys);
                    if (newest > lastEdgeCycle) lastEdgeCycle = newest;
                    else if (sys.Cpu.Cycles - lastEdgeCycle > QuietCycles) break;
                }
            }
            long endCycle = LastEdgeCycle(sys);
            if (endCycle <= startCycle)
            {
                Console.WriteLine($"  {fx.Name,-10} — no beeper output, skipped");
                continue;
            }
            // Follin messages loop forever (the entry plays
            // synchronously until something stops it); clamp the
            // rendered range to one ~4 s pass.
            if (fx.Queued) endCycle = Math.Min(endCycle, startCycle + MaxQueuedCycles);
            // Small tail so the final edge doesn't clip.
            endCycle += 17_500;   // ~5 ms
            var pcm = sys.Beeper.RenderPcm(startCycle, endCycle, rate);
            var path = Path.Combine(outDir, $"{fx.Name}.wav");
            WavWriter.WriteMono16(path, pcm, rate);
            Console.WriteLine($"  {fx.Name,-10} → {Path.GetRelativePath(repoRoot, path)}  ({pcm.Length} samples, {(double)pcm.Length / rate:F2}s)");
        }
        return 0;
    }

    /// <summary>CALL <paramref name="entry"/> with a sentinel return
    /// address on a scratch stack; single-step until the routine RETs
    /// to the sentinel (or the cycle cap trips).</summary>
    private static void CallRoutine(Spectrum48 sys, ushort entry)
    {
        sys.Cpu.SP = ScratchStack;
        sys.WriteMemory((ushort)(ScratchStack - 2), Sentinel & 0xFF);
        sys.WriteMemory((ushort)(ScratchStack - 1), Sentinel >> 8);
        sys.Cpu.SP = (ushort)(ScratchStack - 2);
        sys.Cpu.PC = entry;
        // Deterministic flags: several entries ($F8B4/$F8D8/...) gate
        // on $F8A8 whose CCF/SBC result depends on the INCOMING carry;
        // in-game callers guarantee it, our harness must too.
        sys.Cpu.F = 0;
        long cap = sys.Cpu.Cycles + MaxCyclesPerEffect;
        while (sys.Cpu.PC != Sentinel && sys.Cpu.Cycles < cap)
        {
            sys.Cpu.Step();
        }
    }

    private static long LastEdgeCycle(Spectrum48 sys)
        => sys.Beeper.EdgeCount == 0 ? 0 : sys.Beeper.Edges[sys.Beeper.EdgeCount - 1].Cycle;

    private static void ApplyBootKeys(Spectrum48 sys, int frame)
    {
        for (int i = 0; i < 8; i++) sys.KeyHalfRows[i] = 0x1F;
        // ENTER on 10..50 (leave splash), "1" on 80..110 (keyboard scheme).
        if (frame is >= 10 and <= 50) sys.KeyHalfRows[6] &= unchecked((byte)~1);   // ENTER row 6 bit 0
        if (frame is >= 80 and <= 110) sys.KeyHalfRows[3] &= unchecked((byte)~1);  // "1" row 3 bit 0
    }
}

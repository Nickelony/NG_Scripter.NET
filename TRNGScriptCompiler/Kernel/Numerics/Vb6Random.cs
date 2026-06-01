namespace TRNGScriptCompiler.Kernel.Numerics;

/// <summary>
/// A VB6 24-bit linear congruential generator that owns its state, so a seeded sequence repeats whatever the process
/// ran before.
/// </summary>
/// <remarks>
/// <c>VBMath</c> keeps stale seed bytes per thread across runs. This class is not thread-safe, so the NG header encoder
/// builds one per encoding.
/// </remarks>
internal sealed class Vb6Random
{
	/// <summary>VB6 default per-process seed.</summary>
	private const uint DefaultSeed = 0x50000;

	private readonly double? _seed;

	private readonly Func<double> _timerSeconds;

	/// <summary>
	/// Initializes a generator seeded once, as VB6's <c>Randomize seed</c>, so every seed, 0 included,
	/// gives a fixed sequence.
	/// </summary>
	/// <param name="seed">The fixed seed, or <see langword="null"/> to seed from the timer.</param>
	/// <param name="timerSeconds">
	/// The seconds since midnight for a timer seed, or <see langword="null"/> for the clock; a test seam.
	/// </param>
	/// <remarks>
	/// A <see langword="null"/> seed reseeds from the timer before every draw, as VB6's <c>Randomize</c>
	/// without an argument does. A test fixes <paramref name="timerSeconds"/> to show the padding follows the timer.
	/// </remarks>
	internal Vb6Random(double? seed, Func<double>? timerSeconds = null)
	{
		_seed = seed;
		_timerSeconds = timerSeconds ?? (static () => DateTime.Now.TimeOfDay.TotalSeconds);
	}

	/// <summary>
	/// Returns an integer in [0, <paramref name="numMax"/>), matching the VB6 expression <c>Int(Rnd * numMax)</c>.
	/// </summary>
	/// <param name="numMax">The exclusive upper bound; a bound of 0 or less returns 0.</param>
	internal int Next(int numMax)
	{
		if (numMax <= 0)
			return 0;

		if (_seed is { } seed)
		{
			if (!_deterministicSeeded)
			{
				// Start from the per-process VB6 default, so the Randomize transformation has the baseline VB6 gives it.
				_rngState = DefaultSeed;
				SeedRng(seed);
				_deterministicSeeded = true;
			}
		}
		else
		{
			// VB6-compatible timer seeding: Int(Timer * 10000) Mod 16777216.
			// VB6 calls Randomize (no argument) before every Casuale() call,
			// so we re-seed on each invocation too.
			double now = _timerSeconds();

			long ticks = (long)Math.Floor(now * 10000.0);
			long timerSeed = ticks % 16777216L;

			if (timerSeed < 0)
				timerSeed += 16777216L;

			// Timer mode does not need a fresh state, because non-deterministic output
			// is acceptable, and VB6 itself preserves stale bytes here.
			SeedRng(timerSeed);
		}

		// VB6's Rnd returns a Single and MioModulo.Casuale assigns the product to a
		// Single variable (Val = Rnd * Val), so the multiplication stays in Single
		// precision before Int() truncation. The port computes the same product in
		// float and widens it to Double only for Math.Floor.
		return (int)Math.Floor(NextRnd() * numMax);
	}

	// ------------------------------------------------------------------
	// Internal PRNG state and VB6-compatible algorithm
	// ------------------------------------------------------------------

	private bool _deterministicSeeded;

	/// <summary>VB6-compatible 24-bit LCG state.</summary>
	private uint _rngState;

	/// <summary>Applies the VB6 <c>Randomize number</c> seeding transformation to <see cref="_rngState"/>.</summary>
	/// <remarks>
	/// A fresh per-process start needs <c>_rngState</c> at <see cref="DefaultSeed"/> first.
	/// </remarks>
	private void SeedRng(double number)
	{
		// Extract the high-order 32 bits of the IEEE 754 double.
		long bits = BitConverter.DoubleToInt64Bits(number);
		int hiPart = unchecked((int)(bits >> 32));

		// XOR upper and lower 16-bit halves, shift into middle two bytes.
		int lValue = ((hiPart & 0xFFFF) ^ (hiPart >> 16)) << 8;

		// Replace bits 8-23; preserve bits 0-7 and 24-31 (matches VB6 runtime).
		_rngState = (_rngState & 0xFF0000FF) | unchecked((uint)lValue);
	}

	/// <summary>
	/// Returns the next pseudo-random <see cref="float"/> in [0, 1), advancing the 24-bit LCG by one step.
	/// </summary>
	/// <remarks>
	/// The step constants and the <c>/ 16777216.0f</c> scale match the VB6 and VB.NET runtime exactly.
	/// </remarks>
	private float NextRnd()
	{
		_rngState = unchecked((uint)((((ulong)_rngState * 0x43FD43FDUL) + 0xC39EC3UL) & 0xFFFFFFUL));
		return _rngState / 16777216.0f;
	}
}

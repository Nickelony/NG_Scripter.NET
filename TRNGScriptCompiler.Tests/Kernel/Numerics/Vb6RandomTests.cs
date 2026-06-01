using Microsoft.VisualBasic;
using TRNGScriptCompiler.Kernel.Numerics;

namespace TRNGScriptCompiler.Tests.Kernel.Numerics;

[TestClass]
[TestCategory("Fast")]
public sealed class Vb6RandomTests
{
	private static Vb6Random CreateSeeded(double seed) => new(seed);

	[TestMethod]
	public void Next_ZeroOrNegativeBound_ReturnsZero()
	{
		Vb6Random random = CreateSeeded(12345);

		Assert.AreEqual(0, random.Next(0));
		Assert.AreEqual(0, random.Next(-7));
	}

	[TestMethod]
	public void Next_BoundOfOne_AlwaysReturnsZero()
	{
		Vb6Random random = CreateSeeded(12345);

		for (int i = 0; i < 20; i++)
			Assert.AreEqual(0, random.Next(1), "Int(Rnd * 1) is always 0.");
	}

	[TestMethod]
	public void SeedOfZero_IsDeterministic()
	{
		// Randomize 0 writes 0 into bits 8-23 of the default state 0x50000, which leaves 0, so one LCG step gives 0xC39EC3, which Next(2^24)
		// returns whole; a timer seed would write the bits of the time instead.
		Vb6Random random = CreateSeeded(0);
		Assert.AreEqual(0xC39EC3, random.Next(1 << 24));
	}

	[TestMethod]
	[DataRow(12345)]
	[DataRow(54321)]
	[DataRow(99999)]
	[DataRow(0)]
	public void SeededSequence_MatchesVbMathOnAFreshThread(int seed)
	{
		// VB6's Casuale seeds once with Randomize and draws Int(Rnd * NumMax) in Single precision (MioModulo.bas:844-864). VBMath keeps the
		// VB runtime's generator in a per-thread state that starts at 0x50000, so a fresh thread draws what a fresh VB6 process draws.
		int[] bounds = [9, 16, 4, 1000, 1 << 24];
		int[] expected = new int[50];

		var thread = new Thread(() =>
		{
			VBMath.Randomize(seed);

			for (int i = 0; i < expected.Length; i++)
				expected[i] = (int)Math.Floor(VBMath.Rnd() * bounds[i % bounds.Length]);
		});

		thread.Start();
		thread.Join();

		Vb6Random random = CreateSeeded(seed);
		int[] actual = [.. Enumerable.Range(0, expected.Length).Select(i => random.Next(bounds[i % bounds.Length]))];

		Assert.AreSequenceEqual(expected, actual);
	}

	[TestMethod]
	public void DifferentSeeds_ProduceDifferentSequences()
	{
		Vb6Random first = CreateSeeded(12345);
		Vb6Random second = CreateSeeded(54321);

		bool allEqual = true;

		for (int i = 0; i < 20 && allEqual; i++)
			allEqual = first.Next(1000) == second.Next(1000);

		Assert.IsFalse(allEqual, "Distinct seeds must not produce identical sequences.");
	}

	[TestMethod]
	public void Next_StaysWithinBound()
	{
		Vb6Random random = CreateSeeded(777);

		for (int i = 0; i < 1000; i++)
		{
			int value = random.Next(100);

			Assert.IsGreaterThanOrEqualTo(0, value, $"Next(100) returned {value}; the lower bound is inclusive 0.");
			Assert.IsLessThan(100, value, $"Next(100) returned {value}; the upper bound is exclusive.");
		}
	}
}

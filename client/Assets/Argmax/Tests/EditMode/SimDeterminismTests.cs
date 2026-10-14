using Argmax.Sim;
using NUnit.Framework;

namespace Argmax.Tests
{
    /// <summary>
    /// The same golden hashes the .NET test suite and the SpacetimeDB server produce. If these pass, Unity's
    /// runtime, the server's wasm runtime and native .NET agree bit for bit: one simulation, three hosts.
    /// </summary>
    public class SimDeterminismTests
    {
        [Test]
        public void Duel_matches_native_and_server_golden_hash()
        {
            var w = Sim.Sim.NewDuel(77, SeatKind.Ai, SeatKind.Ai, Presets.Overmind, Presets.Turtle);
            for (int t = 0; t < 1000; t++) Sim.Sim.Step(w, null);
            Assert.AreEqual(9935245210653069819UL, w.StateHash());
        }

        [Test]
        public void Server_shaped_match_matches_golden_hash()
        {
            var w = Sim.Sim.NewDuel(42, SeatKind.Human, SeatKind.Ai, 0, Presets.Turtle);
            for (int t = 0; t < 300; t++) Sim.Sim.Step(w, null);
            Assert.AreEqual(4987065449793598049UL, w.StateHash());
        }

        [Test]
        public void Every_chamber_reference_solution_passes_in_unity()
        {
            string[] solutions =
            {
                "when not full: mine 3\nwhen full: seek home 4\nalways: spread",
                "always: merge 2\nalways: flock 2",
                "always: seek beacon 4",
                "always: seek scrap 2\nwhen enemy: seek enemy 3\nalways: flock",
                "always: merge 2\nalways: flock\nwhen big: seek core 3",
            };
            for (int i = 0; i < solutions.Length; i++)
            {
                var r = Chambers.Run(Chambers.Get(i + 1), solutions[i]);
                Assert.IsTrue(r.Success, "chamber " + (i + 1) + " progress " + r.Progress);
            }
        }

        [Test]
        public void Presets_compile()
        {
            foreach (var p in Presets.All) Assert.IsTrue(Argot.Compile(p.Source).Ok, p.Name);
        }
    }
}

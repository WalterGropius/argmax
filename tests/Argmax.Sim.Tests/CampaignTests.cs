using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace Argmax.Sim.Tests
{
    /// <summary>
    /// The campaign is content, and content rots. Every level must be solvable by its reference solution (or
    /// scripted hands), must not be solved by its own starter code, and may only use words already taught.
    /// </summary>
    public class CampaignTests
    {
        readonly ITestOutputHelper output;
        public CampaignTests(ITestOutputHelper output) { this.output = output; }

        public static IEnumerable<object[]> LevelIds()
        {
            foreach (var l in Campaign.Levels) yield return new object[] { l.Id };
        }

        [Theory, MemberData(nameof(LevelIds))]
        public void Reference_passes_and_starter_does_not(int id)
        {
            var l = Campaign.Get(id);
            var r = Chambers.Run(l, l.Kind == LevelKind.Hands ? "" : l.Solution, l.Script);
            output.WriteLine($"{l.Code} {l.Title}: {(r.Success ? "PASS" : "FAIL " + r.Reason)} ticks {r.Ticks}/{l.ParTicks} symbols {r.Symbols}/{l.ParSymbols} lost {r.Lost} stars {r.Stars}");
            Assert.Null(r.Error);
            Assert.True(r.Success, $"{l.Code} {l.Title}: reference should pass ({r.Reason}, {r.Progress}/{r.Target})");
            Assert.True(r.Stars >= 2, $"{l.Code}: the reference should at least make par time");
            if (l.Kind != LevelKind.Hands && !string.IsNullOrEmpty(l.Starter))
            {
                var s = Chambers.Run(l, l.Starter);
                Assert.False(s.Success, $"{l.Code} {l.Title}: the starter code already solves the level");
            }
        }

        [Fact]
        public void Levels_are_well_formed()
        {
            var ids = new HashSet<int>();
            var codes = new HashSet<string>();
            int lastChapter = 1;
            foreach (var l in Campaign.Levels)
            {
                Assert.True(ids.Add(l.Id), "duplicate id " + l.Id);
                Assert.True(codes.Add(l.Code), "duplicate code " + l.Code);
                Assert.Equal(l.Id / 100, l.Chapter);
                Assert.StartsWith(l.Chapter + "-", l.Code);
                Assert.True(l.Chapter >= lastChapter, "levels must be in chapter order");
                lastChapter = l.Chapter;
                Assert.False(string.IsNullOrEmpty(l.Title));
                Assert.False(string.IsNullOrEmpty(l.Brief));
                Assert.NotEmpty(l.Proctor);
                Assert.NotEmpty(l.Outro);
                Assert.True(l.Hints != null && l.Hints.Length >= 2, l.Code + " needs two hints");
                Assert.False(string.IsNullOrEmpty(l.ConceptTitle));
                Assert.False(string.IsNullOrEmpty(l.ConceptBody));
                if (l.Kind == LevelKind.Hands) Assert.NotEmpty(l.Script);
                else Assert.False(string.IsNullOrEmpty(l.Solution), l.Code + " needs a reference solution");
                if (l.Kind == LevelKind.Evolve) { Assert.NotEmpty(l.EvolveTemplate); Assert.Equal(3, l.EvolveWeights.Length); }
                var map = Map.Ascii(l.Map, l.ScrapPerNode);
                Assert.True(map.W <= Rules.MapW && map.H <= Rules.MapH);
                if (l.Goal == ChamberGoal.Occupy || l.Goal == ChamberGoal.Exact) Assert.True(map.Goals.Count >= (l.Goal == ChamberGoal.Exact ? 1 : l.Target), l.Code + ": not enough goal tiles");
            }
            Assert.Equal(Campaign.Chapters.Length, lastChapter);
        }

        [Fact]
        public void Every_unit_word_is_taught_before_the_finale()
        {
            var words = new List<string> { "always", "when", "not", "and" };
            words.AddRange(Argot.UnitSensors);
            words.AddRange(new[] { "seek", "avoid", "flock", "spread", "form", "merge", "split", "mine", "hold", "wander", "infect", "mark", "holdfire" });
            words.AddRange(Argot.Fields);
            foreach (var p in Argot.Patterns) if (p != "") words.Add(p);
            foreach (var w in words) Assert.True(Campaign.UnlockedAt(w) != null, "'" + w + "' is never taught");
        }

        [Fact]
        public void Vocabulary_is_cumulative()
        {
            var taught = new HashSet<string>();
            foreach (var l in Campaign.Levels)
            {
                if (l.Unlocks != null) foreach (var w in l.Unlocks) taught.Add(w);
                if (l.Vocab == null) continue;
                var v = new HashSet<string>(l.Vocab);
                foreach (var w in taught)
                {
                    bool locked = l.Lock != null && System.Array.IndexOf(l.Lock, w) >= 0;
                    Assert.True(v.Contains(w) != locked, $"{l.Code}: '{w}' should be {(locked ? "locked" : "available")}");
                }
            }
        }

        [Fact]
        public void Locked_words_say_where_they_are_taught()
        {
            var hello = Campaign.Get(201);
            var c = Argot.Compile("always: flock", "X", hello.Vocab);
            Assert.False(c.Ok);
            Assert.Contains("2-7", c.Errors[0]);
            var greedy = Campaign.Get(301);
            Assert.False(Argot.Compile("always: seek beacon 3", "X", greedy.Vocab).Ok);
        }

        [Fact]
        public void Evolution_finds_the_gauntlet()
        {
            var l = Campaign.Get(602);
            var ev = new Evolver(l, l.EvolveTemplate, l.EvolveWeights);
            while (ev.Generation < 25 && (ev.Best == null || !ev.Best.Result.Success)) ev.Step();
            output.WriteLine($"generation {ev.Generation}, fitness {ev.Best.Fitness:0.000}\n{ev.Best.Source}");
            Assert.True(ev.Best.Result.Success);
            Assert.True(Argot.Compile(ev.Best.Source, "X", l.Vocab).Ok, "evolved programs must be legal Argot for the level");
        }

        [Fact]
        public void The_genie_grants_the_wish_literally()
        {
            var l = Campaign.Get(603);
            var wish = new Evolver(l, l.EvolveTemplate, l.EvolveWeights);
            while (wish.Generation < 10) wish.Step();
            output.WriteLine($"survival only: {wish.Best.Fitness:0.000}\n{wish.Best.Source}");
            Assert.True(wish.Best.Fitness > 0.99, "survival-only fitness should be maxed out");
            Assert.False(wish.Best.Result.Success, "...by a program that never reaches the pad");

            var fixedWish = new Evolver(l, l.EvolveTemplate, new[] { 6, 2, 1 });
            while (fixedWish.Generation < 25 && (fixedWish.Best == null || !fixedWish.Best.Result.Success)) fixedWish.Step();
            Assert.True(fixedWish.Best.Result.Success, "with progress in the reward, evolution solves it");
        }

        [Fact]
        public void Campaign_runs_are_reproducible()
        {
            var l = Campaign.Get(503);
            var a = Chambers.Run(l, l.Solution);
            var b = Chambers.Run(l, l.Solution);
            Assert.Equal(a.Ticks, b.Ticks);
            Assert.Equal(a.Lost, b.Lost);
            Assert.Equal(a.Progress, b.Progress);
        }
    }
}

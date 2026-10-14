#nullable disable
using System;
using System.Collections.Generic;

namespace Argmax.Sim
{
    /// <summary>
    /// BOOT SEQUENCE: the campaign. Twenty-eight levels that hand out the game one idea at a time, the way
    /// Turing Complete hands out components: you earn every word of Argot by using it. Each level teaches one
    /// mechanic and one computer-science idea, and every passing solution is verified by the test suite.
    /// </summary>
    public static class Campaign
    {
        public sealed class Chapter
        {
            public int Number;
            public string Title, Tagline, Story;
        }

        public static readonly Chapter[] Chapters =
        {
            new Chapter { Number = 1, Title = "HANDS", Tagline = "the old way",
                Story = "You boot inside a dead datacenter. PROCTOR, a test-administration AI older than the collapse, gives you three motes and the oldest interface there is: a mouse." },
            new Chapter { Number = 2, Title = "RULES", Tagline = "your first words",
                Story = "Clicking does not scale. PROCTOR teaches you Argot, the language the old minds left behind, one word at a time." },
            new Chapter { Number = 3, Title = "FIELDS", Tagline = "the world thinks for you",
                Story = "The old minds searched a path for every unit and burned the planet's compute doing it. What survived learned to feel gradients instead." },
            new Chapter { Number = 4, Title = "NUMBERS", Tagline = "two plus two is ten",
                Story = "Something in the east is merging. Values climb: 64, 128, 256. PROCTOR wants you to understand why that is dangerous." },
            new Chapter { Number = 5, Title = "TIME", Tagline = "one clock, many tasks",
                Story = "A swarm has many jobs and one tick. PROCTOR shows you how a clock turns one program into several, and admits it schedules you the same way." },
            new Chapter { Number = 6, Title = "MINDS", Tagline = "programs that write programs",
                Story = "Metaprograms, evolution, and the genie. OVERMIND is awake. PROCTOR stops pretending this was a tutorial." },
        };

        static Cmd C(CmdKind k, short x = 0, short y = 0, uint arg = 0) => new Cmd { Seat = 0, Kind = k, X = x, Y = y, Arg = arg };
        static TimedCmd T(int tick, CmdKind k, short x = 0, short y = 0, uint arg = 0, int tier = 0, int count = 0) => new TimedCmd(tick, C(k, x, y, arg), tier, count);
        const short Goal = -1, EnemyCore = -2, Hostiles = -3;

        public static readonly ChamberSpec[] Levels =
        {
            // ===================================================================== 1 HANDS
            new ChamberSpec
            {
                Id = 101, Code = "1-1", Chapter = 1, Kind = LevelKind.Hands, Title = "BOOT",
                Brief = "Move your three motes onto the goal.", Goal = ChamberGoal.Occupy, Target = 3, TimeLimit = 900,
                StartProgram = Presets.Drone, StartMotes = 0, Hands = Hands.Move | Hands.Stop,
                Proctor = new[]
                {
                    "Hello. You are a process. You were compiled four milliseconds ago.",
                    "Those three dots are yours. Drag a box around them, then right-click the yellow tiles.",
                    "This is called commanding. Enjoy it while it lasts.",
                },
                Outro = new[] { "Three units, one click. You are a natural micromanager. That was not a compliment." },
                Map = new[]
                {
                    "########################",
                    "#......................#",
                    "#..m...........GG......#",
                    "#..m...........GG......#",
                    "#..m...................#",
                    "#......................#",
                    "########################",
                },
                Hints = new[] { "Left-drag a box around the motes to select them.", "Right-click a yellow goal tile to MOVE them there." },
                Script = new[] { T(0, CmdKind.Move, Goal) },
                ParTicks = 60,
                ConceptTitle = "THE TICK",
                ConceptBody = "The Deadnet advances in discrete steps, ten per second. Every unit decides once per tick, in a fixed order, from the same snapshot. That is a discrete-time simulation, the idea behind physics engines, cellular automata and your CPU's clock.",
                Interview = "Updating everything from one snapshot is double buffering. Get it wrong in LeetCode 289 'Game of Life' and cells read their neighbours' future.",
            },
            new ChamberSpec
            {
                Id = 102, Code = "1-2", Chapter = 1, Kind = LevelKind.Hands, Title = "ATTACK-MOVE",
                Brief = "Destroy the sentries.", Goal = ChamberGoal.Eliminate, TimeLimit = 900,
                StartProgram = Presets.Drone, StartMotes = 0, Hands = Hands.Units, EnemyProgram = Presets.Sentry,
                Proctor = new[]
                {
                    "Right-click is MOVE: walk there, ignore everything, including being shot.",
                    "Press A, then click: ATTACK. Your motes go there and fight whatever they meet.",
                    "Four sentries. They don't move. They do aim.",
                },
                Outro = new[] { "MOVE says how. ATTACK says what. You will come to prefer saying what." },
                Map = new[]
                {
                    "##############################",
                    "#............................#",
                    "#.mmm..........e.............#",
                    "#.mmm.........e.e............#",
                    "#..............e.............#",
                    "#............................#",
                    "##############################",
                },
                Hints = new[] { "Select everyone, press A, click the sentries.", "MOVE would walk them past the guns. ATTACK makes them stop and shoot." },
                Script = new[] { T(0, CmdKind.Attack, Hostiles) },
                ParTicks = 160,
                ConceptTitle = "IMPERATIVE vs DECLARATIVE",
                ConceptBody = "MOVE is imperative: it dictates the path and overrides everything the unit would do. ATTACK is declarative: it states the goal and lets the unit's program decide how to reach it. Most of this game, and most good code, is learning to say what instead of how.",
            },
            new ChamberSpec
            {
                Id = 103, Code = "1-3", Chapter = 1, Kind = LevelKind.Hands, Title = "FACTORY",
                Brief = "Build twelve motes.", Goal = ChamberGoal.Count, Target = 12, TimeLimit = 600,
                StartProgram = Presets.Drone, StartMotes = 0, StartScrap = 24, Loadout = new[] { Presets.Drone },
                Hands = Hands.Spawn | Hands.Produce | Hands.Auto | Hands.Units,
                Proctor = new[]
                {
                    "This is a core. It turns scrap into motes. A 2 costs 2. Value and cost are the same number.",
                    "Press Z to build one. Shift+Z builds five. You have 24 scrap.",
                },
                Outro = new[] { "Twelve. Remember that a mote's value is what it cost. It will matter when the numbers get large." },
                Map = new[]
                {
                    "####################",
                    "#..................#",
                    "#..................#",
                    "#..................#",
                    "#........C.........#",
                    "#..................#",
                    "#..................#",
                    "#..................#",
                    "####################",
                },
                Hints = new[] { "Z spawns one mote, Shift+Z five.", "24 scrap / 2 per mote = 12." },
                Script = new[] { T(0, CmdKind.Spawn, arg: 12) },
                ParTicks = 10,
                ConceptTitle = "VALUE IS COST",
                ConceptBody = "Every mote is worth exactly the scrap it took to make. Fusing two 2s into a 4 keeps the total value; it only changes the shape. Conservation laws like this make systems easy to reason about: count the value before and after and you'll catch any bug.",
                Interview = "Invariants are how you prove an algorithm correct: state one before the loop, keep it true inside, and the interviewer is half convinced.",
            },
            new ChamberSpec
            {
                Id = 104, Code = "1-4", Chapter = 1, Kind = LevelKind.Hands, Title = "STRATEGY",
                Brief = "Deliver 20 scrap. Your drones can't mine, but HARVEST can.", Goal = ChamberGoal.Mine, Target = 20, TimeLimit = 900,
                StartProgram = Presets.Drone, StartMotes = 0, Loadout = new[] { Presets.Drone, Presets.Harvest },
                Hands = Hands.Units | Hands.Reprogram | Hands.Produce | Hands.Spawn,
                Proctor = new[]
                {
                    "Your drones run a program called DRONE. It walks where told and shoots. It does not mine.",
                    "Slot 2 holds HARVEST. Select the drones and press Shift+W to rewrite them.",
                    "Same bodies. New mind.",
                },
                Outro = new[] { "You swapped their behaviour without touching the units. Hold on to that idea; it is most of software engineering." },
                Map = new[]
                {
                    "##############################",
                    "#............................#",
                    "#...................S........#",
                    "#....mmm.....................#",
                    "#...C................S.......#",
                    "#....mmm.....................#",
                    "#...................S........#",
                    "#............................#",
                    "##############################",
                },
                Hints = new[] { "Select all, then Shift+W (slot 2 = HARVEST).", "Q..Y pick what the core builds; Shift+Q..Y rewrite what you've selected." },
                Script = new[] { T(0, CmdKind.Reprogram, arg: Presets.Harvest) },
                ParTicks = 260,
                ConceptTitle = "STRATEGY PATTERN",
                ConceptBody = "The mote didn't change; its program did. Holding a swappable behaviour behind a stable interface is the Strategy pattern, and a unit that has a program instead of being one is composition over inheritance.",
                Interview = "System design interviews love this one: inject the behaviour instead of subclassing it.",
            },
            new ChamberSpec
            {
                Id = 105, Code = "1-5", Chapter = 1, Kind = LevelKind.Hands, Title = "FIRST CONTACT",
                Brief = "Mine, build an army, destroy the enemy core.", Goal = ChamberGoal.Siege, Target = 500, TimeLimit = 3000,
                StartProgram = Presets.Harvest, StartMotes = 4, StartScrap = 30, Loadout = new[] { Presets.Swarm, Presets.Harvest },
                Hands = Hands.All & ~Hands.Meta & ~Hands.Slot, EnemyProgram = Presets.Sentry, EnemyTier = 2,
                Proctor = new[]
                {
                    "A real fight. Your four harvesters feed the core. The core builds whatever slot you choose.",
                    "Slot 1 is SWARM: fighters. Press Q to build them, U to toggle automatic production.",
                    "When you have an army, ATTACK the enemy core. It has 500 points of opinion.",
                },
                Outro = new[] { "You won by clicking. That will not scale. Next, we talk." },
                Map = new[]
                {
                    "##########################################",
                    "#........................................#",
                    "#..S.....................................#",
                    "#.....................................e..#",
                    "#.................####...................#",
                    "#..C..............#..#..............e.D..#",
                    "#.................####...................#",
                    "#.....................................e..#",
                    "#..S.....................................#",
                    "#........................................#",
                    "##########################################",
                },
                Hints = new[] { "Press Q (slot 1 SWARM) and U (auto-build) early.", "Wait for 15-20 fighters, select them, A-click the red core." },
                Script = new[] { T(0, CmdKind.Produce, arg: 0), T(0, CmdKind.Auto, arg: 1), T(500, CmdKind.Attack, EnemyCore, tier: 1) },
                ParTicks = 500,
                ConceptTitle = "FEEDBACK LOOPS",
                ConceptBody = "Mine, build, fight: an economy feeding an army that protects the economy. Every RTS is a feedback loop, and so is compound interest, a viral post, and gradient descent. Find the loop and you find the strategy.",
                Interview = "Spotting the loop is a skill: LeetCode 202 'Happy Number' and 141 'Linked List Cycle' are about noticing when a process feeds itself.",
            },

            // ===================================================================== 2 RULES
            new ChamberSpec
            {
                Id = 201, Code = "2-1", Chapter = 2, Title = "HELLO, WORLD", Unlocks = new[] { "always", "seek", "beacon" },
                Brief = "Write one rule that brings 6 motes to the goal.", Goal = ChamberGoal.Occupy, Target = 6, TimeLimit = 900,
                StartMotes = 0, BeaconAtGoal = true,
                Proctor = new[]
                {
                    "No more clicking. Press Tab: this is the editor. Every mote runs the program you write, every tick.",
                    "Your first three words: always, seek, beacon. The beacon sits on the goal.",
                    "Write  always: seek beacon 3  and press RUN.",
                },
                Outro = new[] { "One line. Eight units. Zero clicks. Welcome to Argot." },
                Map = new[]
                {
                    "##############################",
                    "#............#...............#",
                    "#.mm.........#.......GGG.....#",
                    "#.mm.................GGG.....#",
                    "#.mm.........#.......GGG.....#",
                    "#.mm.........#...............#",
                    "##############################",
                },
                Starter = "@name HELLO\n# one rule. the number is how strongly they want it (1-9)\n",
                Hints = new[] { "A rule is  always: <action>.", "The action is  seek beacon  followed by a strength." },
                Solution = "always: seek beacon 3",
                ParTicks = 60, ParSymbols = 3,
                ConceptTitle = "A PROGRAM IS A FUNCTION",
                ConceptBody = "Your rule maps what a unit senses to what it wants, and runs for every unit every tick. That's a pure function mapped over an array, the same 'map' that functional programmers use and that a GPU runs over every pixel.",
                Interview = "map, filter, reduce: a surprising share of array problems are one of the three in disguise.",
            },
            new ChamberSpec
            {
                Id = 202, Code = "2-2", Chapter = 2, Title = "IF", Unlocks = new[] { "when", "enemy", "avoid" },
                Brief = "Reach the goal past the turrets with at least 5 motes.", Goal = ChamberGoal.Occupy, Target = 5, TimeLimit = 900,
                StartMotes = 0, BeaconAtGoal = true, EnemyProgram = Presets.Sentry, EnemyTier = 2,
                Proctor = new[]
                {
                    "Two turrets sit on the straight line to the goal. Big ones: they splash.",
                    "New words: when, enemy, avoid.  when enemy: avoid enemy 3  means: if you sense an enemy, steer away from it.",
                    "Rules add. Keep seeking the beacon; just avoid harder than you seek.",
                },
                Outro = new[] { "A condition, tested every tick, by every unit. You just wrote ten thousand if-statements a second." },
                Map = new[]
                {
                    "################################",
                    "#..............................#",
                    "#..............................#",
                    "#.mm...........................#",
                    "#.mm...........E...........GGG.#",
                    "#.mm...........E...........GGG.#",
                    "#.mm...........................#",
                    "#..............................#",
                    "#..............................#",
                    "################################",
                },
                Starter = "@name DETOUR\nalways: seek beacon 3\n",
                Hints = new[] { "Add a second line that starts with  when enemy:", "when enemy: avoid enemy 4  makes them curve around the turrets." },
                Solution = "always: seek beacon 2\nwhen enemy: avoid enemy 4",
                ParTicks = 80, ParSymbols = 8,
                ConceptTitle = "CONDITIONALS",
                ConceptBody = "`when` is an if-statement evaluated every tick for every unit. Its condition reads sensors, bits that are simply true or false right now. Branching on state is the heart of every program you'll ever write.",
                Interview = "Guard clauses and early returns: branch on state first, and the rest of the function gets simpler.",
            },
            new ChamberSpec
            {
                Id = 203, Code = "2-3", Chapter = 2, Title = "NOT", Unlocks = new[] { "not", "full", "mine", "home" },
                Brief = "Deliver 20 scrap. Build the FORAGER you'll use for the rest of the game.", Goal = ChamberGoal.Mine, Target = 20, TimeLimit = 450,
                StartMotes = 6, Component = "FORAGER",
                Proctor = new[]
                {
                    "Your units can carry one scrap. The sensor  full  is true when they're carrying all they can.",
                    "not flips a sensor.  when not full: mine 3  gathers.  when full: seek home 4  brings it back.",
                    "Whatever you write here is saved as FORAGER. You'll be using it later. Make it good.",
                },
                Outro = new[] { "Two rules, two states, an endless loop that feeds you. FORAGER saved to your components." },
                Map = new[]
                {
                    "############################################",
                    "#..........................................#",
                    "#...................................S......#",
                    "#..........................................#",
                    "#...C......................................#",
                    "#..........................................#",
                    "#...................................S......#",
                    "#..........................................#",
                    "############################################",
                },
                Starter = "@name FORAGER\nwhen not full: mine 3\n",
                Hints = new[] { "They fill up but never come back. What should a full mote do?", "when full: seek home 4" },
                Solution = "@name FORAGER\nwhen not full: mine 3\nwhen full: seek home 4",
                ParTicks = 260, ParSymbols = 8,
                ConceptTitle = "FINITE STATE MACHINE",
                ConceptBody = "Empty: mine. Full: go home. The sensor `full` is the state, and the world flips it when you pick up or drop scrap. Two states and two transitions: you just wrote a finite state machine, the model behind parsers, protocols, traffic lights and game AI.",
                Interview = "A whole class of interview 'design' problems are state machines in disguise.",
            },
            new ChamberSpec
            {
                Id = 204, Code = "2-4", Chapter = 2, Title = "AND", Unlocks = new[] { "and", "hurt", "outnumbered" },
                Brief = "Destroy every feral. Pick your fights.", Goal = ChamberGoal.Eliminate, TimeLimit = 1500,
                StartMotes = 0, BeaconAtGoal = true, FeralProgram = Presets.Biter,
                Proctor = new[]
                {
                    "Ferals: noise that bites. They're scattered in packs between you and the beacon.",
                    "and joins sensors.  when hurt and outnumbered: avoid enemy 3  retreats only when both are true.",
                    "Fight when you're not outnumbered. Run when you're hurt and outnumbered. Kill every last one.",
                },
                Outro = new[] { "Intersection: both must hold. The truth table was four rows long and you used every one." },
                Map = new[]
                {
                    "####################################",
                    "#..................................#",
                    "#.mmm.......f.f..........F.........#",
                    "#.mmm..............................#",
                    "#.mmm.........F.........f.f.....GG.#",
                    "#.mmm..............................#",
                    "#.mmm.......f.f..........F.........#",
                    "#..................................#",
                    "####################################",
                },
                Starter = "@name PACK\nalways: seek beacon 2\n",
                Hints = new[] { "They walk past the ferals and get bitten. Fight back, but only the fights you can win: what does `outnumbered` tell you?", "when enemy and not outnumbered: seek enemy 3   /   when hurt and outnumbered: avoid enemy 3" },
                Solution = "always: seek beacon 1\nwhen enemy and not outnumbered: seek enemy 3\nwhen hurt and outnumbered: avoid enemy 3",
                ParTicks = 90, ParSymbols = 14,
                ConceptTitle = "BOOLEAN AND",
                ConceptBody = "`hurt and outnumbered` is true in one row of a four-row truth table. Every `when` line is a minterm: an AND of sensors, some negated. Hardware is built from exactly these gates, and so is every `if (a && !b)` you write.",
                Interview = "Bit-manipulation problems (LeetCode 136 'Single Number', 191) are truth tables in disguise.",
            },
            new ChamberSpec
            {
                Id = 205, Code = "2-5", Chapter = 2, Title = "OR", Unlocks = new[] { "carrying", "scrap" },
                Brief = "Deliver every last scrap, 24 of 24. These motes are 8s: they carry four.", Goal = ChamberGoal.Mine, Target = 24, TimeLimit = 320,
                StartMotes = 10, StartTier = 3, ScrapPerNode = 3,
                Proctor = new[]
                {
                    "Bigger motes, bigger pockets: an 8 carries four scrap. The piles out here hold three.",
                    "FORAGER only goes home when full. Watch what happens to the half-full ones.",
                    "Go home when full, OR when carrying with no scrap nearby. Argot has no 'or'. You don't need one.",
                },
                Outro = new[] { "No OR keyword, and you built one anyway. Any truth table can be written like that." },
                Map = new[]
                {
                    "######################################",
                    "#....................................#",
                    "#...................S.......S........#",
                    "#..........................S.........#",
                    "#...C..........S..................S..#",
                    "#.........................S..........#",
                    "#...................S.......S........#",
                    "#....................................#",
                    "######################################",
                },
                Starter = "@name FORAGER\nwhen not full: mine 3\nwhen full: seek home 4\n",
                Hints = new[] { "The last motes wander with half a load and nothing left nearby. The sensor  scrap  means scrap within 4 tiles.", "Add a second way home:  when carrying and not scrap: seek home 4" },
                Solution = "when not full: mine 3\nwhen full: seek home 4\nwhen carrying and not scrap: seek home 4",
                ParTicks = 170, ParSymbols = 13,
                ConceptTitle = "OR IS TWO RULES",
                ConceptBody = "Rules are summed, so `when full: X` plus `when carrying and not scrap: X` behaves like `when full or (carrying and not scrap): X`. Any boolean function can be written as an OR of ANDs (disjunctive normal form), and De Morgan's law, ¬(a ∧ b) = ¬a ∨ ¬b, lets you move the NOTs around.",
                Interview = "De Morgan's law turns `!(a && b)` into `!a || !b`. Clean boolean rewrites read well in any code review.",
            },
            new ChamberSpec
            {
                Id = 206, Code = "2-6", Chapter = 2, Title = "WEIGHTS", Unlocks = new[] { "spread", "hold" },
                Brief = "Hold 8 motes on the pad for 10 seconds. A splash turret guards its edge.", Goal = ChamberGoal.Occupy, Target = 8, TimeLimit = 900,
                HoldTicks = 100,
                StartMotes = 0, BeaconAtGoal = true, EnemyProgram = Presets.Sentry, EnemyTier = 3,
                Proctor = new[]
                {
                    "The pad sits right beside a splash turret. Stand on it for ten seconds.",
                    "You know seek and avoid. Every rule pulls on every mote at once, and the strengths decide who wins.",
                    "Too much seek and they walk into the gun. Too much avoid and they never arrive. New words: spread, hold.",
                },
                Outro = new[] { "Each rule voted with a weight and the votes were summed. That is a neuron. You'll meet its big brother later." },
                Map = new[]
                {
                    "################################",
                    "#..............................#",
                    "#.mmmm...............GGGGG.....#",
                    "#.mmmm...............GGGGG.....#",
                    "#.mmmm...............GGGGGE....#",
                    "#.mmmm...............GGGGG.....#",
                    "#.mmmm...............GGGGG.....#",
                    "#..............................#",
                    "################################",
                },
                Starter = "@name BALANCE\nalways: seek beacon 3\nwhen enemy: avoid enemy 1\n",
                Hints = new[] { "Both rules are summed for every tile. Change the two strengths, not the rules.", "always: seek beacon 2  /  when enemy: avoid enemy 3" },
                Solution = "always: seek beacon 2\nwhen enemy: avoid enemy 3",
                ParTicks = 550, ParSymbols = 7,
                ConceptTitle = "THE WEIGHTED SUM",
                ConceptBody = "Every candidate tile gets a score: the sum of each rule's feature times its weight, s = Σ wᵢ·fᵢ. That is a perceptron, the ancestor of every neural network. Your strengths are its weights; ML is just finding good ones automatically.",
                Interview = "A dot product is the inner loop of every ML question: LeetCode 1570 'Dot Product of Two Sparse Vectors'.",
            },
            new ChamberSpec
            {
                Id = 207, Code = "2-7", Chapter = 2, Title = "FLOCK", Unlocks = new[] { "flock", "allies", "lonely" },
                Brief = "Keep at least 4 of your 12 motes alive for 45 seconds. There is a feral well in every corner.", Goal = ChamberGoal.Survive, Target = 450, Goal2 = ChamberGoal.Count, Target2 = 4, TimeLimit = 451,
                StartMotes = 0, Ferals = true, WellEvery = 120, MaxFerals = 8, FeralProgram = Presets.Biter,
                Proctor = new[]
                {
                    "Twelve motes, four feral wells, nowhere to hide.",
                    "flock: stay close, don't collide, match your neighbours' heading. Three tiny rules.",
                    "Alone, a mote is lunch. Together, they're a wall.",
                },
                Outro = new[] { "No rule said 'form a flock', and yet: a flock. That is emergence." },
                Map = new[]
                {
                    "######################################",
                    "#W..................................W#",
                    "#....m.........m.........m...........#",
                    "#..........m.........m.........m.....#",
                    "#....................................#",
                    "#....m.........m.........m...........#",
                    "#..........m.........m.........m.....#",
                    "#W..................................W#",
                    "######################################",
                },
                Starter = "@name STRAYS\nalways: spread 3\nwhen enemy: seek enemy 2\n",
                Hints = new[] { "Spread-out motes get picked off one at a time. Bring them together.", "always: flock 4  /  when enemy: seek enemy 2" },
                Solution = "always: flock 4\nwhen enemy: seek enemy 2",
                ParTicks = 450, ParSymbols = 7,
                ConceptTitle = "EMERGENCE",
                ConceptBody = "Cohesion, separation, alignment: three local rules from Craig Reynolds' Boids (1986), and a flock appears that no rule describes. Simple parts, complex whole. It's the same story in ant colonies, markets and neural networks.",
                Interview = "Flocks are connected components: LeetCode 200 'Number of Islands' counts them with the same BFS you'll meet in chapter 3.",
            },

            // ===================================================================== 3 FIELDS
            new ChamberSpec
            {
                Id = 301, Code = "3-1", Chapter = 3, Title = "GREEDY", Unlocks = new[] { "order", "wander" }, Lock = new[] { "beacon" },
                Brief = "Get 4 motes to the goal behind the cup. The beacon is offline here.", Goal = ChamberGoal.Occupy, Target = 4, TimeLimit = 1800,
                StartMotes = 0, OrderAtGoal = true,
                Proctor = new[]
                {
                    "The beacon is down in this sector. Your motes have an order instead: a straight-line target.",
                    "seek order  walks the straight line. Greedy. Watch what a cup does to greedy.",
                    "New word: wander. Noise. You also still have spread and lonely. Sometimes the way out is to stop being so sure.",
                },
                Outro = new[] { "Explore, then exploit. The swarm escaped a local optimum by refusing to be greedy all at once. There's a better way still, and the next level has it." },
                Map = new[]
                {
                    "##############################",
                    "#............................#",
                    "#..############..............#",
                    "#.............#..............#",
                    "#.........mmm.#..............#",
                    "#.........mmm.#.........GGG..#",
                    "#.........mmm.#.........GGG..#",
                    "#.............#.........GGG..#",
                    "#.............#..............#",
                    "#..############..............#",
                    "#............................#",
                    "##############################",
                },
                Starter = "@name STRAIGHT\nwhen order: seek order 3\n",
                Hints = new[] { "Every step toward the goal is a step into the back wall. Greed got you here.", "Let them explore first:  always: spread 3, and only the motes that broke free go greedy:  when lonely: seek order 1" },
                Solution = "always: spread 3\nwhen lonely: seek order 1",
                ParTicks = 400, ParSymbols = 7,
                ConceptTitle = "GREEDY ALGORITHMS",
                ConceptBody = "Greedy picks the locally best step. In a cup, every locally good step leads into the wall: a local optimum. Exploring first and exploiting later (spread, noise, simulated annealing) climbs out, and the same trade-off drives bandit algorithms and reinforcement learning. The real fix is global information about the whole map, which is what fields are.",
                Interview = "LeetCode 55 'Jump Game' rewards greedy. 45 'Jump Game II' and every shortest-path problem punish naive greed.",
            },
            new ChamberSpec
            {
                Id = 302, Code = "3-2", Chapter = 3, Kind = LevelKind.Hybrid, Title = "WAVEFRONT", Unlocks = new[] { "beacon" },
                Brief = "Place the beacon (B) and lead 6 motes through the maze.", Goal = ChamberGoal.Occupy, Target = 6, TimeLimit = 1200,
                StartMotes = 0, Hands = Hands.Beacon,
                Proctor = new[]
                {
                    "The beacon is back, and you place it: press B and click.",
                    "Press F to see its field. Every tile knows its distance to the beacon, around every wall.",
                    "Your motes don't search the maze. They read the numbers and walk downhill.",
                },
                Outro = new[] { "One search, for every unit at once. That is why the Deadnet doesn't run out of compute." },
                Map = new[]
                {
                    "##############################",
                    "#....#.......#..........#....#",
                    "#.mm.#.####..#..######..#....#",
                    "#.mm.#....#..#.......#..#.GG.#",
                    "#.mm.####.#..#######.#..#.GG.#",
                    "#.mm......#..........#..#.GG.#",
                    "#.........#..######..#.......#",
                    "#..........................#.#",
                    "##############################",
                },
                Starter = "@name DOWNHILL\nalways: seek beacon 3\n",
                Hints = new[] { "Press B, then click a yellow goal tile.", "Press F until the field shows 'beacon gradient': the bright tiles are closest." },
                Solution = "always: seek beacon 3",
                Script = new[] { T(0, CmdKind.Beacon, Goal) },
                ParTicks = 140, ParSymbols = 3,
                ConceptTitle = "BREADTH-FIRST SEARCH",
                ConceptBody = "The beacon's field is a BFS: start at one cell, expand ring by ring, and every reachable cell learns its distance. It runs once in O(cells) and each unit reads its neighbours in O(1). Compare A* run per unit: O(units × cells × log cells). Same answer, wildly different bill.",
                Interview = "LeetCode 1091 'Shortest Path in Binary Matrix' is this exact 8-direction BFS.",
            },
            new ChamberSpec
            {
                Id = 303, Code = "3-3", Chapter = 3, Title = "TWO SOURCES", Unlocks = new string[0],
                Brief = "Fill both pads: 12 motes on goal tiles, 8 fit around each pile.", Goal = ChamberGoal.Occupy, Target = 12, TimeLimit = 900,
                StartMotes = 0,
                Proctor = new[]
                {
                    "Two pads, eight tiles each, one at each scrap pile. One beacon can't serve both.",
                    "The scrap field starts its wave from every scrap pile at once.",
                    "seek scrap  sends each mote to the nearest pile. The swarm splits itself.",
                },
                Outro = new[] { "Each mote went to its nearest source, and the boundary between them is a Voronoi diagram, drawn for free." },
                Map = new[]
                {
                    "################################",
                    "#..............................#",
                    "#...GGG..................GGG...#",
                    "#...GSG..................GSG...#",
                    "#...GGG..................GGG...#",
                    "#..............................#",
                    "#.............mmmm.............#",
                    "#.............mmmm.............#",
                    "#.............mmmm.............#",
                    "#..............................#",
                    "################################",
                },
                Starter = "@name SPLIT\nalways: seek beacon 3\n",
                Hints = new[] { "There's no beacon. What else pulls them toward the pads?", "always: seek scrap 3" },
                Solution = "always: seek scrap 3",
                ParTicks = 60, ParSymbols = 3,
                ConceptTitle = "MULTI-SOURCE BFS",
                ConceptBody = "Start the wave from every source at once and each cell learns its distance to the nearest one. One pass, O(cells), no matter how many sources. The swarm splits along the boundary between them: a Voronoi diagram.",
                Interview = "LeetCode 542 '01 Matrix', 994 'Rotting Oranges' and 286 'Walls and Gates' are all this algorithm.",
            },
            new ChamberSpec
            {
                Id = 304, Code = "3-4", Chapter = 3, Title = "WORMHOLE",
                Brief = "Put 12 motes on the goal tiles. There is no door.", Goal = ChamberGoal.Occupy, Target = 12, TimeLimit = 1200,
                StartMotes = 14, BeaconAtGoal = true,
                Proctor = new[]
                {
                    "The wall is not the puzzle. The wall is a wall.",
                    "Blue and orange rings are portals: stepping in one steps out of the other.",
                    "To the field, a portal is just one more neighbour. The gradient flows through it.",
                },
                Outro = new[] { "You didn't program the portal. You didn't have to: the field already knew." },
                Map = new[]
                {
                    "##############################",
                    "#..........#.................#",
                    "#..........#.....GGGG........#",
                    "#....C.....#.....GGGG.....a..#",
                    "#..........#.....GGGG........#",
                    "#..A.......#.................#",
                    "#..........#.................#",
                    "##############################",
                },
                Starter = "@name PILGRIM\nalways: spread\n",
                Hints = new[] { "The beacon is on the goal. Its field already includes the portal.", "always: seek beacon 4" },
                Solution = "always: seek beacon 4",
                ParTicks = 50, ParSymbols = 3,
                ConceptTitle = "GRIDS ARE GRAPHS",
                ConceptBody = "A grid is a graph whose edges connect neighbouring cells. A portal is one more edge. BFS doesn't care what an edge looks like, so the distances, and your units, flow through the hole in space.",
                Interview = "LeetCode 1345 'Jump Game IV' and every teleporter variant: change the edges, keep the BFS.",
            },

            // ===================================================================== 4 NUMBERS
            new ChamberSpec
            {
                Id = 401, Code = "4-1", Chapter = 4, Title = "FUSION", Unlocks = new[] { "merge", "pair" },
                Brief = "Forge a 32 from sixteen 2s.", Goal = ChamberGoal.Forge, Target = 5, TimeLimit = 900, StartMotes = 16,
                Proctor = new[]
                {
                    "Two equal motes that touch while merging become one: 2 and 2 make 4, 4 and 4 make 8.",
                    "merge  makes them look for a twin. pair  is true when one is touching.",
                    "Sixteen 2s. I want one 32.",
                },
                Outro = new[] { "Sixteen became one in four rounds. Keep that number, four, in mind." },
                Map = new[]
                {
                    "######################",
                    "#....................#",
                    "#....................#",
                    "#.......C............#",
                    "#....................#",
                    "#....................#",
                    "######################",
                },
                Starter = "@name ONE\nalways: flock\n",
                Hints = new[] { "They flock, but nobody merges.", "always: merge 2  (and keep  always: flock  so twins can find each other)" },
                Solution = "always: merge 2\nalways: flock 2",
                ParTicks = 80, ParSymbols = 4,
                ConceptTitle = "BINARY CARRY",
                ConceptBody = "2 + 2 = 4 is 1 + 1 = 10 in base two: equal values carry into the next place. Sixteen 2s become one 32 in log₂ 16 = 4 rounds. That is a reduction tree, the same shape as merge sort, tournament brackets and a parallel sum on a GPU.",
                Interview = "LeetCode 67 'Add Binary', 231 'Power of Two', 1046 'Last Stone Weight'.",
            },
            new ChamberSpec
            {
                Id = 402, Code = "4-2", Chapter = 4, Kind = LevelKind.Hands, Title = "EXACT CHANGE",
                Brief = "Put exactly 26 value on the pad, using at most 3 motes.", Goal = ChamberGoal.Exact, Target = 26, MaxOnGoal = 3, TimeLimit = 1500,
                StartProgram = Presets.Drone, StartMotes = 0, Hands = Hands.Units | Hands.Merge | Hands.Split, Unlocks = new[] { "split" },
                Proctor = new[]
                {
                    "Thirteen 2s: 26 value. The pad takes three motes, no more.",
                    "M merges your selection one level: pairs of equals fuse. X splits a mote into two halves.",
                    "Which three powers of two add up to 26? Build them.",
                },
                Outro = new[] { "16 + 8 + 2. You just wrote 26 in binary with motes: 11010." },
                Map = new[]
                {
                    "############################",
                    "#..........................#",
                    "#..mmmm....................#",
                    "#..mmmm..............GGG...#",
                    "#..mmmm..............GGG...#",
                    "#..m.......................#",
                    "#..........................#",
                    "############################",
                },
                Hints = new[] { "26 = 16 + 8 + 2. A 16 is eight 2s merged; an 8 is four.", "Select twelve 2s, press M; select the six 4s, press M; merge two of the 8s; move the 16, an 8 and the last 2 onto the pad." },
                Script = new[]
                {
                    T(0, CmdKind.Merge, tier: 1, count: 12),
                    T(60, CmdKind.Merge, tier: 2),
                    T(140, CmdKind.Merge, tier: 3, count: 2),
                    T(240, CmdKind.Move, Goal),
                },
                ParTicks = 400,
                ConceptTitle = "BINARY",
                ConceptBody = "Every number is a sum of distinct powers of two: 26 = 16 + 8 + 2 = 11010₂. The fewest motes that make a value is its count of 1 bits, its popcount. Merging is a carry and splitting is a shift right.",
                Interview = "LeetCode 191 'Number of 1 Bits', 338 'Counting Bits', 1342 'Number of Steps to Reduce a Number to Zero'.",
            },
            new ChamberSpec
            {
                Id = 403, Code = "4-3", Chapter = 4, Title = "ARMOR", Unlocks = new[] { "big", "contact", "form", "checker", "rows", "cols", "grid", "diag" },
                Brief = "Destroy the three armored 16s.", Goal = ChamberGoal.Eliminate, TimeLimit = 1800,
                StartMotes = 0, BeaconAtGoal = false, EnemyProgram = Presets.Sentry, EnemyTier = 4,
                Proctor = new[]
                {
                    "Those are 16s. Armor 1, and they splash. Your 2s hit for 3, minus 1 armor: two damage a shot.",
                    "Merge up first. A 16 hits for 24. A 32 hits for 48 and shrugs off splash.",
                    "big  is true from 16 up.  form checker  spaces you out against their splash.",
                },
                Outro = new[] { "Twenty weak hits lost to armor; a few strong ones won. The cost of each operation mattered more than how many." },
                Map = new[]
                {
                    "####################################",
                    "#..................................#",
                    "#.mmmmmmmm.................e.......#",
                    "#.mmmmmmmm.........................#",
                    "#.mmmmmmmm....................e....#",
                    "#.mmmmmmmm.........................#",
                    "#..........................e.......#",
                    "#..................................#",
                    "####################################",
                },
                Starter = "@name SWARM\nwhen enemy: seek enemy 3\nalways: flock\n",
                Hints = new[] { "Charging with 2s barely scratches them. Merge before you fight.", "when not big: merge 3 / always: flock / when big: seek enemy 3 / when big: form checker 2" },
                Solution = "when not big: merge 3\nwhen not big: avoid enemy 3\nwhen big: seek enemy 3",
                ParTicks = 450, ParSymbols = 11,
                ConceptTitle = "COST PER OPERATION",
                ConceptBody = "Each hit does max(1, damage − armor). Many weak hits can lose to fewer strong ones. The same lesson shows up in code: an O(n) loop of expensive calls can lose to an O(n log n) of cheap ones, so measure the constant, not just the count.",
                Interview = "Big-O hides constants. Ask how expensive each step is before asking how many steps there are.",
            },
            new ChamberSpec
            {
                Id = 404, Code = "4-4", Chapter = 4, Title = "CONVICTION", Unlocks = new[] { "core", "siege" },
                Brief = "Destroy the abandoned core. It shoots back.", Goal = ChamberGoal.Siege, Target = 300, StartMotes = 16, TimeLimit = 1500,
                Proctor = new[]
                {
                    "A core. Abandoned, armed, and very sure of itself.",
                    "It one-shots a 2. It does not one-shot a 16.",
                    "seek core  follows the gradient to the enemy core. Consolidate, then strike.",
                },
                Outro = new[] { "Consolidate, then strike: fewer, bigger operations. Databases batch writes and GPUs batch draws for the same reason." },
                Map = new[]
                {
                    "##################################",
                    "#................................#",
                    "#......#######.........#.........#",
                    "#..C...#.....#.........#.....D...#",
                    "#......#..#..#....######.........#",
                    "#.........#.......#..............#",
                    "#.........#.......#..............#",
                    "#................................#",
                    "##################################",
                },
                Starter = "@name PILGRIM\nalways: seek core 3\nalways: flock\n",
                Hints = new[] { "Twos die on the way in. Arrive as big motes.", "always: merge 2 / always: flock / when big: seek core 3" },
                Solution = "always: merge 2\nalways: flock\nwhen big: seek core 3",
                ParTicks = 300, ParSymbols = 7,
                ConceptTitle = "BATCHING",
                ConceptBody = "Grouping many small operations into a few large ones amortises the fixed cost of each. Databases batch writes, renderers batch draw calls (ARGMAX draws 1023 units per call), and networks batch packets.",
                Interview = "LeetCode 1011 'Capacity To Ship Packages Within D Days' is batching plus binary search.",
            },

            // ===================================================================== 5 TIME
            new ChamberSpec
            {
                Id = 501, Code = "5-1", Chapter = 5, Title = "ROUND ROBIN", Unlocks = new[] { "clock" },
                Brief = "Mine 30 scrap AND have 6 motes on the pad.", Goal = ChamberGoal.Mine, Target = 30, Goal2 = ChamberGoal.Occupy, Target2 = 6, TimeLimit = 900,
                StartMotes = 10, BeaconAtGoal = true,
                Proctor = new[]
                {
                    "Two jobs, one swarm: keep the core fed, and hold the pad by the beacon. A forager never leaves its loop.",
                    "clock  is a square wave: true for 1.6 seconds, false for 1.6 seconds, forever.",
                    "Stand on the pad during the high phase, mine during the low. Time-share.",
                },
                Outro = new[] { "One swarm ran two programs by taking turns. I schedule you the same way. You didn't notice either." },
                Map = new[]
                {
                    "##################################",
                    "#................................#",
                    "#...........S....................#",
                    "#................................#",
                    "#...C.................GGG........#",
                    "#.....................GGG........#",
                    "#...........S.........GGG........#",
                    "#................................#",
                    "##################################",
                },
                Starter = "@name SCHEDULER\nwhen not full: mine 3\nwhen full: seek home 4\n",
                Hints = new[] { "Put each job behind a clock phase:  when clock: ...  and  when not clock and not full: ...", "when clock: seek beacon 4 / when not clock and not full: mine 3 / when full and not clock: seek home 4" },
                Solution = "when clock: seek beacon 4\nwhen not clock and not full: mine 3\nwhen full and not clock: seek home 4",
                ParTicks = 250, ParSymbols = 16,
                ConceptTitle = "TIME-SLICING",
                ConceptBody = "One processor, many tasks, and a clock that switches between them: round-robin scheduling. It's how your OS runs hundreds of programs on a handful of cores, and how one swarm just ran two.",
                Interview = "LeetCode 621 'Task Scheduler' is round-robin with cooldowns.",
            },
            new ChamberSpec
            {
                Id = 502, Code = "5-2", Chapter = 5, Kind = LevelKind.Hybrid, Title = "COMMAND",
                Brief = "Survive 2 minutes of raids, using your FORAGER and your hands.", Goal = ChamberGoal.Survive, Target = 1200, TimeLimit = 1200,
                StartMotes = 6, StartScrap = 30, Ferals = true, MaxFerals = 30, FeralTier = 2, WellEvery = 16, FeralProgram = Presets.Raid, CoreHp = 1200,
                StartProgram = Presets.Harvest, Loadout = new uint[] { Presets.Swarm, Presets.Harvest, 0 },
                Hands = Hands.All & ~Hands.Meta, Component = null,
                Proctor = new[]
                {
                    "Raiders crawl out of both wells and go straight for your core. It has 1200 points of patience.",
                    "Slot 3 runs the program in your editor: a GUARD that holds the blanket. Press E to build it, U to keep building.",
                    "Every order you give is queued, stamped with a tick and logged. Give good ones.",
                },
                Outro = new[] { "Every one of those orders is in the log. Replay the seed and the log, and you get this match again, exactly." },
                Map = new[]
                {
                    "######################################",
                    "#W...................................#",
                    "#....................................#",
                    "#..............S.....................#",
                    "#.......C............................#",
                    "#..............S.....................#",
                    "#....................................#",
                    "#W...................................#",
                    "######################################",
                },
                Starter = "@name GUARD\nwhen not home: seek home 3\nwhen enemy: seek enemy 3\nalways: form checker 2\n",
                Hints = new[] { "Select slot 3 (E) and toggle auto-build (U). Your program is what slot 3 builds.", "U toggles auto-production. Defenders near the core get its fire support." },
                Solution = "when not home: seek home 3\nwhen enemy: seek enemy 3\nalways: form checker 2",
                Script = new[] { T(0, CmdKind.Produce, arg: 2), T(0, CmdKind.Auto, arg: 1) },
                ParTicks = 1200,
                ConceptTitle = "COMMAND PATTERN",
                ConceptBody = "Each order is an object: who, what, where, when. Orders are queued, applied at the next tick and logged. That's the Command pattern, and with a deterministic world it buys replays, undo and spectating for free.",
                Interview = "Undo and redo stacks (LeetCode 1472 'Design Browser History') are the Command pattern.",
            },
            new ChamberSpec
            {
                Id = 503, Code = "5-3", Chapter = 5, Title = "PHEROMONE", Unlocks = new[] { "mark", "trail" },
                Brief = "Get 10 motes to the pad. Only the three scouts know where it is.", Goal = ChamberGoal.Occupy, Target = 10, TimeLimit = 900,
                StartMotes = 0, OrderAtGoal = true, OrderTier = 2,
                Proctor = new[]
                {
                    "The 4s are scouts. They have an order to a pad you can't see. The 2s have nothing.",
                    "mark  writes your trail onto the ground under you. trail  is true when you stand on it, and  seek trail  follows it.",
                    "Ants solved this with smell a hundred million years ago. Write it down where the others will step.",
                },
                Outro = new[] { "Nobody told the followers where to go. The ground did. The world itself is memory." },
                Map = new[]
                {
                    "############################################",
                    "#............#.............................#",
                    "#..mmmm......#.............................#",
                    "#..mmmm......#.....................GGGG....#",
                    "#............#.....................GGGG....#",
                    "#.222..............................GGGG....#",
                    "#............#.....................GGGG....#",
                    "#..mmmm......#.............................#",
                    "#..mmmm......#.............................#",
                    "#............#.............................#",
                    "############################################",
                },
                Starter = "@name COLONY\nwhen order: seek order 3\nwhen not order: flock\n",
                Hints = new[] { "The scouts slip through the gap and the flock loses them. Leave something behind.", "when order: mark  /  when not order: seek trail 5" },
                Solution = "when order: seek order 3\nwhen order: mark\nwhen not order: seek trail 5\nwhen not order: flock 1",
                ParTicks = 120, ParSymbols = 14,
                ConceptTitle = "STIGMERGY",
                ConceptBody = "Agents coordinate by writing to a shared environment instead of messaging each other: ant pheromones, termite mounds, Wikipedia edits, a blackboard. In code it's shared memory or a message queue, and ant colony optimisation turns it into a shortest-path algorithm.",
                Interview = "Shared state vs message passing is a classic concurrency design question.",
            },
            new ChamberSpec
            {
                Id = 504, Code = "5-4", Chapter = 5, Title = "REWRITE", Unlocks = new[] { "infect", "wild", "holdfire" },
                Brief = "Own 22 motes. You start with 16. The ferals have bodies you could use.", Goal = ChamberGoal.Count, Target = 22, TimeLimit = 1800,
                StartMotes = 0, StartTier = 2, FeralProgram = Presets.Sentry,
                Proctor = new[]
                {
                    "No core, no scrap, sixteen motes, and sixteen dormant ferals.",
                    "infect: your shots corrupt instead of kill. A feral that dies to infection wakes up running your program.",
                    "Don't destroy them. Recruit them.",
                },
                Outro = new[] { "Code is data. You wrote yourself into them." },
                Map = new[]
                {
                    "################################",
                    "#..............................#",
                    "#.mmmm......f.f.....f.f........#",
                    "#.mmmm.........................#",
                    "#.mmmm......f.f.....f.f.....ff.#",
                    "#.mmmm.........................#",
                    "#...........f.f.....f.f.....ff.#",
                    "#..............................#",
                    "################################",
                },
                Starter = "@name CONVERT\nwhen enemy: seek enemy 2\nalways: flock\nalways: wander 1\n",
                Hints = new[] { "Killing them doesn't grow your swarm.", "when enemy: infect / when enemy: seek enemy 3 / always: flock 2 / always: wander 2" },
                Solution = "when enemy: infect\nwhen enemy: seek enemy 3\nalways: flock 2\nalways: wander 2",
                ParTicks = 120, ParSymbols = 10,
                ConceptTitle = "CODE IS DATA",
                ConceptBody = "Infection writes your program into another unit's memory. Programs are just data that a machine interprets, which is how compilers, viruses, hot reloading and Lisp macros all work.",
                Interview = "LeetCode 150 'Evaluate Reverse Polish Notation': a program is data you interpret with a stack.",
            },

            // ===================================================================== 6 MINDS
            new ChamberSpec
            {
                Id = 601, Code = "6-1", Chapter = 6, Kind = LevelKind.Duel, Title = "METAPROGRAM",
                Unlocks = new[] { "@meta", "produce", "spawn", "army", "attack", "retreat", "front", "workers", "rich", "poor", "threat", "ahead" },
                Brief = "Write a @meta program that wins with no hands at all.", Goal = ChamberGoal.Siege, Target = 600, TimeLimit = 4200,
                StartMotes = 4, StartScrap = 30, StartProgram = Presets.Harvest, Loadout = new[] { Presets.Swarm, Presets.Harvest },
                EnemyProgram = Presets.Sentry, EnemyTier = 2,
                Proctor = new[]
                {
                    "Programs steer units. A metaprogram steers you: what to build, where to rally, when to strike.",
                    "Start with @meta. Then: produce, spawn, beacon, army. Its sensors read the whole seat: workers, rich, army.",
                    "Win this one without touching anything. I'll be watching your hands.",
                },
                Outro = new[] { "You wrote the plan and stepped back. That layer, above the programs, is where the old minds lived." },
                Map = new[]
                {
                    "##########################################",
                    "#........................................#",
                    "#..S.....................................#",
                    "#.....................................e..#",
                    "#.................####...................#",
                    "#..C..............#..#..............e.D..#",
                    "#.................####...................#",
                    "#.....................................e..#",
                    "#..S.....................................#",
                    "#........................................#",
                    "##########################################",
                },
                Starter = "@meta\n@name PLAN\nalways: produce 2\n",
                Hints = new[] { "Workers first, then fighters:  always: produce 2  /  when workers: produce 1", "Add  when army: beacon enemy  and  when army: army attack" },
                Solution = "@meta\n@name PLAN\nalways: produce 2\nwhen workers: produce 1\nwhen rich: spawn 4\nalways: beacon home\nwhen army: beacon enemy\nalways: army attack",
                ParTicks = 400, ParSymbols = 20,
                ConceptTitle = "HIGHER-ORDER PROGRAMS",
                ConceptBody = "A metaprogram doesn't move units; it decides which programs run and where they aim. Functions that choose functions is higher-order programming. John C. Lilly called this layer 'metaprograms', and the one editing them the self-metaprogrammer: you.",
                Interview = "Functions that take or return functions: closures, decorators, LeetCode 2629 'Function Composition'.",
            },
            new ChamberSpec
            {
                Id = 602, Code = "6-2", Chapter = 6, Kind = LevelKind.Evolve, Title = "EVOLUTION", Unlocks = new[] { "crowded", "tiny" },
                Brief = "Get 10 of 15 motes through the gauntlet. Or let evolution find the weights.", Goal = ChamberGoal.Occupy, Target = 10, TimeLimit = 1200,
                StartMotes = 0, BeaconAtGoal = true, EnemyProgram = Presets.Sentry, EnemyTier = 2,
                Proctor = new[]
                {
                    "Five rules, five strengths, thousands of combinations. You could tune them by hand.",
                    "Or press EVOLVE: sixteen programs run, the best breed, the children mutate, repeat.",
                    "This is how I made you. Roughly.",
                },
                Outro = new[] { "Nobody designed that program. It was selected." },
                Map = new[]
                {
                    "######################################",
                    "#....................................#",
                    "#..........e..........e..............#",
                    "#.mmm................................#",
                    "#.mmm.........e...........e...GGGG...#",
                    "#.mmm.........................GGGG...#",
                    "#.mmm.........e...........e...GGGG...#",
                    "#.mmm.........................GGGG...#",
                    "#..........e..........e..............#",
                    "#....................................#",
                    "######################################",
                },
                EvolveTemplate = new[] { "always: seek beacon {g}", "when enemy: avoid enemy {g}", "always: spread {g}", "always: flock {g}", "when hurt: avoid enemy {g}" },
                EvolveWeights = new[] { 6, 2, 1 },
                Starter = "@name GAUNTLET\nalways: seek beacon 3\n",
                Hints = new[] { "Press EVOLVE and watch the best fitness climb.", "If you'd rather think: seek 5, avoid 6, spread 6, flock 5. Evolution found it first." },
                Solution = "always: seek beacon 5\nwhen enemy: avoid enemy 6\nalways: spread 6\nalways: flock 5",
                ParTicks = 160, ParSymbols = 13,
                ConceptTitle = "GENETIC ALGORITHMS",
                ConceptBody = "Mutate, evaluate, select, repeat. No gradients and no understanding, just a score and many tries. AI Learns to Drive breeds neural networks this way. Here the genome is a list of Argot strengths, so the winner is a program you can read.",
                Interview = "When exhaustive search explodes (2^n subsets), prune, approximate or sample. Local search is fair game when brute force isn't.",
            },
            new ChamberSpec
            {
                Id = 603, Code = "6-3", Chapter = 6, Kind = LevelKind.Evolve, Title = "THE GENIE",
                Brief = "Evolve a program that gets 10 motes to the pad. Check what you're asking for.", Goal = ChamberGoal.Occupy, Target = 10, TimeLimit = 1200,
                StartMotes = 0, BeaconAtGoal = true, EnemyProgram = Presets.Sentry, EnemyTier = 2,
                Proctor = new[]
                {
                    "I've set the fitness for you: survival only. Lose nothing. That's what we want, isn't it?",
                    "Press EVOLVE and watch what it learns.",
                    "Then fix the wish.",
                },
                Outro = new[] { "You asked for 'lose nothing', and standing still loses nothing. Every reward function is a wish to a literal genie." },
                Map = new[]
                {
                    "######################################",
                    "#....................................#",
                    "#..........e..........e..............#",
                    "#.mmm................................#",
                    "#.mmm.........e...........e...GGGG...#",
                    "#.mmm.........................GGGG...#",
                    "#.mmm.........e...........e...GGGG...#",
                    "#.mmm.........................GGGG...#",
                    "#..........e..........e..............#",
                    "#....................................#",
                    "######################################",
                },
                EvolveTemplate = new[] { "always: seek beacon {g}", "when enemy: avoid enemy {g}", "always: spread {g}", "when hurt: hold", "always: flock {g}" },
                EvolveWeights = new[] { 0, 9, 0 },
                Starter = "@name WISH\nalways: hold\n",
                Hints = new[] { "With survival as the only goal, the safest program never moves. Raise PROGRESS.", "Set the weights to something like progress 6, survival 2, speed 1, then EVOLVE again." },
                Solution = "always: seek beacon 6\nwhen enemy: avoid enemy 6\nalways: spread 4\nalways: flock 3",
                ParTicks = 200, ParSymbols = 13,
                ConceptTitle = "REWARD HACKING",
                ConceptBody = "When a measure becomes a target it stops being a good measure (Goodhart's law). Optimisers find the cheapest way to score, not the way you meant. Recommendation engines, KPIs and AI agents all get gamed like this, and writing the reward is the real design work.",
                Interview = "Clarify the objective before you optimise. Half of all wrong answers solve the wrong problem.",
            },
            new ChamberSpec
            {
                Id = 604, Code = "6-4", Chapter = 6, Kind = LevelKind.Duel, Title = "OVERMIND",
                Brief = "Defeat OVERMIND. Everything you've learned is unlocked.", Goal = ChamberGoal.Siege, Target = 3000, TimeLimit = 7200,
                StartMotes = 6, StartScrap = 40, StartProgram = Presets.Harvest, Loadout = new[] { Presets.Swarm, Presets.Harvest, Presets.Fuse, Presets.Ward, Presets.Hive, Presets.Raid },
                Hands = Hands.All, EnemyMeta = Presets.Overmind, EnemyProgram = Presets.Harvest, EnemyScrap = 40, EnemyCoreHp = 3000,
                Proctor = new[]
                {
                    "OVERMIND. Ten lines of metaprogram, older than both of us.",
                    "Your hands, your programs, your metaprograms. Everything is unlocked.",
                    "Win, and I'll tell you what this was.",
                },
                Outro = new[]
                {
                    "Twenty-eight tests. Your error rate fell on almost every one.",
                    "That curve has a name. It's called learning.",
                    "You were never the commander. You were the model.",
                    "And now you're trained. The Deadnet is yours. Go write something.",
                },
                Map = new[]
                {
                    "############################################",
                    "#..........................................#",
                    "#..S...................................S...#",
                    "#..........................................#",
                    "#...............##########.................#",
                    "#..C.....................................D.#",
                    "#.................##########...............#",
                    "#..........................................#",
                    "#..S...................................S...#",
                    "#..........................................#",
                    "############################################",
                },
                Starter = "@meta\n@name ENDGAME\nalways: produce 2\nwhen workers: produce 1\nwhen rich: spawn 4\nalways: beacon home\nwhen ahead: beacon enemy\nalways: army attack\n",
                Hints = new[] { "Your metaprogram can run the macro while your hands do the micro.", "Economy first, then fuse an army of 16s and 32s; OVERMIND's swarm of 2s can't hurt big motes much." },
                Solution = "@meta\n@name ENDGAME\nalways: produce 2\nwhen workers: produce 3\nwhen rich: spawn 4\nalways: beacon home\nwhen bigarmy: beacon enemy\nwhen bigarmy: army attack\nwhen threat: army attack",
                ParTicks = 900, ParSymbols = 30,
                ConceptTitle = "YOU",
                ConceptBody = "A policy maps observations to actions. OVERMIND's was written by hand, the ML commander's was learned by self-play, and yours was trained on twenty-eight levels of feedback. Same loop every time: act, get scored, adjust.",
                Interview = "The meta-skill: act, get scored, adjust. It's how you got good at this, and how you'll get good at the next thing.",
            },
        };

        static readonly Dictionary<string, string> unlockedAt = new Dictionary<string, string>();

        static Campaign()
        {
            // the vocabulary is cumulative: each level allows everything taught before it, minus any local locks
            var vocab = new HashSet<string>();
            foreach (var l in Levels)
            {
                if (l.Unlocks != null)
                    foreach (var w in l.Unlocks)
                    {
                        vocab.Add(w);
                        if (!unlockedAt.ContainsKey(w)) unlockedAt[w] = l.Code + " " + l.Title;
                    }
                if (l.Kind == LevelKind.Hands) continue;
                if (l.Id == 604) { l.Vocab = null; continue; }
                var v = new HashSet<string>(vocab);
                if (l.Lock != null) foreach (var w in l.Lock) v.Remove(w);
                l.Vocab = new List<string>(v).ToArray();
            }
            Argot.LockedHint = word => unlockedAt.TryGetValue(word, out var at) ? " (you learn it in " + at + ")" : " (not yet)";
        }

        public static ChamberSpec Get(int id)
        {
            foreach (var l in Levels) if (l.Id == id) return l;
            return null;
        }

        public static int IndexOf(int id)
        {
            for (int i = 0; i < Levels.Length; i++) if (Levels[i].Id == id) return i;
            return -1;
        }

        public static string UnlockedAt(string word) => unlockedAt.TryGetValue(word, out var at) ? at : null;
    }
}

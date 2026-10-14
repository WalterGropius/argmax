#nullable disable
namespace Argmax.Sim
{
    public static partial class Sim
    {
        /// <summary>
        /// A gradient field as the motes of <paramref name="seat"/> currently read it, for visualisation.
        /// Distances in tiles; <see cref="Fields.Unreachable"/> where no gradient exists. Null if it doesn't apply.
        /// </summary>
        public static ushort[] Field(World w, Channel ch, int seat)
        {
            var S = Prepare(w);
            ComputeFields(w);
            switch (ch)
            {
                case Channel.Scrap: return S.ScrapField;
                case Channel.Home: return seat < 2 ? w.Map.Home(seat) : null;
                case Channel.Beacon: return seat < 2 && w.Seats[seat].BeaconOn ? S.Beacon[seat] : null;
                case Channel.Core: return seat < 2 ? (w.Map.HasCore[1 - seat] ? w.Map.Home(1 - seat) : null) : w.Map.AnyCore;
            }
            return null;
        }

        /// <summary>Current seat tallies (counts, values, threats) after the last refresh.</summary>
        public static SeatTally Tally(World w, int seat)
        {
            if (w.S == null) Prepare(w);
            return w.S.Tally[seat];
        }
    }
}

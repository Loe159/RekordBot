using BinarySerialization;

namespace CueGen.Analysis
{
    /// <summary>
    /// Describes an individual beat in a Rekordbox beat grid.
    /// </summary>
    public class AnlzBeat
    {
        /// <summary>
        /// Gets or sets the the position of the beat within its musical bar.
        /// </summary>
        [FieldOrder(0)]
        [FieldLength(2)]
        public ushort BeatNumber { get; set; }

        /// <summary>
        /// Gets or sets the tempo at the time of this beat (BPM * 100).
        /// </summary>
        [FieldOrder(1)]
        [FieldLength(2)]
        public ushort Tempo { get; set; }

        /// <summary>
        /// Gets or sets the time in milliseconds at which this beat occurs.
        /// </summary>
        [FieldOrder(2)]
        [FieldLength(4)]
        public uint Time { get; set; }
    }
}
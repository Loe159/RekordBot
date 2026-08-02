using BinarySerialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CueGen.Analysis
{
    /// <summary>
    /// Holds a list of all the beats found within the track.
    /// </summary>
    public class BeatGridSection : AnlzSectionContent
    {
        /// <summary>
        /// Gets or sets an unknown value.
        /// </summary>
        [FieldOrder(0)]
        [FieldLength(4)]
        public uint Unknown { get; set; }

        /// <summary>
        /// Gets or sets an unknown value.
        /// </summary>
        [FieldOrder(1)]
        [FieldLength(4)]
        public uint Unknown2 { get; set; }

        /// <summary>
        /// Gets or sets the number of beat entries which follow.
        /// </summary>
        [FieldOrder(2)]
        [FieldLength(4)]
        public uint Length { get; set; }

        /// <summary>
        /// Gets or sets the entries of the beat grid.
        /// </summary>
        [FieldOrder(3)]
        [FieldCount(nameof(Length))]
        public List<AnlzBeat> Beats { get; set; }
    }
}

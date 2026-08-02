using BinarySerialization;
using System.Collections.Generic;

namespace CueGen.Analysis
{
    /// <summary>
    /// A section in a Rekordbox analysis file.
    /// </summary>
    public class AnlzSection
    {
        /// <summary>
        /// Gets or sets the magic value identifying the section type.
        /// </summary>
        [FieldOrder(0)]
        public AnlzMagic Magic { get; set; }

        /// <summary>
        /// Gets or sets the length of the section header in bytes.
        /// </summary>
        [FieldOrder(1)]
        [FieldLength(4)]
        public uint LenHeader { get; set; }

        /// <summary>
        /// Gets or sets the total length of the section (including header) in bytes.
        /// </summary>
        [FieldOrder(2)]
        [FieldLength(4)]
        public uint LenTag { get; set; }

        /// <summary>
        /// Gets or sets the content of the section.
        /// </summary>
        [FieldOrder(3)]
        [Subtype(nameof(Magic), AnlzMagic.PCOB, typeof(CueSection))]
        [Subtype(nameof(Magic), AnlzMagic.PCO2, typeof(CueExtendedSection))]
        [Subtype(nameof(Magic), AnlzMagic.PQTZ, typeof(BeatGridSection))]
        [Subtype(nameof(Magic), AnlzMagic.PSSI, typeof(PhraseSection))]
        [Subtype(nameof(Magic), AnlzMagic.PPTH, typeof(PathSection))]
        [Subtype(nameof(Magic), AnlzMagic.PVBR, typeof(VbrSection))]
        [Subtype(nameof(Magic), AnlzMagic.PWAV, typeof(WaveformPreviewSection))]
        [Subtype(nameof(Magic), AnlzMagic.PWV2, typeof(TinyWaveformPreviewSection))]
        [Subtype(nameof(Magic), AnlzMagic.PWV3, typeof(WaveformDetailSection))]
        [Subtype(nameof(Magic), AnlzMagic.PWV4, typeof(WaveformColorPreviewSection))]
        [Subtype(nameof(Magic), AnlzMagic.PWV5, typeof(WaveformColorDetailSection))]
        [Subtype(nameof(Magic), AnlzMagic.PWV6, typeof(Waveform3BandPreviewSection))]
        [Subtype(nameof(Magic), AnlzMagic.PWV7, typeof(Waveform3BandDetailSection))]
        [SubtypeDefault(typeof(UnknownSection))]
        [FieldLength(nameof(LenTag), ConverterType = typeof(LengthConverter), ConverterParameter = 12)]
        public AnlzSectionContent Content { get; set; }
    }
}
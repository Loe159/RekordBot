using BinarySerialization;
using System.Collections.Generic;

namespace CueGen.Analysis
{
    /// <summary>
    /// Base class for waveform sections.
    /// </summary>
    public abstract class WaveformSectionBase : AnlzSectionContent
    {
    }

    /// <summary>
    /// Waveform Preview Tag ("PWAV").
    /// </summary>
    public class WaveformPreviewSection : WaveformSectionBase
    {
        [FieldOrder(0)]
        public uint LenPreview { get; set; }

        [FieldOrder(1)]
        public uint Unknown { get; set; }

        [FieldOrder(2)]
        [FieldLength(nameof(LenPreview))]
        public byte[] Data { get; set; }
    }

    /// <summary>
    /// Tiny Waveform Preview Tag ("PWV2").
    /// </summary>
    public class TinyWaveformPreviewSection : WaveformSectionBase
    {
        [FieldOrder(0)]
        public uint LenPreview { get; set; }

        [FieldOrder(1)]
        public uint Unknown { get; set; }

        [FieldOrder(2)]
        [FieldLength(nameof(LenPreview))]
        public byte[] Data { get; set; }
    }

    /// <summary>
    /// Waveform Detail Tag (Monochrome) ("PWV3").
    /// </summary>
    public class WaveformDetailSection : WaveformSectionBase
    {
        [FieldOrder(0)]
        public uint LenEntryBytes { get; set; }

        [FieldOrder(1)]
        public uint LenEntries { get; set; }

        [FieldOrder(2)]
        public uint Unknown { get; set; }

        [FieldOrder(3)]
        [FieldLength(nameof(TotalLength))]
        public byte[] Data { get; set; }

        [Ignore]
        public long TotalLength => (long)LenEntryBytes * LenEntries;
    }

    /// <summary>
    /// Waveform Color Preview Tag ("PWV4").
    /// </summary>
    public class WaveformColorPreviewSection : WaveformSectionBase
    {
        [FieldOrder(0)]
        public uint LenEntryBytes { get; set; }

        [FieldOrder(1)]
        public uint LenEntries { get; set; }

        [FieldOrder(2)]
        public uint Unknown { get; set; }

        [FieldOrder(3)]
        [FieldLength(nameof(TotalLength))]
        public byte[] Data { get; set; }

        [Ignore]
        public long TotalLength => (long)LenEntryBytes * LenEntries;
    }

    /// <summary>
    /// Waveform Color Detail Tag ("PWV5").
    /// </summary>
    public class WaveformColorDetailSection : WaveformSectionBase
    {
        [FieldOrder(0)]
        public uint LenEntryBytes { get; set; }

        [FieldOrder(1)]
        public uint LenEntries { get; set; }

        [FieldOrder(2)]
        public uint Unknown { get; set; }

        [FieldOrder(3)]
        [FieldLength(nameof(TotalLength))]
        public byte[] Data { get; set; }

        [Ignore]
        public long TotalLength => (long)LenEntryBytes * LenEntries;
    }

    /// <summary>
    /// Waveform 3-Band Detail Tag ("PWV7").
    /// </summary>
    public class Waveform3BandDetailSection : WaveformSectionBase
    {
        [FieldOrder(0)]
        public uint LenEntryBytes { get; set; }

        [FieldOrder(1)]
        public uint LenEntries { get; set; }

        [FieldOrder(2)]
        public uint Unknown { get; set; }

        [FieldOrder(3)]
        [FieldLength(nameof(TotalLength))]
        public byte[] Data { get; set; }

        [Ignore]
        public long TotalLength => (long)LenEntryBytes * LenEntries;
    }

    /// <summary>
    /// Waveform 3-Band Preview Tag ("PWV6").
    /// </summary>
    public class Waveform3BandPreviewSection : WaveformSectionBase
    {
        [FieldOrder(0)]
        public uint LenEntryBytes { get; set; }

        [FieldOrder(1)]
        public uint LenEntries { get; set; }

        [FieldOrder(2)]
        [FieldLength(nameof(TotalLength))]
        public byte[] Data { get; set; }

        [Ignore]
        public long TotalLength => (long)LenEntryBytes * LenEntries;
    }
}

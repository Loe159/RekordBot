using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;

namespace CueGen.Analysis
{
    /// <summary>
    /// Rekordbox analysis file and section magic values (FourCC).
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AnlzMagic : uint
    {
        /// <summary>
        /// Analysis file header magic ("PMAI").
        /// </summary>
        PMAI = 0x504D4149,

        /// <summary>
        /// Beat Grid Tag ("PQTZ").
        /// </summary>
        PQTZ = 0x5051545A,

        /// <summary>
        /// Cue List Tag (Standard) ("PCOB").
        /// </summary>
        PCOB = 0x50434F42,

        /// <summary>
        /// Extended (nxs2) Cue List Tag ("PCO2").
        /// </summary>
        PCO2 = 0x50434F32,

        /// <summary>
        /// Path Tag ("PPTH").
        /// </summary>
        PPTH = 0x50505448,

        /// <summary>
        /// VBR Tag ("PVBR").
        /// </summary>
        PVBR = 0x50564252,

        /// <summary>
        /// Waveform Preview Tag ("PWAV").
        /// </summary>
        PWAV = 0x50574156,

        /// <summary>
        /// Tiny Waveform Preview Tag ("PWV2").
        /// </summary>
        PWV2 = 0x50575632,

        /// <summary>
        /// Waveform Detail Tag (Monochrome) ("PWV3").
        /// </summary>
        PWV3 = 0x50575633,

        /// <summary>
        /// Waveform Color Preview Tag ("PWV4").
        /// </summary>
        PWV4 = 0x50575634,

        /// <summary>
        /// Waveform Color Detail Tag ("PWV5").
        /// </summary>
        PWV5 = 0x50575635,

        /// <summary>
        /// Waveform 3-Band Preview Tag ("PWV6").
        /// </summary>
        PWV6 = 0x50575636,

        /// <summary>
        /// Waveform 3-Band Detail Tag ("PWV7").
        /// </summary>
        PWV7 = 0x50575637,

        /// <summary>
        /// Song Structure Tag ("PSSI").
        /// </summary>
        PSSI = 0x50535349,

        /// <summary>
        /// Cue Entry Tag ("PCPT").
        /// </summary>
        PCPT = 0x50435054,

        /// <summary>
        /// Extended Cue Entry Tag ("PCP2").
        /// </summary>
        PCP2 = 0x50435032
    }
}

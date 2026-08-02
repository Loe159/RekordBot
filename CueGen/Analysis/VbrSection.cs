using BinarySerialization;

namespace CueGen.Analysis
{
    /// <summary>
    /// VBR Tag ("PVBR").
    /// </summary>
    public class VbrSection : AnlzSectionContent
    {
        [FieldOrder(0)]
        public uint Unknown1 { get; set; }

        [FieldOrder(1)]
        public uint Unknown2 { get; set; }

        [FieldOrder(2)]
        public byte[] Body { get; set; }
    }
}

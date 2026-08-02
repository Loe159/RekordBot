using BinarySerialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace CueGen.Analysis
{
    public class UnknownSection: AnlzSectionContent
    {
        [FieldOrder(0)]
        public byte[] Body { get; set; }
    }
}

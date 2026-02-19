using CueGen.Analysis;
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace CueGen
{
    public class CuePoint
    {
        public double Time { get; set; }
        public string Name { get; set; }
        public int Energy { get; set; }
        public PhraseEntry Phrase { get; set; }
        
        [JsonIgnore]
        public CueType Type { get; set; }
    }
}

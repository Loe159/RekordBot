using BinarySerialization;
using System;

namespace CueGen.Analysis
{
    /// <summary>
    /// Path Tag ("PPTH").
    /// </summary>
    public class PathSection : AnlzSectionContent
    {
        /// <summary>
        /// Gets or sets the length of the path in bytes.
        /// </summary>
        [FieldOrder(0)]
        public uint LenPath { get; set; }

        /// <summary>
        /// Gets or sets the file path.
        /// </summary>
        [FieldOrder(1)]
        [FieldLength(nameof(LenPath))]
        [FieldEncoding("utf-16BE")]
        public string Path { get; set; }
    }
}

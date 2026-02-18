using BinarySerialization;
using System;
using System.Collections.Generic;

namespace CueGen.Analysis
{
    /// <summary>
    /// Rekordbox analysis data.
    /// </summary>
    public class Anlz
    {
        /// <summary>
        /// Gets or sets a value that identifies this as an analysis file. Always "PMAI".
        /// </summary>
        /// <value>
        /// The magic value.
        /// </value>
        [FieldOrder(0)]
        public AnlzMagic Magic { get; set; }

        /// <summary>
        /// Gets or sets the length of the header in bytes.
        /// </summary>
        /// <value>
        /// The header length.
        /// </value>
        [FieldOrder(1)]
        [FieldLength(4)]
        public uint LenHeader { get; set; }

        /// <summary>
        /// Gets or sets the length of the file in bytes.
        /// </summary>
        /// <value>
        /// The file length.
        /// </value>
        [FieldOrder(2)]
        [FieldLength(4)]
        public uint LenFile { get; set; }

        /// <summary>
        /// Gets or sets an unknown value.
        /// </summary>
        /// <value>
        /// The unknown value.
        /// </value>
        [FieldOrder(3)]
        [FieldLength(nameof(LenHeader), ConverterType = typeof(LengthConverter), ConverterParameter = 12)]
        public byte[] Unknown { get; set; }

        /// <summary>
        /// Gets or sets the sections.
        /// </summary>
        /// <value>
        /// The sections.
        /// </value>
        [FieldOrder(4)]
        public List<AnlzSection> Sections { get; set; }

        /// <summary>
        /// Deserializes the specified bytes.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns></returns>
        public static Anlz Deserialize(byte[] bytes)
        {
            var serializer = new BinarySerializer { Endianness = Endianness.Big };
            var anlz = serializer.Deserialize<Anlz>(bytes);
            return anlz;
        }

        /// <summary>
        /// Serializes the specified instance to bytes.
        /// </summary>
        /// <returns>The serialized bytes.</returns>
        public byte[] Serialize()
        {
            var serializer = new BinarySerializer { Endianness = Endianness.Big };
            
            // First pass to get the correct lengths
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.Serialize(stream, this);
                var bytes = stream.ToArray();
                
                // Update LenFile (offset 8)
                uint len = (uint)bytes.Length;
                byte[] lenBytes = BitConverter.GetBytes(len);
                if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
                Array.Copy(lenBytes, 0, bytes, 8, 4);
                
                return bytes;
            }
        }

        public Anlz Clone()
        {
            return Deserialize(Serialize());
        }

        public void FilterWaveforms()
        {
            if (Sections == null) return;
            Sections.RemoveAll(s => s.Magic.ToString().StartsWith("PW"));
        }
    }
}

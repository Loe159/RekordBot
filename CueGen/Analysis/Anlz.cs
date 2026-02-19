using BinarySerialization;
using System;
using System.Collections.Generic;
using System.Linq;

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
            
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.Serialize(stream, this);
                var bytes = stream.ToArray();
                
                // Update LenFile property and patch the byte array at offset 8
                uint len = (uint)bytes.Length;
                this.LenFile = len;
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

        public void SyncFrom(Anlz parent)
        {
            if (parent == null || parent.Sections == null) return;
            if (Sections == null) Sections = new List<AnlzSection>();

            // Synchronisation de PQTZ (Beat Grid)
            var parentPqtz = parent.Sections.FirstOrDefault(s => s.Magic == AnlzMagic.PQTZ);
            if (parentPqtz != null)
            {
                var index = Sections.FindIndex(s => s.Magic == AnlzMagic.PQTZ);
                if (index >= 0)
                    Sections[index] = parentPqtz;
                else
                {
                    var insertIndex = Sections.FindIndex(s => !new[] { AnlzMagic.PPTH, AnlzMagic.PVBR }.Contains(s.Magic));
                    if (insertIndex >= 0) Sections.Insert(insertIndex, parentPqtz);
                    else Sections.Add(parentPqtz);
                }
            }

            // Synchronisation de PVBR (VBR)
            // L'utilisateur soupçonne que PVBR cause des bugs. 
            // Si le parent a un PVBR, on l'utilise. Si le parent n'en a pas, on supprime celui du stem
            // pour garantir la cohérence avec la grille (PQTZ).
            var parentPvbr = parent.Sections.FirstOrDefault(s => s.Magic == AnlzMagic.PVBR);
            var stemPvbrIndex = Sections.FindIndex(s => s.Magic == AnlzMagic.PVBR);
            
            if (parentPvbr != null)
            {
                if (stemPvbrIndex >= 0)
                    Sections[stemPvbrIndex] = parentPvbr;
                else
                    Sections.Insert(0, parentPvbr); // Souvent au début
            }
            else if (stemPvbrIndex >= 0)
            {
                // Le parent n'a pas de VBR, on supprime celui du stem pour être cohérent.
                Sections.RemoveAt(stemPvbrIndex);
            }

            // Synchronisation de PSSI (Song Structure)
            var parentPssi = parent.Sections.FirstOrDefault(s => s.Magic == AnlzMagic.PSSI);
            var stemPssiIndex = Sections.FindIndex(s => s.Magic == AnlzMagic.PSSI);
            
            if (parentPssi != null)
            {
                if (stemPssiIndex >= 0)
                    Sections[stemPssiIndex] = parentPssi;
                else
                    Sections.Add(parentPssi); // PSSI est souvent à la fin
            }
            else if (stemPssiIndex >= 0)
            {
                // Le parent n'a pas de structure de morceau, on supprime celle du stem
                Sections.RemoveAt(stemPssiIndex);
            }

            // Synchronisation de PCOB (Standard Cue List)
            var parentPcob = parent.Sections.FirstOrDefault(s => s.Magic == AnlzMagic.PCOB);
            var stemPcobIndex = Sections.FindIndex(s => s.Magic == AnlzMagic.PCOB);
            
            if (parentPcob != null)
            {
                if (stemPcobIndex >= 0)
                    Sections[stemPcobIndex] = parentPcob;
                else
                    Sections.Add(parentPcob);
            }
            else if (stemPcobIndex >= 0)
            {
                Sections.RemoveAt(stemPcobIndex);
            }

            // Synchronisation de PCO2 (Extended Cue List)
            var parentPco2 = parent.Sections.FirstOrDefault(s => s.Magic == AnlzMagic.PCO2);
            var stemPco2Index = Sections.FindIndex(s => s.Magic == AnlzMagic.PCO2);
            
            if (parentPco2 != null)
            {
                if (stemPco2Index >= 0)
                    Sections[stemPco2Index] = parentPco2;
                else
                    Sections.Add(parentPco2);
            }
            else if (stemPco2Index >= 0)
            {
                Sections.RemoveAt(stemPco2Index);
            }
        }
    }
}

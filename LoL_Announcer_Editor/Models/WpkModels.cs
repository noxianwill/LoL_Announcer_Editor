using System;
using System.Collections.Generic;

namespace LoL_Announcer_Editor.Models
{
    /// <summary>
    /// Represents a single file entry within a WPK package.
    /// </summary>
    public class WpkFileEntry
    {
        public uint DataOffset { get; set; }
        public uint DataLength { get; set; }
        public required string Filename { get; set; }
        public required byte[] Data { get; set; }
    }

    /// <summary>
    /// Represents the full structure of a WPK (Riot Package) file.
    /// </summary>
    public class WpkFile
    {
        public required byte[] Magic { get; set; }
        public uint Version { get; set; }
        public uint FileCount { get; set; }
        public uint OffsetAmount { get; set; }
        public List<uint> Offsets { get; set; } = new List<uint>();
        public List<WpkFileEntry> Entries { get; set; } = new List<WpkFileEntry>();
    }
}

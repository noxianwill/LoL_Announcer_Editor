namespace LoL_Announcer_Editor.Models
{
    /// <summary>
    /// Represents a string and its corresponding FNV1 hash, often used for event mapping in BIN files.
    /// </summary>
    public class StringHash
    {
        public required string String { get; set; }
        public uint Hash { get; set; }
        public uint ContainerId { get; set; }
        public uint MusicSegmentId { get; set; }
        public uint SwitchId { get; set; }
        public uint SoundIndex { get; set; }
    }
}

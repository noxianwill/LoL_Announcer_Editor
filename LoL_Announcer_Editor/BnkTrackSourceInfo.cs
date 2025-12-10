namespace LoL_Announcer_Editor
{
    public class TrackSourceInfo
    {
        public uint TrackIndex { get; set; }
        public uint FileId { get; set; }
        public uint EventId { get; set; }
        public double PlayAt { get; set; }
        public double BeginTrimOffset { get; set; }
        public double EndTrimOffset { get; set; }
        public double SourceDuration { get; set; }
    }
} 
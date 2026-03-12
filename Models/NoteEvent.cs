using System.Text.Json.Serialization;

namespace MusicBox.Models
{
    public sealed class NoteEvent
    {
        public int Midi { get; set; }
        public int StartTick { get; set; }
        public int DurationTicks { get; set; }
        public int BaseDurationTicks { get; set; }
        public int AugmentationDots { get; set; }
        public bool IsRest { get; set; }
        public int Voice { get; set; } = 1;
        public NoteAccidental Accidental { get; set; } = NoteAccidental.None;
        public bool IsStaccato { get; set; }
        public bool IsStaccatissimo { get; set; }
        public bool IsAccent { get; set; }
        public NoteOrnament Ornament { get; set; } = NoteOrnament.None;
        public float OrnamentOffsetX { get; set; }
        public float OrnamentOffsetY { get; set; }
        public float GraceOrnamentOffsetX { get; set; }
        public float GraceOrnamentOffsetY { get; set; }
        public bool TieStart { get; set; }
        public bool TieEnd { get; set; }
        public int BeamGroupId { get; set; }
        public bool? StemUpOverride { get; set; }

        [JsonIgnore]
        public bool IsSelected { get; set; }

        public bool? PreferTrebleStaff { get; set; }
    }
}

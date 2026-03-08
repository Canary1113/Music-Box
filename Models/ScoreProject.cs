using System;
using System.Collections.Generic;

namespace 音乐魔盒.Models
{
    public sealed class ScoreProject
    {
        public string Title { get; set; } = "Untitled";
        public int Bpm { get; set; } = 120;
        public TimeSignature TimeSignature { get; set; } = new(4, 4);
        public KeySignature KeySignature { get; set; } = new(0, KeyMode.Major);
        public int Ppq { get; set; } = 480;
        public List<NoteEvent> Notes { get; set; } = new();
        public List<ExpressionMark> ExpressionMarks { get; set; } = new();
        public List<TimeSignatureChange> TimeSignatureChanges { get; set; } = new();
        public List<KeySignatureChange> KeySignatureChanges { get; set; } = new();
        public Dictionary<string, string> StaffClefs { get; set; } = new();
        public List<int> LayoutSystemMeasureCounts { get; set; } = new();
        public Dictionary<int, float> LayoutBarlineOffsets { get; set; } = new();
        public int LayoutMeasuresPerSystemOverride { get; set; }
        public int LayoutAutoMeasuresPerSystem { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    }
}

using System.Text.Json.Serialization;

namespace 音乐魔盒.Models
{
    public sealed class ExpressionMark
    {
        public string Code { get; set; } = "mf";
        public int StartTick { get; set; }
        public float StaffStepOffset { get; set; } = 18f;
        public float SpanBeats { get; set; } = 1.8f;
        public float ShapeHeightSteps { get; set; } = 6f;
        public float SlopeSteps { get; set; } = 0f;

        [JsonIgnore]
        public bool IsSelected { get; set; }
    }
}

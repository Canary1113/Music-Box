namespace 音乐魔盒.Models
{
    public sealed class TimeSignatureChange
    {
        public int Tick { get; set; }
        public int Numerator { get; set; } = 4;
        public int Denominator { get; set; } = 4;
    }
}

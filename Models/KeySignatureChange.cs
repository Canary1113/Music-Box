namespace 音乐魔盒.Models
{
    public sealed class KeySignatureChange
    {
        public int Tick { get; set; }
        public int Fifths { get; set; }
        public KeyMode Mode { get; set; } = KeyMode.Major;
    }
}

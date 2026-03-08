namespace “Ù¿÷ƒß∫–.Models
{
    public enum KeyMode
    {
        Major,
        Minor
    }

    public sealed class KeySignature
    {
        public int Fifths { get; set; }
        public KeyMode Mode { get; set; }

        public KeySignature() : this(0, KeyMode.Major) { }

        public KeySignature(int fifths, KeyMode mode)
        {
            Fifths = fifths;
            Mode = mode;
        }
    }
}

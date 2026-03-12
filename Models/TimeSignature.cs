using System;

namespace MusicBox.Models
{
    public sealed class TimeSignature
    {
        public int Numerator { get; set; }
        public int Denominator { get; set; }

        public TimeSignature() : this(4, 4) { }

        public TimeSignature(int numerator, int denominator)
        {
            Numerator = Math.Clamp(numerator, 1, 12);
            Denominator = denominator is 1 or 2 or 4 or 8 or 16 ? denominator : 4;
        }

        public int TicksPerBeat(int ppq)
        {
            return (int)Math.Round(ppq * 4.0 / Denominator);
        }

        public int TicksPerMeasure(int ppq)
        {
            return TicksPerBeat(ppq) * Numerator;
        }
    }
}

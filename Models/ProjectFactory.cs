using System;

namespace “Ù¿÷ƒß∫–.Models
{
    public static class ProjectFactory
    {
        public static ScoreProject CreateDefault()
        {
            var project = new ScoreProject
            {
                Title = "Untitled",
                Bpm = 120,
                TimeSignature = new TimeSignature(4, 4),
                KeySignature = new KeySignature(0, KeyMode.Major),
                Ppq = 480,
                UpdatedAt = DateTimeOffset.Now
            };

            return project;
        }
    }
}

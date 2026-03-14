using System;
using System.Collections.Generic;
using System.Linq;
using MusicBox.Models;

namespace MusicBox.Services
{
    public sealed class SmartComposeService
    {
        private static readonly int[] MajorScale = { 0, 2, 4, 5, 7, 9, 11 };
        private static readonly int[] MinorScale = { 0, 2, 3, 5, 7, 8, 10 };
        private static readonly DurationSpec[] DurationCandidates =
        {
            new(NoteLengthUtils.ToTicks(NoteLength.Whole, 480), NoteLengthUtils.ToTicks(NoteLength.Whole, 480), 0),
            new(NoteLengthUtils.ToTicks(NoteLength.Half, 480), NoteLengthUtils.ToTicks(NoteLength.Half, 480), 0),
            new(NoteLengthUtils.ToTicks(NoteLength.Quarter, 480), NoteLengthUtils.ToTicks(NoteLength.Quarter, 480), 0),
            new(NoteLengthUtils.ToTicks(NoteLength.Eighth, 480), NoteLengthUtils.ToTicks(NoteLength.Eighth, 480), 0),
            new(NoteLengthUtils.ToTicks(NoteLength.Sixteenth, 480), NoteLengthUtils.ToTicks(NoteLength.Sixteenth, 480), 0),
            new(NoteLengthUtils.ToTicks(NoteLength.Half, 480), NoteLengthUtils.ToTicks(NoteLength.Half, 480) + NoteLengthUtils.ToTicks(NoteLength.Quarter, 480), 1),
            new(NoteLengthUtils.ToTicks(NoteLength.Quarter, 480), NoteLengthUtils.ToTicks(NoteLength.Quarter, 480) + NoteLengthUtils.ToTicks(NoteLength.Eighth, 480), 1),
            new(NoteLengthUtils.ToTicks(NoteLength.Eighth, 480), NoteLengthUtils.ToTicks(NoteLength.Eighth, 480) + NoteLengthUtils.ToTicks(NoteLength.Sixteenth, 480), 1)
        };

        public SmartComposeResult Generate(SmartComposeRequest request)
        {
            return GenerateCandidates(request, 1).First();
        }

        public IReadOnlyList<SmartComposeResult> GenerateCandidates(SmartComposeRequest request, int count = 3)
        {
            SmartComposeRequest normalized = request ?? new SmartComposeRequest();
            int baseSeed = normalized.Seed == 0 ? Environment.TickCount : normalized.Seed;
            int safeCount = Math.Clamp(count, 1, 6);
            var results = new List<SmartComposeResult>(safeCount);

            for (int index = 0; index < safeCount; index++)
            {
                results.Add(GenerateVariant(normalized, baseSeed + index * 7919, index));
            }

            return results;
        }

        private static SmartComposeResult GenerateVariant(SmartComposeRequest request, int seed, int variantIndex)
        {
            var random = new Random(seed);
            TimeSignature timeSignature = request.TimeSignature ?? new TimeSignature(4, 4);
            MoodSpec mood = ResolveMood(request.MoodId);
            StyleSpec style = ResolveStyle(request.StyleId, timeSignature);
            VariantSpec variant = ResolveVariant(variantIndex);
            int ppq = 480;
            int measures = Math.Clamp(request.Measures, 4, 64);
            int ticksPerMeasure = timeSignature.TicksPerMeasure(ppq);
            int measureUnits = ResolveMeasureUnits(timeSignature);
            int unitTicks = Math.Max(1, ticksPerMeasure / measureUnits);
            int[] scale = request.Mode == KeyMode.Minor ? MinorScale : MajorScale;
            int tonicPitchClass = Mod(request.KeyFifths * 7 + (request.Mode == KeyMode.Minor ? 9 : 0), 12);
            int melodyAnchor = ClosestMidiToTarget(tonicPitchClass, style.CenterMidi + mood.RangeOffset + variant.MelodyOffset);
            int bassAnchor = ClosestMidiToTarget(tonicPitchClass, 43 + variant.BassOffset);
            int[] progression = BuildProgression(style, measures, variant, random);

            var project = new ScoreProject
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? "智能创作" : request.Title.Trim(),
                Bpm = Math.Clamp((request.Bpm <= 0 ? style.DefaultTempo : request.Bpm) + mood.TempoOffset + variant.TempoOffset, mood.MinTempo, mood.MaxTempo),
                TimeSignature = new TimeSignature(timeSignature.Numerator, timeSignature.Denominator),
                KeySignature = new KeySignature(Math.Clamp(request.KeyFifths, -7, 7), request.Mode),
                Ppq = ppq,
                UpdatedAt = DateTimeOffset.Now
            };

            var chordNames = new List<string>(measures);
            List<int>? motifA = null;
            List<int>? motifB = null;
            int previousDegree = mood.StartDegree;

            for (int measure = 0; measure < measures; measure++)
            {
                int chordDegree = progression[measure];
                int startTick = measure * ticksPerMeasure;
                MeasureRole role = ResolveRole(measure, measures);
                chordNames.Add(BuildChordName(tonicPitchClass, scale, chordDegree, request.Mode));

                if (request.IncludeBass)
                {
                    AddBass(project, startTick, ticksPerMeasure, unitTicks, bassAnchor, tonicPitchClass, scale, chordDegree, variant);
                }

                AddHarmony(project, startTick, ticksPerMeasure, unitTicks, tonicPitchClass, scale, chordDegree, style, mood, variant);

                int[] rhythm = PickRhythm(style, mood, variant, role, measureUnits);
                List<int> degrees = role switch
                {
                    MeasureRole.Opening or MeasureRole.Return => motifA = BuildDegrees(rhythm.Length, previousDegree, chordDegree, style, mood, variant, role, random),
                    MeasureRole.Answer when motifA != null => VaryDegrees(motifA, chordDegree, style, mood, variant, role, random),
                    MeasureRole.Contrast or MeasureRole.Climax => motifB = BuildDegrees(rhythm.Length, previousDegree + 1, chordDegree, style, mood, variant, role, random),
                    MeasureRole.Cadence or MeasureRole.FinalCadence when motifB != null => BuildCadenceDegrees(motifB, chordDegree, style, mood, variant),
                    _ => BuildDegrees(rhythm.Length, previousDegree, chordDegree, style, mood, variant, role, random)
                };

                int cursor = startTick;
                for (int index = 0; index < rhythm.Length; index++)
                {
                    int durationTicks = rhythm[index] * unitTicks;
                    int degree = degrees[Math.Min(index, degrees.Count - 1)];
                    int midi = ClampToRange(GetScaleMidi(tonicPitchClass, melodyAnchor, scale, degree), style.MinMidi + variant.RangeFloorOffset, style.MaxMidi + variant.RangeCeilingOffset);
                    NoteEvent note = CreateNote(midi, cursor, durationTicks, ppq, 1, true);
                    ApplyMelodyExpression(note, index, rhythm.Length, role, mood, variant);
                    project.Notes.Add(note);
                    previousDegree = degree;
                    cursor += durationTicks;
                }
            }

            AddExpressionMarks(project, mood, variant, measures, ticksPerMeasure);
            project.Notes = project.Notes.OrderBy(note => note.StartTick).ThenBy(note => note.Voice).ThenByDescending(note => note.Midi).ToList();
            string summary = $"{variant.DisplayName} · {variant.Description} · {style.DisplayName} · {mood.DisplayName}";
            return new SmartComposeResult(project, string.Join("  |  ", chordNames), summary, seed);
        }

        private static int[] BuildProgression(StyleSpec style, int measures, VariantSpec variant, Random random)
        {
            int[][] bank = style.Progressions[variant.ProgressionBank % style.Progressions.Length];
            int[] phrase = bank[(variant.PhraseIndex + random.Next(bank.Length)) % bank.Length];
            var output = new List<int>(measures);

            while (output.Count < measures)
            {
                int remaining = measures - output.Count;
                output.AddRange(remaining >= phrase.Length ? phrase : phrase.Take(remaining));
            }

            output[^1] = style.FinalDegree;
            return output.ToArray();
        }

        private static int[] PickRhythm(StyleSpec style, MoodSpec mood, VariantSpec variant, MeasureRole role, int measureUnits)
        {
            int[][] bank = role switch
            {
                MeasureRole.Cadence or MeasureRole.FinalCadence => mood.CadencePatterns,
                MeasureRole.Contrast or MeasureRole.Climax => style.LiftPatterns,
                _ => style.CorePatterns
            };

            List<int[]> normalized = bank.Select(pattern => NormalizePattern(pattern, measureUnits)).OrderBy(pattern => pattern.Length).ToList();
            return variant.Texture switch
            {
                VariantTexture.Anthem => normalized[^1],
                VariantTexture.Atmosphere => normalized[0],
                _ => normalized[(normalized.Count - 1) / 2]
            };
        }

        private static int[] NormalizePattern(IReadOnlyList<int> pattern, int measureUnits)
        {
            int[] copy = pattern.ToArray();
            copy[^1] += measureUnits - copy.Sum();
            return copy;
        }

        private static List<int> BuildDegrees(int noteCount, int previousDegree, int chordDegree, StyleSpec style, MoodSpec mood, VariantSpec variant, MeasureRole role, Random random)
        {
            var result = new List<int>(noteCount);
            int chordRoot = chordDegree - 1;
            int current = previousDegree;

            for (int index = 0; index < noteCount; index++)
            {
                if (index == 0)
                {
                    current = chordRoot + Pick(role is MeasureRole.Contrast or MeasureRole.Climax ? mood.LiftChoices : mood.OpeningChoices, random) + variant.ContourBias;
                }
                else if (index == noteCount - 1)
                {
                    current = SmoothDegree(current, chordRoot + Pick(role is MeasureRole.Cadence or MeasureRole.FinalCadence ? mood.CadenceChoices : mood.AnswerChoices, random), mood.MaxLeap + Math.Abs(variant.ContourBias));
                }
                else
                {
                    int delta = Pick(role == MeasureRole.Climax ? mood.ClimaxMotion : role == MeasureRole.Contrast ? mood.LiftMotion : mood.CoreMotion, random) + variant.ContourBias;
                    if (mood.FavorStepwise)
                    {
                        delta = Math.Clamp(delta, -1, 1);
                    }

                    if (variant.Texture == VariantTexture.Atmosphere && random.NextDouble() < 0.45)
                    {
                        delta = random.NextDouble() < 0.7 ? 0 : 1;
                    }

                    if (variant.Texture == VariantTexture.Anthem && index % 2 == 1)
                    {
                        delta += 1;
                    }

                    current = SmoothDegree(current, current + delta, mood.MaxLeap + Math.Abs(variant.ContourBias));
                    bool strongBeat = index == 0 || index == noteCount / 2 || index == noteCount - 1;
                    if (index % 2 == 0 || strongBeat)
                    {
                        current = RefineDegreeForMood(current, chordRoot, mood, strongBeat);
                    }
                }

                result.Add(Math.Clamp(current, style.MinDegree, style.MaxDegree));
            }

            return result;
        }

        private static List<int> VaryDegrees(IReadOnlyList<int> source, int chordDegree, StyleSpec style, MoodSpec mood, VariantSpec variant, MeasureRole role, Random random)
        {
            int chordRoot = chordDegree - 1;
            var result = new List<int>(source.Count);

            for (int index = 0; index < source.Count; index++)
            {
                int degree = source[index] + (variant.Texture == VariantTexture.Anthem ? (index % 2 == 0 ? 1 : 0) : random.Next(-1, 2));
                if (mood.FavorStepwise && index < source.Count - 1)
                {
                    degree = SmoothDegree(source[index], degree, 1);
                }

                if (index % 2 == 0 || index == source.Count - 1)
                {
                    degree = RefineDegreeForMood(degree, chordRoot, mood, strongBeat: true);
                }

                if (index == source.Count - 1)
                {
                    degree = SmoothDegree(degree, chordRoot + Pick(role is MeasureRole.Cadence or MeasureRole.FinalCadence ? mood.CadenceChoices : mood.AnswerChoices, random), mood.MaxLeap + Math.Abs(variant.ContourBias));
                }

                result.Add(Math.Clamp(degree, style.MinDegree, style.MaxDegree));
            }

            return result;
        }

        private static List<int> BuildCadenceDegrees(IReadOnlyList<int> source, int chordDegree, StyleSpec style, MoodSpec mood, VariantSpec variant)
        {
            int chordRoot = chordDegree - 1;
            var result = source.ToList();
            if (result.Count == 0)
            {
                result.Add(chordRoot);
            }

            for (int index = Math.Max(0, result.Count - 2); index < result.Count; index++)
            {
                result[index] = index == result.Count - 1
                    ? Math.Clamp(chordRoot + (variant.Texture == VariantTexture.Anthem ? 4 : variant.Texture == VariantTexture.Atmosphere ? 2 : 0), style.MinDegree, style.MaxDegree)
                    : Math.Clamp(RefineDegreeForMood(result[index] - 1, chordRoot, mood, true), style.MinDegree, style.MaxDegree);
            }

            return result;
        }

        private static void AddBass(ScoreProject project, int startTick, int ticksPerMeasure, int unitTicks, int bassAnchor, int tonicPitchClass, IReadOnlyList<int> scale, int chordDegree, VariantSpec variant)
        {
            int root = ClosestMidiToTarget(GetScaleMidi(tonicPitchClass, bassAnchor, scale, chordDegree - 1), bassAnchor);
            int fifth = ClosestMidiToTarget(GetScaleMidi(tonicPitchClass, bassAnchor + 5, scale, chordDegree + 3), bassAnchor + 5);

            if (variant.Texture == VariantTexture.Atmosphere)
            {
                project.Notes.Add(CreateNote(root, startTick, ticksPerMeasure, 480, 2, false));
                return;
            }

            if (variant.Texture == VariantTexture.Anthem)
            {
                AddBassHit(project, root, startTick, unitTicks * 2, true);
                AddBassHit(project, root + 12, startTick + unitTicks * 2, unitTicks * 2, false);
                AddBassHit(project, fifth, startTick + unitTicks * 4, unitTicks * 2, true);
                AddBassHit(project, root + 12, startTick + unitTicks * 6, ticksPerMeasure - unitTicks * 6, false);
                return;
            }

            int half = ticksPerMeasure / 2;
            project.Notes.Add(CreateNote(root, startTick, half, 480, 2, false));
            project.Notes.Add(CreateNote(fifth, startTick + half, ticksPerMeasure - half, 480, 2, false));
        }

        private static void AddBassHit(ScoreProject project, int midi, int startTick, int durationTicks, bool accent)
        {
            if (durationTicks <= 0)
            {
                return;
            }

            NoteEvent note = CreateNote(midi, startTick, durationTicks, 480, 2, false);
            note.IsAccent = accent;
            note.IsStaccato = !accent;
            project.Notes.Add(note);
        }

        private static void AddHarmony(ScoreProject project, int startTick, int ticksPerMeasure, int unitTicks, int tonicPitchClass, IReadOnlyList<int> scale, int chordDegree, StyleSpec style, MoodSpec mood, VariantSpec variant)
        {
            int center = style.CenterMidi - 3 + variant.MelodyOffset;
            int[] voicing = BuildChordVoicing(tonicPitchClass, center, scale, chordDegree, mood, variant);

            if (variant.Texture == VariantTexture.Atmosphere)
            {
                foreach (int midi in voicing)
                {
                    project.Notes.Add(CreateNote(midi, startTick, ticksPerMeasure, 480, 3, true));
                }
                return;
            }

            if (variant.Texture == VariantTexture.Anthem)
            {
                for (int beat = 0; beat < 4; beat++)
                {
                    int hitStart = startTick + beat * unitTicks * 2;
                    int hitDuration = beat == 3 ? ticksPerMeasure - beat * unitTicks * 2 : unitTicks * 2;
                    foreach (int midi in voicing.Take(2))
                    {
                        NoteEvent note = CreateNote(midi + (beat % 2 == 0 ? 0 : 12), hitStart, hitDuration, 480, 3, true);
                        note.IsAccent = beat is 0 or 2;
                        note.IsStaccato = beat is 1 or 3;
                        project.Notes.Add(note);
                    }
                }
                return;
            }

            int half = ticksPerMeasure / 2;
            foreach (int hitStart in new[] { startTick, startTick + half })
            {
                foreach (int midi in voicing)
                {
                    int duration = hitStart == startTick ? half : ticksPerMeasure - half;
                    project.Notes.Add(CreateNote(midi, hitStart, duration, 480, 3, true));
                }
            }
        }

        private static int[] BuildChordVoicing(int tonicPitchClass, int center, IReadOnlyList<int> scale, int chordDegree, MoodSpec mood, VariantSpec variant)
        {
            int root = ClampToRange(GetScaleMidi(tonicPitchClass, center - 4, scale, chordDegree - 1), 48, 84);
            int third = ClampToRange(GetScaleMidi(tonicPitchClass, center, scale, chordDegree + 1), 52, 88);
            int fifth = ClampToRange(GetScaleMidi(tonicPitchClass, center + 5, scale, chordDegree + 3), 55, 92);

            int[] voicing = variant.Texture switch
            {
                VariantTexture.Atmosphere => new[] { root, fifth, third + 12 },
                VariantTexture.Anthem => new[] { root, fifth, root + 12 },
                _ => new[] { root, third, fifth }
            };

            Array.Sort(voicing);
            int minimumGap = mood.TensionLevel >= 2 ? 2 : 4;
            for (int index = 1; index < voicing.Length; index++)
            {
                while (voicing[index] - voicing[index - 1] < minimumGap)
                {
                    voicing[index] += 12;
                }
            }

            return voicing.Select(midi => ClampToRange(midi, 48, 92)).Distinct().ToArray();
        }

        private static void ApplyMelodyExpression(NoteEvent note, int index, int count, MeasureRole role, MoodSpec mood, VariantSpec variant)
        {
            if (index == 0 && role is MeasureRole.Opening or MeasureRole.Return or MeasureRole.Climax)
            {
                note.IsAccent = true;
            }

            if (variant.Texture == VariantTexture.Anthem && index % 2 == 1)
            {
                note.IsAccent = true;
            }

            if (variant.Texture == VariantTexture.Narrative && count > 3 && index == 1)
            {
                note.IsStaccato = true;
            }

            if (variant.Texture == VariantTexture.Atmosphere && role == MeasureRole.Climax && index == Math.Max(0, count - 2) && mood.TensionLevel > 0)
            {
                note.Ornament = NoteOrnament.Appoggiatura;
            }
        }

        private static void AddExpressionMarks(ScoreProject project, MoodSpec mood, VariantSpec variant, int measures, int ticksPerMeasure)
        {
            int totalTicks = measures * ticksPerMeasure;
            project.ExpressionMarks.Add(new ExpressionMark { Code = variant.Texture == VariantTexture.Anthem ? "f" : variant.Texture == VariantTexture.Atmosphere ? "p" : "mf", StartTick = 0, StaffStepOffset = 17f });
            project.ExpressionMarks.Add(new ExpressionMark { Code = variant.Texture == VariantTexture.Anthem ? "cresc" : "cresc_text", StartTick = totalTicks / 3, StaffStepOffset = 17f, SpanBeats = 3.5f });
            project.ExpressionMarks.Add(new ExpressionMark { Code = "rit", StartTick = Math.Max(0, totalTicks - ticksPerMeasure * 2), StaffStepOffset = 17f, SpanBeats = 2.4f });
            project.ExpressionMarks.Add(new ExpressionMark { Code = variant.Texture == VariantTexture.Atmosphere ? "pp" : "mp", StartTick = Math.Max(0, totalTicks - ticksPerMeasure), StaffStepOffset = 17f });

            if (!mood.SustainPedal && !variant.ForcePedal)
            {
                return;
            }

            for (int measure = 0; measure < measures; measure += 2)
            {
                int start = measure * ticksPerMeasure;
                int end = Math.Min(totalTicks, start + ticksPerMeasure * 2);
                project.ExpressionMarks.Add(new ExpressionMark { Code = "ped", StartTick = start, StaffStepOffset = 30f });
                project.ExpressionMarks.Add(new ExpressionMark { Code = "ped_release", StartTick = end, StaffStepOffset = 30f });
            }
        }

        private static NoteEvent CreateNote(int midi, int startTick, int durationTicks, int ppq, int voice, bool preferTrebleStaff)
        {
            DurationSpec notation = ResolveNotationDuration(durationTicks, ppq);
            return new NoteEvent
            {
                Midi = midi,
                StartTick = startTick,
                DurationTicks = durationTicks,
                BaseDurationTicks = notation.BaseTicks,
                AugmentationDots = notation.Dots,
                Voice = voice,
                PreferTrebleStaff = preferTrebleStaff
            };
        }

        private static DurationSpec ResolveNotationDuration(int durationTicks, int ppq)
        {
            int scale = Math.Max(1, ppq / 480);
            foreach (DurationSpec candidate in DurationCandidates)
            {
                if (candidate.TotalTicks * scale == durationTicks)
                {
                    return new DurationSpec(candidate.BaseTicks * scale, durationTicks, candidate.Dots);
                }
            }

            return new DurationSpec(durationTicks, durationTicks, 0);
        }

        private static int GetScaleMidi(int tonicPitchClass, int tonicMidiNearTarget, IReadOnlyList<int> scale, int degreeIndex)
        {
            int octaveOffset = FloorDiv(degreeIndex, scale.Count);
            int scaleIndex = Mod(degreeIndex, scale.Count);
            int pitchClass = Mod(tonicPitchClass + scale[scaleIndex], 12);
            int candidate = tonicMidiNearTarget + scale[scaleIndex] + octaveOffset * 12;
            while (Mod(candidate, 12) != pitchClass)
            {
                candidate++;
            }

            return candidate;
        }

        private static string BuildChordName(int tonicPitchClass, IReadOnlyList<int> scale, int degree, KeyMode mode)
        {
            int index = Mod(degree - 1, scale.Count);
            int pitchClass = Mod(tonicPitchClass + scale[index], 12);
            string root = pitchClass switch
            {
                0 => "C",
                1 => "C#",
                2 => "D",
                3 => "Eb",
                4 => "E",
                5 => "F",
                6 => "F#",
                7 => "G",
                8 => "Ab",
                9 => "A",
                10 => "Bb",
                11 => "B",
                _ => "C"
            };

            bool isMinor = mode == KeyMode.Minor ? index is 0 or 3 or 4 : index is 1 or 2 or 5;
            return isMinor ? $"{root}m" : root;
        }

        private static int ClosestMidiToTarget(int pitchClass, int targetMidi)
        {
            int baseMidi = targetMidi - Mod(targetMidi - pitchClass, 12);
            int below = baseMidi;
            int above = baseMidi + 12;
            return Math.Abs(below - targetMidi) <= Math.Abs(above - targetMidi) ? below : above;
        }

        private static int ClampToRange(int midi, int minMidi, int maxMidi)
        {
            while (midi < minMidi)
            {
                midi += 12;
            }

            while (midi > maxMidi)
            {
                midi -= 12;
            }

            return Math.Clamp(midi, minMidi, maxMidi);
        }

        private static int ResolveMeasureUnits(TimeSignature timeSignature)
        {
            return (timeSignature.Numerator, timeSignature.Denominator) switch
            {
                (3, 4) => 6,
                (6, 8) => 6,
                _ => 8
            };
        }

        private static MeasureRole ResolveRole(int measureIndex, int totalMeasures)
        {
            int slot = measureIndex % 4;
            bool finalPhrase = measureIndex >= Math.Max(0, totalMeasures - 4);
            return slot switch
            {
                0 => finalPhrase && measureIndex > 0 ? MeasureRole.Return : MeasureRole.Opening,
                1 => MeasureRole.Answer,
                2 => finalPhrase ? MeasureRole.Climax : MeasureRole.Contrast,
                _ => finalPhrase ? MeasureRole.FinalCadence : MeasureRole.Cadence
            };
        }

        private static int Pick(IReadOnlyList<int> values, Random random)
        {
            return values[random.Next(values.Count)];
        }

        private static int RefineDegreeForMood(int degree, int chordRoot, MoodSpec mood, bool strongBeat)
        {
            if (mood.TensionLevel >= 2)
            {
                return strongBeat ? SnapNearChord(degree, chordRoot) : degree;
            }

            int[] stableTones = mood.TensionLevel == 0
                ? new[] { chordRoot, chordRoot + 2, chordRoot + 4 }
                : new[] { chordRoot, chordRoot + 2, chordRoot + 4, chordRoot + 5 };

            return stableTones.OrderBy(value => Math.Abs(value - degree)).First();
        }

        private static int SnapNearChord(int degree, int chordRoot)
        {
            return new[] { chordRoot, chordRoot + 2, chordRoot + 4, chordRoot + 7 }
                .OrderBy(value => Math.Abs(value - degree))
                .First();
        }

        private static int SmoothDegree(int previous, int target, int maxLeap)
        {
            int delta = target - previous;
            if (delta > maxLeap)
            {
                return previous + maxLeap;
            }

            if (delta < -maxLeap)
            {
                return previous - maxLeap;
            }

            return target;
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static int Mod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static MoodSpec ResolveMood(string? moodId)
        {
            string mood = moodId?.Trim().ToLowerInvariant() ?? "calm";
            return mood switch
            {
                "positive" => new MoodSpec("积极", 10, 102, 150, 3, 2, new[] { 0, 2, 4 }, new[] { 2, 4, 5 }, new[] { 4, 5, 6 }, new[] { 0, 2, 4 }, new[] { 1, 1, 2, 0 }, new[] { 2, 1, 1, -1 }, new[] { new[] { 2, 2, 2, 2 }, new[] { 1, 1, 2, 2, 2 } }, false, 0, false),
                "sad" => new MoodSpec("伤感", -14, 58, 88, -4, 2, new[] { 4, 2, 0 }, new[] { 2, 0, -1 }, new[] { 4, 2, 0 }, new[] { 0, 2 }, new[] { -1, 0, 0, -1 }, new[] { 1, -1, -1 }, new[] { new[] { 4, 4 }, new[] { 2, 2, 4 } }, false, 0, true),
                "sleep" => new MoodSpec("助眠", -20, 50, 72, -5, 0, new[] { 0, 2 }, new[] { 0, 1 }, new[] { 2, 4 }, new[] { 0, 2 }, new[] { 0, 1, 0 }, new[] { 1, 0, -1 }, new[] { new[] { 6, 2 }, new[] { 8 } }, true, 0, true),
                "hopeful" => new MoodSpec("希望", 8, 92, 136, 2, 2, new[] { 0, 2, 4 }, new[] { 2, 4, 5 }, new[] { 4, 5, 6 }, new[] { 2, 4 }, new[] { 1, 2, 0, 1 }, new[] { 2, 1, 0 }, new[] { new[] { 2, 2, 4 }, new[] { 4, 4 } }, false, 0, false),
                "nostalgic" => new MoodSpec("怀旧", -6, 66, 104, -2, 2, new[] { 2, 4, 5 }, new[] { 2, 0, -1 }, new[] { 4, 5, 2 }, new[] { 0, 2 }, new[] { -1, 0, 1, -1 }, new[] { 1, -1, 1 }, new[] { new[] { 4, 4 }, new[] { 6, 2 } }, false, 0, true),
                "dreamy" => new MoodSpec("梦幻", -10, 68, 100, 1, 2, new[] { 0, 2, 4 }, new[] { 2, 4 }, new[] { 4, 5, 6 }, new[] { 2, 4 }, new[] { 1, 0, 1, -1 }, new[] { 1, 1, -1 }, new[] { new[] { 6, 2 }, new[] { 8 } }, true, 1, true),
                "tense" => new MoodSpec("紧张", 14, 104, 154, 2, 4, new[] { 4, 5, 6 }, new[] { 2, 4, 6 }, new[] { 6, 5, 4 }, new[] { 2, 4 }, new[] { 2, -1, 2, -2 }, new[] { 2, 1, -2, 3 }, new[] { new[] { 2, 2, 2, 2 }, new[] { 1, 1, 2, 2, 2 } }, false, 2, false),
                _ => new MoodSpec("平静", -6, 70, 102, -1, 2, new[] { 0, 2, 4 }, new[] { 2, 4, 5 }, new[] { 2, 4, 5 }, new[] { 0, 2 }, new[] { 0, 1, 0, -1 }, new[] { 1, 0, -1, 1 }, new[] { new[] { 4, 4 }, new[] { 2, 2, 4 } }, false, 0, true)
            };
        }

        private static StyleSpec ResolveStyle(string? styleId, TimeSignature timeSignature)
        {
            string style = styleId?.Trim().ToLowerInvariant() ?? "pop";
            bool compound = ResolveMeasureUnits(timeSignature) == 6;
            int[][] calmCore = compound ? new[] { new[] { 3, 3 }, new[] { 2, 2, 2 }, new[] { 4, 2 } } : new[] { new[] { 4, 4 }, new[] { 2, 2, 4 }, new[] { 4, 2, 2 } };
            int[][] driveCore = compound ? new[] { new[] { 2, 2, 2 }, new[] { 1, 1, 2, 2 }, new[] { 1, 1, 1, 1, 2 } } : new[] { new[] { 2, 2, 2, 2 }, new[] { 1, 1, 2, 2, 2 }, new[] { 2, 1, 1, 2, 2 } };
            int[][] lift = compound ? new[] { new[] { 2, 2, 2 }, new[] { 1, 1, 2, 2 } } : new[] { new[] { 2, 2, 2, 2 }, new[] { 2, 1, 1, 2, 2 } };

            return style switch
            {
                "folk" => new StyleSpec("民谣", 96, 58, 79, -1, 10, 1, new[] { new[] { new[] { 1, 4, 1, 5 }, new[] { 6, 4, 1, 5 }, new[] { 1, 5, 4, 1 } }, new[] { new[] { 1, 4, 6, 5 }, new[] { 6, 1, 4, 5 }, new[] { 1, 5, 6, 4 } }, new[] { new[] { 1, 1, 4, 5 }, new[] { 6, 4, 5, 1 }, new[] { 1, 4, 5, 1 } } }, driveCore, lift),
                "ambient" => new StyleSpec("氛围", 82, 60, 84, -1, 11, 1, new[] { new[] { new[] { 1, 6, 4, 1 }, new[] { 4, 1, 6, 5 }, new[] { 1, 5, 4, 1 } }, new[] { new[] { 6, 1, 4, 5 }, new[] { 1, 4, 6, 1 }, new[] { 4, 5, 1, 1 } }, new[] { new[] { 1, 1, 6, 4 }, new[] { 4, 1, 5, 1 }, new[] { 6, 4, 1, 1 } } }, calmCore, lift),
                "dance" => new StyleSpec("舞曲", 124, 61, 82, 0, 12, 5, new[] { new[] { new[] { 1, 5, 6, 4 }, new[] { 6, 4, 1, 5 }, new[] { 1, 1, 6, 4 } }, new[] { new[] { 1, 6, 4, 5 }, new[] { 6, 5, 1, 4 }, new[] { 1, 5, 4, 6 } }, new[] { new[] { 1, 5, 6, 5 }, new[] { 6, 4, 5, 1 }, new[] { 1, 4, 6, 5 } } }, driveCore, lift),
                _ => new StyleSpec("流行", 108, 60, 80, -1, 11, 1, new[] { new[] { new[] { 1, 5, 6, 4 }, new[] { 1, 6, 4, 5 }, new[] { 6, 4, 1, 5 } }, new[] { new[] { 1, 4, 6, 5 }, new[] { 6, 1, 5, 4 }, new[] { 1, 5, 4, 1 } }, new[] { new[] { 1, 1, 6, 4 }, new[] { 6, 4, 5, 1 }, new[] { 1, 4, 5, 1 } } }, driveCore, lift)
            };
        }

        private static VariantSpec ResolveVariant(int index)
        {
            return (index % 3) switch
            {
                1 => new VariantSpec("副歌型", "更高更密，推动感更强", VariantTexture.Anthem, 1, 1, 4, 2, 8, 0, 4, 1, false),
                2 => new VariantSpec("氛围型", "更长音、更留白、更悬浮", VariantTexture.Atmosphere, 2, 2, 6, -3, -10, -2, 6, -1, true),
                _ => new VariantSpec("主歌型", "句法更顺，低音更稳", VariantTexture.Narrative, 0, 0, 0, 0, 0, 0, 0, 0, false)
            };
        }

        private enum MeasureRole { Opening, Answer, Contrast, Cadence, Return, Climax, FinalCadence }
        private enum VariantTexture { Narrative, Anthem, Atmosphere }
        private sealed record StyleSpec(string DisplayName, int DefaultTempo, int MinMidi, int MaxMidi, int MinDegree, int MaxDegree, int FinalDegree, int[][][] Progressions, int[][] CorePatterns, int[][] LiftPatterns) { public int CenterMidi => (MinMidi + MaxMidi) / 2; }
        private sealed record MoodSpec(string DisplayName, int TempoOffset, int MinTempo, int MaxTempo, int RangeOffset, int StartDegree, int[] OpeningChoices, int[] AnswerChoices, int[] LiftChoices, int[] CadenceChoices, int[] CoreMotion, int[] LiftMotion, int[][] CadencePatterns, bool SustainPedal, int TensionLevel, bool FavorStepwise) { public int MaxLeap => SustainPedal ? 2 : 3; public int[] ClimaxMotion => SustainPedal ? new[] { 1, 0, 2, -1 } : new[] { 2, 1, -1, 3, -2 }; }
        private sealed record VariantSpec(string DisplayName, string Description, VariantTexture Texture, int ProgressionBank, int PhraseIndex, int MelodyOffset, int BassOffset, int TempoOffset, int RangeFloorOffset, int RangeCeilingOffset, int ContourBias, bool ForcePedal);
        private sealed record DurationSpec(int BaseTicks, int TotalTicks, int Dots);
    }

    public sealed class SmartComposeRequest
    {
        public string Title { get; set; } = "智能创作";
        public int Bpm { get; set; } = 112;
        public int Measures { get; set; } = 8;
        public int KeyFifths { get; set; }
        public KeyMode Mode { get; set; } = KeyMode.Major;
        public TimeSignature TimeSignature { get; set; } = new(4, 4);
        public string StyleId { get; set; } = "pop";
        public string MoodId { get; set; } = "calm";
        public string LengthId { get; set; } = "short";
        public bool IncludeBass { get; set; } = true;
        public int Seed { get; set; }
    }

    public sealed class SmartComposeResult
    {
        public SmartComposeResult(ScoreProject project, string chordProgression, string summary, int seed)
        {
            Project = project;
            ChordProgression = chordProgression;
            Summary = summary;
            Seed = seed;
        }

        public ScoreProject Project { get; }
        public string ChordProgression { get; }
        public string Summary { get; }
        public int Seed { get; }
    }
}

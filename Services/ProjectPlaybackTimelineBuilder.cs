using System;
using System.Collections.Generic;
using System.Linq;
using MusicBox.Models;

namespace MusicBox.Services
{
    internal static class ProjectPlaybackTimelineBuilder
    {
        public static IReadOnlyList<PlaybackEvent> BuildEvents(ScoreProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            var events = new List<PlaybackEvent>();
            foreach (PlaybackNoteSpan span in BuildNoteSpans(project))
            {
                events.Add(new PlaybackEvent(span.StartTick, 1, span.Midi, true, span.Velocity));
                events.Add(new PlaybackEvent(span.SustainedEndTick, 0, span.Midi, false, 0));
            }

            events.Sort(static (a, b) =>
            {
                int tick = a.Tick.CompareTo(b.Tick);
                return tick != 0 ? tick : a.Order.CompareTo(b.Order);
            });
            return events;
        }

        public static IReadOnlyList<PlaybackNoteSpan> BuildNoteSpans(ScoreProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            List<PedalRange> pedalRanges = BuildPedalRanges(project);
            var spans = new List<PlaybackNoteSpan>();

            foreach (NoteEvent note in project.Notes
                .Where(static n => n != null && !n.IsRest)
                .OrderBy(static n => n.StartTick)
                .ThenBy(static n => n.Voice)
                .ThenByDescending(static n => n.Midi))
            {
                AddPlaybackNote(spans, note, project, pedalRanges);
            }

            spans.Sort(static (a, b) =>
            {
                int start = a.StartTick.CompareTo(b.StartTick);
                return start != 0 ? start : a.Midi.CompareTo(b.Midi);
            });
            return spans;
        }

        private static List<PedalRange> BuildPedalRanges(ScoreProject project)
        {
            var result = new List<PedalRange>();
            int? startTick = null;

            foreach (ExpressionMark mark in project.ExpressionMarks.OrderBy(static m => m.StartTick))
            {
                string code = NormalizeCode(mark.Code);
                if (code is "ped" or "ped_line")
                {
                    startTick ??= Math.Max(0, mark.StartTick);
                }
                else if (code == "ped_release" && startTick.HasValue)
                {
                    result.Add(new PedalRange(startTick.Value, Math.Max(startTick.Value + 1, mark.StartTick)));
                    startTick = null;
                }
            }

            if (startTick.HasValue)
            {
                int lastTick = project.Notes.Count == 0
                    ? project.TimeSignature.TicksPerMeasure(project.Ppq)
                    : project.Notes.Max(static n => n.StartTick + n.DurationTicks);
                result.Add(new PedalRange(startTick.Value, lastTick));
            }

            return result;
        }

        private static void AddPlaybackNote(List<PlaybackNoteSpan> spans, NoteEvent note, ScoreProject project, IReadOnlyList<PedalRange> pedalRanges)
        {
            int ticksPerBeat = Math.Max(1, project.TimeSignature.TicksPerBeat(project.Ppq));
            int start = Math.Max(0, note.StartTick);
            int duration = Math.Max(1, note.DurationTicks);
            int midi = Math.Clamp(GetEffectiveMidi(note, GetEffectiveKeySignatureFifthsAtTick(project, note.StartTick)), 0, 127);
            int velocity = GetVelocity(project, note.StartTick);

            if (note.IsAccent)
            {
                velocity += 42;
            }

            if (note.IsStaccatissimo)
            {
                duration = Math.Max(1, (int)Math.Round(duration * 0.08));
                velocity += 14;
            }
            else if (note.IsStaccato)
            {
                duration = Math.Max(1, (int)Math.Round(duration * 0.18));
                velocity += 8;
            }

            switch (note.Ornament)
            {
                case NoteOrnament.Trill:
                    AddTrill(spans, start, duration, midi, velocity, ticksPerBeat);
                    return;
                case NoteOrnament.Appoggiatura:
                    AddGrace(spans, start, duration, midi + 2, midi, velocity, ticksPerBeat / 4);
                    return;
                case NoteOrnament.Acciaccatura:
                    AddGrace(spans, Math.Max(0, start - ticksPerBeat / 8), duration, midi + 1, midi, velocity, ticksPerBeat / 8);
                    return;
                case NoteOrnament.TremoloSingle:
                case NoteOrnament.TremoloDouble:
                    AddTremolo(spans, start, duration, midi, velocity, note.Ornament == NoteOrnament.TremoloDouble ? ticksPerBeat / 8 : ticksPerBeat / 4);
                    return;
            }

            int keyUp = Math.Max(start + 1, start + duration);
            int sustainedEnd = ExtendWithPedal(start, keyUp, pedalRanges);
            spans.Add(new PlaybackNoteSpan(start, keyUp, sustainedEnd, midi, Math.Clamp(velocity, 1, 127)));
        }

        private static void AddTrill(List<PlaybackNoteSpan> spans, int start, int duration, int midi, int velocity, int ticksPerBeat)
        {
            int alt = Math.Clamp(midi + 2, 0, 127);
            int unit = Math.Max(1, ticksPerBeat / 6);
            int cursor = start;
            bool useAlt = false;
            while (cursor < start + duration)
            {
                int span = Math.Min(unit, start + duration - cursor);
                int end = cursor + span;
                spans.Add(new PlaybackNoteSpan(cursor, end, end, useAlt ? alt : midi, Math.Clamp(velocity - 8, 1, 127)));
                cursor += span;
                useAlt = !useAlt;
            }
        }

        private static void AddGrace(List<PlaybackNoteSpan> spans, int start, int duration, int graceMidi, int midi, int velocity, int graceTicks)
        {
            int safeGrace = Math.Clamp(graceTicks, 1, Math.Max(1, duration / 3));
            int graceEnd = start + safeGrace;
            int mainEnd = start + duration;
            spans.Add(new PlaybackNoteSpan(start, graceEnd, graceEnd, Math.Clamp(graceMidi, 0, 127), Math.Clamp(velocity - 12, 1, 127)));
            spans.Add(new PlaybackNoteSpan(graceEnd, mainEnd, mainEnd, Math.Clamp(midi, 0, 127), Math.Clamp(velocity, 1, 127)));
        }

        private static void AddTremolo(List<PlaybackNoteSpan> spans, int start, int duration, int midi, int velocity, int unit)
        {
            int cursor = start;
            int safeUnit = Math.Max(1, unit);
            while (cursor < start + duration)
            {
                int span = Math.Min(safeUnit, start + duration - cursor);
                int end = cursor + span;
                spans.Add(new PlaybackNoteSpan(cursor, end, end, midi, Math.Clamp(velocity + 6, 1, 127)));
                cursor += span;
            }
        }

        private static int GetVelocity(ScoreProject project, int tick)
        {
            int safeTick = Math.Max(0, tick);
            int velocity = 96;
            foreach (ExpressionMark mark in project.ExpressionMarks.OrderBy(static m => m.StartTick))
            {
                int markTick = Math.Max(0, mark.StartTick);
                if (markTick > safeTick)
                {
                    break;
                }

                velocity = NormalizeCode(mark.Code) switch
                {
                    "ppp" => 46,
                    "pp" => 58,
                    "p" => 70,
                    "mp" => 82,
                    "mf" => 96,
                    "f" => 110,
                    "ff" => 122,
                    "fff" => 127,
                    "sf" => 126,
                    _ => velocity
                };
            }

            velocity += GetHairpinVelocityDeltaAtTick(project, safeTick);
            return Math.Clamp(velocity, 24, 127);
        }

        private static int GetHairpinVelocityDeltaAtTick(ScoreProject project, int sourceTick)
        {
            int safeTick = Math.Max(0, sourceTick);
            int ticksPerBeat = Math.Max(1, project.TimeSignature.TicksPerBeat(project.Ppq));
            double delta = 0d;
            foreach (ExpressionMark mark in project.ExpressionMarks)
            {
                string code = NormalizeCode(mark.Code);
                if (code is not ("cresc" or "dim" or "cresc_text" or "dim_text"))
                {
                    continue;
                }

                int start = Math.Max(0, mark.StartTick);
                int spanTicks = Math.Max(1, (int)Math.Round(Math.Max(0.2f, mark.SpanBeats) * ticksPerBeat));
                int end = Math.Max(start + 1, start + spanTicks);
                if (safeTick < start || safeTick > end)
                {
                    continue;
                }

                double progress = (safeTick - start) / (double)Math.Max(1, end - start);
                double amount = code is "cresc" or "cresc_text" ? 18d : -18d;
                if (code is "cresc_text" or "dim_text")
                {
                    amount *= 0.72d;
                }

                delta += amount * progress;
            }

            return (int)Math.Round(delta);
        }

        private static int ExtendWithPedal(int start, int end, IReadOnlyList<PedalRange> pedalRanges)
        {
            int extended = Math.Max(start + 1, end);
            foreach (PedalRange range in pedalRanges)
            {
                if (IsPedalCapturingKeyRelease(end, range))
                {
                    extended = Math.Max(extended, range.EndTick);
                }
            }

            return extended;
        }

        private static bool IsPedalCapturingKeyRelease(int keyUpTick, PedalRange range)
        {
            int safeKeyUp = Math.Max(0, keyUpTick);
            return safeKeyUp >= range.StartTick && safeKeyUp < range.EndTick;
        }

        private static int GetAccidentalSemitoneOffset(NoteAccidental accidental)
        {
            return accidental switch
            {
                NoteAccidental.DoubleSharp => 2,
                NoteAccidental.Sharp => 1,
                NoteAccidental.Flat => -1,
                NoteAccidental.DoubleFlat => -2,
                _ => 0
            };
        }

        private static int QuantizeToNaturalMidi(int midi)
        {
            int clamped = Math.Clamp(midi, 0, 127);
            int octaveBlock = clamped / 12;
            int pitchClass = clamped % 12;
            int[] naturalPitchClasses = { 0, 2, 4, 5, 7, 9, 11 };

            int best = naturalPitchClasses[0];
            int bestDiff = Math.Abs(pitchClass - best);
            for (int i = 1; i < naturalPitchClasses.Length; i++)
            {
                int candidate = naturalPitchClasses[i];
                int diff = Math.Abs(pitchClass - candidate);
                if (diff < bestDiff || (diff == bestDiff && candidate < best))
                {
                    best = candidate;
                    bestDiff = diff;
                }
            }

            return Math.Clamp(octaveBlock * 12 + best, 0, 127);
        }

        private static int GetKeySignatureSemitoneOffset(int naturalMidi, int fifths)
        {
            if (fifths == 0) return 0;

            int pitchClass = QuantizeToNaturalMidi(naturalMidi) % 12;
            if (fifths > 0)
            {
                int[] sharpOrder = { 5, 0, 7, 2, 9, 4, 11 };
                int count = Math.Min(fifths, sharpOrder.Length);
                for (int i = 0; i < count; i++)
                {
                    if (pitchClass == sharpOrder[i]) return 1;
                }
            }
            else
            {
                int[] flatOrder = { 11, 4, 9, 2, 7, 0, 5 };
                int count = Math.Min(Math.Abs(fifths), flatOrder.Length);
                for (int i = 0; i < count; i++)
                {
                    if (pitchClass == flatOrder[i]) return -1;
                }
            }

            return 0;
        }

        private static int GetEffectiveMidi(NoteEvent note, int keySignatureFifths)
        {
            int naturalMidi = QuantizeToNaturalMidi(note.Midi - GetAccidentalSemitoneOffset(note.Accidental));
            int offset = note.Accidental switch
            {
                NoteAccidental.DoubleSharp => 2,
                NoteAccidental.Sharp => 1,
                NoteAccidental.Flat => -1,
                NoteAccidental.DoubleFlat => -2,
                NoteAccidental.Natural => 0,
                _ => GetKeySignatureSemitoneOffset(naturalMidi, keySignatureFifths)
            };
            return Math.Clamp(naturalMidi + offset, 0, 127);
        }

        private static int GetEffectiveKeySignatureFifthsAtTick(ScoreProject project, int tick)
        {
            int safeTick = Math.Max(0, tick);
            int active = Math.Clamp(project.KeySignature.Fifths, -7, 7);
            if (project.KeySignatureChanges == null || project.KeySignatureChanges.Count == 0)
            {
                return active;
            }

            foreach (KeySignatureChange change in project.KeySignatureChanges
                .Where(static c => c != null && c.Tick > 0)
                .OrderBy(static c => c.Tick))
            {
                if (change.Tick <= safeTick)
                {
                    active = Math.Clamp(change.Fifths, -7, 7);
                }
                else
                {
                    break;
                }
            }

            return active;
        }

        private static string NormalizeCode(string? code)
        {
            return (code ?? string.Empty).Trim().ToLowerInvariant();
        }

        private readonly record struct PedalRange(int StartTick, int EndTick);
    }

    internal readonly record struct PlaybackEvent(int Tick, int Order, int Midi, bool IsOn, int Velocity);
    internal readonly record struct PlaybackNoteSpan(int StartTick, int KeyUpTick, int SustainedEndTick, int Midi, int Velocity);
}

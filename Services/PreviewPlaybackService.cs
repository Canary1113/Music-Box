using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBox.Models;
using Windows.Devices.Midi;

namespace MusicBox.Services
{
    public sealed class PreviewPlaybackService : IDisposable
    {
        private MidiSynthesizer? _synth;
        private CancellationTokenSource? _playbackCts;
        private readonly HashSet<int> _activeNotes = new();

        public bool IsPlaying { get; private set; }
        public int ActiveIndex { get; private set; } = -1;

        public async Task TogglePlayAsync(int index, ScoreProject project)
        {
            if (IsPlaying && ActiveIndex == index)
            {
                Stop();
                return;
            }

            Stop();
            _playbackCts = new CancellationTokenSource();
            IsPlaying = true;
            ActiveIndex = index;

            try
            {
                _synth ??= await MidiSynthesizer.CreateAsync();
                await PlayInternalAsync(project, _playbackCts.Token);
            }
            catch
            {
            }
            finally
            {
                Stop();
            }
        }

        public void Stop()
        {
            _playbackCts?.Cancel();
            _playbackCts?.Dispose();
            _playbackCts = null;
            StopAllNotes();
            IsPlaying = false;
            ActiveIndex = -1;
        }

        private async Task PlayInternalAsync(ScoreProject project, CancellationToken cancellationToken)
        {
            List<PlaybackEvent> events = BuildEvents(project);
            if (events.Count == 0)
            {
                return;
            }

            double msPerTick = 60000d / Math.Max(20, project.Bpm) / Math.Max(1, project.Ppq);
            int currentTick = 0;

            foreach (PlaybackEvent evt in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int waitTicks = Math.Max(0, evt.Tick - currentTick);
                int waitMs = (int)Math.Round(waitTicks * msPerTick);
                if (waitMs > 0)
                {
                    await Task.Delay(waitMs, cancellationToken);
                }

                if (evt.IsOn)
                {
                    SendNoteOn(evt.Midi, evt.Velocity);
                }
                else
                {
                    SendNoteOff(evt.Midi);
                }

                currentTick = evt.Tick;
            }
        }

        private static List<PlaybackEvent> BuildEvents(ScoreProject project)
        {
            List<PedalRange> pedalRanges = BuildPedalRanges(project);
            var events = new List<PlaybackEvent>();

            foreach (NoteEvent note in project.Notes
                .Where(n => n != null && !n.IsRest)
                .OrderBy(n => n.StartTick)
                .ThenBy(n => n.Voice)
                .ThenByDescending(n => n.Midi))
            {
                AddPlaybackNote(events, note, project, pedalRanges);
            }

            events.Sort((a, b) =>
            {
                int tick = a.Tick.CompareTo(b.Tick);
                return tick != 0 ? tick : a.Order.CompareTo(b.Order);
            });

            return events;
        }

        private static List<PedalRange> BuildPedalRanges(ScoreProject project)
        {
            var result = new List<PedalRange>();
            int? startTick = null;

            foreach (ExpressionMark mark in project.ExpressionMarks.OrderBy(m => m.StartTick))
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
                int lastTick = project.Notes.Count == 0 ? project.TimeSignature.TicksPerMeasure(project.Ppq) : project.Notes.Max(n => n.StartTick + n.DurationTicks);
                result.Add(new PedalRange(startTick.Value, lastTick));
            }

            return result;
        }

        private static void AddPlaybackNote(List<PlaybackEvent> events, NoteEvent note, ScoreProject project, IReadOnlyList<PedalRange> pedalRanges)
        {
            int ticksPerBeat = Math.Max(1, project.TimeSignature.TicksPerBeat(project.Ppq));
            int start = Math.Max(0, note.StartTick);
            int duration = Math.Max(1, note.DurationTicks);
            int midi = Math.Clamp(note.Midi, 0, 127);
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
                    AddTrill(events, start, duration, midi, velocity, ticksPerBeat);
                    return;
                case NoteOrnament.Appoggiatura:
                    AddGrace(events, start, duration, midi + 2, midi, velocity, ticksPerBeat / 4);
                    return;
                case NoteOrnament.Acciaccatura:
                    AddGrace(events, Math.Max(0, start - ticksPerBeat / 8), duration, midi + 1, midi, velocity, ticksPerBeat / 8);
                    return;
                case NoteOrnament.TremoloSingle:
                case NoteOrnament.TremoloDouble:
                    AddTremolo(events, start, duration, midi, velocity, note.Ornament == NoteOrnament.TremoloDouble ? ticksPerBeat / 8 : ticksPerBeat / 4);
                    return;
            }

            int end = ExtendWithPedal(start, start + duration, pedalRanges);
            events.Add(new PlaybackEvent(start, 1, midi, true, Math.Clamp(velocity, 1, 127)));
            events.Add(new PlaybackEvent(end, 0, midi, false, 0));
        }

        private static void AddTrill(List<PlaybackEvent> events, int start, int duration, int midi, int velocity, int ticksPerBeat)
        {
            int alt = Math.Clamp(midi + 2, 0, 127);
            int unit = Math.Max(1, ticksPerBeat / 6);
            int cursor = start;
            bool useAlt = false;
            while (cursor < start + duration)
            {
                int span = Math.Min(unit, start + duration - cursor);
                int pitch = useAlt ? alt : midi;
                events.Add(new PlaybackEvent(cursor, 1, pitch, true, Math.Clamp(velocity - 8, 1, 127)));
                events.Add(new PlaybackEvent(cursor + span, 0, pitch, false, 0));
                cursor += span;
                useAlt = !useAlt;
            }
        }

        private static void AddGrace(List<PlaybackEvent> events, int start, int duration, int graceMidi, int midi, int velocity, int graceTicks)
        {
            int safeGrace = Math.Clamp(graceTicks, 1, Math.Max(1, duration / 3));
            events.Add(new PlaybackEvent(start, 1, Math.Clamp(graceMidi, 0, 127), true, Math.Clamp(velocity - 12, 1, 127)));
            events.Add(new PlaybackEvent(start + safeGrace, 0, Math.Clamp(graceMidi, 0, 127), false, 0));
            events.Add(new PlaybackEvent(start + safeGrace, 1, Math.Clamp(midi, 0, 127), true, Math.Clamp(velocity, 1, 127)));
            events.Add(new PlaybackEvent(start + duration, 0, Math.Clamp(midi, 0, 127), false, 0));
        }

        private static void AddTremolo(List<PlaybackEvent> events, int start, int duration, int midi, int velocity, int unit)
        {
            int cursor = start;
            int safeUnit = Math.Max(1, unit);
            while (cursor < start + duration)
            {
                int span = Math.Min(safeUnit, start + duration - cursor);
                events.Add(new PlaybackEvent(cursor, 1, midi, true, Math.Clamp(velocity + 6, 1, 127)));
                events.Add(new PlaybackEvent(cursor + span, 0, midi, false, 0));
                cursor += span;
            }
        }

        private static int GetVelocity(ScoreProject project, int tick)
        {
            int safeTick = Math.Max(0, tick);
            int velocity = 96;
            foreach (ExpressionMark mark in project.ExpressionMarks.OrderBy(m => m.StartTick))
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
                if (start >= range.StartTick && start < range.EndTick)
                {
                    extended = Math.Max(extended, range.EndTick);
                }
            }

            return extended;
        }

        private static string NormalizeCode(string? code)
        {
            return (code ?? string.Empty).Trim().ToLowerInvariant();
        }

        private void SendNoteOn(int midi, int velocity)
        {
            if (_synth == null)
            {
                return;
            }

            byte pitch = (byte)Math.Clamp(midi, 0, 127);
            byte vel = (byte)Math.Clamp(velocity, 1, 127);
            _synth.SendMessage(new MidiNoteOnMessage(0, pitch, vel));
            _activeNotes.Add(pitch);
        }

        private void SendNoteOff(int midi)
        {
            if (_synth == null)
            {
                return;
            }

            byte pitch = (byte)Math.Clamp(midi, 0, 127);
            _synth.SendMessage(new MidiNoteOffMessage(0, pitch, 0));
            _activeNotes.Remove(pitch);
        }

        private void StopAllNotes()
        {
            if (_synth == null || _activeNotes.Count == 0)
            {
                return;
            }

            foreach (int pitch in _activeNotes.ToArray())
            {
                _synth.SendMessage(new MidiNoteOffMessage(0, (byte)pitch, 0));
            }

            _activeNotes.Clear();
        }

        public void Dispose()
        {
            Stop();
            _synth?.Dispose();
            _synth = null;
        }

        private sealed record PlaybackEvent(int Tick, int Order, int Midi, bool IsOn, int Velocity);
        private sealed record PedalRange(int StartTick, int EndTick);
    }
}

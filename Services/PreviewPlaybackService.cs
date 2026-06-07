using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private Task? _playbackTask;
        private readonly HashSet<int> _activeNotes = new();
        private IReadOnlyList<PlaybackEvent> _events = Array.Empty<PlaybackEvent>();
        private IReadOnlyList<PlaybackNoteSpan> _noteSpans = Array.Empty<PlaybackNoteSpan>();
        private int _preparedProjectIdentity;
        private int _currentTick;
        private int _totalTicks;
        private double _ticksPerSecond = 1d;
        private DateTimeOffset _playbackStartTimeUtc;
        private bool _pauseRequested;

        public event EventHandler? PlaybackStateChanged;
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public int ActiveIndex { get; private set; } = -1;
        public int CurrentTick => IsPlaying ? GetCurrentTickFromClock() : _currentTick;

        public async Task TogglePlayAsync(int index, ScoreProject project)
        {
            if (project == null)
            {
                return;
            }

            int identity = RuntimeHelpers.GetHashCode(project);
            bool samePreparedProject = ActiveIndex == index && _preparedProjectIdentity == identity;

            if (IsPlaying && samePreparedProject)
            {
                Pause();
                return;
            }

            if (!samePreparedProject)
            {
                Reset();
                PrepareProject(index, identity, project);
            }

            if (_events.Count == 0)
            {
                return;
            }

            if (!samePreparedProject || _currentTick >= _totalTicks)
            {
                _currentTick = 0;
            }

            _playbackTask = StartPlaybackAsync();
            await _playbackTask.ConfigureAwait(true);
        }

        public void Pause()
        {
            if (!IsPlaying && !IsPaused)
            {
                return;
            }

            if (IsPlaying)
            {
                _currentTick = GetCurrentTickFromClock();
            }

            _pauseRequested = true;
            IsPlaying = false;
            IsPaused = _totalTicks > 0 && _currentTick < _totalTicks;
            StopAllNotes();
            _playbackCts?.Cancel();
            NotifyPlaybackStateChanged();
        }

        public void Reset()
        {
            if (IsPlaying)
            {
                _currentTick = GetCurrentTickFromClock();
            }

            _pauseRequested = false;
            IsPlaying = false;
            IsPaused = false;
            StopAllNotes();
            _playbackCts?.Cancel();
            _playbackCts?.Dispose();
            _playbackCts = null;
            _playbackTask = null;
            _events = Array.Empty<PlaybackEvent>();
            _noteSpans = Array.Empty<PlaybackNoteSpan>();
            _preparedProjectIdentity = 0;
            _currentTick = 0;
            _totalTicks = 0;
            _ticksPerSecond = 1d;
            ActiveIndex = -1;
            NotifyPlaybackStateChanged();
        }

        private void PrepareProject(int index, int identity, ScoreProject project)
        {
            _events = ProjectPlaybackTimelineBuilder.BuildEvents(project);
            _noteSpans = ProjectPlaybackTimelineBuilder.BuildNoteSpans(project);
            _preparedProjectIdentity = identity;
            _ticksPerSecond = Math.Max(1e-6, Math.Max(20, project.Bpm) * Math.Max(1, project.TimeSignature.TicksPerBeat(project.Ppq)) / 60d);
            _totalTicks = Math.Max(
                _events.Count == 0 ? 0 : _events.Max(static evt => evt.Tick),
                _noteSpans.Count == 0 ? 0 : _noteSpans.Max(static span => span.SustainedEndTick));
            _currentTick = 0;
            ActiveIndex = index;
            IsPaused = false;
        }

        private Task StartPlaybackAsync()
        {
            _pauseRequested = false;
            _playbackCts?.Cancel();
            _playbackCts?.Dispose();
            _playbackCts = new CancellationTokenSource();
            IsPlaying = true;
            IsPaused = false;
            _playbackStartTimeUtc = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(_currentTick / Math.Max(1e-6, _ticksPerSecond));
            NotifyPlaybackStateChanged();
            return PlayInternalAsync(_playbackCts.Token, _playbackCts);
        }

        private async Task PlayInternalAsync(CancellationToken cancellationToken, CancellationTokenSource playbackCts)
        {
            try
            {
                _synth ??= await MidiSynthesizer.CreateAsync();

                if (_currentTick > 0)
                {
                    ResumeActiveNotesAtTick(_currentTick);
                }

                int eventIndex = FindPlaybackEventIndex(_currentTick);
                while (eventIndex < _events.Count)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    PlaybackEvent evt = _events[eventIndex];
                    int waitTicks = Math.Max(0, evt.Tick - GetCurrentTickFromClock());
                    int waitMs = (int)Math.Round(waitTicks * 1000d / Math.Max(1e-6, _ticksPerSecond));
                    if (waitMs > 0)
                    {
                        await Task.Delay(waitMs, cancellationToken).ConfigureAwait(true);
                    }

                    if (evt.IsOn)
                    {
                        SendNoteOn(evt.Midi, evt.Velocity);
                    }
                    else
                    {
                        SendNoteOff(evt.Midi);
                    }

                    _currentTick = evt.Tick;
                    eventIndex++;
                }

                _currentTick = _totalTicks;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                _pauseRequested = false;
                _currentTick = 0;
                ActiveIndex = -1;
            }
            finally
            {
                StopAllNotes();

                bool paused = _pauseRequested && _currentTick < _totalTicks;
                IsPlaying = false;
                IsPaused = paused;
                _pauseRequested = false;

                if (!paused && _currentTick >= _totalTicks)
                {
                    ActiveIndex = -1;
                }

                NotifyPlaybackStateChanged();

                if (ReferenceEquals(_playbackCts, playbackCts))
                {
                    _playbackCts.Dispose();
                    _playbackCts = null;
                }

                _playbackTask = null;
            }
        }

        private void ResumeActiveNotesAtTick(int tick)
        {
            foreach (PlaybackNoteSpan span in _noteSpans)
            {
                if (span.StartTick <= tick && tick < span.SustainedEndTick)
                {
                    SendNoteOn(span.Midi, span.Velocity);
                }
            }
        }

        private int GetCurrentTickFromClock()
        {
            if (!IsPlaying)
            {
                return _currentTick;
            }

            int tick = (int)Math.Round((DateTimeOffset.UtcNow - _playbackStartTimeUtc).TotalSeconds * _ticksPerSecond);
            return Math.Clamp(tick, 0, Math.Max(0, _totalTicks));
        }

        private int FindPlaybackEventIndex(int tick)
        {
            int low = 0;
            int high = _events.Count;
            while (low < high)
            {
                int mid = low + ((high - low) / 2);
                if (_events[mid].Tick <= tick)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return Math.Clamp(low, 0, _events.Count);
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

        private void NotifyPlaybackStateChanged()
        {
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Reset();
            _synth?.Dispose();
            _synth = null;
        }
    }
}

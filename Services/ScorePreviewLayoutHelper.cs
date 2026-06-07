using System;
using System.Collections.Generic;
using System.Linq;
using MusicBox.Models;

namespace MusicBox.Services
{
    internal static class ScorePreviewLayoutHelper
    {
        internal readonly struct PreviewBoundaryDecoration
        {
            public PreviewBoundaryDecoration(bool startRepeat, bool endRepeat, bool final)
            {
                StartRepeat = startRepeat;
                EndRepeat = endRepeat;
                Final = final;
            }

            public bool StartRepeat { get; }
            public bool EndRepeat { get; }
            public bool Final { get; }

            public PreviewBoundaryDecoration WithStartRepeat()
                => new(startRepeat: true, endRepeat: EndRepeat, final: Final);

            public PreviewBoundaryDecoration WithEndRepeat()
                => new(startRepeat: StartRepeat, endRepeat: true, final: Final);

            public PreviewBoundaryDecoration WithFinal()
                => new(startRepeat: StartRepeat, endRepeat: EndRepeat, final: true);
        }

        private const string ScoreMarkFinalBarline = "score_final_barline";
        private const string ScoreMarkRepeatBarline = "score_repeat_barline";

        public static int GetContentMeasureCount(ScoreProject? project)
        {
            if (project == null)
            {
                return 1;
            }

            int ppq = Math.Max(1, project.Ppq);
            TimeSignature defaultTimeSignature = project.TimeSignature ?? new TimeSignature(4, 4);
            int defaultMeasureTicks = Math.Max(1, defaultTimeSignature.TicksPerMeasure(ppq));
            int ticksPerBeat = Math.Max(1, (int)Math.Round(ppq * (4d / Math.Max(1, defaultTimeSignature.Denominator))));

            int maxNoteTick = project.Notes == null || project.Notes.Count == 0
                ? 0
                : project.Notes.Max(note =>
                {
                    if (note == null)
                    {
                        return 0;
                    }

                    int start = Math.Max(0, note.StartTick);
                    int duration = Math.Max(1, note.DurationTicks);
                    return start + duration - 1;
                });

            int maxExpressionTick = project.ExpressionMarks == null || project.ExpressionMarks.Count == 0
                ? 0
                : project.ExpressionMarks.Max(mark =>
                {
                    if (mark == null)
                    {
                        return 0;
                    }

                    int start = Math.Max(0, mark.StartTick);
                    int spanTicks = Math.Max(1, (int)Math.Round(Math.Max(0.2f, mark.SpanBeats) * ticksPerBeat));
                    int anchorTick = start;
                    if (anchorTick > 0 && IsBarlineAnchoredScoreMark(NormalizeExpressionCode(mark.Code)))
                    {
                        anchorTick--;
                    }

                    int end = Math.Max(anchorTick, start + spanTicks - 1);
                    return Math.Max(anchorTick, end);
                });

            int maxTimeSigTick = project.TimeSignatureChanges == null || project.TimeSignatureChanges.Count == 0
                ? 0
                : project.TimeSignatureChanges.Max(change => Math.Max(0, change?.Tick ?? 0));

            int maxKeySigTick = project.KeySignatureChanges == null || project.KeySignatureChanges.Count == 0
                ? 0
                : project.KeySignatureChanges.Max(change => Math.Max(0, change?.Tick ?? 0));

            int layoutMeasureCount = project.LayoutSystemMeasureCounts == null || project.LayoutSystemMeasureCounts.Count == 0
                ? 0
                : project.LayoutSystemMeasureCounts.Where(count => count > 0).Sum();

            int targetTick = Math.Max(
                defaultMeasureTicks,
                Math.Max(
                    Math.Max(maxNoteTick + 1, maxExpressionTick + 1),
                    Math.Max(maxTimeSigTick + 1, maxKeySigTick + 1)));

            int count = 0;
            int cursor = 0;
            while (cursor < targetTick && count < 8192)
            {
                TimeSignature activeTimeSignature = GetEffectiveTimeSignatureAtTick(project, cursor);
                int measureTicks = Math.Max(1, activeTimeSignature.TicksPerMeasure(ppq));
                cursor += measureTicks;
                count++;
            }

            return Math.Max(1, Math.Max(count, layoutMeasureCount));
        }

        public static Dictionary<int, PreviewBoundaryDecoration> BuildBarlineMap(
            IReadOnlyList<ExpressionMark>? marks,
            int measureTicks,
            int measureCount)
        {
            var map = new Dictionary<int, PreviewBoundaryDecoration>();
            if (marks == null || marks.Count == 0)
            {
                return map;
            }

            foreach (ExpressionMark mark in marks.Where(m => m != null).OrderBy(m => m.StartTick))
            {
                string code = NormalizeExpressionCode(mark.Code);
                if (code != ScoreMarkRepeatBarline && code != ScoreMarkFinalBarline)
                {
                    continue;
                }

                int boundaryIndex = ResolveBoundaryIndex(mark.StartTick, measureTicks, measureCount);
                map.TryGetValue(boundaryIndex, out PreviewBoundaryDecoration existing);
                if (code == ScoreMarkFinalBarline)
                {
                    map[boundaryIndex] = existing.WithFinal();
                    continue;
                }

                bool isStartRepeat = mark.ShapeHeightSteps < 0f;
                map[boundaryIndex] = isStartRepeat
                    ? existing.WithStartRepeat()
                    : existing.WithEndRepeat();
            }

            return map;
        }

        public static int ResolveBoundaryIndex(int tick, int measureTicks, int measureCount)
        {
            int safeMeasureTicks = Math.Max(1, measureTicks);
            int safeTick = Math.Max(0, tick);
            int boundary = (int)Math.Round(safeTick / (double)safeMeasureTicks, MidpointRounding.AwayFromZero);
            return Math.Clamp(boundary, 0, Math.Max(1, measureCount));
        }

        public static string NormalizeExpressionCode(string? code)
        {
            return string.IsNullOrWhiteSpace(code)
                ? "mf"
                : code.Trim().ToLowerInvariant();
        }

        public static bool IsBarlineAnchoredScoreMark(string code)
        {
            return code is
                "score_repeat_barline" or
                "score_final_barline" or
                "score_ending_1" or
                "score_ending_2" or
                "score_gclef" or
                "score_fclef";
        }

        private static TimeSignature GetEffectiveTimeSignatureAtTick(ScoreProject project, int tick)
        {
            TimeSignature current = project.TimeSignature ?? new TimeSignature(4, 4);
            if (project.TimeSignatureChanges == null || project.TimeSignatureChanges.Count == 0)
            {
                return current;
            }

            foreach (TimeSignatureChange change in project.TimeSignatureChanges
                .Where(change => change != null)
                .OrderBy(change => change.Tick))
            {
                if (change.Tick > tick)
                {
                    break;
                }

                current = new TimeSignature(change.Numerator, change.Denominator);
            }

            return current;
        }
    }
}

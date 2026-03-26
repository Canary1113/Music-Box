using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MusicBox.Models;

namespace MusicBox.Services
{
    public sealed class ComposePreferenceService
    {
        private const int MinimumRatingsForRerank = 8;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly string _storePath;
        private PreferenceStore? _store;

        public ComposePreferenceService()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicBox");
            _storePath = Path.Combine(root, "compose-preferences.json");
        }

        public IReadOnlyList<ComposeCandidateRanking> RankCandidates(SmartComposeRequest request, IReadOnlyList<SmartComposeResult> generated)
        {
            EnsureLoaded();
            PreferenceStore store = _store ?? new PreferenceStore();
            var candidates = generated
                .Select(result =>
                {
                    ComposeFeatureVector features = ExtractFeatures(result.Project);
                    double predictedScore = PredictScore(features, request?.MoodId);
                    int? savedRating = store.Ratings
                        .Where(r => r.Seed == result.Seed)
                        .OrderByDescending(r => r.CreatedAtUtc)
                        .Select(r => (int?)r.Score)
                        .FirstOrDefault();
                    return new ComposeCandidateRanking(result, features, predictedScore, savedRating);
                })
                .ToList();

            if (store.Ratings.Count < MinimumRatingsForRerank)
            {
                return candidates;
            }

            return candidates
                .OrderByDescending(c => c.PredictedScore)
                .ThenBy(c => c.Result.Seed)
                .ToList();
        }

        public void RecordRating(SmartComposeRequest request, SmartComposeResult result, int score)
        {
            EnsureLoaded();
            PreferenceStore store = _store ?? new PreferenceStore();
            score = Math.Clamp(score, 0, 100);
            ComposeFeatureVector features = ExtractFeatures(result.Project);

            store.Ratings.Add(new ComposeRatingRecord
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Seed = result.Seed,
                Score = score,
                MoodId = request?.MoodId ?? string.Empty,
                LengthId = request?.LengthId ?? string.Empty,
                Bpm = result.Project.Bpm,
                FeatureRange = features.PitchRange,
                FeatureDensity = features.NoteDensity,
                FeatureChordDensity = features.ChordDensity,
                FeatureLargeLeapRatio = features.LargeLeapRatio,
                FeatureBassShare = features.BassShare,
                FeatureRepetitionRatio = features.RepetitionRatio,
                FeatureDurationMismatch = features.DurationMismatch
            });

            TrimStore(store);
            SaveStore(store);
        }

        public int GetRatingCount()
        {
            EnsureLoaded();
            return _store?.Ratings.Count ?? 0;
        }

        private double PredictScore(ComposeFeatureVector candidate, string? moodId)
        {
            PreferenceStore store = _store ?? new PreferenceStore();
            List<ComposeRatingRecord> samples = store.Ratings
                .Where(r => string.IsNullOrWhiteSpace(moodId) || string.Equals(r.MoodId, moodId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (samples.Count < 4)
            {
                samples = store.Ratings.ToList();
            }

            if (samples.Count == 0)
            {
                return 50d;
            }

            double[] min = ToVector(samples[0]);
            double[] max = ToVector(samples[0]);
            foreach (ComposeRatingRecord sample in samples.Skip(1))
            {
                double[] vector = ToVector(sample);
                for (int i = 0; i < vector.Length; i++)
                {
                    min[i] = Math.Min(min[i], vector[i]);
                    max[i] = Math.Max(max[i], vector[i]);
                }
            }

            double[] candidateVector = ToVector(candidate);
            double weightedScore = 0d;
            double totalWeight = 0d;
            foreach (ComposeRatingRecord sample in samples)
            {
                double[] sampleVector = ToVector(sample);
                double distanceSquared = 0d;
                for (int i = 0; i < sampleVector.Length; i++)
                {
                    double scale = Math.Max(0.15d, max[i] - min[i]);
                    double delta = (candidateVector[i] - sampleVector[i]) / scale;
                    distanceSquared += delta * delta;
                }

                double weight = 1d / (1d + distanceSquared);
                weightedScore += weight * sample.Score;
                totalWeight += weight;
            }

            if (totalWeight <= 0d)
            {
                return samples.Average(r => r.Score);
            }

            return Math.Clamp(weightedScore / totalWeight, 0d, 100d);
        }

        private static ComposeFeatureVector ExtractFeatures(ScoreProject project)
        {
            var notes = project.Notes
                .Where(n => !n.IsRest && n.DurationTicks > 0)
                .OrderBy(n => n.StartTick)
                .ThenBy(n => n.Voice)
                .ToList();

            int ppq = Math.Max(1, project.Ppq);
            int ticksPerMeasure = Math.Max(1, project.TimeSignature.TicksPerMeasure(ppq));
            int totalTicks = notes.Count == 0
                ? ticksPerMeasure
                : Math.Max(ticksPerMeasure, notes.Max(n => n.StartTick + Math.Max(1, n.DurationTicks)));
            int measureCount = Math.Max(1, (int)Math.Ceiling(totalTicks / (double)ticksPerMeasure));
            if (notes.Count == 0)
            {
                return new ComposeFeatureVector();
            }

            int minMidi = notes.Min(n => n.Midi);
            int maxMidi = notes.Max(n => n.Midi);
            double pitchRange = maxMidi - minMidi;
            double noteDensity = notes.Count / (double)measureCount;

            var onsetGroups = notes.GroupBy(n => n.StartTick).Select(g => g.ToList()).ToList();
            double chordDensity = onsetGroups.Average(g => g.Count);
            double bassShare = notes.Count(n => n.Voice > 1 || n.Midi < 60) / (double)notes.Count;

            var melody = notes
                .Where(n => n.Voice <= 1 || n.PreferTrebleStaff == true)
                .GroupBy(n => n.StartTick)
                .Select(g => g.OrderByDescending(n => n.Midi).First())
                .OrderBy(n => n.StartTick)
                .ToList();
            if (melody.Count < 2)
            {
                melody = onsetGroups
                    .Select(g => g.OrderByDescending(n => n.Midi).First())
                    .OrderBy(n => n.StartTick)
                    .ToList();
            }

            int leapCount = 0;
            int repeatCount = 0;
            for (int i = 1; i < melody.Count; i++)
            {
                int delta = Math.Abs(melody[i].Midi - melody[i - 1].Midi);
                if (delta >= 7)
                {
                    leapCount++;
                }

                if (melody[i].Midi == melody[i - 1].Midi)
                {
                    repeatCount++;
                }
            }

            double largeLeapRatio = melody.Count <= 1 ? 0d : leapCount / (double)(melody.Count - 1);
            double repetitionRatio = melody.Count <= 1 ? 0d : repeatCount / (double)(melody.Count - 1);

            double durationMismatch = onsetGroups
                .Where(g => g.Count > 1)
                .Select(g =>
                {
                    int minDuration = g.Min(n => Math.Max(1, n.DurationTicks));
                    int maxDuration = g.Max(n => Math.Max(1, n.DurationTicks));
                    return (maxDuration - minDuration) / (double)maxDuration;
                })
                .DefaultIfEmpty(0d)
                .Average();

            return new ComposeFeatureVector
            {
                PitchRange = pitchRange,
                NoteDensity = noteDensity,
                ChordDensity = chordDensity,
                LargeLeapRatio = largeLeapRatio,
                BassShare = bassShare,
                RepetitionRatio = repetitionRatio,
                DurationMismatch = durationMismatch
            };
        }

        private static double[] ToVector(ComposeFeatureVector vector)
        {
            return
            [
                vector.PitchRange,
                vector.NoteDensity,
                vector.ChordDensity,
                vector.LargeLeapRatio,
                vector.BassShare,
                vector.RepetitionRatio,
                vector.DurationMismatch
            ];
        }

        private static double[] ToVector(ComposeRatingRecord record)
        {
            return
            [
                record.FeatureRange,
                record.FeatureDensity,
                record.FeatureChordDensity,
                record.FeatureLargeLeapRatio,
                record.FeatureBassShare,
                record.FeatureRepetitionRatio,
                record.FeatureDurationMismatch
            ];
        }

        private static void TrimStore(PreferenceStore store)
        {
            const int maxRatings = 1200;
            if (store.Ratings.Count <= maxRatings)
            {
                return;
            }

            store.Ratings = store.Ratings
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(maxRatings)
                .OrderBy(r => r.CreatedAtUtc)
                .ToList();
        }

        private void EnsureLoaded()
        {
            if (_store != null)
            {
                return;
            }

            try
            {
                if (File.Exists(_storePath))
                {
                    string json = File.ReadAllText(_storePath);
                    _store = JsonSerializer.Deserialize<PreferenceStore>(json, _jsonOptions) ?? new PreferenceStore();
                    return;
                }
            }
            catch
            {
            }

            _store = new PreferenceStore();
        }

        private void SaveStore(PreferenceStore store)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
                string json = JsonSerializer.Serialize(store, _jsonOptions);
                File.WriteAllText(_storePath, json);
                _store = store;
            }
            catch
            {
            }
        }

        private sealed class PreferenceStore
        {
            public List<ComposeRatingRecord> Ratings { get; set; } = new();
        }

        private sealed class ComposeRatingRecord
        {
            public DateTimeOffset CreatedAtUtc { get; set; }
            public int Seed { get; set; }
            public int Score { get; set; }
            public string MoodId { get; set; } = string.Empty;
            public string LengthId { get; set; } = string.Empty;
            public int Bpm { get; set; }
            public double FeatureRange { get; set; }
            public double FeatureDensity { get; set; }
            public double FeatureChordDensity { get; set; }
            public double FeatureLargeLeapRatio { get; set; }
            public double FeatureBassShare { get; set; }
            public double FeatureRepetitionRatio { get; set; }
            public double FeatureDurationMismatch { get; set; }
        }
    }

    public sealed class ComposeFeatureVector
    {
        public double PitchRange { get; set; }
        public double NoteDensity { get; set; }
        public double ChordDensity { get; set; }
        public double LargeLeapRatio { get; set; }
        public double BassShare { get; set; }
        public double RepetitionRatio { get; set; }
        public double DurationMismatch { get; set; }
    }

    public sealed class ComposeCandidateRanking
    {
        public ComposeCandidateRanking(SmartComposeResult result, ComposeFeatureVector features, double predictedScore, int? savedRating)
        {
            Result = result;
            Features = features;
            PredictedScore = predictedScore;
            SavedRating = savedRating;
        }

        public SmartComposeResult Result { get; }
        public ComposeFeatureVector Features { get; }
        public double PredictedScore { get; }
        public int? SavedRating { get; }
    }
}

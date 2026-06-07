using MusicBox.Services;
using NAudio.Wave;

const int SampleRate = 44100;

string tempRoot = Path.Combine(Path.GetTempPath(), "MusicBoxAudioPitchRecognizerTests");
Directory.CreateDirectory(tempRoot);

Run("A4 single tone is MIDI 69", TestA4SingleTone);
Run("Repeated C4 notes are split", TestRepeatedC4Onsets);
Run("C4 with strong second harmonic stays near C4", TestC4WithSecondHarmonic);

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

void TestA4SingleTone()
{
    string path = Path.Combine(tempRoot, "a4.wav");
    WriteWav(path, Sine(440d, 1.0d, 0.62d));

    var notes = AudioPitchRecognizer.DetectNotesFromAudio(path, mode: AudioRecognitionMode.Balanced);
    var best = notes.OrderByDescending(n => n.DurationSeconds).FirstOrDefault()
        ?? throw new InvalidOperationException("No note detected.");

    if (Math.Abs(best.Midi - 69) > 1)
    {
        throw new InvalidOperationException($"Expected MIDI 69 +/- 1, got {best.Midi}. Notes: {Describe(notes)}");
    }
}

void TestRepeatedC4Onsets()
{
    string path = Path.Combine(tempRoot, "c4_c4_c4.wav");
    var samples = new List<float>();
    for (int i = 0; i < 3; i++)
    {
        samples.AddRange(Sine(261.63d, 0.25d, 0.70d));
        if (i < 2)
        {
            samples.AddRange(Sine(261.63d, 0.05d, 0.10d));
        }
    }

    WriteWav(path, samples);
    var notes = AudioPitchRecognizer.DetectNotesFromAudio(path, mode: AudioRecognitionMode.Balanced);
    int c4Count = notes.Count(n => Math.Abs(n.Midi - 60) <= 1);
    if (c4Count < 3)
    {
        throw new InvalidOperationException($"Expected at least 3 split C4 notes, got {c4Count}. Notes: {Describe(notes)}");
    }
}

void TestC4WithSecondHarmonic()
{
    string path = Path.Combine(tempRoot, "c4_second_harmonic.wav");
    float[] samples = HarmonicTone(261.63d, 1.0d, 0.52d, secondHarmonicLevel: 0.85d);
    WriteWav(path, samples);

    var notes = AudioPitchRecognizer.DetectNotesFromAudio(path, mode: AudioRecognitionMode.Balanced);
    var best = notes.OrderByDescending(n => n.DurationSeconds).FirstOrDefault()
        ?? throw new InvalidOperationException("No note detected.");

    if (Math.Abs(best.Midi - 60) > 1)
    {
        throw new InvalidOperationException($"Expected C4 MIDI 60 +/- 1, got {best.Midi}. Notes: {Describe(notes)}");
    }
}

static float[] Sine(double frequency, double seconds, double amplitude)
{
    int count = Math.Max(1, (int)Math.Round(seconds * SampleRate));
    var samples = new float[count];
    for (int i = 0; i < count; i++)
    {
        double envelope = AttackReleaseEnvelope(i, count);
        samples[i] = (float)(Math.Sin(2d * Math.PI * frequency * i / SampleRate) * amplitude * envelope);
    }

    return samples;
}

static float[] HarmonicTone(double frequency, double seconds, double amplitude, double secondHarmonicLevel)
{
    int count = Math.Max(1, (int)Math.Round(seconds * SampleRate));
    var samples = new float[count];
    for (int i = 0; i < count; i++)
    {
        double t = i / (double)SampleRate;
        double envelope = AttackReleaseEnvelope(i, count);
        double y = Math.Sin(2d * Math.PI * frequency * t)
            + Math.Sin(2d * Math.PI * frequency * 2d * t) * secondHarmonicLevel;
        samples[i] = (float)(y * amplitude * envelope / (1d + secondHarmonicLevel));
    }

    return samples;
}

static double AttackReleaseEnvelope(int index, int count)
{
    int ramp = Math.Max(1, Math.Min(count / 8, (int)(SampleRate * 0.025d)));
    if (index < ramp)
    {
        return index / (double)ramp;
    }

    int fromEnd = count - 1 - index;
    return fromEnd < ramp ? fromEnd / (double)ramp : 1d;
}

static void WriteWav(string path, IEnumerable<float> samples)
{
    using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1));
    foreach (float sample in samples)
    {
        writer.WriteSample(Math.Clamp(sample, -1f, 1f));
    }
}

static string Describe(IReadOnlyList<DetectedAudioNote> notes)
{
    return notes.Count == 0
        ? "<none>"
        : string.Join(", ", notes.Select(n => $"{n.Midi}@{n.StartSeconds:0.000}+{n.DurationSeconds:0.000}"));
}

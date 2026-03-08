using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using 音乐魔盒.Models;

namespace 音乐魔盒.Services
{
    public sealed class ScoreOmrService
    {
        private const string AudiverisEnvKey = "MBX_AUDIVERIS_CLI";
        private const int PdfRenderDpi = 400;
        private const int MaxImageVariants = 5;
        private const int MaxPdfPageVariants = 3;

        private static readonly HashSet<string> DirectMusicXmlExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".musicxml", ".xml"
        };

        private static readonly HashSet<string> SupportedInputExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".musicxml", ".xml", ".mxl"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"
        };

        private readonly OmrRuntimeManager _runtimeManager;
        private readonly MusicXmlImporter _importer = new();
        private readonly MusicXmlExporter _exporter = new();
        private readonly OmrImportPostProcessor _omrPostProcessor = new();

        public ScoreOmrService() : this(new OmrRuntimeManager()) { }

        public ScoreOmrService(OmrRuntimeManager runtimeManager)
        {
            _runtimeManager = runtimeManager;
        }

        public Task<OmrRecognitionResult> RecognizeToMusicXmlAsync(
            string inputPath,
            CancellationToken cancellationToken = default)
        {
            return RecognizeToMusicXmlAsync(inputPath, progress: null, cancellationToken);
        }

        public async Task<OmrRecognitionResult> RecognizeToMusicXmlAsync(
            string inputPath,
            IProgress<OmrProgressInfo>? progress,
            CancellationToken cancellationToken = default)
        {
            string artifactRoot = CreateArtifactRoot();
            var diagnostics = new OmrRecognitionDiagnostics { ArtifactRoot = artifactRoot };
            void Report(string stage, int current, int total, string detail)
                => progress?.Report(new OmrProgressInfo(stage, current, total, detail));

            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return OmrRecognitionResult.Fail("No input file path.", diagnostics);
            }

            if (!File.Exists(inputPath))
            {
                return OmrRecognitionResult.Fail($"Input file not found: {inputPath}", diagnostics);
            }

            string extension = Path.GetExtension(inputPath);
            if (!SupportedInputExtensions.Contains(extension))
            {
                return OmrRecognitionResult.Fail("Unsupported input type. Please use PDF/PNG/JPG/BMP/TIFF/MusicXML.", diagnostics);
            }

            try
            {
                Report("初始化", 0, 1, "准备输入文件");
                Directory.CreateDirectory(artifactRoot);
                string stagedInput = StageInputFile(inputPath, artifactRoot);

                if (DirectMusicXmlExtensions.Contains(extension))
                {
                    OmrCandidateInfo directCandidate = EvaluateCandidate(stagedInput, "direct", "input", null, "ok");
                    diagnostics.Candidates.Add(directCandidate);
                    diagnostics.BestCandidate = directCandidate;
                    return OmrRecognitionResult.Ok(stagedInput, "Input is already MusicXML.", diagnostics);
                }

                if (string.Equals(extension, ".mxl", StringComparison.OrdinalIgnoreCase))
                {
                    string extracted = await ExtractMxlToMusicXmlAsync(stagedInput, cancellationToken).ConfigureAwait(false);
                    OmrCandidateInfo mxlCandidate = EvaluateCandidate(extracted, "mxl", "input", null, "ok");
                    diagnostics.Candidates.Add(mxlCandidate);
                    diagnostics.BestCandidate = mxlCandidate;
                    return OmrRecognitionResult.Ok(extracted, "MXL unpacked to MusicXML.", diagnostics);
                }

                OmrRuntimeStatus runtimeStatus = _runtimeManager.GetRuntimeStatus();
                string? homrCommand = runtimeStatus.HomrReady ? _runtimeManager.GetHomrCommand() : null;
                string? audiverisPath = DetectAudiverisCliPath();

                var runTargets = new List<OmrInputVariant>();
                if (ImageExtensions.Contains(extension))
                {
                    runTargets.AddRange(BuildImageVariants(stagedInput, Path.Combine(artifactRoot, "variants"), null, MaxImageVariants));
                }
                else if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    IReadOnlyList<string> pages = await RenderPdfToPngAsync(stagedInput, Path.Combine(artifactRoot, "pdf_pages"), cancellationToken).ConfigureAwait(false);
                    int pageIndex = 1;
                    foreach (string pagePath in pages)
                    {
                        runTargets.AddRange(BuildImageVariants(pagePath, Path.Combine(artifactRoot, "variants", $"page_{pageIndex:000}"), pageIndex, MaxPdfPageVariants));
                        pageIndex++;
                    }
                }

                int totalRuns = runTargets.Sum(t => (t.AllowHomr ? 1 : 0) + (t.AllowAudiveris ? 1 : 0));
                int runIndex = 0;
                foreach (OmrInputVariant target in runTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (target.AllowHomr)
                    {
                        runIndex++;
                        Report("识别中", runIndex, Math.Max(1, totalRuns), $"homr/{target.Name}{(target.PageIndex.HasValue ? $"(p{target.PageIndex.Value})" : string.Empty)}");
                        if (!string.IsNullOrWhiteSpace(homrCommand))
                        {
                            EngineExecutionResult homrRun = await RunHomrAsync(homrCommand!, target, artifactRoot, cancellationToken).ConfigureAwait(false);
                            diagnostics.EngineRuns.Add(homrRun.RunInfo);
                            if (homrRun.Candidate != null) diagnostics.Candidates.Add(homrRun.Candidate);
                        }
                        else
                        {
                            diagnostics.EngineRuns.Add(new OmrEngineRunInfo
                            {
                                Engine = "homr",
                                InputVariant = target.Name,
                                PageIndex = target.PageIndex,
                                ExitCode = -1,
                                StderrSummary = "homr unavailable",
                                Status = "skipped"
                            });
                        }
                    }

                    if (target.AllowAudiveris)
                    {
                        runIndex++;
                        Report("识别中", runIndex, Math.Max(1, totalRuns), $"Audiveris/{target.Name}{(target.PageIndex.HasValue ? $"(p{target.PageIndex.Value})" : string.Empty)}");
                        if (!string.IsNullOrWhiteSpace(audiverisPath))
                        {
                            EngineExecutionResult audRun = await RunAudiverisAsync(audiverisPath!, target, artifactRoot, cancellationToken).ConfigureAwait(false);
                            diagnostics.EngineRuns.Add(audRun.RunInfo);
                            if (audRun.Candidate != null) diagnostics.Candidates.Add(audRun.Candidate);
                        }
                        else
                        {
                            diagnostics.EngineRuns.Add(new OmrEngineRunInfo
                            {
                                Engine = "Audiveris",
                                InputVariant = target.Name,
                                PageIndex = target.PageIndex,
                                ExitCode = -1,
                                StderrSummary = "Audiveris unavailable",
                                Status = "skipped"
                            });
                        }
                    }
                }

                if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    Report("后处理", 1, 2, "拼接多页候选");
                    IReadOnlyList<OmrCandidateInfo> stitched = await BuildStitchedCandidatesAsync(diagnostics.Candidates, artifactRoot, cancellationToken).ConfigureAwait(false);
                    foreach (OmrCandidateInfo candidate in stitched)
                    {
                        diagnostics.Candidates.Add(candidate);
                    }
                }

                Report("后处理", 2, 2, "候选评分与选优");
                OmrCandidateInfo? best = SelectBestCandidate(diagnostics.Candidates);
                diagnostics.BestCandidate = best;
                if (best == null)
                {
                    string message = diagnostics.Candidates.Count == 0
                        ? "No usable OMR candidate was generated."
                        : "Recognition finished, but all candidates were invalid (pitchedNotes=0).";
                    return OmrRecognitionResult.Fail(message, diagnostics);
                }

                return OmrRecognitionResult.Ok(
                    best.MusicXmlPath,
                    $"Recognition finished. Best={best.Engine}/{best.InputVariant}, score={best.QualityScore}, notes={best.PitchedNotes}.",
                    diagnostics);
            }
            catch (OperationCanceledException)
            {
                return OmrRecognitionResult.Fail("Recognition canceled.", diagnostics);
            }
            catch (Exception ex)
            {
                return OmrRecognitionResult.Fail($"Recognition failed: {ex.Message}", diagnostics);
            }
        }

        public static string? DetectAudiverisCliPath()
        {
            string? fromEnv = Environment.GetEnvironmentVariable(AudiverisEnvKey)
                ?? Environment.GetEnvironmentVariable(AudiverisEnvKey, EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable(AudiverisEnvKey, EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                string candidate = TrimQuotes(fromEnv);
                if (File.Exists(candidate)) return candidate;
            }

            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Audiveris", "Audiveris.bat"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Audiveris", "Audiveris.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Audiveris", "bin", "Audiveris.bat"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Audiveris", "bin", "Audiveris.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Audiveris", "Audiveris.bat"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Audiveris", "Audiveris.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Audiveris", "bin", "Audiveris.bat"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Audiveris", "bin", "Audiveris.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Audiveris", "Audiveris.bat"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Audiveris", "Audiveris.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Audiveris", "bin", "Audiveris.bat"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Audiveris", "bin", "Audiveris.exe")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }

            return TryResolveFromPath("Audiveris.bat")
                ?? TryResolveFromPath("Audiveris.exe")
                ?? TryResolveFromPath("audiveris.exe");
        }

        public static string? DetectHomrCliPath()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("MBX_HOMR_CLI")
                ?? Environment.GetEnvironmentVariable("MBX_HOMR_CLI", EnvironmentVariableTarget.User)
                ?? Environment.GetEnvironmentVariable("MBX_HOMR_CLI", EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            string venvHomr = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicBox", "omr", "py311", "Scripts", "homr.exe");
            if (File.Exists(venvHomr)) return venvHomr;

            return TryResolveFromPath("homr");
        }

        private static string CreateArtifactRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "MusicBoxOmr", DateTime.Now.ToString("yyyyMMdd-HHmmss"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string StageInputFile(string sourcePath, string artifactRoot)
        {
            string inputDir = Path.Combine(artifactRoot, "input");
            Directory.CreateDirectory(inputDir);
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            string staged = Path.Combine(inputDir, $"source{ext}");
            File.Copy(sourcePath, staged, overwrite: true);
            return staged;
        }

        private async Task<EngineExecutionResult> RunHomrAsync(string homrCommand, OmrInputVariant target, string artifactRoot, CancellationToken cancellationToken)
        {
            string runDir = Path.Combine(artifactRoot, "runs", "homr", $"{SanitizeFilePart(target.Name)}{(target.PageIndex.HasValue ? $"_p{target.PageIndex.Value:000}" : string.Empty)}");
            Directory.CreateDirectory(runDir);

            string stagedInput = Path.Combine(runDir, "input" + Path.GetExtension(target.InputPath));
            File.Copy(target.InputPath, stagedInput, overwrite: true);

            ParseCommand(homrCommand, out string fileName, out string argPrefix);
            ProcessRunResult run = await RunProcessAsync(fileName, $"{argPrefix} \"{stagedInput}\"".Trim(), runDir, cancellationToken).ConfigureAwait(false);

            string outputPath = string.Empty;
            OmrCandidateInfo? candidate = null;
            string status = "failed";
            if (run.ExitCode == 0)
            {
                outputPath = TryFindMusicXml(runDir) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    string? mxlPath = TryFindMxl(runDir);
                    if (!string.IsNullOrWhiteSpace(mxlPath))
                    {
                        outputPath = await ExtractMxlToMusicXmlAsync(mxlPath, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    candidate = EvaluateCandidate(outputPath, "homr", target.Name, target.PageIndex, "ok");
                    status = candidate.IsValid ? "ok" : "invalid";
                }
            }

            return new EngineExecutionResult(
                new OmrEngineRunInfo
                {
                    Engine = "homr",
                    InputVariant = target.Name,
                    PageIndex = target.PageIndex,
                    ExitCode = run.ExitCode,
                    StderrSummary = FirstNonEmptyLine(run.Stderr) ?? FirstNonEmptyLine(run.Stdout) ?? string.Empty,
                    OutputPath = outputPath,
                    Status = status,
                    Command = $"{fileName} {argPrefix}".Trim()
                },
                candidate);
        }

        private async Task<EngineExecutionResult> RunAudiverisAsync(string audiverisPath, OmrInputVariant target, string artifactRoot, CancellationToken cancellationToken)
        {
            string runDir = Path.Combine(artifactRoot, "runs", "audiveris", $"{SanitizeFilePart(target.Name)}{(target.PageIndex.HasValue ? $"_p{target.PageIndex.Value:000}" : string.Empty)}");
            Directory.CreateDirectory(runDir);

            string stagedInput = Path.Combine(runDir, "input" + Path.GetExtension(target.InputPath));
            File.Copy(target.InputPath, stagedInput, overwrite: true);

            bool isBatch = string.Equals(Path.GetExtension(audiverisPath), ".bat", StringComparison.OrdinalIgnoreCase);
            string fileName = isBatch ? "cmd.exe" : audiverisPath;
            string args = isBatch
                ? $"/c \"\"{audiverisPath}\" -batch -export -output \"{runDir}\" \"{stagedInput}\"\""
                : $"-batch -export -output \"{runDir}\" \"{stagedInput}\"";

            ProcessRunResult run = await RunProcessAsync(fileName, args, runDir, cancellationToken).ConfigureAwait(false);

            string outputPath = string.Empty;
            OmrCandidateInfo? candidate = null;
            string status = "failed";
            if (run.ExitCode == 0)
            {
                outputPath = TryFindMusicXml(runDir) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    string? mxlPath = TryFindMxl(runDir);
                    if (!string.IsNullOrWhiteSpace(mxlPath))
                    {
                        outputPath = await ExtractMxlToMusicXmlAsync(mxlPath, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    candidate = EvaluateCandidate(outputPath, "Audiveris", target.Name, target.PageIndex, "ok");
                    status = candidate.IsValid ? "ok" : "invalid";
                }
            }

            return new EngineExecutionResult(
                new OmrEngineRunInfo
                {
                    Engine = "Audiveris",
                    InputVariant = target.Name,
                    PageIndex = target.PageIndex,
                    ExitCode = run.ExitCode,
                    StderrSummary = FirstNonEmptyLine(run.Stderr) ?? FirstNonEmptyLine(run.Stdout) ?? string.Empty,
                    OutputPath = outputPath,
                    Status = status,
                    Command = isBatch ? $"{audiverisPath} -batch -export" : $"{fileName} -batch -export"
                },
                candidate);
        }
        private async Task<IReadOnlyList<OmrCandidateInfo>> BuildStitchedCandidatesAsync(IReadOnlyList<OmrCandidateInfo> candidates, string artifactRoot, CancellationToken cancellationToken)
        {
            var stitched = new List<OmrCandidateInfo>();
            var pageCandidates = candidates
                .Where(c => c.PageIndex.HasValue && !string.IsNullOrWhiteSpace(c.MusicXmlPath) && File.Exists(c.MusicXmlPath))
                .GroupBy(c => $"{c.Engine}|{c.InputVariant}", StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, OmrCandidateInfo> group in pageCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var byPage = group.GroupBy(c => c.PageIndex!.Value)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.QualityScore).First());
                if (byPage.Count <= 1) continue;

                List<OmrCandidateInfo> ordered = byPage.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();
                string outputDir = Path.Combine(artifactRoot, "stitched");
                Directory.CreateDirectory(outputDir);
                string outputPath = Path.Combine(outputDir, $"{SanitizeFilePart(group.First().Engine)}_{SanitizeFilePart(group.First().InputVariant)}.musicxml");

                ScoreProject merged = MergePageProjects(ordered.Select(c => c.MusicXmlPath));
                _exporter.Export(merged, outputPath);
                stitched.Add(EvaluateCandidate(outputPath, group.First().Engine, $"{group.First().InputVariant}_stitched", null, "ok"));
            }

            return await Task.FromResult(stitched).ConfigureAwait(false);
        }

        private ScoreProject MergePageProjects(IEnumerable<string> musicXmlPaths)
        {
            var projects = musicXmlPaths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => _importer.Import(path))
                .ToList();
            if (projects.Count == 0)
            {
                return ProjectFactory.CreateDefault();
            }

            ScoreProject first = projects[0];
            var merged = new ScoreProject
            {
                Title = first.Title,
                Bpm = first.Bpm,
                Ppq = Math.Max(1, first.Ppq),
                TimeSignature = new TimeSignature(first.TimeSignature.Numerator, first.TimeSignature.Denominator),
                KeySignature = new KeySignature(first.KeySignature.Fifths, first.KeySignature.Mode),
                Notes = new List<NoteEvent>(),
                ExpressionMarks = new List<ExpressionMark>(),
                TimeSignatureChanges = new List<TimeSignatureChange>(),
                KeySignatureChanges = new List<KeySignatureChange>(),
                StaffClefs = new Dictionary<string, string>(first.StaffClefs),
                LayoutBarlineOffsets = new Dictionary<int, float>(),
                LayoutSystemMeasureCounts = new List<int>()
            };

            int currentOffset = 0;
            TimeSignature activeTime = merged.TimeSignature;
            KeySignature activeKey = merged.KeySignature;

            foreach (ScoreProject page in projects)
            {
                int targetPpq = merged.Ppq;
                int sourcePpq = Math.Max(1, page.Ppq);
                double ratio = targetPpq / (double)sourcePpq;
                int Scale(int tick) => (int)Math.Round(Math.Max(0, tick) * ratio);

                var pageTime = new TimeSignature(page.TimeSignature.Numerator, page.TimeSignature.Denominator);
                var pageKey = new KeySignature(page.KeySignature.Fifths, page.KeySignature.Mode);

                if (currentOffset > 0)
                {
                    if (activeTime.Numerator != pageTime.Numerator || activeTime.Denominator != pageTime.Denominator)
                    {
                        merged.TimeSignatureChanges.Add(new TimeSignatureChange
                        {
                            Tick = currentOffset,
                            Numerator = pageTime.Numerator,
                            Denominator = pageTime.Denominator
                        });
                        activeTime = pageTime;
                    }

                    if (activeKey.Fifths != pageKey.Fifths || activeKey.Mode != pageKey.Mode)
                    {
                        merged.KeySignatureChanges.Add(new KeySignatureChange
                        {
                            Tick = currentOffset,
                            Fifths = pageKey.Fifths,
                            Mode = pageKey.Mode
                        });
                        activeKey = pageKey;
                    }
                }

                foreach (NoteEvent note in page.Notes)
                {
                    merged.Notes.Add(new NoteEvent
                    {
                        Midi = note.Midi,
                        StartTick = currentOffset + Scale(note.StartTick),
                        DurationTicks = Math.Max(1, Scale(note.DurationTicks)),
                        BaseDurationTicks = Math.Max(1, Scale(note.BaseDurationTicks)),
                        AugmentationDots = note.AugmentationDots,
                        IsRest = note.IsRest,
                        Voice = note.Voice,
                        Accidental = note.Accidental,
                        IsStaccato = note.IsStaccato,
                        IsStaccatissimo = note.IsStaccatissimo,
                        IsAccent = note.IsAccent,
                        Ornament = note.Ornament,
                        OrnamentOffsetX = note.OrnamentOffsetX,
                        OrnamentOffsetY = note.OrnamentOffsetY,
                        GraceOrnamentOffsetX = note.GraceOrnamentOffsetX,
                        GraceOrnamentOffsetY = note.GraceOrnamentOffsetY,
                        TieStart = note.TieStart,
                        TieEnd = note.TieEnd,
                        BeamGroupId = note.BeamGroupId,
                        StemUpOverride = note.StemUpOverride,
                        PreferTrebleStaff = note.PreferTrebleStaff
                    });
                }

                foreach (ExpressionMark mark in page.ExpressionMarks)
                {
                    merged.ExpressionMarks.Add(new ExpressionMark
                    {
                        Code = mark.Code,
                        StartTick = currentOffset + Scale(mark.StartTick),
                        StaffStepOffset = mark.StaffStepOffset,
                        SpanBeats = mark.SpanBeats,
                        ShapeHeightSteps = mark.ShapeHeightSteps,
                        SlopeSteps = mark.SlopeSteps
                    });
                }

                foreach (TimeSignatureChange change in page.TimeSignatureChanges)
                {
                    merged.TimeSignatureChanges.Add(new TimeSignatureChange
                    {
                        Tick = currentOffset + Scale(change.Tick),
                        Numerator = change.Numerator,
                        Denominator = change.Denominator
                    });
                }

                foreach (KeySignatureChange change in page.KeySignatureChanges)
                {
                    merged.KeySignatureChanges.Add(new KeySignatureChange
                    {
                        Tick = currentOffset + Scale(change.Tick),
                        Fifths = change.Fifths,
                        Mode = change.Mode
                    });
                }

                int maxTick = page.Notes.Count == 0 ? 0 : page.Notes.Max(n => Math.Max(0, n.StartTick) + Math.Max(1, n.DurationTicks));
                int scaledMax = Math.Max(1, Scale(maxTick));
                int ticksPerMeasure = Math.Max(1, pageTime.TicksPerMeasure(targetPpq));
                int alignedSpan = AlignUp(Math.Max(scaledMax, ticksPerMeasure), ticksPerMeasure);
                currentOffset += alignedSpan;
            }

            merged.Notes = merged.Notes.OrderBy(n => n.StartTick).ThenBy(n => n.Midi).ToList();
            merged.ExpressionMarks = merged.ExpressionMarks.OrderBy(m => m.StartTick).ToList();
            merged.TimeSignatureChanges = merged.TimeSignatureChanges
                .Where(c => c.Tick > 0)
                .GroupBy(c => c.Tick)
                .Select(g => g.Last())
                .OrderBy(c => c.Tick)
                .ToList();
            merged.KeySignatureChanges = merged.KeySignatureChanges
                .Where(c => c.Tick > 0)
                .GroupBy(c => c.Tick)
                .Select(g => g.Last())
                .OrderBy(c => c.Tick)
                .ToList();
            merged.UpdatedAt = DateTimeOffset.Now;
            return merged;
        }

        private OmrCandidateInfo EvaluateCandidate(string musicXmlPath, string engine, string inputVariant, int? pageIndex, string status)
        {
            try
            {
                ScoreProject project = _importer.Import(musicXmlPath);
                _omrPostProcessor.Apply(project);
                var playableNotes = project.Notes.Where(n => !n.IsRest).ToList();
                int restNotes = project.Notes.Count(n => n.IsRest);
                int expressionMarks = project.ExpressionMarks.Count;
                int slurMarks = project.ExpressionMarks.Count(m => string.Equals(m.Code, "slur", StringComparison.OrdinalIgnoreCase));
                int repeatBars = project.ExpressionMarks.Count(m => string.Equals(m.Code, "score_repeat_barline", StringComparison.OrdinalIgnoreCase));
                int finalBars = project.ExpressionMarks.Count(m => string.Equals(m.Code, "score_final_barline", StringComparison.OrdinalIgnoreCase));
                int keyChanges = project.KeySignatureChanges.Count;
                int timeChanges = project.TimeSignatureChanges.Count;
                int pitchedNotes = playableNotes.Count;
                int distinctOnsets = playableNotes.Select(n => n.StartTick).Distinct().Count();

                int maxTick = project.Notes.Count == 0 ? 0 : project.Notes.Max(n => Math.Max(0, n.StartTick) + Math.Max(1, n.DurationTicks));
                int ticksPerMeasure = Math.Max(1, project.TimeSignature.TicksPerMeasure(Math.Max(1, project.Ppq)));
                int measures = Math.Max(1, (int)Math.Ceiling(maxTick / (double)ticksPerMeasure));

                var activeMeasures = new HashSet<int>();
                foreach (NoteEvent note in playableNotes)
                {
                    activeMeasures.Add(Math.Max(0, note.StartTick / ticksPerMeasure));
                }

                double coverage = measures <= 0 ? 0 : activeMeasures.Count / (double)measures;
                int rhythmicCompletenessBonus = (int)Math.Round(Math.Clamp(coverage * 24.0, 0, 24)) + Math.Min(12, distinctOnsets / 2);
                int structuralBonus = Math.Min(
                    24,
                    (restNotes / 3)
                    + Math.Min(12, expressionMarks)
                    + Math.Min(8, slurMarks * 2)
                    + Math.Min(6, repeatBars * 2)
                    + Math.Min(6, finalBars * 2));

                int penalties = 0;
                if (pitchedNotes == 0)
                {
                    penalties += 999;
                }
                else
                {
                    if (distinctOnsets <= 1) penalties += 8;
                    if (pitchedNotes < 3) penalties += 6;
                    if (measures > 2 && activeMeasures.Count <= 1) penalties += 8;
                    if (pitchedNotes / (double)Math.Max(1, measures) < 0.4) penalties += 8;
                    if (measures > pitchedNotes * 8) penalties += Math.Min(42, (measures - (pitchedNotes * 8)) / 2);
                    if (restNotes > pitchedNotes * 3) penalties += Math.Min(24, (restNotes - pitchedNotes * 3) / 2);
                    if (expressionMarks > pitchedNotes * 2) penalties += Math.Min(18, (expressionMarks - pitchedNotes * 2));
                    if (keyChanges > Math.Max(2, measures / 3)) penalties += Math.Min(36, (keyChanges - Math.Max(2, measures / 3)) * 4);
                    if (timeChanges > Math.Max(1, measures / 4)) penalties += Math.Min(36, (timeChanges - Math.Max(1, measures / 4)) * 5);
                    if (pageIndex.HasValue) penalties += 6;
                }

                int qualityScore = (pitchedNotes * 3) + distinctOnsets + rhythmicCompletenessBonus + structuralBonus - penalties;
                bool isValid = pitchedNotes > 0;

                return new OmrCandidateInfo
                {
                    Engine = engine,
                    InputVariant = inputVariant,
                    PageIndex = pageIndex,
                    MusicXmlPath = musicXmlPath,
                    PitchedNotes = pitchedNotes,
                    Measures = measures,
                    DistinctOnsets = distinctOnsets,
                    RhythmicCompletenessBonus = rhythmicCompletenessBonus,
                    StructuralBonus = structuralBonus,
                    RestNotes = restNotes,
                    ExpressionMarks = expressionMarks,
                    SlurMarks = slurMarks,
                    RepeatBarlines = repeatBars,
                    FinalBarlines = finalBars,
                    KeyChanges = keyChanges,
                    TimeChanges = timeChanges,
                    Penalties = penalties,
                    QualityScore = qualityScore,
                    Status = isValid ? status : "invalid",
                    IsValid = isValid
                };
            }
            catch (Exception ex)
            {
                return new OmrCandidateInfo
                {
                    Engine = engine,
                    InputVariant = inputVariant,
                    PageIndex = pageIndex,
                    MusicXmlPath = musicXmlPath,
                    PitchedNotes = 0,
                    Measures = 0,
                    DistinctOnsets = 0,
                    RhythmicCompletenessBonus = 0,
                    StructuralBonus = 0,
                    RestNotes = 0,
                    ExpressionMarks = 0,
                    SlurMarks = 0,
                    RepeatBarlines = 0,
                    FinalBarlines = 0,
                    KeyChanges = 0,
                    TimeChanges = 0,
                    Penalties = 999,
                    QualityScore = -999,
                    Status = $"parse_error: {ex.Message}",
                    IsValid = false
                };
            }
        }

        private static OmrCandidateInfo? SelectBestCandidate(IReadOnlyList<OmrCandidateInfo> candidates)
        {
            return candidates
                .Where(c => c.IsValid && File.Exists(c.MusicXmlPath))
                .OrderByDescending(c => c.QualityScore)
                .ThenBy(c => c.PageIndex.HasValue ? 1 : 0)
                .ThenByDescending(c => string.Equals(c.Engine, "homr", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }

        private static List<OmrInputVariant> BuildImageVariants(string imagePath, string outputDir, int? pageIndex, int maxVariants)
        {
            Directory.CreateDirectory(outputDir);
            var variants = new List<OmrInputVariant>();

            string rawPath = Path.Combine(outputDir, "raw.png");
            File.Copy(imagePath, rawPath, overwrite: true);
            variants.Add(new OmrInputVariant(rawPath, "raw", pageIndex, AllowHomr: true, AllowAudiveris: true));

            try
            {
                using var source = new Bitmap(imagePath);

                using (var gray = CreateGrayContrastBitmap(source, 1.35f))
                {
                    string path = Path.Combine(outputDir, "gray_contrast.png");
                    gray.Save(path, ImageFormat.Png);
                    variants.Add(new OmrInputVariant(path, "gray_contrast", pageIndex, AllowHomr: true, AllowAudiveris: true));
                }

                using (var adaptive = CreateAdaptiveBinaryBitmap(source, 31, 12))
                {
                    string path = Path.Combine(outputDir, "adaptive_binary.png");
                    adaptive.Save(path, ImageFormat.Png);
                    variants.Add(new OmrInputVariant(path, "adaptive_binary", pageIndex, AllowHomr: true, AllowAudiveris: true));

                    using var deskew = CreateDeskewBinaryBitmap(adaptive);
                    string deskewPath = Path.Combine(outputDir, "deskew_binary.png");
                    deskew.Save(deskewPath, ImageFormat.Png);
                    variants.Add(new OmrInputVariant(deskewPath, "deskew_binary", pageIndex, AllowHomr: true, AllowAudiveris: true));
                }

                int shortSide = Math.Min(source.Width, source.Height);
                if (shortSide < 1700)
                {
                    float scale = 1700f / Math.Max(1, shortSide);
                    using var upscaled = ResizeBitmap(source, scale);
                    string path = Path.Combine(outputDir, "upscaled.png");
                    upscaled.Save(path, ImageFormat.Png);
                    variants.Add(new OmrInputVariant(path, "upscaled", pageIndex, AllowHomr: true, AllowAudiveris: true));
                }
            }
            catch
            {
            }

            return variants
                .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(v => v.Name switch
                {
                    "raw" => 0,
                    "gray_contrast" => 1,
                    "adaptive_binary" => 2,
                    "deskew_binary" => 3,
                    "upscaled" => 4,
                    _ => 99
                })
                .Take(Math.Clamp(maxVariants, 1, MaxImageVariants))
                .ToList();
        }

        private static async Task<IReadOnlyList<string>> RenderPdfToPngAsync(string pdfPath, string outputDir, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDir);
            var pages = new List<string>();

            StorageFile file = await StorageFile.GetFileFromPathAsync(pdfPath);
            PdfDocument document = await PdfDocument.LoadFromFileAsync(file);
            for (uint i = 0; i < document.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using PdfPage page = document.GetPage(i);
                double scale = PdfRenderDpi / 96.0;
                uint width = (uint)Math.Max(1, Math.Round(page.Size.Width * scale));
                uint height = (uint)Math.Max(1, Math.Round(page.Size.Height * scale));

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = width,
                    DestinationHeight = height,
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
                };

                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, options);
                byte[] bytes = await ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);

                string outputPath = Path.Combine(outputDir, $"page_{i + 1:000}_raw.png");
                await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
                pages.Add(outputPath);
            }

            return pages;
        }

        private static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStream stream, CancellationToken cancellationToken)
        {
            stream.Seek(0);
            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            await reader.LoadAsync((uint)stream.Size).AsTask(cancellationToken).ConfigureAwait(false);
            byte[] buffer = new byte[stream.Size];
            reader.ReadBytes(buffer);
            return buffer;
        }
        private static Bitmap ResizeBitmap(Bitmap source, float scale)
        {
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bitmap);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 0, width, height));
            return bitmap;
        }

        private static Bitmap CreateGrayContrastBitmap(Bitmap source, float contrast)
        {
            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Color p = source.GetPixel(x, y);
                    int gray = ClampToByte((p.R * 0.299f) + (p.G * 0.587f) + (p.B * 0.114f));
                    int contrasted = ClampToByte(((gray - 128f) * contrast) + 128f);
                    bitmap.SetPixel(x, y, Color.FromArgb(contrasted, contrasted, contrasted));
                }
            }

            return bitmap;
        }

        private static Bitmap CreateAdaptiveBinaryBitmap(Bitmap source, int windowSize, int thresholdOffset)
        {
            int width = source.Width;
            int height = source.Height;
            int radius = Math.Max(1, windowSize / 2);

            var gray = new int[height, width];
            var integral = new long[height + 1, width + 1];

            for (int y = 0; y < height; y++)
            {
                long rowSum = 0;
                for (int x = 0; x < width; x++)
                {
                    Color p = source.GetPixel(x, y);
                    int g = ClampToByte((p.R * 0.299f) + (p.G * 0.587f) + (p.B * 0.114f));
                    gray[y, x] = g;
                    rowSum += g;
                    integral[y + 1, x + 1] = integral[y, x + 1] + rowSum;
                }
            }

            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < height; y++)
            {
                int y0 = Math.Max(0, y - radius);
                int y1 = Math.Min(height - 1, y + radius);
                for (int x = 0; x < width; x++)
                {
                    int x0 = Math.Max(0, x - radius);
                    int x1 = Math.Min(width - 1, x + radius);
                    int area = (x1 - x0 + 1) * (y1 - y0 + 1);
                    long sum = integral[y1 + 1, x1 + 1] - integral[y0, x1 + 1] - integral[y1 + 1, x0] + integral[y0, x0];
                    int avg = (int)(sum / Math.Max(1, area));
                    int value = gray[y, x] < (avg - thresholdOffset) ? 0 : 255;
                    bitmap.SetPixel(x, y, Color.FromArgb(value, value, value));
                }
            }

            return bitmap;
        }

        private static Bitmap CreateDeskewBinaryBitmap(Bitmap binarySource)
            => (Bitmap)binarySource.Clone();

        private static int ClampToByte(float value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return (int)Math.Round(value);
        }

        private static int AlignUp(int value, int quantum)
        {
            if (quantum <= 0) return value;
            int remainder = value % quantum;
            return remainder == 0 ? value : value + (quantum - remainder);
        }

        private static string TrimQuotes(string value)
            => value.Trim().Trim('"');

        private static string SanitizeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "item";

            var sb = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.') sb.Append(ch);
                else sb.Append('_');
            }

            return sb.ToString();
        }

        private static async Task<ProcessRunResult> RunProcessAsync(string fileName, string args, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = args,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = workingDirectory,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);
                return new ProcessRunResult(process.ExitCode, stdout, stderr);
            }
            catch (Exception ex) when (
                ex is FileNotFoundException ||
                ex is DirectoryNotFoundException ||
                ex is System.ComponentModel.Win32Exception)
            {
                return new ProcessRunResult(-1, string.Empty, ex.Message);
            }
        }

        private static void ParseCommand(string command, out string fileName, out string argPrefix)
        {
            fileName = string.Empty;
            argPrefix = string.Empty;
            if (string.IsNullOrWhiteSpace(command)) return;

            string trimmed = command.Trim();
            if (trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                int endQuote = trimmed.IndexOf('"', 1);
                if (endQuote > 1)
                {
                    fileName = trimmed[1..endQuote];
                    argPrefix = trimmed[(endQuote + 1)..].Trim();
                    return;
                }
            }

            int firstSpace = trimmed.IndexOf(' ');
            if (firstSpace < 0)
            {
                fileName = trimmed;
                return;
            }

            fileName = trimmed[..firstSpace].Trim();
            argPrefix = trimmed[(firstSpace + 1)..].Trim();
        }

        private static string? TryResolveFromPath(string command)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where.exe",
                        Arguments = command,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                string stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout)) return null;

                return stdout
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            }
            catch
            {
                return null;
            }
        }

        private static string? TryFindMusicXml(string dir)
        {
            return Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    string ext = Path.GetExtension(path);
                    return string.Equals(ext, ".musicxml", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase);
                })
                .Where(path => !path.EndsWith("container.xml", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
        }

        private static string? TryFindMxl(string dir)
        {
            return Directory.GetFiles(dir, "*.mxl", SearchOption.AllDirectories)
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
        }

        private static async Task<string> ExtractMxlToMusicXmlAsync(string mxlPath, CancellationToken cancellationToken)
        {
            using var archive = ZipFile.OpenRead(mxlPath);
            string? musicXmlEntryPath = null;

            var containerEntry = archive.GetEntry("META-INF/container.xml");
            if (containerEntry != null)
            {
                using var stream = containerEntry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                string xml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                const string marker = "full-path=\"";
                int idx = xml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    int start = idx + marker.Length;
                    int end = xml.IndexOf('"', start);
                    if (end > start) musicXmlEntryPath = xml[start..end];
                }
            }

            if (string.IsNullOrWhiteSpace(musicXmlEntryPath))
            {
                musicXmlEntryPath = archive.Entries
                    .Where(e => e.FullName.EndsWith(".musicxml", StringComparison.OrdinalIgnoreCase)
                             || e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .Where(e => !e.FullName.EndsWith("container.xml", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.Length)
                    .Select(e => e.FullName)
                    .FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(musicXmlEntryPath)) throw new InvalidDataException("No MusicXML entry found in MXL.");

            var targetEntry = archive.GetEntry(musicXmlEntryPath);
            if (targetEntry == null) throw new InvalidDataException("Invalid MusicXML entry in MXL.");

            string outDir = Path.Combine(Path.GetTempPath(), "MusicBoxOmr", "mxl", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(mxlPath) + ".musicxml");

            using (var entryStream = targetEntry.Open())
            using (var fileStream = File.Create(outPath))
            {
                await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            return outPath;
        }

        private static string? FirstNonEmptyLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        }

        private sealed record OmrInputVariant(string InputPath, string Name, int? PageIndex, bool AllowHomr, bool AllowAudiveris);
        private sealed record ProcessRunResult(int ExitCode, string Stdout, string Stderr);
        private sealed record EngineExecutionResult(OmrEngineRunInfo RunInfo, OmrCandidateInfo? Candidate);
    }

    public sealed class OmrRecognitionResult
    {
        private OmrRecognitionResult(bool success, string musicXmlPath, string message, OmrRecognitionDiagnostics diagnostics)
        {
            Success = success;
            MusicXmlPath = musicXmlPath;
            Message = message;
            Diagnostics = diagnostics;
        }

        public bool Success { get; }
        public string MusicXmlPath { get; }
        public string Message { get; }
        public OmrRecognitionDiagnostics Diagnostics { get; }

        public static OmrRecognitionResult Ok(string musicXmlPath, string message, OmrRecognitionDiagnostics diagnostics)
            => new(true, musicXmlPath, message, diagnostics);

        public static OmrRecognitionResult Fail(string message, OmrRecognitionDiagnostics diagnostics)
            => new(false, string.Empty, message, diagnostics);
    }

    public sealed class OmrRecognitionDiagnostics
    {
        public string ArtifactRoot { get; set; } = string.Empty;
        public List<OmrEngineRunInfo> EngineRuns { get; } = new();
        public List<OmrCandidateInfo> Candidates { get; } = new();
        public OmrCandidateInfo? BestCandidate { get; set; }
    }

    public sealed class OmrEngineRunInfo
    {
        public string Engine { get; set; } = string.Empty;
        public string InputVariant { get; set; } = string.Empty;
        public int? PageIndex { get; set; }
        public int ExitCode { get; set; }
        public string StderrSummary { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
    }

    public sealed class OmrCandidateInfo
    {
        public string Engine { get; set; } = string.Empty;
        public string InputVariant { get; set; } = string.Empty;
        public int? PageIndex { get; set; }
        public string MusicXmlPath { get; set; } = string.Empty;
        public int PitchedNotes { get; set; }
        public int RestNotes { get; set; }
        public int Measures { get; set; }
        public int DistinctOnsets { get; set; }
        public int RhythmicCompletenessBonus { get; set; }
        public int StructuralBonus { get; set; }
        public int ExpressionMarks { get; set; }
        public int SlurMarks { get; set; }
        public int RepeatBarlines { get; set; }
        public int FinalBarlines { get; set; }
        public int KeyChanges { get; set; }
        public int TimeChanges { get; set; }
        public int Penalties { get; set; }
        public int QualityScore { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsValid { get; set; }
    }

    public readonly record struct OmrProgressInfo(string Stage, int Current, int Total, string Detail);
}

using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using 音乐魔盒.Models;
using 音乐魔盒.Services;
using 音乐魔盒.ViewModels;

namespace 音乐魔盒
{
    public sealed partial class ConvertPage : Page
    {
        private static ConvertPageStateCache? s_cache;

        private MainViewModel? _viewModel;
        private readonly JianpuConverter _jianpuConverter = new();
        private readonly MusicXmlExporter _musicXmlExporter = new();
        private JianpuConverter.NativePreviewModel? _nativePreview;
        private string _latestPreviewHtml = string.Empty;
        private bool _previewLoaded;
        private ScoreProject? _sourceProject;

        private readonly CanvasTextFormat _titleFormat = new()
        {
            FontFamily = "Microsoft YaHei UI",
            FontSize = 32f,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private readonly CanvasTextFormat _metaFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 16f,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private readonly CanvasTextFormat _tokenFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 25f,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private readonly CanvasTextFormat _accidentalFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 14f,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private readonly CanvasTextFormat _chordDegreeFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 21f,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private readonly CanvasTextFormat _chordAccidentalFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 12f,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private readonly CanvasTextFormat _barFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 28f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private readonly CanvasTextFormat _braceFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 94f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private readonly CanvasTextFormat _measureNumberFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 20f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private readonly CanvasTextFormat _statusFormat = new()
        {
            FontFamily = "Microsoft YaHei UI",
            FontSize = 18f,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };

        private const float ChordRowStep = 19.5f;
        private const float ChordDotSpacing = 4.2f;

        public ConvertPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            Loaded += ConvertPage_Loaded;
            ActualThemeChanged += ConvertPage_ActualThemeChanged;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _viewModel = vm;
                DataContext = vm;
            }
            else if (_viewModel == null && DataContext is MainViewModel existingVm)
            {
                _viewModel = existingVm;
            }

            RestorePageStateFromCache();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SavePageStateToCache();
        }

        private async void ConvertPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_sourceProject != null && _nativePreview != null)
            {
                UpdateCanvasSize();
                JianpuCanvas?.Invalidate();
                return;
            }

            await RefreshPreviewAsync();
        }

        private async void ConvertPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            await RefreshPreviewAsync();
        }

        public void HandleTitleBarImportCommand(string command)
        {
            string normalized = command?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized == "import_file")
            {
                _ = ImportFromPickerAsync();
                return;
            }

            if (normalized == "import_editor")
            {
                _ = ImportFromEditorAsync();
            }
        }

        public void HandleTitleBarFormatCommand(string command)
        {
            string normalized = command?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized == "staff_to_jianpu")
            {
                _ = RefreshPreviewAsync();
            }
        }

        public void HandleTitleBarExportCommand(string command)
        {
            string normalized = command?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized == "export_pdf")
            {
                _ = ExportPreviewToPdfAsync();
                return;
            }

            if (normalized == "export_musicxml")
            {
                _ = ExportSourceToMusicXmlAsync();
            }
        }

        private async Task ImportFromPickerAsync()
        {
            if (_viewModel == null)
            {
                SetStatus(Loc("未找到工程数据。", "Project data not found."));
                return;
            }

            string? path = await PickOpenPathAsync(".json", ".musicxml", ".xml");
            if (string.IsNullOrWhiteSpace(path))
            {
                SetStatus(Loc("已取消导入。", "Import canceled."));
                return;
            }

            try
            {
                string ext = Path.GetExtension(path)?.Trim().ToLowerInvariant() ?? string.Empty;
                if (ext == ".json")
                {
                    _viewModel.LoadProjectFromPath(path);
                }
                else if (ext is ".musicxml" or ".xml")
                {
                    _viewModel.ImportMusicXmlFromPath(path);
                }
                else
                {
                    SetStatus(Loc("不支持的文件类型。", "Unsupported file type."));
                    return;
                }

                _sourceProject = CloneProject(_viewModel.Project);
                await RefreshPreviewAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"{Loc("导入失败", "Import failed")}: {ex.Message}");
            }
        }

        private async Task ImportFromEditorAsync()
        {
            if (_viewModel == null)
            {
                SetStatus(Loc("未找到工程数据。", "Project data not found."));
                return;
            }

            _sourceProject = CloneProject(_viewModel.Project);
            await RefreshPreviewAsync();
            SetStatus(Loc("已从编辑页导入。", "Imported from editor."));
        }

        private async Task RefreshPreviewAsync()
        {
            if (_sourceProject == null)
            {
                _previewLoaded = false;
                _nativePreview = null;
                _latestPreviewHtml = string.Empty;
                JianpuCanvas?.Invalidate();
                SavePageStateToCache();
                SetStatus(Loc("请先在“导入”菜单里选择“编辑页导入”或“从文件导入”。", "Choose Import -> From Editor or From File first."));
                return;
            }

            if (_viewModel == null)
            {
                SetStatus(Loc("未找到工程数据。", "Project data not found."));
                return;
            }

            try
            {
                SetStatus(Loc("正在转换简谱...", "Converting to jianpu..."));
                bool darkTheme = ActualTheme == ElementTheme.Dark;
                _latestPreviewHtml = _jianpuConverter.BuildPreviewHtml(_sourceProject, darkTheme);

                float viewportWidth = (float)Math.Max(760d, (PreviewScrollViewer?.ActualWidth ?? RootGrid?.ActualWidth ?? 1200d) - 92d);
                _nativePreview = _jianpuConverter.BuildNativePreviewModel(_sourceProject, viewportWidth);
                _previewLoaded = true;
                UpdateCanvasSize();
                JianpuCanvas.Invalidate();
                SavePageStateToCache();
                SetStatus(Loc("简谱预览已更新（原生渲染）。", "Jianpu preview updated (native rendering)."));
            }
            catch (Exception ex)
            {
                _previewLoaded = false;
                _nativePreview = null;
                JianpuCanvas?.Invalidate();
                SavePageStateToCache();
                SetStatus($"{Loc("转换失败", "Conversion failed")}: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private static ScoreProject CloneProject(ScoreProject source)
        {
            var project = new ScoreProject
            {
                Title = source.Title,
                Bpm = source.Bpm,
                TimeSignature = new TimeSignature(source.TimeSignature.Numerator, source.TimeSignature.Denominator),
                KeySignature = new KeySignature(source.KeySignature.Fifths, source.KeySignature.Mode),
                Ppq = source.Ppq,
                UpdatedAt = source.UpdatedAt
            };

            project.Notes = source.Notes.Select(n => new NoteEvent
            {
                Midi = n.Midi,
                StartTick = n.StartTick,
                DurationTicks = n.DurationTicks,
                BaseDurationTicks = n.BaseDurationTicks,
                AugmentationDots = n.AugmentationDots,
                IsRest = n.IsRest,
                Voice = n.Voice,
                Accidental = n.Accidental,
                IsStaccato = n.IsStaccato,
                IsStaccatissimo = n.IsStaccatissimo,
                IsAccent = n.IsAccent,
                Ornament = n.Ornament,
                OrnamentOffsetX = n.OrnamentOffsetX,
                OrnamentOffsetY = n.OrnamentOffsetY,
                GraceOrnamentOffsetX = n.GraceOrnamentOffsetX,
                GraceOrnamentOffsetY = n.GraceOrnamentOffsetY,
                TieStart = n.TieStart,
                TieEnd = n.TieEnd,
                BeamGroupId = n.BeamGroupId,
                StemUpOverride = n.StemUpOverride,
                PreferTrebleStaff = n.PreferTrebleStaff
            }).ToList();

            project.ExpressionMarks = source.ExpressionMarks.Select(m => new ExpressionMark
            {
                Code = m.Code,
                StartTick = m.StartTick,
                StaffStepOffset = m.StaffStepOffset,
                SpanBeats = m.SpanBeats,
                ShapeHeightSteps = m.ShapeHeightSteps,
                SlopeSteps = m.SlopeSteps
            }).ToList();

            project.TimeSignatureChanges = source.TimeSignatureChanges.Select(c => new TimeSignatureChange
            {
                Tick = c.Tick,
                Numerator = c.Numerator,
                Denominator = c.Denominator
            }).ToList();
            project.KeySignatureChanges = source.KeySignatureChanges.Select(c => new KeySignatureChange
            {
                Tick = c.Tick,
                Fifths = c.Fifths,
                Mode = c.Mode
            }).ToList();
            project.StaffClefs = source.StaffClefs.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            project.LayoutSystemMeasureCounts = source.LayoutSystemMeasureCounts.ToList();
            project.LayoutBarlineOffsets = source.LayoutBarlineOffsets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            project.LayoutMeasuresPerSystemOverride = source.LayoutMeasuresPerSystemOverride;
            project.LayoutAutoMeasuresPerSystem = source.LayoutAutoMeasuresPerSystem;
            return project;
        }

        private void PreviewScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCanvasSize();
            JianpuCanvas?.Invalidate();
        }

        private void UpdateCanvasSize()
        {
            if (JianpuCanvas == null)
            {
                return;
            }

            float viewportWidth = (float)Math.Max(760d, PreviewScrollViewer?.ActualWidth ?? RootGrid?.ActualWidth ?? 1200d);
            float viewportHeight = (float)Math.Max(560d, PreviewScrollViewer?.ActualHeight ?? RootGrid?.ActualHeight ?? 720d);
            float targetWidth = Math.Max(viewportWidth - 4f, _nativePreview?.ContentWidth ?? 1200f);
            float targetHeight = Math.Max(viewportHeight - 4f, EstimateNativeContentHeight());

            JianpuCanvas.Width = targetWidth;
            JianpuCanvas.Height = targetHeight;
        }

        private void JianpuCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            Color ink = GetInkColor();
            Color subInk = Color.FromArgb((byte)(ink.A == 255 ? 190 : ink.A), ink.R, ink.G, ink.B);
            Color barInk = Color.FromArgb(255, 0, 0, 0);
            var ds = args.DrawingSession;

            if (_nativePreview == null)
            {
                ds.DrawText(Loc("当前工程没有音符。", "No notes in current project."), 20f, 20f, subInk, _statusFormat);
                return;
            }

            float canvasWidth = (float)Math.Max(200d, JianpuCanvas.ActualWidth);
            float left = 34f;
            float braceX = left;
            float rowStartX = left + 54f;

            ds.DrawText(_nativePreview.Title, 0f, 62f, canvasWidth, 60f, ink, _titleFormat);
            string meta = $"1={_nativePreview.KeyText}   {_nativePreview.MeterText}   {_nativePreview.Bpm} BPM";
            ds.DrawText(meta, rowStartX, 154f, subInk, _metaFormat);

            float systemTop = 226f;
            foreach (var system in _nativePreview.Systems)
            {
                float upperExtraRise = EstimateUpperChordRise(system);
                float extraSystemPadding = Math.Max(0f, upperExtraRise - 10f);
                float upperRowTop = systemTop + 26f + extraSystemPadding;
                float lowerRowTop = upperRowTop + 70f;

                ds.DrawText(system.StartMeasureNumber.ToString(), left - 10f, systemTop - 18f, subInk, _measureNumberFormat);
                ds.DrawText("{", braceX + 6f, systemTop + 16f, subInk, _braceFormat);

                DrawStaffRow(ds, system, upperRowTop, isUpper: true, ink, subInk, barInk, rowStartX, canvasWidth);
                DrawStaffRow(ds, system, lowerRowTop, isUpper: false, ink, subInk, barInk, rowStartX, canvasWidth);

                systemTop += 188f + extraSystemPadding;
            }

            if (!_previewLoaded)
            {
                ds.DrawText(Loc("正在准备预览...", "Preparing preview..."), left, systemTop + 8f, subInk, _statusFormat);
            }
        }

        private float EstimateNativeContentHeight()
        {
            if (_nativePreview == null)
            {
                return 720f;
            }

            float systemTop = 226f;
            foreach (var system in _nativePreview.Systems)
            {
                float upperExtraRise = EstimateUpperChordRise(system);
                float extraSystemPadding = Math.Max(0f, upperExtraRise - 10f);
                systemTop += 188f + extraSystemPadding;
            }

            return Math.Max(720f, systemTop + 40f);
        }

        private static float EstimateUpperChordRise(JianpuConverter.NativeSystem system)
        {
            if (system?.Measures == null || system.Measures.Count == 0)
            {
                return 0f;
            }

            float maxRise = 0f;
            foreach (var measure in system.Measures)
            {
                var tokens = measure?.UpperTokens;
                if (tokens == null || tokens.Count == 0)
                {
                    continue;
                }

                foreach (var token in tokens)
                {
                    if (token?.ChordPitches == null || token.ChordPitches.Count <= 1)
                    {
                        continue;
                    }

                    int rowCount = token.ChordPitches.Count;
                    int maxTopDots = token.ChordPitches.Max(p => Math.Max(0, p.TopDots));
                    float rise = GetChordVerticalRise(rowCount, maxTopDots);
                    maxRise = Math.Max(maxRise, rise);
                }
            }

            return maxRise;
        }

        private void DrawStaffRow(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            JianpuConverter.NativeSystem system,
            float rowTop,
            bool isUpper,
            Color ink,
            Color subInk,
            Color barInk,
            float rowStartX,
            float canvasWidth)
        {
            float x = rowStartX;
            const float keyWidth = 56f;
            const float barWidth = 22f;
            float rightEdge = Math.Max(rowStartX + 160f, canvasWidth - 24f);

            if (!string.IsNullOrWhiteSpace(system.LeftKeyText))
            {
                if (isUpper)
                {
                    ds.DrawText(system.LeftKeyText, x, rowTop + 2f, subInk, _metaFormat);
                }

                x += keyWidth;
            }

            DrawBarText(ds, system.LeftBarText, x, rowTop + 11f, barInk);
            x += barWidth;

            for (int measureIndex = 0; measureIndex < system.Measures.Count; measureIndex++)
            {
                var measure = system.Measures[measureIndex];
                var tokens = isUpper ? measure.UpperTokens : measure.LowerTokens;
                bool hasLeftBoundaryKey = measureIndex == 0
                    ? !string.IsNullOrWhiteSpace(system.LeftKeyText)
                    : !string.IsNullOrWhiteSpace(system.Measures[measureIndex - 1].RightKeyText);
                bool hasRightBoundaryKey = !string.IsNullOrWhiteSpace(measure.RightKeyText);

                DrawMeasureTokens(
                    ds,
                    tokens,
                    x,
                    rowTop,
                    measure.Width,
                    measure.MeasureTicks,
                    measure.BeatTicks,
                    ink,
                    hasLeftBoundaryKey,
                    hasRightBoundaryKey);
                x += measure.Width;

                if (!string.IsNullOrWhiteSpace(measure.RightKeyText))
                {
                    if (x + keyWidth + barWidth > rightEdge)
                    {
                        break;
                    }

                    if (isUpper)
                    {
                        ds.DrawText(measure.RightKeyText, x, rowTop + 2f, subInk, _metaFormat);
                    }

                    x += keyWidth;
                }
                else if (x + barWidth > rightEdge)
                {
                    // Avoid rendering a clipped trailing barline at system end.
                    break;
                }

                DrawBarText(ds, measure.RightBarText, x, rowTop + 11f, barInk);
                x += barWidth;
            }
        }

        private static float AlignPixel(float value, float thickness)
        {
            float rounded = (float)Math.Round(value);
            return ((int)Math.Round(thickness)) % 2 == 1 ? rounded + 0.5f : rounded;
        }

        private void DrawBarText(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, string text, float x, float y, Color color)
        {
            string safe = string.IsNullOrWhiteSpace(text) ? "|" : text;
            const float barWidth = 22f;
            float left = x;
            float right = x + barWidth;
            float center = (left + right) * 0.5f;
            float top = y + 1f;
            float bottom = y + 34f;
            float thin = 1.3f;
            float thick = 2.2f;

            void DrawVertical(float cx, float thickness)
            {
                float px = AlignPixel(cx, thickness);
                ds.DrawLine(px, top, px, bottom, color, thickness);
            }

            void DrawRepeatDots(float cx)
            {
                float dotX = AlignPixel(cx, 1f);
                ds.FillCircle(dotX, y + 12f, 1.4f, color);
                ds.FillCircle(dotX, y + 24f, 1.4f, color);
            }

            switch (safe)
            {
                case "||":
                    DrawVertical(center - 2.2f, thin);
                    DrawVertical(center + 2.2f, thick);
                    break;
                case "|:":
                    DrawVertical(center - 1.2f, thick);
                    DrawRepeatDots(center + 4.2f);
                    break;
                case ":|":
                    DrawRepeatDots(center - 4.2f);
                    DrawVertical(center + 1.2f, thick);
                    break;
                case ":|:":
                    DrawRepeatDots(center - 4.4f);
                    DrawVertical(center, thick);
                    DrawRepeatDots(center + 4.4f);
                    break;
                default:
                    DrawVertical(center, thin);
                    break;
            }
        }

        private void DrawMeasureTokens(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            IReadOnlyList<JianpuConverter.NativeToken> tokens,
            float x,
            float rowTop,
            float measureWidth,
            int measureTicks,
            int beatTicks,
            Color ink,
            bool hasLeftBoundaryKey,
            bool hasRightBoundaryKey)
        {
            if (tokens == null || tokens.Count == 0)
            {
                return;
            }

            float leftInset = hasLeftBoundaryKey ? 8f : 5f;
            float rightInset = hasRightBoundaryKey ? 11f : 7f;
            float innerX = x + leftInset;
            float available = Math.Max(24f, measureWidth - leftInset - rightInset);
            float noteY = rowTop + 13.6f;
            float measureClipStart = innerX + 1f;
            float measureClipEnd = Math.Max(measureClipStart + 2f, x + measureWidth - rightInset - 1f);
            int safeMeasureTicks = Math.Max(1, measureTicks);
            int safeBeatTicks = Math.Max(1, beatTicks);
            int beatCount = Math.Max(1, (int)Math.Ceiling(safeMeasureTicks / (double)safeBeatTicks));
            float beatGap = 7.2f;
            float timelineWidth = Math.Max(18f, available - Math.Max(0, beatCount - 1) * beatGap);

            float ResolveTimelineX(int tickInMeasure)
            {
                int clamped = Math.Clamp(tickInMeasure, 0, safeMeasureTicks);
                int beatIndex = Math.Min(Math.Max(0, beatCount - 1), clamped / safeBeatTicks);
                return innerX
                    + timelineWidth * (clamped / (float)safeMeasureTicks)
                    + beatIndex * beatGap;
            }

            var orderedTokens = tokens
                .OrderBy(t => t.TickInMeasure)
                .ThenBy(t => t.DurationTicks)
                .ToList();

            var placements = new List<TokenPlacement>(orderedTokens.Count);
            float previousBodyRight = measureClipStart - 2f;
            float previousAccidentalRight = measureClipStart - 2f;
            float minNoteGap = 1.6f;
            for (int i = 0; i < orderedTokens.Count; i++)
            {
                var token = orderedTokens[i];
                float anchorX = ResolveTimelineX(token.TickInMeasure);
                float tokenWidth = ResolveTokenRenderWidth(token, safeBeatTicks);
                float xLeft = anchorX - tokenWidth * 0.5f;
                xLeft = Math.Clamp(xLeft, measureClipStart, measureClipEnd - 6f);
                if (xLeft < previousBodyRight + minNoteGap)
                {
                    xLeft = Math.Min(measureClipEnd - 6f, previousBodyRight + minNoteGap);
                }

                TokenDrawMetrics metrics = DrawTokenText(
                    ds,
                    token,
                    xLeft,
                    noteY,
                    tokenWidth,
                    ink,
                    measureClipStart,
                    measureClipEnd,
                    previousAccidentalRight);

                previousBodyRight = Math.Max(previousBodyRight, metrics.BodyRight);
                previousAccidentalRight = Math.Max(previousAccidentalRight, metrics.AccidentalRight);
                placements.Add(new TokenPlacement(token, xLeft, tokenWidth, metrics.CenterX));
            }

            const float dotSpacing = 3.15f;
            const float dotRadius = 1.2f;
            foreach (var p in placements)
            {
                float topBase = noteY - 1.1f;
                for (int d = 0; d < p.Token.TopDots; d++)
                {
                    ds.FillCircle(p.CenterX, topBase - d * dotSpacing, dotRadius, ink);
                }
            }

            int maxUnderline = placements.Count == 0 ? 0 : placements.Max(p => p.Token.UnderlineCount);
            Color lineColor = Color.FromArgb(255, 0, 0, 0);
            float lineBase = noteY + 22.4f;
            float lineStep = 2.1f;
            for (int level = 0; level < maxUnderline; level++)
            {
                int i = 0;
                while (i < placements.Count)
                {
                    if (placements[i].Token.UnderlineCount <= level)
                    {
                        i++;
                        continue;
                    }

                    float startX = placements[i].CenterX - 4.2f;
                    float endX = placements[i].CenterX + 4.2f;
                    int runBeat = placements[i].Token.TickInMeasure / safeBeatTicks;
                    i++;
                    while (i < placements.Count && placements[i].Token.UnderlineCount > level)
                    {
                        int nextBeat = placements[i].Token.TickInMeasure / safeBeatTicks;
                        if (nextBeat != runBeat)
                        {
                            break;
                        }

                        endX = placements[i].CenterX + 4.2f;
                        i++;
                    }

                    startX = Math.Clamp(startX, measureClipStart, measureClipEnd);
                    endX = Math.Clamp(endX, measureClipStart, measureClipEnd);
                    if (endX <= startX + 0.8f)
                    {
                        continue;
                    }

                    float y = lineBase + level * lineStep;
                    float py = AlignPixel(y, 1f);
                    ds.DrawLine(startX, py, endX, py, lineColor, 0.65f);
                }
            }

            // Draw lower octave dots after underlines so mixed cases stay clear.
            foreach (var p in placements)
            {
                float downBase = lineBase + p.Token.UnderlineCount * lineStep + 3.25f;
                for (int d = 0; d < p.Token.BottomDots; d++)
                {
                    ds.FillCircle(p.CenterX, downBase + d * dotSpacing, dotRadius, ink);
                }
            }
        }

        private TokenDrawMetrics DrawTokenText(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            JianpuConverter.NativeToken token,
            float x,
            float y,
            float width,
            Color color,
            float clipStart,
            float clipEnd,
            float previousAccidentalRight)
        {
            if (token.IsChord)
            {
                return DrawChordTokenText(ds, token, x, y, width, color, clipStart, clipEnd);
            }

            string text = string.IsNullOrWhiteSpace(token.Text) ? "0" : token.Text;
            int splitIndex = 0;
            while (splitIndex < text.Length && IsAccidentalChar(text[splitIndex]))
            {
                splitIndex++;
            }

            string accidentalPrefix = splitIndex > 0 ? text[..splitIndex] : string.Empty;
            string bodyAndExtend = splitIndex < text.Length ? text[splitIndex..] : "0";
            if (string.IsNullOrWhiteSpace(bodyAndExtend))
            {
                bodyAndExtend = "0";
            }

            int extendCount = 0;
            while (extendCount < bodyAndExtend.Length && bodyAndExtend[^(extendCount + 1)] == '-')
            {
                extendCount++;
            }

            string bodyText = extendCount > 0 ? bodyAndExtend[..^extendCount] : bodyAndExtend;
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                bodyText = "0";
            }

            float accidentalWidth = 0f;
            for (int i = 0; i < accidentalPrefix.Length; i++)
            {
                accidentalWidth += MeasureGlyphWidth(ds, accidentalPrefix[i].ToString(), _accidentalFormat);
            }

            float bodyWidth = MeasureGlyphWidth(ds, bodyText, _tokenFormat);
            float extendWidth = MeasureExtendDrawWidth(ds, _tokenFormat, extendCount);
            float totalWidth = bodyWidth + (extendWidth > 0f ? 2.2f + extendWidth : 0f);
            float bodyStart = x + Math.Max(0f, (width - totalWidth) * 0.5f);
            bodyStart = Math.Clamp(bodyStart, clipStart + 1f, Math.Max(clipStart + 1f, clipEnd - totalWidth - 1f));

            float accidentalRight = float.MinValue;
            if (accidentalWidth > 0f)
            {
                float accidentalGap = 0.55f;
                float minGapToBody = 0.25f;
                float accidentalStart = bodyStart - accidentalGap - accidentalWidth;
                float maxAllowedStart = bodyStart - minGapToBody - accidentalWidth;
                accidentalStart = Math.Min(accidentalStart, maxAllowedStart);
                if (previousAccidentalRight > clipStart)
                {
                    float minStartAfterPrevious = previousAccidentalRight + 0.55f;
                    accidentalStart = Math.Max(accidentalStart, minStartAfterPrevious);
                    accidentalStart = Math.Min(accidentalStart, maxAllowedStart);
                }

                accidentalStart = Math.Max(clipStart, accidentalStart);
                float cursor = accidentalStart;
                for (int i = 0; i < accidentalPrefix.Length; i++)
                {
                    string glyph = accidentalPrefix[i].ToString();
                    float w = MeasureGlyphWidth(ds, glyph, _accidentalFormat);
                    ds.DrawText(glyph, cursor, y - 4.9f, color, _accidentalFormat);
                    cursor += w;
                }

                accidentalRight = cursor;
            }

            ds.DrawText(bodyText, bodyStart, y, color, _tokenFormat);
            float centerX = bodyStart + bodyWidth * 0.5f;
            float bodyRight = bodyStart + bodyWidth;
            if (extendCount > 0)
            {
                DrawExtendSegments(ds, bodyRight + 2.2f, y, extendCount, color, _tokenFormat);
            }

            if (accidentalWidth <= 0f)
            {
                accidentalRight = float.MinValue;
            }

            return new TokenDrawMetrics(bodyStart, bodyStart + totalWidth, accidentalRight, centerX);
        }

        private static float MeasureExtendDrawWidth(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, CanvasTextFormat format, int extendCount)
        {
            if (extendCount <= 0)
            {
                return 0f;
            }

            float dashWidth = MeasureGlyphWidth(ds, "-", format);
            float gap = Math.Max(6f, dashWidth * 1.25f);
            return extendCount * dashWidth + Math.Max(0, extendCount - 1) * gap;
        }

        private static void DrawExtendSegments(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float startX,
            float y,
            int extendCount,
            Color color,
            CanvasTextFormat format)
        {
            if (extendCount <= 0)
            {
                return;
            }

            float dashWidth = MeasureGlyphWidth(ds, "-", format);
            float gap = Math.Max(6f, dashWidth * 1.25f);
            float cursor = startX;
            for (int i = 0; i < extendCount; i++)
            {
                ds.DrawText("-", cursor, y, color, format);
                cursor += dashWidth + gap;
            }
        }

        private static float GetChordFontScale(int rowCount)
        {
            return rowCount switch
            {
                <= 3 => 1f,
                4 => 0.92f,
                5 => 0.84f,
                _ => 0.76f
            };
        }

        private static float GetChordRowStep(int rowCount)
        {
            float scale = GetChordFontScale(rowCount);
            return Math.Max(13.8f, ChordRowStep * (0.84f + scale * 0.16f));
        }

        private static float GetChordVerticalRise(int rowCount, int maxTopDots)
        {
            float scale = GetChordFontScale(rowCount);
            float rowStep = GetChordRowStep(rowCount);
            float dotSpacing = Math.Max(3.1f, ChordDotSpacing * (0.82f + scale * 0.18f));
            return Math.Max(0, rowCount - 1) * rowStep
                + Math.Max(0, maxTopDots) * dotSpacing
                + 6.5f * (0.88f + scale * 0.12f);
        }

        private static CanvasTextFormat CreateScaledTextFormat(CanvasTextFormat source, float scale)
        {
            return new CanvasTextFormat
            {
                FontFamily = source.FontFamily,
                FontSize = Math.Max(8f, source.FontSize * scale),
                FontWeight = source.FontWeight,
                HorizontalAlignment = source.HorizontalAlignment,
                VerticalAlignment = source.VerticalAlignment
            };
        }

        private TokenDrawMetrics DrawChordTokenText(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            JianpuConverter.NativeToken token,
            float x,
            float y,
            float width,
            Color color,
            float clipStart,
            float clipEnd)
        {
            if (token.ChordPitches == null || token.ChordPitches.Count == 0)
            {
                return new TokenDrawMetrics(x, x + width, float.MinValue, x + width * 0.5f);
            }

            var rows = token.ChordPitches;
            int rowCount = rows.Count;
            float fontScale = GetChordFontScale(rowCount);
            float rowStep = GetChordRowStep(rowCount);
            float dotSpacing = Math.Max(3.1f, ChordDotSpacing * (0.82f + fontScale * 0.18f));
            float dotRadius = Math.Max(0.94f, 1.12f * (0.84f + fontScale * 0.16f));
            float accidentalYOffset = -4.3f * (0.82f + fontScale * 0.18f);
            float bottomDotBaseOffset = Math.Max(15.6f, 22.6f * (0.76f + fontScale * 0.24f));
            var degreeFormat = CreateScaledTextFormat(_chordDegreeFormat, fontScale);
            var accidentalFormat = CreateScaledTextFormat(_chordAccidentalFormat, Math.Min(1f, fontScale + 0.02f));

            var accidentalWidths = new float[rowCount];
            var degreeWidths = new float[rowCount];
            float maxAccidentalWidth = 0f;
            float maxDegreeWidth = 0f;
            for (int i = 0; i < rowCount; i++)
            {
                float accW = 0f;
                if (!string.IsNullOrWhiteSpace(rows[i].Accidental))
                {
                    foreach (char ch in rows[i].Accidental)
                    {
                        accW += MeasureGlyphWidth(ds, ch.ToString(), accidentalFormat);
                    }
                }

                float degW = MeasureGlyphWidth(ds, string.IsNullOrWhiteSpace(rows[i].Degree) ? "0" : rows[i].Degree, degreeFormat);
                accidentalWidths[i] = accW;
                degreeWidths[i] = degW;
                maxAccidentalWidth = Math.Max(maxAccidentalWidth, accW);
                maxDegreeWidth = Math.Max(maxDegreeWidth, degW);
            }

            float stackWidth = maxAccidentalWidth + maxDegreeWidth;
            float extendWidth = MeasureExtendDrawWidth(ds, degreeFormat, token.ExtendCount);
            float totalWidth = stackWidth + (extendWidth > 0f ? extendWidth + 2.2f : 0f);
            float bodyStart = x + Math.Max(0f, (width - totalWidth) * 0.5f);
            bodyStart = Math.Clamp(bodyStart, clipStart + 1f, Math.Max(clipStart + 1f, clipEnd - totalWidth - 1f));

            float degreeStart = bodyStart + maxAccidentalWidth;
            float topRowY = y - Math.Max(0, rowCount - 1) * rowStep + 5.4f;
            float maxAccidentalRight = float.MinValue;
            for (int i = 0; i < rowCount; i++)
            {
                var row = rows[i];
                float rowY = topRowY + i * rowStep;
                float cursor = degreeStart - accidentalWidths[i];

                if (!string.IsNullOrWhiteSpace(row.Accidental))
                {
                    foreach (char ch in row.Accidental)
                    {
                        string glyph = ch.ToString();
                        float w = MeasureGlyphWidth(ds, glyph, accidentalFormat);
                        ds.DrawText(glyph, cursor, rowY + accidentalYOffset, color, accidentalFormat);
                        cursor += w;
                    }

                    maxAccidentalRight = Math.Max(maxAccidentalRight, degreeStart);
                }

                string degreeText = string.IsNullOrWhiteSpace(row.Degree) ? "0" : row.Degree;
                float degreeX = degreeStart;
                ds.DrawText(degreeText, degreeX, rowY, color, degreeFormat);
                float degreeCenterX = degreeX + degreeWidths[i] * 0.5f;
                for (int d = 0; d < row.TopDots; d++)
                {
                    ds.FillCircle(degreeCenterX, rowY - 1.5f - d * dotSpacing, dotRadius, color);
                }

                for (int d = 0; d < row.BottomDots; d++)
                {
                    ds.FillCircle(degreeCenterX, rowY + bottomDotBaseOffset + d * dotSpacing, dotRadius, color);
                }
            }

            if (token.ExtendCount > 0)
            {
                DrawExtendSegments(ds, bodyStart + stackWidth + 2.2f, y, token.ExtendCount, color, degreeFormat);
            }

            if (maxAccidentalRight < -1e20f)
            {
                maxAccidentalRight = float.MinValue;
            }

            float bodyLeft = bodyStart;
            float bodyRight = bodyStart + totalWidth;
            float centerX = degreeStart + maxDegreeWidth * 0.5f;
            return new TokenDrawMetrics(bodyLeft, bodyRight, maxAccidentalRight, centerX);
        }

        private static float ResolveTokenRenderWidth(JianpuConverter.NativeToken token, int beatTicks)
        {
            int safeBeatTicks = Math.Max(1, beatTicks);
            float ratio = token.DurationTicks / (float)safeBeatTicks;
            float durationScale = ratio switch
            {
                <= 0.18f => 0.40f,
                <= 0.30f => 0.50f,
                <= 0.55f => 0.65f,
                <= 0.90f => 0.80f,
                <= 1.20f => 0.96f,
                <= 2.40f => 1.10f,
                _ => 1.24f
            };

            if (token.IsChord)
            {
                int rowCount = Math.Max(2, token.ChordPitches?.Count ?? 0);
                int maxGlyphCount = 1;
                if (token.ChordPitches != null && token.ChordPitches.Count > 0)
                {
                    maxGlyphCount = token.ChordPitches.Max(p =>
                        Math.Max(1, p.Degree?.Length ?? 0)
                        + (string.IsNullOrWhiteSpace(p.Accidental) ? 0 : p.Accidental.Length));
                }

                float chordWidth = 22f
                    + Math.Max(0, maxGlyphCount - 1) * 5.2f
                    + Math.Min(6, Math.Max(0, token.ExtendCount)) * 3.8f
                    + (rowCount >= 3 ? 1.4f : 0f);
                float rowScale = 0.88f + GetChordFontScale(rowCount) * 0.12f;
                float scaledChordWidth = chordWidth * MathF.Sqrt(Math.Max(0.35f, durationScale)) * rowScale;
                return Math.Clamp(scaledChordWidth, 14f, 56f);
            }

            float textWeight = MathF.Max(0f, (token.Text?.Length ?? 1) - 1) * 1.6f;
            float width = 32f * durationScale + textWeight;
            if (HasLeadingAccidental(token.Text))
            {
                width += 2.2f;
            }

            return Math.Clamp(width, 11f, 46f);
        }

        private readonly struct TokenDrawMetrics
        {
            public TokenDrawMetrics(float bodyLeft, float bodyRight, float accidentalRight, float centerX)
            {
                BodyLeft = bodyLeft;
                BodyRight = bodyRight;
                AccidentalRight = accidentalRight;
                CenterX = centerX;
            }

            public float BodyLeft { get; }
            public float BodyRight { get; }
            public float AccidentalRight { get; }
            public float CenterX { get; }
        }

        private readonly struct TokenPlacement
        {
            public TokenPlacement(JianpuConverter.NativeToken token, float x, float width, float centerX)
            {
                Token = token;
                X = x;
                Width = width;
                CenterX = centerX;
            }

            public JianpuConverter.NativeToken Token { get; }
            public float X { get; }
            public float Width { get; }
            public float CenterX { get; }
        }

        private static bool IsAccidentalChar(char ch)
        {
            return ch == '#' || ch == '♯' || ch == 'b' || ch == '♭' || ch == '♮';
        }

        private static bool HasLeadingAccidental(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return IsAccidentalChar(text[0]);
        }

        private static float MeasureGlyphWidth(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            string text,
            CanvasTextFormat format)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 1f;
            }

            using var layout = new CanvasTextLayout(ds.Device, text, format, 0f, 0f);
            float w = (float)Math.Max(layout.LayoutBounds.Width, layout.DrawBounds.Width);
            return Math.Max(1f, w);
        }

        private Color GetInkColor()
        {
            return ActualTheme == ElementTheme.Dark
                ? Color.FromArgb(255, 238, 238, 238)
                : Color.FromArgb(255, 20, 20, 20);
        }

        private async Task ExportSourceToMusicXmlAsync()
        {
            if (_sourceProject == null)
            {
                SetStatus(Loc("请先导入工程，再导出 MusicXML。", "Import a score before exporting MusicXML."));
                return;
            }

            try
            {
                string suggested = GetSuggestedExportName("musicxml");
                string? path = await PickSavePathAsync(".musicxml", "MusicXML 乐谱", suggested);
                if (string.IsNullOrWhiteSpace(path))
                {
                    SetStatus(Loc("已取消导出。", "Export canceled."));
                    return;
                }

                _musicXmlExporter.Export(CloneProject(_sourceProject), path);
                SetStatus($"{Loc("已导出 MusicXML", "MusicXML exported")}: {path}");
            }
            catch (Exception ex)
            {
                SetStatus($"{Loc("导出失败", "Export failed")}: {ex.Message}");
            }
        }
        private async Task ExportPreviewToPdfAsync()
        {
            if (_viewModel == null)
            {
                SetStatus(Loc("未找到工程数据。", "Project data not found."));
                return;
            }

            try
            {
                await RefreshPreviewAsync();

                if (string.IsNullOrWhiteSpace(_latestPreviewHtml))
                {
                    SetStatus(Loc("预览尚未就绪，无法导出 PDF。", "Preview not ready. Cannot export PDF."));
                    return;
                }

                string suggested = GetSuggestedExportName("pdf");
                string? path = await PickSavePathAsync(".pdf", "PDF 文档", suggested);
                if (string.IsNullOrWhiteSpace(path))
                {
                    SetStatus(Loc("已取消导出。", "Export canceled."));
                    return;
                }

                await EnsurePdfExportWebViewReadyAsync();
                CoreWebView2? core = PdfExportWebView.CoreWebView2;
                if (core == null)
                {
                    SetStatus(Loc("PDF 导出内核未就绪。", "PDF export core is not ready."));
                    return;
                }

                var tcs = new TaskCompletionSource<bool>();
                void Handler(CoreWebView2 _, CoreWebView2NavigationCompletedEventArgs __)
                {
                    core.NavigationCompleted -= Handler;
                    tcs.TrySetResult(true);
                }

                core.NavigationCompleted += Handler;
                PdfExportWebView.NavigateToString(_latestPreviewHtml);
                _ = Task.Delay(5000).ContinueWith(_ => tcs.TrySetResult(false));
                await tcs.Task;

                CoreWebView2PrintSettings printSettings = core.Environment.CreatePrintSettings();
                printSettings.ShouldPrintBackgrounds = true;
                printSettings.ShouldPrintHeaderAndFooter = false;

                bool ok = await core.PrintToPdfAsync(path, printSettings);
                if (!ok)
                {
                    SetStatus(Loc("导出失败：WebView2 未生成 PDF。", "Export failed: WebView2 did not generate PDF."));
                    return;
                }

                _viewModel.SetStatus($"{Loc("已导出简谱 PDF", "Jianpu PDF exported")}: {Path.GetFileName(path)}");
                SetStatus($"{Loc("已导出", "Exported")}: {path}");
            }
            catch (Exception ex)
            {
                SetStatus($"{Loc("导出失败", "Export failed")}: {ex.Message}");
            }
        }

        private async Task EnsurePdfExportWebViewReadyAsync()
        {
            PdfExportWebView.DefaultBackgroundColor = Color.FromArgb(255, 255, 255, 255);
            if (PdfExportWebView.CoreWebView2 == null)
            {
                await PdfExportWebView.EnsureCoreWebView2Async();
            }
        }

        private async Task<string?> PickOpenPathAsync(params string[] extensions)
        {
            if (App.MainWindow == null)
            {
                return null;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };

            foreach (string ext in extensions)
            {
                if (string.IsNullOrWhiteSpace(ext)) continue;
                picker.FileTypeFilter.Add(ext.StartsWith('.') ? ext : $".{ext}");
            }

            if (picker.FileTypeFilter.Count == 0)
            {
                picker.FileTypeFilter.Add("*");
            }

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            StorageFile? file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        private async Task<string?> PickSavePathAsync(string extension, string fileTypeDescription, string suggestedFileName)
        {
            if (App.MainWindow == null)
            {
                return null;
            }

            string normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName)
            };
            picker.FileTypeChoices.Add(fileTypeDescription, new List<string> { normalizedExtension });

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            StorageFile? file = await picker.PickSaveFileAsync();
            return file?.Path;
        }

        private string GetSuggestedExportName(string extension)
        {
            string title = _viewModel?.Title ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Untitled";
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                title = title.Replace(invalid, '_');
            }

            string normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
            return $"{title}-简谱{normalizedExtension}";
        }

        private void RestorePageStateFromCache()
        {
            if (_sourceProject != null || s_cache == null)
            {
                return;
            }

            _sourceProject = s_cache.SourceProject != null ? CloneProject(s_cache.SourceProject) : null;
            _latestPreviewHtml = s_cache.LatestPreviewHtml ?? string.Empty;
            _previewLoaded = false;
            _nativePreview = null;
        }

        private void SavePageStateToCache()
        {
            s_cache = new ConvertPageStateCache
            {
                SourceProject = _sourceProject != null ? CloneProject(_sourceProject) : null,
                LatestPreviewHtml = _latestPreviewHtml,
                PreviewLoaded = _previewLoaded
            };
        }

        private void SetStatus(string text)
        {
            _viewModel?.SetStatus(text);
        }

        private static string Loc(string zh, string en)
        {
            try
            {
                string lang = AppSettingsService.Instance.ResolveLanguageTag();
                if (!string.IsNullOrWhiteSpace(lang) && lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                {
                    return en;
                }
            }
            catch
            {
            }

            return zh;
        }

        private sealed class ConvertPageStateCache
        {
            public ScoreProject? SourceProject { get; set; }
            public string? LatestPreviewHtml { get; set; }
            public bool PreviewLoaded { get; set; }
        }
    }
}
























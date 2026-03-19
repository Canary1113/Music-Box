using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using MusicBox.Models;
using MusicBox.Services;
using MusicBox.ViewModels;

namespace MusicBox
{
    public sealed partial class ComposeWorkbenchPage : Page
    {
        private const int CandidateCount = 3;

        private readonly SmartComposeService _service = new();
        private readonly PreviewPlaybackService _playback = new();
        private readonly List<SmartComposeResult> _candidates = new();
        private readonly bool[] _keptCandidates = new bool[CandidateCount];
        private MainViewModel? _viewModel;
        private int _seedBase;
        private int _generationSerial;

        public ComposeWorkbenchPage()
        {
            DebugTrace.Write("ComposeWorkbenchPage.ctor begin");
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            Loaded += ComposeWorkbenchPage_Loaded;
            Unloaded += ComposeWorkbenchPage_Unloaded;
            MoodBox.SelectedIndex = 0;
            LengthBox.SelectedIndex = 1;
            ApplyStaticButtonVisuals();
            ResetCandidateSurface();
            DebugTrace.Write("ComposeWorkbenchPage.ctor end");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                _viewModel = vm;
                DataContext = vm;
            }
        }

        private void ComposeWorkbenchPage_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel?.SetStatus("已打开智能创作页。");
        }

        private void ComposeWorkbenchPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _playback.Stop();
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            _seedBase = Environment.TickCount;
            _generationSerial++;
            Array.Fill(_keptCandidates, false);
            GenerateCandidates(preserveKept: false);
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            _seedBase = Environment.TickCount;
            _generationSerial++;
            GenerateCandidates(preserveKept: true);
        }

        private async void PlayCandidateButton_Click(object sender, RoutedEventArgs e)
        {
            int index = ResolveCandidateIndex(sender);
            if (!HasCandidate(index))
            {
                return;
            }

            SmartComposeResult result = _candidates[index];
            var playbackTask = _playback.TogglePlayAsync(index, result.Project);
            RefreshPlayButtons();
            await playbackTask;
            RefreshPlayButtons();
        }

        private void ApplyCandidateButton_Click(object sender, RoutedEventArgs e)
        {
            int index = ResolveCandidateIndex(sender);
            if (!HasCandidate(index) || _viewModel == null)
            {
                return;
            }

            SmartComposeResult result = _candidates[index];
            _viewModel.LoadProjectSnapshot(CloneProject(result.Project));
            _viewModel.SetStatus($"已采用方案 {(char)('A' + index)} 并写入编辑页：{result.Project.Title}");
            if (App.MainWindow is MainWindow window)
            {
                window.NavigateToPage("editor");
            }
        }

        private void SendCandidateButton_Click(object sender, RoutedEventArgs e)
        {
            int index = ResolveCandidateIndex(sender);
            if (!HasCandidate(index) || _viewModel == null)
            {
                return;
            }

            SmartComposeResult result = _candidates[index];
            _viewModel.LoadProjectSnapshot(CloneProject(result.Project));
            _viewModel.SetStatus($"已采用方案 {(char)('A' + index)} 并发送到转换页：{result.Project.Title}");
            if (App.MainWindow is MainWindow window)
            {
                window.NavigateToConvertAndImportEditor();
            }
        }

        private void ToggleKeepButton_Click(object sender, RoutedEventArgs e)
        {
            int index = ResolveCandidateIndex(sender);
            if (!HasCandidate(index))
            {
                return;
            }

            _keptCandidates[index] = !_keptCandidates[index];
            RefreshKeepButtons();
            UpdateStatusText();
        }

        private void GenerateCandidates(bool preserveKept)
        {
            try
            {
                _playback.Stop();
                SmartComposeRequest request = BuildRequest();
                request.Seed = _seedBase ^ (_generationSerial * 104729);

                IReadOnlyList<SmartComposeResult> generated = _service.GenerateCandidates(request, CandidateCount);
                var nextCandidates = new List<SmartComposeResult>(CandidateCount);

                for (int index = 0; index < CandidateCount; index++)
                {
                    if (preserveKept && _keptCandidates[index] && HasCandidate(index))
                    {
                        nextCandidates.Add(_candidates[index]);
                    }
                    else
                    {
                        nextCandidates.Add(generated[index]);
                    }
                }

                _candidates.Clear();
                _candidates.AddRange(nextCandidates);
                RenderCandidates();
                UpdateStatusText();
                _viewModel?.SetStatus("已生成 3 组智能创作候选方案。");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"生成失败：{ex.Message}";
                _viewModel?.SetStatus($"智能创作生成失败：{ex.Message}");
                ResetCandidateSurface("生成失败");
            }
        }

        private SmartComposeRequest BuildRequest()
        {
            string moodId = SelectedTag(MoodBox);
            string lengthId = SelectedTag(LengthBox);
            int autoBpm = ResolveAutoBpm(moodId);
            (int numerator, int denominator) = ResolveAutoMeter(moodId);
            int autoKey = ResolveAutoKey(moodId);
            KeyMode autoMode = ResolveAutoMode(moodId);

            return new SmartComposeRequest
            {
                Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "智能创作" : TitleBox.Text.Trim(),
                Bpm = autoBpm,
                Measures = ResolveMeasureCount(lengthId),
                KeyFifths = autoKey,
                Mode = autoMode,
                TimeSignature = new TimeSignature(numerator, denominator),
                MoodId = moodId,
                LengthId = lengthId,
                IncludeBass = true
            };
        }

        private void RenderCandidates()
        {
            RenderCandidate(0, Option1SummaryText, Option1ApplyButton, Option1SendButton);
            RenderCandidate(1, Option2SummaryText, Option2ApplyButton, Option2SendButton);
            RenderCandidate(2, Option3SummaryText, Option3ApplyButton, Option3SendButton);
            RefreshPlayButtons();
            RefreshKeepButtons();
        }

        private void RenderCandidate(int index, TextBlock summaryText, Button applyButton, Button sendButton)
        {
            if (!HasCandidate(index))
            {
                summaryText.Text = "等待生成";
                applyButton.IsEnabled = false;
                sendButton.IsEnabled = false;
                return;
            }

            SmartComposeResult result = _candidates[index];
            summaryText.Text = result.Summary;
            applyButton.IsEnabled = true;
            sendButton.IsEnabled = true;
        }

        private void ResetCandidateSurface(string placeholder = "等待生成")
        {
            _candidates.Clear();
            Option1SummaryText.Text = placeholder;
            Option2SummaryText.Text = placeholder;
            Option3SummaryText.Text = placeholder;
            Option1ApplyButton.IsEnabled = false;
            Option2ApplyButton.IsEnabled = false;
            Option3ApplyButton.IsEnabled = false;
            Option1SendButton.IsEnabled = false;
            Option2SendButton.IsEnabled = false;
            Option3SendButton.IsEnabled = false;
            RefreshPlayButtons();
            RefreshKeepButtons();
        }

        private void RefreshPlayButtons()
        {
            UpdatePlayButton(Option1PlayButton, 0);
            UpdatePlayButton(Option2PlayButton, 1);
            UpdatePlayButton(Option3PlayButton, 2);
        }

        private void UpdatePlayButton(Button button, int index)
        {
            bool enabled = HasCandidate(index);
            button.IsEnabled = enabled;
            bool isActive = enabled && _playback.IsPlaying && _playback.ActiveIndex == index;
            SetButtonContent(button, isActive ? Symbol.Pause : Symbol.Play, isActive ? "暂停" : "播放");
        }

        private void RefreshKeepButtons()
        {
            UpdateKeepButton(Option1KeepButton, 0);
            UpdateKeepButton(Option2KeepButton, 1);
            UpdateKeepButton(Option3KeepButton, 2);
        }

        private void UpdateKeepButton(Button button, int index)
        {
            bool enabled = HasCandidate(index);
            button.IsEnabled = enabled;
            ToolTipService.SetToolTip(button, enabled && _keptCandidates[index] ? "取消保留" : "保留方案");
            SetButtonContent(button, Symbol.Accept, null);
            TryApplyAccentStyle(button, enabled && _keptCandidates[index]);
        }

        private void ApplyStaticButtonVisuals()
        {
            TryApplyAccentStyle(GenerateButton, true);
            TryApplyAccentStyle(Option1PlayButton, true);
            TryApplyAccentStyle(Option2PlayButton, true);
            TryApplyAccentStyle(Option3PlayButton, true);
            SetButtonContent(RetryButton, Symbol.Refresh, "重试");
        }

        private void UpdateStatusText()
        {
            int keptCount = _keptCandidates.Count(value => value);
            StatusText.Text = keptCount > 0
                ? $"已生成 3 组候选方案，保留 {keptCount} 组。"
                : "已生成 3 组候选方案。";
        }

        private bool HasCandidate(int index)
        {
            return index >= 0 && index < _candidates.Count;
        }

        private static void TryApplyAccentStyle(Button button, bool applyAccent)
        {
            if (applyAccent)
            {
                Color accent = ResolveAccentColor();
                button.Background = new SolidColorBrush(accent);
                button.BorderBrush = new SolidColorBrush(accent);
                button.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                return;
            }

            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.BorderBrushProperty);
            button.ClearValue(Control.ForegroundProperty);
        }

        private static Color ResolveAccentColor()
        {
            if (Application.Current.Resources.TryGetValue("SystemAccentColor", out object value)
                && value is Color accentColor)
            {
                return accentColor;
            }

            return Color.FromArgb(255, 54, 103, 153);
        }

        private static void SetButtonContent(Button button, Symbol symbol, string? text)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = text == null ? 0 : 6,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(new SymbolIcon(symbol));
            if (!string.IsNullOrWhiteSpace(text))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = text,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            button.Content = panel;
        }

        private static int ResolveMeasureCount(string lengthId)
        {
            return lengthId switch
            {
                "long" => 64,
                "medium" => 32,
                _ => 16
            };
        }

        private static int ResolveAutoBpm(string moodId)
        {
            return moodId switch
            {
                "sleep" => 64,
                "sad" => 76,
                "positive" => 118,
                "hopeful" => 104,
                "tense" => 132,
                _ => 96
            };
        }

        private static (int Numerator, int Denominator) ResolveAutoMeter(string moodId)
        {
            return moodId switch
            {
                "sleep" => (6, 8),
                "sad" => (3, 4),
                _ => (4, 4)
            };
        }

        private static int ResolveAutoKey(string moodId)
        {
            return moodId switch
            {
                "sad" => -2,
                "sleep" => -3,
                "nostalgic" => -1,
                "positive" => 2,
                "hopeful" => 1,
                _ => 0
            };
        }

        private static KeyMode ResolveAutoMode(string moodId)
        {
            return moodId switch
            {
                "sad" or "sleep" or "nostalgic" => KeyMode.Minor,
                _ => KeyMode.Major
            };
        }

        private static int ResolveCandidateIndex(object sender)
        {
            return sender is FrameworkElement element
                && int.TryParse(element.Tag?.ToString(), out int index)
                ? index
                : -1;
        }

        private static string SelectedTag(ComboBox box)
        {
            return (box.SelectedItem as ComboBoxItem)?.Tag?.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        private static ScoreProject CloneProject(ScoreProject source)
        {
            return new ScoreProject
            {
                Title = source.Title,
                Bpm = source.Bpm,
                TimeSignature = new TimeSignature(source.TimeSignature.Numerator, source.TimeSignature.Denominator),
                KeySignature = new KeySignature(source.KeySignature.Fifths, source.KeySignature.Mode),
                Ppq = source.Ppq,
                UpdatedAt = source.UpdatedAt,
                Notes = source.Notes.Select(note => new NoteEvent
                {
                    Midi = note.Midi,
                    StartTick = note.StartTick,
                    DurationTicks = note.DurationTicks,
                    BaseDurationTicks = note.BaseDurationTicks,
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
                }).ToList(),
                ExpressionMarks = source.ExpressionMarks.Select(mark => new ExpressionMark
                {
                    Code = mark.Code,
                    StartTick = mark.StartTick,
                    StaffStepOffset = mark.StaffStepOffset,
                    SpanBeats = mark.SpanBeats,
                    ShapeHeightSteps = mark.ShapeHeightSteps,
                    SlopeSteps = mark.SlopeSteps
                }).ToList(),
                TimeSignatureChanges = source.TimeSignatureChanges.Select(change => new TimeSignatureChange
                {
                    Tick = change.Tick,
                    Numerator = change.Numerator,
                    Denominator = change.Denominator
                }).ToList(),
                KeySignatureChanges = source.KeySignatureChanges.Select(change => new KeySignatureChange
                {
                    Tick = change.Tick,
                    Fifths = change.Fifths,
                    Mode = change.Mode
                }).ToList(),
                StaffClefs = source.StaffClefs.ToDictionary(entry => entry.Key, entry => entry.Value),
                LayoutSystemMeasureCounts = source.LayoutSystemMeasureCounts.ToList(),
                LayoutBarlineOffsets = source.LayoutBarlineOffsets.ToDictionary(entry => entry.Key, entry => entry.Value),
                LayoutMeasuresPerSystemOverride = source.LayoutMeasuresPerSystemOverride,
                LayoutAutoMeasuresPerSystem = source.LayoutAutoMeasuresPerSystem
            };
        }
    }
}

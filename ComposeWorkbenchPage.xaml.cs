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
        private const string ChineseLanguage = "zh-Hans";
        private const string EnglishLanguage = "en-US";
        private static readonly string[] SharpNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        private static readonly string[] FlatNames = { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };

        private readonly SmartComposeService _service = new();
        private readonly PreviewPlaybackService _playback = new();
        private readonly List<SmartComposeResult> _candidates = new();
        private readonly bool[] _keptCandidates = new bool[CandidateCount];
        private readonly AppSettingsService _settings = AppSettingsService.Instance;
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
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            MoodBox.SelectedIndex = 0;
            LengthBox.SelectedIndex = 1;
            ApplyLocalizedText();
            ApplyStaticButtonVisuals();
            HideStatusText();
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
            ApplyLocalizedText();
            _viewModel?.SetStatus(T("compose.status.ready"));
        }

        private void ComposeWorkbenchPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _playback.Stop();
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            ApplyLocalizedText();
            RenderCandidates();
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            _seedBase = Environment.TickCount;
            _generationSerial++;
            Array.Fill(_keptCandidates, false);
            HideStatusText();
            GenerateCandidates(preserveKept: false);
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            _seedBase = Environment.TickCount;
            _generationSerial++;
            HideStatusText();
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
            _viewModel.SetStatus(TF("compose.status.applied", (char)('A' + index), result.Project.Title));

            if (App.MainWindow is MainWindow window)
            {
                window.NavigateToPage("editor");
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
                HideStatusText();
                _viewModel?.SetStatus(TF("compose.status.generated", CandidateCount));
            }
            catch (Exception ex)
            {
                string message = TF("compose.status.generation_failed", ex.Message);
                ShowStatusText(message);
                _viewModel?.SetStatus(message);
                ResetCandidateSurface(T("compose.status.failed_short"));
            }
        }

        private SmartComposeRequest BuildRequest()
        {
            string moodId = SelectedTag(MoodBox);
            string lengthId = SelectedTag(LengthBox);
            int autoBpm = ResolveAutoBpm(moodId);
            (int numerator, int denominator) = ResolveAutoMeter(moodId);

            return new SmartComposeRequest
            {
                Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? T("compose.default_title") : TitleBox.Text.Trim(),
                Bpm = autoBpm,
                Measures = ResolveMeasureCount(lengthId),
                KeyFifths = 0,
                Mode = KeyMode.Major,
                TimeSignature = new TimeSignature(numerator, denominator),
                MoodId = moodId,
                LengthId = lengthId,
                IncludeBass = false,
                AutoTonality = true,
                UseSustainPedal = false
            };
        }

        private void RenderCandidates()
        {
            RenderCandidate(0, Option1SummaryText, Option1ApplyButton);
            RenderCandidate(1, Option2SummaryText, Option2ApplyButton);
            RenderCandidate(2, Option3SummaryText, Option3ApplyButton);
            RefreshPlayButtons();
            RefreshKeepButtons();
        }

        private void RenderCandidate(int index, TextBlock summaryText, Button applyButton)
        {
            summaryText.FontSize = IsEnglishUi() ? 13 : 14;
            summaryText.LineHeight = IsEnglishUi() ? 20 : 22;

            if (!HasCandidate(index))
            {
                summaryText.Text = T("compose.action.waiting");
                applyButton.IsEnabled = false;
                return;
            }

            SmartComposeResult result = _candidates[index];
            summaryText.Text = BuildCandidateDetails(result);
            applyButton.IsEnabled = true;
        }

        private void ResetCandidateSurface(string? placeholder = null)
        {
            string text = string.IsNullOrWhiteSpace(placeholder)
                ? T("compose.action.waiting")
                : placeholder;

            _candidates.Clear();
            Option1SummaryText.Text = text;
            Option2SummaryText.Text = text;
            Option3SummaryText.Text = text;
            Option1ApplyButton.IsEnabled = false;
            Option2ApplyButton.IsEnabled = false;
            Option3ApplyButton.IsEnabled = false;
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
            SetButtonContent(
                button,
                isActive ? Symbol.Pause : Symbol.Play,
                T(isActive ? "compose.action.pause" : "compose.action.play"),
                14,
                IsEnglishUi() ? 13 : 14);
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
            ToolTipService.SetToolTip(button, T(enabled && _keptCandidates[index] ? "compose.action.unkeep" : "compose.action.keep"));
            SetButtonContent(button, Symbol.Accept, null, 12);
            TryApplyAccentStyle(button, enabled && _keptCandidates[index]);
        }

        private void ApplyLocalizedText()
        {
            bool isEnglish = IsEnglishUi();
            string localizedDefaultTitle = T("compose.default_title");
            string chineseDefaultTitle = LocalizationService.TranslateForLanguage(ChineseLanguage, "compose.default_title");
            string englishDefaultTitle = LocalizationService.TranslateForLanguage(EnglishLanguage, "compose.default_title");

            PageTitleText.Text = T("compose.page_title");
            PageTitleText.FontSize = isEnglish ? 23 : 24;

            PageSubtitleText.Text = T("compose.page_subtitle");
            PageSubtitleText.Visibility = Visibility.Collapsed;
            PageSubtitleText.FontSize = isEnglish ? 13 : 14;

            TitleLabelText.Text = T("compose.label.title");
            MoodLabelText.Text = T("compose.label.mood");
            LengthLabelText.Text = T("compose.label.length");

            if (string.IsNullOrWhiteSpace(TitleBox.Text)
                || string.Equals(TitleBox.Text.Trim(), chineseDefaultTitle, StringComparison.Ordinal)
                || string.Equals(TitleBox.Text.Trim(), englishDefaultTitle, StringComparison.Ordinal))
            {
                TitleBox.Text = localizedDefaultTitle;
            }

            MoodCalmItem.Content = T("compose.mood.calm");
            MoodPositiveItem.Content = T("compose.mood.positive");
            MoodSadItem.Content = T("compose.mood.sad");
            MoodSleepItem.Content = T("compose.mood.sleep");
            MoodHopefulItem.Content = T("compose.mood.hopeful");
            MoodNostalgicItem.Content = T("compose.mood.nostalgic");
            MoodDreamyItem.Content = T("compose.mood.dreamy");
            MoodTenseItem.Content = T("compose.mood.tense");

            LengthShortItem.Content = T("compose.length.short");
            LengthMediumItem.Content = T("compose.length.medium");
            LengthLongItem.Content = T("compose.length.long");

            Option1TitleText.Text = T("compose.option.a");
            Option2TitleText.Text = T("compose.option.b");
            Option3TitleText.Text = T("compose.option.c");

            SetPlainButtonContent(GenerateButton, T("compose.action.generate"), isEnglish ? 13 : 14);
            SetButtonContent(RetryButton, Symbol.Refresh, T("compose.action.retry"), 12, isEnglish ? 13 : 14);

            Option1ApplyButton.Content = T("compose.action.apply_to_editor");
            Option2ApplyButton.Content = T("compose.action.apply_to_editor");
            Option3ApplyButton.Content = T("compose.action.apply_to_editor");

            double compactTextSize = isEnglish ? 13 : 14;
            TitleLabelText.FontSize = compactTextSize;
            MoodLabelText.FontSize = compactTextSize;
            LengthLabelText.FontSize = compactTextSize;
            Option1ApplyButton.FontSize = compactTextSize;
            Option2ApplyButton.FontSize = compactTextSize;
            Option3ApplyButton.FontSize = compactTextSize;

            RenderCandidates();
            ApplyStaticButtonVisuals();
        }

        private void ApplyStaticButtonVisuals()
        {
            TryApplyAccentStyle(GenerateButton, true);
            TryApplyAccentStyle(Option1PlayButton, true);
            TryApplyAccentStyle(Option2PlayButton, true);
            TryApplyAccentStyle(Option3PlayButton, true);
        }

        private void ShowStatusText(string message)
        {
            StatusText.Text = message;
            StatusText.FontSize = IsEnglishUi() ? 13 : 14;
            StatusText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void HideStatusText()
        {
            StatusText.Text = string.Empty;
            StatusText.Visibility = Visibility.Collapsed;
        }

        private bool HasCandidate(int index)
        {
            return index >= 0 && index < _candidates.Count;
        }

        private bool IsEnglishUi()
        {
            return _settings.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
        }

        private static string T(string key)
        {
            return LocalizationService.Translate(key);
        }

        private static string TF(string key, params object?[] args)
        {
            return LocalizationService.Format(key, args);
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

        private static void SetPlainButtonContent(Button button, string text, double textSize)
        {
            button.Content = new TextBlock
            {
                Text = text,
                FontSize = textSize,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        private static void SetButtonContent(Button button, Symbol symbol, string? text, double iconSize = 16, double textSize = 14)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = text == null ? 0 : 6,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(new Viewbox
            {
                Width = iconSize,
                Height = iconSize,
                Child = new SymbolIcon(symbol)
            });

            if (!string.IsNullOrWhiteSpace(text))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = textSize,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            button.Content = panel;
        }

        private string BuildCandidateDetails(SmartComposeResult result)
        {
            ScoreProject project = result.Project;
            int safePpq = Math.Max(1, project.Ppq);
            int ticksPerMeasure = Math.Max(1, project.TimeSignature.TicksPerMeasure(safePpq));
            int totalTicks = project.Notes.Count == 0
                ? ticksPerMeasure
                : Math.Max(ticksPerMeasure, project.Notes.Max(note => note.StartTick + Math.Max(1, note.DurationTicks)));
            int measureCount = Math.Max(1, (int)Math.Ceiling(totalTicks / (double)ticksPerMeasure));

            return string.Join(Environment.NewLine, new[]
            {
                $"{T("compose.meta.key")}: {BuildLocalizedKeyLabel(project.KeySignature.Fifths, project.KeySignature.Mode)}",
                $"{T("compose.meta.meter")}: {project.TimeSignature.Numerator}/{project.TimeSignature.Denominator}",
                $"{T("compose.meta.measures")}: {measureCount}",
                $"{T("compose.meta.tempo")}: {project.Bpm} BPM",
                $"{T("compose.meta.duration")}: {FormatDuration(totalTicks, safePpq, project.Bpm)}"
            });
        }

        private string BuildLocalizedKeyLabel(int fifths, KeyMode mode)
        {
            int pitchClass = Mod(fifths * 7 + (mode == KeyMode.Minor ? 9 : 0), 12);
            string tonic = fifths >= 0 ? SharpNames[pitchClass] : FlatNames[pitchClass];
            return $"{tonic} {T(mode == KeyMode.Minor ? "compose.mode.minor" : "compose.mode.major")}";
        }

        private static string FormatDuration(int totalTicks, int ppq, int bpm)
        {
            double seconds = totalTicks * 60d / (Math.Max(1, ppq) * Math.Max(1, bpm));
            TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            int totalMinutes = (int)Math.Floor(span.TotalMinutes);
            return $"{totalMinutes}:{span.Seconds:00}";
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
                "sleep" => 52,
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

        private static int Mod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
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

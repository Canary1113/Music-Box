using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System;
using System.IO;
using 音乐魔盒.Services;
using 音乐魔盒.ViewModels;

namespace 音乐魔盒
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new();
        private readonly AppSettingsService _settings = AppSettingsService.Instance;

        public MainWindow()
        {
            InitializeComponent();
            TryConfigureCustomTitleBar();
            TrySetWindowIcon();

            RootGrid.DataContext = ViewModel;
            TryApplyBackdrop();
            ApplyWindowTheme();
            ApplyLocalizedText();

            _settings.SettingsChanged += Settings_SettingsChanged;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Closed += MainWindow_Closed;

            MainNavigation.SelectedItem = NavEditor;
            NavigateTo("editor");
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _settings.SettingsChanged -= Settings_SettingsChanged;
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void MainNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is string tag)
            {
                NavigateTo(tag);
            }
        }

        private void NavigateTo(string tag)
        {
            Type? target = tag switch
            {
                "editor" => typeof(EditorPage),
                "convert" => typeof(ConvertPage),
                "recognize" => typeof(RecognizePage),
                "settings" => typeof(SettingsPage),
                _ => null
            };

            if (target != null)
            {
                ContentHost.MainFrame.Navigate(target, ViewModel);
                UpdateTitleMenuVisibility(tag);
            }
        }

        public void NavigateToPage(string tag)
        {
            string normalized = tag?.Trim().ToLowerInvariant() ?? string.Empty;
            NavigationViewItem? navItem = normalized switch
            {
                "editor" => NavEditor,
                "convert" => NavConvert,
                "recognize" => NavRecognize,
                "settings" => NavSettings,
                _ => null
            };

            if (navItem != null)
            {
                if (!ReferenceEquals(MainNavigation.SelectedItem, navItem))
                {
                    MainNavigation.SelectedItem = navItem;
                }
                else
                {
                    NavigateTo(normalized);
                }
            }
        }

        private EditorPage? GetCurrentEditorPage()
        {
            return ContentHost.MainFrame.Content as EditorPage;
        }

        private ConvertPage? GetCurrentConvertPage()
        {
            return ContentHost.MainFrame.Content as ConvertPage;
        }

        private void UpdateTitleMenuVisibility(string activeTag)
        {
            bool isEditorPage = string.Equals(activeTag, "editor", StringComparison.OrdinalIgnoreCase);
            bool isConvertPage = string.Equals(activeTag, "convert", StringComparison.OrdinalIgnoreCase);

            if (EditorTitleMenuBar != null)
            {
                EditorTitleMenuBar.Visibility = isEditorPage ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ConvertTitleMenuBar != null)
            {
                ConvertTitleMenuBar.Visibility = isConvertPage ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TitleBarMenuDivider != null)
            {
                TitleBarMenuDivider.Visibility = (isEditorPage || isConvertPage) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void EditorTitleFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
            {
                return;
            }

            string command = item.Tag?.ToString() ?? string.Empty;
            GetCurrentEditorPage()?.HandleTitleBarFileCommand(command);
        }

        private void EditorTitleTimeSignatureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioMenuFlyoutItem item)
            {
                return;
            }

            string signature = item.Tag?.ToString() ?? item.Text;
            GetCurrentEditorPage()?.HandleTitleBarTimeSignatureCommand(signature);
        }

        private void EditorTitleKeySignatureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioMenuFlyoutItem item)
            {
                return;
            }

            string fifthsTag = item.Tag?.ToString() ?? "0";
            GetCurrentEditorPage()?.HandleTitleBarKeySignatureCommand(fifthsTag);
        }

        private void EditorTitleTempoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioMenuFlyoutItem item)
            {
                return;
            }

            string bpmTag = item.Tag?.ToString() ?? "120";
            GetCurrentEditorPage()?.HandleTitleBarTempoCommand(bpmTag);
        }

        private void EditorTitleSnapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioMenuFlyoutItem item)
            {
                return;
            }

            string divisionTag = item.Tag?.ToString() ?? "2";
            GetCurrentEditorPage()?.HandleTitleBarSnapCommand(divisionTag);
        }

        private void EditorTitleDisplayToggleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleMenuFlyoutItem item)
            {
                return;
            }

            string command = item.Tag?.ToString() ?? string.Empty;
            GetCurrentEditorPage()?.HandleTitleBarDisplayToggleCommand(command, item.IsChecked);
        }

        private void EditorTitleAutoAdjustMeasureRatioMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleMenuFlyoutItem item)
            {
                return;
            }

            GetCurrentEditorPage()?.HandleTitleBarAutoAdjustMeasureRatioCommand(item.IsChecked);
        }

        private void EditorTitleMeasuresPerSystemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioMenuFlyoutItem item)
            {
                return;
            }

            string tag = item.Tag?.ToString() ?? "auto";
            GetCurrentEditorPage()?.HandleTitleBarMeasuresPerSystemCommand(tag);
        }

        private void EditorTitleMusicFontMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioMenuFlyoutItem item)
            {
                return;
            }

            string tag = item.Tag?.ToString() ?? string.Empty;
            GetCurrentEditorPage()?.HandleTitleBarMusicFontCommand(tag);
        }

        private void EditorTitleClearMenuItem_Click(object sender, RoutedEventArgs e)
        {
            GetCurrentEditorPage()?.HandleTitleBarClearCommand();
        }

        private void ConvertTitleImportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item)
            {
                string command = item.Tag?.ToString() ?? string.Empty;
                GetCurrentConvertPage()?.HandleTitleBarImportCommand(command);
                return;
            }

            GetCurrentConvertPage()?.HandleTitleBarImportCommand("import_editor");
        }

        private void ConvertTitleFormatMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
            {
                return;
            }

            string command = item.Tag?.ToString() ?? string.Empty;
            GetCurrentConvertPage()?.HandleTitleBarFormatCommand(command);
        }

        private void ConvertTitleExportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
            {
                return;
            }

            string command = item.Tag?.ToString() ?? string.Empty;
            GetCurrentConvertPage()?.HandleTitleBarExportCommand(command);
        }

        private void Settings_SettingsChanged(object? sender, EventArgs e)
        {
            ApplyWindowTheme();
            ApplyLocalizedText();
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            ApplyLocalizedText();
        }

        private void ApplyWindowTheme()
        {
            RootGrid.RequestedTheme = _settings.ResolveElementTheme();
            ApplyTitleBarButtonTheme();
        }

        private void ApplyLocalizedText()
        {
            Title = LocalizationService.Translate("window.title");
            if (WindowTitleText != null) WindowTitleText.Text = LocalizationService.Translate("window.title");
            if (NavEditorText != null) NavEditorText.Text = LocalizationService.Translate("nav.editor");
            if (NavConvertText != null) NavConvertText.Text = LocalizationService.Translate("nav.convert");
            if (NavRecognizeText != null) NavRecognizeText.Text = LocalizationService.Translate("nav.recognize");
            if (NavSettingsText != null) NavSettingsText.Text = LocalizationService.Translate("nav.settings");

            bool isEnglish = _settings.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
            if (isEnglish)
            {
                if (NavEditorText != null) NavEditorText.Text = "Edit";
                if (NavConvertText != null) NavConvertText.Text = "Conv.";
                if (NavRecognizeText != null) NavRecognizeText.Text = "Recog.";
                if (NavSettingsText != null) NavSettingsText.Text = "Prefs";
            }
            if (ConvertImportMenu != null) ConvertImportMenu.Title = isEnglish ? "Import" : "导入";
            if (ConvertFormatMenu != null) ConvertFormatMenu.Title = isEnglish ? "Format" : "格式转换";
            if (ConvertExportMenu != null) ConvertExportMenu.Title = isEnglish ? "Export" : "导出";
            if (ConvertImportFromEditorMenuItem != null) ConvertImportFromEditorMenuItem.Text = isEnglish ? "Import From Editor" : "编辑页导入";
            if (ConvertImportFromFileMenuItem != null) ConvertImportFromFileMenuItem.Text = isEnglish ? "Import From File" : "从文件导入";
            if (ConvertStaffToJianpuMenuItem != null) ConvertStaffToJianpuMenuItem.Text = isEnglish ? "Staff -> Jianpu" : "五线谱 → 简谱";
            if (ConvertExportPdfMenuItem != null) ConvertExportPdfMenuItem.Text = "PDF";
            if (ConvertExportMusicXmlMenuItem != null) ConvertExportMusicXmlMenuItem.Text = "MusicXML";

            if (EditorFileMenu != null) EditorFileMenu.Title = isEnglish ? "File" : "文件";
            if (EditorTimeMenu != null) EditorTimeMenu.Title = isEnglish ? "Time" : "拍号";
            if (EditorKeyMenu != null) EditorKeyMenu.Title = isEnglish ? "Key" : "调号";
            if (EditorTempoMenu != null) EditorTempoMenu.Title = isEnglish ? "Tempo" : "速度";
            if (EditorSnapMenu != null) EditorSnapMenu.Title = isEnglish ? "Snap" : "音符吸附";
            if (EditorDisplayMenu != null) EditorDisplayMenu.Title = isEnglish ? "View" : "显示";
            if (EditorMeasurePerLineSubItem != null) EditorMeasurePerLineSubItem.Text = isEnglish ? "Measures Per Line" : "每行小节数";
            if (EditorAutoAdjustRatioMenuItem != null) EditorAutoAdjustRatioMenuItem.Text = isEnglish ? "Auto Adjust Ratio" : "自动调整比例";
            if (EditorClearMenuItem != null) EditorClearMenuItem.Text = isEnglish ? "Clear" : "清空";

            ApplyEditorMenuItemLocalization(isEnglish);
        }

        private void ApplyEditorMenuItemLocalization(bool isEnglish)
        {
            var fileMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["new"] = isEnglish ? "New" : "新建",
                ["open"] = isEnglish ? "Open" : "打开",
                ["save"] = isEnglish ? "Save" : "保存",
                ["saveas"] = isEnglish ? "Save As" : "另存为",
                ["import_musicxml"] = isEnglish ? "Import MusicXML" : "导入 MusicXML",
                ["export_musicxml"] = isEnglish ? "Export MusicXML" : "导出 MusicXML",
                ["print"] = isEnglish ? "Print..." : "打印..."
            };

            var displayMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["show_grid"] = isEnglish ? "Grid" : "网格",
                ["area_select"] = isEnglish ? "Area Select" : "区域选择",
                ["clear"] = isEnglish ? "Clear" : "清空",
                ["auto"] = isEnglish ? "Auto (Default)" : "自动（默认）"
            };

            var snapMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = isEnglish ? "Quarter Note" : "四分音符",
                ["2"] = isEnglish ? "Eighth Note" : "八分音符",
                ["4"] = isEnglish ? "16th Note" : "十六分音符",
                ["8"] = isEnglish ? "32nd Note" : "三十二分音符"
            };

            var keyMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["0"] = isEnglish ? "C Major / A minor" : "C 大调 / a 小调",
                ["1"] = isEnglish ? "G Major / E minor" : "G 大调 / e 小调",
                ["2"] = isEnglish ? "D Major / B minor" : "D 大调 / b 小调",
                ["3"] = isEnglish ? "A Major / F# minor" : "A 大调 / 升f 小调",
                ["4"] = isEnglish ? "E Major / C# minor" : "E 大调 / 升c 小调",
                ["5"] = isEnglish ? "B Major / G# minor" : "B 大调 / 升g 小调",
                ["6"] = isEnglish ? "F# Major / D# minor" : "升F 大调 / 升d 小调",
                ["7"] = isEnglish ? "C# Major / A# minor" : "升C 大调 / 升a 小调",
                ["-1"] = isEnglish ? "F Major / D minor" : "F 大调 / d 小调",
                ["-2"] = isEnglish ? "Bb Major / G minor" : "降B 大调 / g 小调",
                ["-3"] = isEnglish ? "Eb Major / C minor" : "降E 大调 / c 小调",
                ["-4"] = isEnglish ? "Ab Major / F minor" : "降A 大调 / f 小调",
                ["-5"] = isEnglish ? "Db Major / Bb minor" : "降D 大调 / 降b 小调",
                ["-6"] = isEnglish ? "Gb Major / Eb minor" : "降G 大调 / 降e 小调",
                ["-7"] = isEnglish ? "Cb Major / Ab minor" : "降C 大调 / 降a 小调"
            };

            var tempoMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["24"] = isEnglish ? "Larghissimo" : "Larghissimo 极缓板",
                ["35"] = isEnglish ? "Grave" : "Grave 沉板",
                ["50"] = isEnglish ? "Largo" : "Largo 广板",
                ["56"] = isEnglish ? "Adagio" : "Adagio 柔板",
                ["60"] = isEnglish ? "Lento" : "Lento 慢板",
                ["63"] = isEnglish ? "Larghetto" : "Larghetto 小广板",
                ["66"] = isEnglish ? "Adagietto" : "Adagietto 小柔板",
                ["76"] = isEnglish ? "Andante" : "Andante 行板",
                ["84"] = isEnglish ? "Andantino" : "Andantino 小行板",
                ["88"] = isEnglish ? "Maestoso" : "Maestoso 庄板",
                ["96"] = isEnglish ? "Moderato" : "Moderato 中板",
                ["112"] = isEnglish ? "Allegretto" : "Allegretto 小快板",
                ["120"] = isEnglish ? "Allegro" : "Allegro 快板",
                ["132"] = isEnglish ? "Allegro vivace" : "Allegro vivace 快速板",
                ["144"] = isEnglish ? "Vivace" : "Vivace 活板",
                ["168"] = isEnglish ? "Presto" : "Presto 急板",
                ["200"] = isEnglish ? "Prestissimo" : "Prestissimo 最急板"
            };

            if (EditorFileMenu != null)
            {
                ApplyMenuItemLocalizationRecursive(EditorFileMenu.Items, fileMap);
            }

            if (EditorDisplayMenu != null)
            {
                ApplyMenuItemLocalizationRecursive(EditorDisplayMenu.Items, displayMap);
            }

            if (EditorSnapMenu != null)
            {
                ApplyMenuItemLocalizationRecursive(EditorSnapMenu.Items, snapMap);
            }

            if (EditorKeyMenu != null)
            {
                ApplyMenuItemLocalizationRecursive(EditorKeyMenu.Items, keyMap);
            }

            if (EditorTempoMenu != null)
            {
                ApplyMenuItemLocalizationRecursive(EditorTempoMenu.Items, tempoMap);
            }
        }

        private static void ApplyMenuItemLocalizationRecursive(
            System.Collections.Generic.IEnumerable<object> items,
            System.Collections.Generic.IReadOnlyDictionary<string, string> byTag)
        {
            foreach (object item in items)
            {
                switch (item)
                {
                    case MenuFlyoutSubItem sub:
                        if (sub.Tag is string subTag && byTag.TryGetValue(subTag, out string? subText))
                        {
                            sub.Text = subText;
                        }
                        ApplyMenuItemLocalizationRecursive(sub.Items, byTag);
                        break;
                    case RadioMenuFlyoutItem radioItem:
                        if (radioItem.Tag is string radioTag && byTag.TryGetValue(radioTag, out string? radioText))
                        {
                            radioItem.Text = radioText;
                        }
                        break;
                    case ToggleMenuFlyoutItem toggleItem:
                        if (toggleItem.Tag is string toggleTag && byTag.TryGetValue(toggleTag, out string? toggleText))
                        {
                            toggleItem.Text = toggleText;
                        }
                        break;
                    case MenuFlyoutItem menuItem:
                        if (menuItem.Tag is string menuTag && byTag.TryGetValue(menuTag, out string? menuText))
                        {
                            menuItem.Text = menuText;
                        }
                        break;
                }
            }
        }

        private void TryApplyBackdrop()
        {
            try
            {
                SystemBackdrop = new MicaBackdrop();
            }
            catch
            {
            }
        }

        private void TrySetWindowIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
                AppWindow.SetIcon(iconPath);
            }
            catch
            {
            }
        }

        private void TryConfigureCustomTitleBar()
        {
            try
            {
                ExtendsContentIntoTitleBar = true;
                if (AppTitleBarDragRegion != null)
                {
                    SetTitleBar(AppTitleBarDragRegion);
                }

                if (AppWindow?.TitleBar != null)
                {
                    AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
                    ApplyTitleBarButtonTheme();
                }
            }
            catch
            {
            }
        }

        private void ApplyTitleBarButtonTheme()
        {
            try
            {
                if (AppWindow?.TitleBar == null)
                {
                    return;
                }

                bool isDark = RootGrid.ActualTheme == ElementTheme.Dark;
                var fg = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
                Windows.UI.Color hoverBg = isDark ? Windows.UI.Color.FromArgb(48, 255, 255, 255) : Windows.UI.Color.FromArgb(24, 0, 0, 0);
                Windows.UI.Color pressedBg = isDark ? Windows.UI.Color.FromArgb(86, 255, 255, 255) : Windows.UI.Color.FromArgb(44, 0, 0, 0);

                AppWindow.TitleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                AppWindow.TitleBar.ButtonForegroundColor = fg;
                AppWindow.TitleBar.ButtonInactiveForegroundColor = fg;
                AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBg;
                AppWindow.TitleBar.ButtonHoverForegroundColor = fg;
                AppWindow.TitleBar.ButtonPressedBackgroundColor = pressedBg;
                AppWindow.TitleBar.ButtonPressedForegroundColor = fg;
            }
            catch
            {
            }
        }
    }
}




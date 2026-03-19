using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System;
using System.Collections.Generic;
using System.IO;
using MusicBox.Services;
using MusicBox.ViewModels;

namespace MusicBox
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new();
        private readonly AppSettingsService _settings = AppSettingsService.Instance;
        private NavigationViewItem? NavCompose;
        private TextBlock? NavComposeText;
        private RadioMenuFlyoutItem? ConvertStaffToGuitarTabMenuItem;
        private MenuFlyoutItem? ConvertExportGuitarTabMenuItem;

        public MainWindow()
        {
            DebugTrace.Write("MainWindow.ctor begin");
            InitializeComponent();
            TryConfigureCustomTitleBar();
            TrySetWindowIcon();
            CreateDynamicNavigationAndConvertMenuItems();

            RootGrid.DataContext = ViewModel;
            TryApplyBackdrop();
            ApplyWindowTheme();
            ApplyLocalizedText();
            ApplyExperimentalFeatureVisibility();

            _settings.SettingsChanged += Settings_SettingsChanged;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Closed += MainWindow_Closed;

            MainNavigation.SelectedItem = NavEditor;
            NavigateTo("editor");
            DebugTrace.Write("MainWindow.ctor end");
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
                DebugTrace.Write($"MainNavigation.SelectionChanged: {tag}");
                NavigateTo(tag);
            }
        }

        private void NavigateTo(string tag)
        {
            string normalized = tag?.Trim().ToLowerInvariant() ?? string.Empty;
            DebugTrace.Write($"NavigateTo begin: {normalized}");
            if (string.Equals(normalized, "recognize", StringComparison.OrdinalIgnoreCase)
                && !_settings.ExperimentalFeaturesEnabled)
            {
                normalized = "editor";
            }

            Type? target = normalized switch
            {
                "editor" => typeof(EditorPage),
                "convert" => typeof(ConvertPage),
                "compose" => typeof(ComposeWorkbenchPage),
                "recognize" => typeof(RecognizePage),
                "settings" => typeof(SettingsPage),
                _ => null
            };

            if (target != null)
            {
                try
                {
                    DebugTrace.Write($"Frame.Navigate start: {target.FullName}");
                    ContentHost.MainFrame.Navigate(target, ViewModel);
                    DebugTrace.Write($"Frame.Navigate returned: {target.FullName}");
                    UpdateTitleMenuVisibility(normalized);
                }
                catch (Exception ex)
                {
                    DebugTrace.Write($"NavigateTo catch: {ex}");
                    ViewModel.SetStatus($"页面打开失败: {ex.Message}");
                    if (!string.Equals(normalized, "editor", StringComparison.OrdinalIgnoreCase))
                    {
                        ContentHost.MainFrame.Navigate(typeof(EditorPage), ViewModel);
                        UpdateTitleMenuVisibility("editor");
                        MainNavigation.SelectedItem = NavEditor;
                    }
                }
            }
            else
            {
                DebugTrace.Write($"NavigateTo ignored: {normalized}");
            }
        }

        public void NavigateToPage(string tag)
        {
            string normalized = tag?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.Equals(normalized, "recognize", StringComparison.OrdinalIgnoreCase)
                && !_settings.ExperimentalFeaturesEnabled)
            {
                normalized = "editor";
            }

            NavigationViewItem? navItem = normalized switch
            {
                "editor" => NavEditor,
                "convert" => NavConvert,
                "compose" => NavCompose,
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

        public void NavigateToConvertAndImportEditor()
        {
            NavigateToPage("convert");
            GetCurrentConvertPage()?.HandleTitleBarImportCommand("import_editor");
        }

        private void CreateDynamicNavigationAndConvertMenuItems()
        {
            if (MainNavigation == null)
            {
                return;
            }

            if (NavCompose == null)
            {
                NavComposeText = new TextBlock
                {
                    Text = "创作",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                    MaxLines = 2,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };

                var composePanel = new StackPanel
                {
                    Spacing = 4,
                    Width = 88,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(3, 0, 3, 0)
                };
                composePanel.Children.Add(new FontIcon
                {
                    Glyph = "✎",
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                composePanel.Children.Add(NavComposeText);

                NavCompose = new NavigationViewItem
                {
                    Tag = "compose",
                    Content = composePanel
                };

                if (MainNavigation.Resources.TryGetValue("CompactNavItemStyle", out object style)
                    && style is Style compactNavItemStyle)
                {
                    NavCompose.Style = compactNavItemStyle;
                }

                int insertIndex = MainNavigation.MenuItems.IndexOf(NavRecognize);
                if (insertIndex >= 0)
                {
                    MainNavigation.MenuItems.Insert(insertIndex, NavCompose);
                }
                else
                {
                    MainNavigation.MenuItems.Add(NavCompose);
                }
            }

            if (ConvertFormatMenu != null && ConvertStaffToGuitarTabMenuItem == null)
            {
                ConvertStaffToGuitarTabMenuItem = new RadioMenuFlyoutItem
                {
                    Text = "五线谱 → 吉他谱",
                    Tag = "staff_to_guitar_tab",
                    GroupName = "ConvertFormat"
                };
                ConvertStaffToGuitarTabMenuItem.Click += ConvertTitleFormatMenuItem_Click;
                ConvertFormatMenu.Items.Add(ConvertStaffToGuitarTabMenuItem);
            }

            if (ConvertExportMenu != null && ConvertExportGuitarTabMenuItem == null)
            {
                ConvertExportGuitarTabMenuItem = new MenuFlyoutItem
                {
                    Text = "吉他谱 TXT",
                    Tag = "export_guitar_tab_txt"
                };
                ConvertExportGuitarTabMenuItem.Click += ConvertTitleExportMenuItem_Click;
                ConvertExportMenu.Items.Add(ConvertExportGuitarTabMenuItem);
            }
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
            if (sender is not MenuFlyoutItemBase item)
            {
                return;
            }

            string command = item.Tag?.ToString() ?? string.Empty;
            GetCurrentConvertPage()?.HandleTitleBarFormatCommand(command);
        }

        public void SyncConvertFormatSelection(string activeFormat)
        {
            bool isGuitar = string.Equals(activeFormat, "guitar", StringComparison.OrdinalIgnoreCase);
            if (ConvertStaffToJianpuMenuItem != null)
            {
                ConvertStaffToJianpuMenuItem.IsChecked = !isGuitar;
            }

            if (ConvertStaffToGuitarTabMenuItem != null)
            {
                ConvertStaffToGuitarTabMenuItem.IsChecked = isGuitar;
            }
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
            ApplyExperimentalFeatureVisibility();
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

        private void ApplyExperimentalFeatureVisibility()
        {
            bool showRecognize = _settings.ExperimentalFeaturesEnabled;
            if (NavRecognize != null)
            {
                NavRecognize.Visibility = showRecognize ? Visibility.Visible : Visibility.Collapsed;
            }

            if (!showRecognize
                && (ReferenceEquals(MainNavigation.SelectedItem, NavRecognize) || ContentHost.MainFrame.Content is RecognizePage))
            {
                MainNavigation.SelectedItem = NavEditor;
                NavigateTo("editor");
            }
        }

        private void ApplyLocalizedText()
        {
            bool isEnglish = _settings.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);

            Title = LocalizationService.Translate("window.title");
            if (WindowTitleText != null) WindowTitleText.Text = LocalizationService.Translate("window.title");
            if (NavEditorText != null) NavEditorText.Text = LocalizationService.Translate("nav.editor");
            if (NavConvertText != null) NavConvertText.Text = LocalizationService.Translate("nav.convert");
            if (NavComposeText != null) NavComposeText.Text = LocalizationService.Translate("nav.compose");
            if (NavRecognizeText != null) NavRecognizeText.Text = LocalizationService.Translate("nav.recognize");
            if (NavSettingsText != null) NavSettingsText.Text = LocalizationService.Translate("nav.settings");

            ApplyNavLabelStyle(NavEditorText, isEnglish);
            ApplyNavLabelStyle(NavConvertText, isEnglish);
            ApplyNavLabelStyle(NavComposeText, isEnglish);
            ApplyNavLabelStyle(NavRecognizeText, isEnglish);
            ApplyNavLabelStyle(NavSettingsText, isEnglish);

            if (ConvertImportMenu != null) ConvertImportMenu.Title = isEnglish ? "Import" : "\u5bfc\u5165";
            if (ConvertFormatMenu != null) ConvertFormatMenu.Title = isEnglish ? "Format" : "\u683c\u5f0f\u8f6c\u6362";
            if (ConvertExportMenu != null) ConvertExportMenu.Title = isEnglish ? "Export" : "\u5bfc\u51fa";
            if (ConvertImportFromEditorMenuItem != null) ConvertImportFromEditorMenuItem.Text = isEnglish ? "Import From Editor" : "\u7f16\u8f91\u9875\u5bfc\u5165";
            if (ConvertImportFromFileMenuItem != null) ConvertImportFromFileMenuItem.Text = isEnglish ? "Import From File" : "\u4ece\u6587\u4ef6\u5bfc\u5165";
            if (ConvertStaffToJianpuMenuItem != null) ConvertStaffToJianpuMenuItem.Text = isEnglish ? "Staff -> Jianpu" : "\u4e94\u7ebf\u8c31 \u2192 \u7b80\u8c31";
            if (ConvertStaffToGuitarTabMenuItem != null) ConvertStaffToGuitarTabMenuItem.Text = isEnglish ? "Staff -> Guitar Tab" : "\u4e94\u7ebf\u8c31 \u2192 \u5409\u4ed6\u8c31";
            if (ConvertExportPdfMenuItem != null) ConvertExportPdfMenuItem.Text = "PDF";
            if (ConvertExportMusicXmlMenuItem != null) ConvertExportMusicXmlMenuItem.Text = "MusicXML";
            if (ConvertExportGuitarTabMenuItem != null) ConvertExportGuitarTabMenuItem.Text = isEnglish ? "Guitar Tab TXT" : "\u5409\u4ed6\u8c31 TXT";

            if (EditorFileMenu != null) EditorFileMenu.Title = isEnglish ? "File" : "\u6587\u4ef6";
            if (EditorTimeMenu != null) EditorTimeMenu.Title = isEnglish ? "Time" : "\u62cd\u53f7";
            if (EditorKeyMenu != null) EditorKeyMenu.Title = isEnglish ? "Key" : "\u8c03\u53f7";
            if (EditorTempoMenu != null) EditorTempoMenu.Title = isEnglish ? "Tempo" : "\u901f\u5ea6";
            if (EditorSnapMenu != null) EditorSnapMenu.Title = isEnglish ? "Snap" : "\u97f3\u7b26\u5438\u9644";
            if (EditorDisplayMenu != null) EditorDisplayMenu.Title = isEnglish ? "View" : "\u663e\u793a";
            if (EditorMeasurePerLineSubItem != null) EditorMeasurePerLineSubItem.Text = isEnglish ? "Measures Per Line" : "\u6bcf\u884c\u5c0f\u8282\u6570";
            if (EditorAutoAdjustRatioMenuItem != null) EditorAutoAdjustRatioMenuItem.Text = isEnglish ? "Auto Adjust Ratio" : "\u81ea\u52a8\u8c03\u6574\u6bd4\u4f8b";
            if (EditorClearMenuItem != null) EditorClearMenuItem.Text = isEnglish ? "Clear" : "\u6e05\u7a7a";

            ApplyEditorMenuItemLocalization(isEnglish);
            ApplyExperimentalFeatureVisibility();
        }

        private static void ApplyNavLabelStyle(TextBlock? label, bool isEnglish)
        {
            if (label == null)
            {
                return;
            }

            label.FontSize = isEnglish ? 9.5 : 10;
            label.MaxLines = 2;
            label.TextWrapping = TextWrapping.Wrap;
            label.TextAlignment = TextAlignment.Center;
        }

        private void ApplyEditorMenuItemLocalization(bool isEnglish)
        {
            var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["new"] = isEnglish ? "New" : "\u65b0\u5efa",
                ["open"] = isEnglish ? "Open" : "\u6253\u5f00",
                ["save"] = isEnglish ? "Save" : "\u4fdd\u5b58",
                ["saveas"] = isEnglish ? "Save As" : "\u53e6\u5b58\u4e3a",
                ["import_musicxml"] = isEnglish ? "Import MusicXML" : "\u5bfc\u5165 MusicXML",
                ["export_musicxml"] = isEnglish ? "Export MusicXML" : "\u5bfc\u51fa MusicXML",
                ["print"] = isEnglish ? "Print..." : "\u6253\u5370..."
            };

            var displayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["show_grid"] = isEnglish ? "Grid" : "\u7f51\u683c",
                ["area_select"] = isEnglish ? "Area Select" : "\u533a\u57df\u9009\u62e9",
                ["clear"] = isEnglish ? "Clear" : "\u6e05\u7a7a",
                ["auto"] = isEnglish ? "Auto (Default)" : "\u81ea\u52a8\uff08\u9ed8\u8ba4\uff09"
            };

            var snapMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = isEnglish ? "Quarter Note" : "\u56db\u5206\u97f3\u7b26",
                ["2"] = isEnglish ? "Eighth Note" : "\u516b\u5206\u97f3\u7b26",
                ["4"] = isEnglish ? "16th Note" : "\u5341\u516d\u5206\u97f3\u7b26",
                ["8"] = isEnglish ? "32nd Note" : "\u4e09\u5341\u4e8c\u5206\u97f3\u7b26"
            };

            var keyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["0"] = isEnglish ? "C Major / A minor" : "C \u5927\u8c03 / a \u5c0f\u8c03",
                ["1"] = isEnglish ? "G Major / E minor" : "G \u5927\u8c03 / e \u5c0f\u8c03",
                ["2"] = isEnglish ? "D Major / B minor" : "D \u5927\u8c03 / b \u5c0f\u8c03",
                ["3"] = isEnglish ? "A Major / F# minor" : "A \u5927\u8c03 / \u5347f \u5c0f\u8c03",
                ["4"] = isEnglish ? "E Major / C# minor" : "E \u5927\u8c03 / \u5347c \u5c0f\u8c03",
                ["5"] = isEnglish ? "B Major / G# minor" : "B \u5927\u8c03 / \u5347g \u5c0f\u8c03",
                ["6"] = isEnglish ? "F# Major / D# minor" : "\u5347F \u5927\u8c03 / \u5347d \u5c0f\u8c03",
                ["7"] = isEnglish ? "C# Major / A# minor" : "\u5347C \u5927\u8c03 / \u5347a \u5c0f\u8c03",
                ["-1"] = isEnglish ? "F Major / D minor" : "F \u5927\u8c03 / d \u5c0f\u8c03",
                ["-2"] = isEnglish ? "Bb Major / G minor" : "\u964dB \u5927\u8c03 / g \u5c0f\u8c03",
                ["-3"] = isEnglish ? "Eb Major / C minor" : "\u964dE \u5927\u8c03 / c \u5c0f\u8c03",
                ["-4"] = isEnglish ? "Ab Major / F minor" : "\u964dA \u5927\u8c03 / f \u5c0f\u8c03",
                ["-5"] = isEnglish ? "Db Major / Bb minor" : "\u964dD \u5927\u8c03 / \u964db \u5c0f\u8c03",
                ["-6"] = isEnglish ? "Gb Major / Eb minor" : "\u964dG \u5927\u8c03 / \u964de \u5c0f\u8c03",
                ["-7"] = isEnglish ? "Cb Major / Ab minor" : "\u964dC \u5927\u8c03 / \u964da \u5c0f\u8c03"
            };

            var tempoMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["24"] = isEnglish ? "Larghissimo" : "Larghissimo \u6781\u7f13\u677f",
                ["35"] = isEnglish ? "Grave" : "Grave \u6c89\u677f",
                ["50"] = isEnglish ? "Largo" : "Largo \u5e7f\u677f",
                ["56"] = isEnglish ? "Adagio" : "Adagio \u67d4\u677f",
                ["60"] = isEnglish ? "Lento" : "Lento \u6162\u677f",
                ["63"] = isEnglish ? "Larghetto" : "Larghetto \u5c0f\u5e7f\u677f",
                ["66"] = isEnglish ? "Adagietto" : "Adagietto \u5c0f\u67d4\u677f",
                ["76"] = isEnglish ? "Andante" : "Andante \u884c\u677f",
                ["84"] = isEnglish ? "Andantino" : "Andantino \u5c0f\u884c\u677f",
                ["88"] = isEnglish ? "Maestoso" : "Maestoso \u5e84\u677f",
                ["96"] = isEnglish ? "Moderato" : "Moderato \u4e2d\u677f",
                ["112"] = isEnglish ? "Allegretto" : "Allegretto \u5c0f\u5feb\u677f",
                ["120"] = isEnglish ? "Allegro" : "Allegro \u5feb\u677f",
                ["132"] = isEnglish ? "Allegro vivace" : "Allegro vivace \u5feb\u901f\u677f",
                ["144"] = isEnglish ? "Vivace" : "Vivace \u6d3b\u677f",
                ["168"] = isEnglish ? "Presto" : "Presto \u6025\u677f",
                ["200"] = isEnglish ? "Prestissimo" : "Prestissimo \u6700\u6025\u677f"
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

        private static void ApplyMenuItemLocalizationRecursive(IEnumerable<object> items, IReadOnlyDictionary<string, string> byTag)
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
                var fg = isDark ? Colors.White : Colors.Black;
                Windows.UI.Color hoverBg = isDark ? Windows.UI.Color.FromArgb(48, 255, 255, 255) : Windows.UI.Color.FromArgb(24, 0, 0, 0);
                Windows.UI.Color pressedBg = isDark ? Windows.UI.Color.FromArgb(86, 255, 255, 255) : Windows.UI.Color.FromArgb(44, 0, 0, 0);

                AppWindow.TitleBar.BackgroundColor = Colors.Transparent;
                AppWindow.TitleBar.InactiveBackgroundColor = Colors.Transparent;
                AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
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












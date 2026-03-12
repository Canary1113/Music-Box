using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using MusicBox.Services;
using MusicBox.ViewModels;

namespace MusicBox
{
    public sealed partial class SettingsPage : Page
    {
        private readonly AppSettingsService _settings = AppSettingsService.Instance;
        private bool _syncing;
        private string _lastAppliedLanguageTag = "system";

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += SettingsPage_Loaded;
            Unloaded += SettingsPage_Unloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel vm)
            {
                DataContext = vm;
            }

            SyncControls();
            ApplyLocalizedText();
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            _settings.SettingsChanged += Settings_SettingsChanged;
            SyncControls();
            ApplyLocalizedText();
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            _settings.SettingsChanged -= Settings_SettingsChanged;
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            ApplyLocalizedText();
        }

        private void Settings_SettingsChanged(object? sender, EventArgs e)
        {
            SyncControls();
            ApplyLocalizedText();
        }

        private void SyncControls()
        {
            _syncing = true;
            try
            {
                AppThemePreference preference = _settings.ThemePreference switch
                {
                    AppThemePreference.Light => AppThemePreference.Light,
                    AppThemePreference.Dark => AppThemePreference.Dark,
                    _ => AppThemePreference.System
                };

                ThemeSystemRadio.IsChecked = preference == AppThemePreference.System;
                ThemeLightRadio.IsChecked = preference == AppThemePreference.Light;
                ThemeDarkRadio.IsChecked = preference == AppThemePreference.Dark;
                ExperimentalFeaturesToggleSwitch.IsOn = _settings.ExperimentalFeaturesEnabled;

                string targetLang = _settings.LanguageTag;
                foreach (object obj in LanguageComboBox.Items)
                {
                    if (obj is ComboBoxItem item
                        && string.Equals(item.Tag?.ToString(), targetLang, StringComparison.OrdinalIgnoreCase))
                    {
                        LanguageComboBox.SelectedItem = item;
                        _lastAppliedLanguageTag = targetLang;
                        break;
                    }
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private void ApplyLocalizedText()
        {
            bool isEnglish = IsEnglishUi();
            AppBuildInfo buildInfo = AppBuildInfoService.GetCurrent();

            PageTitleText.Text = isEnglish ? "Settings" : "\u8bbe\u7f6e";
            PersonalizationTitleText.Text = isEnglish ? "Personalization" : "\u4e2a\u6027\u5316";
            ThemeTitleText.Text = isEnglish ? "Theme" : "\u4e3b\u9898";
            ThemeDescText.Text = isEnglish ? "Choose app appearance theme" : "\u9009\u62e9\u5e94\u7528\u7684\u5916\u89c2\u4e3b\u9898";
            ThemeSystemRadio.Content = isEnglish ? "Use system setting" : "\u8ddf\u968f\u7cfb\u7edf";
            ThemeLightRadio.Content = isEnglish ? "Light" : "\u6d45\u8272";
            ThemeDarkRadio.Content = isEnglish ? "Dark" : "\u6df1\u8272";
            UpdateThemeSummaryText(isEnglish);

            LanguageTitleText.Text = isEnglish ? "Language" : "\u8bed\u8a00";
            LanguageDescText.Text = isEnglish ? "Change display language" : "\u5207\u6362\u754c\u9762\u663e\u793a\u8bed\u8a00";
            AboutSectionTitleText.Text = isEnglish ? "About" : "\u5173\u4e8e";

            LabsTitleText.Text = isEnglish ? "Experimental Features" : "\u5b9e\u9a8c\u5ba4\u529f\u80fd";
            LabsDescText.Text = isEnglish
                ? "Show unfinished features such as the Recognize page"
                : "\u663e\u793a\u4ecd\u5728\u5f00\u53d1\u4e2d\u7684\u529f\u80fd\uff0c\u4f8b\u5982\u8bc6\u522b\u9875";
            ExperimentalFeaturesToggleSwitch.OnContent = isEnglish ? "On" : "\u5f00";
            ExperimentalFeaturesToggleSwitch.OffContent = isEnglish ? "Off" : "\u5173";

            foreach (object obj in LanguageComboBox.Items)
            {
                if (obj is not ComboBoxItem item)
                {
                    continue;
                }

                string tag = item.Tag?.ToString() ?? string.Empty;
                item.Content = tag switch
                {
                    "system" => isEnglish ? "Use system setting" : "\u8ddf\u968f\u7cfb\u7edf",
                    "zh-Hans" => isEnglish ? "Chinese (Simplified)" : "\u7b80\u4f53\u4e2d\u6587",
                    "en-US" => "English",
                    _ => tag
                };
            }

            AboutHeaderText.Text = isEnglish ? "MusicBox" : "\u97f3\u4e50\u9b54\u76d2\uff08MusicBox\uff09";
            AboutVersionLabelText.Text = isEnglish ? "Version" : "\u7248\u672c\u53f7";
            AboutBuildLabelText.Text = isEnglish ? "Build Number" : "\u6784\u5efa\u53f7";
            AboutAuthorLabelText.Text = isEnglish ? "Author" : "\u4f5c\u8005";
            AboutVersionValueText.Text = buildInfo.VersionDisplay;
            AboutBuildValueText.Text = buildInfo.BuildNumber;
            AboutAuthorValueText.Text = "Rylan";

            WarningTitleText.Text = "⚠️ Work in Progress";
            WarningBodyText.Text = "This project is currently under active development and some features may be incomplete or unstable.";
        }

        private void UpdateThemeSummaryText(bool isEnglish)
        {
            ThemeSummaryText.Text = _settings.ThemePreference switch
            {
                AppThemePreference.Light => isEnglish ? "Light" : "\u6d45\u8272",
                AppThemePreference.Dark => isEnglish ? "Dark" : "\u6df1\u8272",
                _ => isEnglish ? "Use system setting" : "\u8ddf\u968f\u7cfb\u7edf"
            };
        }

        private bool IsEnglishUi()
        {
            return _settings.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            if (ThemeLightRadio.IsChecked == true)
            {
                _settings.ThemePreference = AppThemePreference.Light;
            }
            else if (ThemeDarkRadio.IsChecked == true)
            {
                _settings.ThemePreference = AppThemePreference.Dark;
            }
            else
            {
                _settings.ThemePreference = AppThemePreference.System;
            }
        }

        private void ExperimentalFeaturesToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            _settings.ExperimentalFeaturesEnabled = ExperimentalFeaturesToggleSwitch.IsOn;
        }

        private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            if (LanguageComboBox.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            string tag = item.Tag?.ToString() ?? "system";
            if (string.Equals(tag, _settings.LanguageTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool restartNow = await ConfirmLanguageRestartAsync().ConfigureAwait(true);
            if (!restartNow)
            {
                SelectLanguageItem(_lastAppliedLanguageTag);
                return;
            }

            _settings.LanguageTag = tag;
            _lastAppliedLanguageTag = _settings.LanguageTag;
            RestartApplication();
        }

        private async System.Threading.Tasks.Task<bool> ConfirmLanguageRestartAsync()
        {
            if (XamlRoot == null)
            {
                return true;
            }

            bool isEnglish = IsEnglishUi();
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = isEnglish ? "Restart Required" : "\u9700\u8981\u91cd\u542f",
                Content = isEnglish
                    ? "Changing display language requires a restart. Unsaved changes may be lost. Restart now?"
                    : "\u4fee\u6539\u754c\u9762\u8bed\u8a00\u9700\u8981\u91cd\u542f\u5e94\u7528\uff0c\u672a\u4fdd\u5b58\u5185\u5bb9\u53ef\u80fd\u4e22\u5931\u3002\u73b0\u5728\u91cd\u542f\u5417\uff1f",
                PrimaryButtonText = isEnglish ? "Restart now" : "\u7acb\u5373\u91cd\u542f",
                CloseButtonText = isEnglish ? "Later" : "\u7a0d\u540e",
                DefaultButton = ContentDialogButton.Close
            };

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private void SelectLanguageItem(string tag)
        {
            _syncing = true;
            try
            {
                foreach (object obj in LanguageComboBox.Items)
                {
                    if (obj is ComboBoxItem item
                        && string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                    {
                        LanguageComboBox.SelectedItem = item;
                        return;
                    }
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        private static void RestartApplication()
        {
            try
            {
                string? executable = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executable))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = executable,
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
            }

            Application.Current?.Exit();
        }
    }
}


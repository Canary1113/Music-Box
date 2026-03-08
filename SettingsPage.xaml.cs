using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.IO;
using 音乐魔盒.Services;
using 音乐魔盒.ViewModels;

namespace 音乐魔盒
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
        }

        private void SyncControls()
        {
            _syncing = true;
            try
            {
                var preference = _settings.ThemePreference switch
                {
                    AppThemePreference.Light => AppThemePreference.Light,
                    AppThemePreference.Dark => AppThemePreference.Dark,
                    _ => AppThemePreference.System
                };
                ThemeSystemRadio.IsChecked = preference == AppThemePreference.System;
                ThemeLightRadio.IsChecked = preference == AppThemePreference.Light;
                ThemeDarkRadio.IsChecked = preference == AppThemePreference.Dark;

                string targetLang = _settings.LanguageTag;
                foreach (var obj in LanguageComboBox.Items)
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
            PageTitleText.Text = LocalizationService.Translate("settings.page_title");
            PersonalizationTitleText.Text = LocalizationService.Translate("settings.section.personalization");
            ThemeTitleText.Text = LocalizationService.Translate("settings.theme");
            LanguageTitleText.Text = LocalizationService.Translate("settings.language");
            AboutSectionTitleText.Text = LocalizationService.Translate("settings.section.about");
            AboutTitleText.Text = LocalizationService.Translate("settings.section.about");
            AboutNameLabelText.Text = LocalizationService.Translate("settings.about.name");
            AboutVersionLabelText.Text = LocalizationService.Translate("settings.about.version");
            AboutBuildLabelText.Text = LocalizationService.Translate("settings.about.build");
            AboutAuthorLabelText.Text = LocalizationService.Translate("settings.about.author");
            AboutEmailLabelText.Text = LocalizationService.Translate("settings.about.email");

            ThemeSystemRadio.Content = LocalizationService.Translate("settings.theme.system");
            ThemeLightRadio.Content = LocalizationService.Translate("settings.theme.light");
            ThemeDarkRadio.Content = LocalizationService.Translate("settings.theme.dark");
            bool isEnglish = _settings.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
            ThemeDescText.Text = isEnglish ? "Choose app appearance theme" : "选择应用的外观主题";
            LanguageDescText.Text = isEnglish ? "Change display language" : "切换界面显示语言";

            foreach (var obj in LanguageComboBox.Items)
            {
                if (obj is not ComboBoxItem item) continue;
                string tag = item.Tag?.ToString() ?? string.Empty;
                item.Content = tag switch
                {
                    "system" => LocalizationService.Translate("settings.language.system"),
                    "zh-Hans" => LocalizationService.Translate("settings.language.zh"),
                    "en-US" => LocalizationService.Translate("settings.language.en"),
                    _ => tag
                };
            }

            AboutNameValueText.Text = LocalizationService.Translate("settings.about.name");
            AboutVersionValueText.Text = GetVersionString();
            AboutBuildValueText.Text = GetBuildDateString();
            AboutAuthorValueText.Text = "蜇人鱼";
            AboutEmailValueText.Text = "y-zheren1@outlook.com";
        }

        private static string GetVersionString()
        {
            return "26H2";
        }

        private static string GetBuildDateString()
        {
            try
            {
                string assemblyPath = typeof(SettingsPage).Assembly.Location;
                DateTime date = File.GetLastWriteTime(assemblyPath);
                return date.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "--";
            }
        }

        private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
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

        private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            if (LanguageComboBox.SelectedItem is not ComboBoxItem item) return;
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

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = LocalizationService.Translate("settings.restart.title"),
                Content = LocalizationService.Translate("settings.restart.content"),
                PrimaryButtonText = LocalizationService.Translate("settings.restart.confirm"),
                CloseButtonText = LocalizationService.Translate("settings.restart.cancel"),
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
                foreach (var obj in LanguageComboBox.Items)
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

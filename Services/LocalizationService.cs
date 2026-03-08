using System;
using System.Collections.Generic;

namespace 音乐魔盒.Services
{
    public static class LocalizationService
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Resources = new(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-Hans"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["window.title"] = "音乐魔盒",
                ["nav.editor"] = "编辑",
                ["nav.convert"] = "转换",
                ["nav.recognize"] = "识别",
                ["nav.settings"] = "设置",
                ["settings.page_title"] = "设置",
                ["settings.section.personalization"] = "个性化",
                ["settings.theme"] = "主题",
                ["settings.theme.system"] = "跟随系统",
                ["settings.theme.light"] = "浅色",
                ["settings.theme.dark"] = "深色",
                ["settings.language"] = "界面语言",
                ["settings.language.system"] = "跟随系统",
                ["settings.language.zh"] = "简体中文",
                ["settings.language.en"] = "English",
                ["settings.section.about"] = "软件信息",
                ["settings.about.name"] = "音乐魔盒",
                ["settings.about.version"] = "版本号",
                ["settings.about.build"] = "构建号",
                ["settings.about.author"] = "作者",
                ["settings.about.email"] = "邮箱",
                ["settings.restart.title"] = "重启提示",
                ["settings.restart.content"] = "修改界面语言需要重启应用，未保存内容可能丢失。是否现在重启？",
                ["settings.restart.confirm"] = "立即重启",
                ["settings.restart.cancel"] = "稍后",
                ["editor.menu.file"] = "文件",
                ["editor.menu.new"] = "新建",
                ["editor.menu.open"] = "打开",
                ["editor.menu.save"] = "保存",
                ["editor.menu.save_as"] = "另存为",
                ["editor.menu.import_musicxml"] = "导入 MusicXML",
                ["editor.menu.export_musicxml"] = "导出 MusicXML",
                ["editor.menu.print"] = "打印...",
                ["editor.menu.time_signature"] = "拍号",
                ["editor.menu.key_signature"] = "调号",
                ["editor.menu.tempo"] = "速度",
                ["editor.menu.note_snap"] = "音符吸附",
                ["editor.menu.display"] = "显示",
                ["editor.menu.grid"] = "网格",
                ["editor.menu.area_select"] = "区域选择",
                ["editor.menu.clear"] = "清空",
                ["editor.toolbar.undo"] = "撤销 (Ctrl+Z)",
                ["editor.toolbar.redo"] = "恢复 (Ctrl+Y)",
                ["editor.toolbar.expressions"] = "表情记号",
                ["editor.toolbar.pedal"] = "踏板",
                ["editor.toolbar.slur"] = "连音",
                ["editor.toolbar.duration"] = "时值",
                ["editor.toolbar.note_type"] = "音符类型",
                ["editor.toolbar.add_system"] = "加一行",
                ["editor.toolbar.add_system_tooltip"] = "增加一行五线谱",
                ["editor.toolbar.play"] = "播放",
                ["editor.toolbar.pause"] = "暂停",
                ["editor.toolbar.stop"] = "停止",
                ["editor.note_length.none"] = "无",
                ["editor.note_length.whole"] = "全音符",
                ["editor.note_length.half"] = "二分",
                ["editor.note_length.quarter"] = "四分",
                ["editor.note_length.eighth"] = "八分",
                ["editor.note_length.sixteenth"] = "十六分",
                ["editor.note_length.thirty_second"] = "三十二分",
                ["editor.duration_mode.note"] = "音符",
                ["editor.duration_mode.rest"] = "休止符",
                ["editor.note_type.sharp"] = "升号",
                ["editor.note_type.flat"] = "降号",
                ["editor.note_type.natural"] = "还原号",
                ["editor.note_type.staccato"] = "跳音",
                ["editor.note_type.staccatissimo"] = "顿音",
                ["editor.note_type.accent"] = "重音",
                ["editor.note_type.dot"] = "附点",
                ["editor.expression.cresc_symbol"] = "渐响符号",
                ["editor.expression.dim_symbol"] = "渐轻符号",
                ["editor.expression.ottava"] = "八度记号",
                ["editor.expression.pedal"] = "踏板",
                ["editor.expression.pedal_release"] = "踏板抬起",
                ["editor.expression.pedal_line"] = "切分踏板线",
                ["editor.expression.tune"] = "调音记号",
                ["editor.expression.stacc"] = "顿音记号"
            },
            ["en-US"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["window.title"] = "Music Box",
                ["nav.editor"] = "Editor",
                ["nav.convert"] = "Convert",
                ["nav.recognize"] = "Recognize",
                ["nav.settings"] = "Settings",
                ["settings.page_title"] = "Settings",
                ["settings.section.personalization"] = "Personalization",
                ["settings.theme"] = "Theme",
                ["settings.theme.system"] = "Use system setting",
                ["settings.theme.light"] = "Light",
                ["settings.theme.dark"] = "Dark",
                ["settings.language"] = "Display language",
                ["settings.language.system"] = "Use system setting",
                ["settings.language.zh"] = "Chinese (Simplified)",
                ["settings.language.en"] = "English",
                ["settings.section.about"] = "About",
                ["settings.about.name"] = "Music Box",
                ["settings.about.version"] = "Version",
                ["settings.about.build"] = "Build",
                ["settings.about.author"] = "Author",
                ["settings.about.email"] = "Email",
                ["settings.restart.title"] = "Restart Required",
                ["settings.restart.content"] = "Changing display language requires a restart. Unsaved changes may be lost. Restart now?",
                ["settings.restart.confirm"] = "Restart now",
                ["settings.restart.cancel"] = "Later",
                ["editor.menu.file"] = "File",
                ["editor.menu.new"] = "New",
                ["editor.menu.open"] = "Open",
                ["editor.menu.save"] = "Save",
                ["editor.menu.save_as"] = "Save As",
                ["editor.menu.import_musicxml"] = "Import MusicXML",
                ["editor.menu.export_musicxml"] = "Export MusicXML",
                ["editor.menu.print"] = "Print...",
                ["editor.menu.time_signature"] = "Time",
                ["editor.menu.key_signature"] = "Key",
                ["editor.menu.tempo"] = "Tempo",
                ["editor.menu.note_snap"] = "Snap",
                ["editor.menu.display"] = "View",
                ["editor.menu.grid"] = "Grid",
                ["editor.menu.area_select"] = "Area Select",
                ["editor.menu.clear"] = "Clear",
                ["editor.toolbar.undo"] = "Undo (Ctrl+Z)",
                ["editor.toolbar.redo"] = "Redo (Ctrl+Y)",
                ["editor.toolbar.expressions"] = "Expressions",
                ["editor.toolbar.pedal"] = "Pedal",
                ["editor.toolbar.slur"] = "Slur",
                ["editor.toolbar.duration"] = "Duration",
                ["editor.toolbar.note_type"] = "Note Type",
                ["editor.toolbar.add_system"] = "Add Staff",
                ["editor.toolbar.add_system_tooltip"] = "Add one staff system",
                ["editor.toolbar.play"] = "Play",
                ["editor.toolbar.pause"] = "Pause",
                ["editor.toolbar.stop"] = "Stop",
                ["editor.note_length.none"] = "None",
                ["editor.note_length.whole"] = "Whole",
                ["editor.note_length.half"] = "Half",
                ["editor.note_length.quarter"] = "Quarter",
                ["editor.note_length.eighth"] = "Eighth",
                ["editor.note_length.sixteenth"] = "16th",
                ["editor.note_length.thirty_second"] = "32nd",
                ["editor.duration_mode.note"] = "Note",
                ["editor.duration_mode.rest"] = "Rest",
                ["editor.note_type.sharp"] = "Sharp",
                ["editor.note_type.flat"] = "Flat",
                ["editor.note_type.natural"] = "Natural",
                ["editor.note_type.staccato"] = "Staccato",
                ["editor.note_type.staccatissimo"] = "Staccatissimo",
                ["editor.note_type.accent"] = "Accent",
                ["editor.note_type.dot"] = "Dot",
                ["editor.expression.cresc_symbol"] = "Crescendo Symbol",
                ["editor.expression.dim_symbol"] = "Diminuendo Symbol",
                ["editor.expression.ottava"] = "Ottava",
                ["editor.expression.pedal"] = "Pedal",
                ["editor.expression.pedal_release"] = "Pedal Release",
                ["editor.expression.pedal_line"] = "Syncopated Pedal",
                ["editor.expression.tune"] = "Natural Sign",
                ["editor.expression.stacc"] = "Staccato Mark"
            }
        };

        static LocalizationService()
        {
            AppSettingsService.Instance.SettingsChanged += (_, __) => LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static event EventHandler? LanguageChanged;

        public static string Translate(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string language = AppSettingsService.Instance.ResolveLanguageTag();
            if (Resources.TryGetValue(language, out var table) && table.TryGetValue(key, out var localized))
            {
                return localized;
            }

            if (Resources.TryGetValue("en-US", out var fallback) && fallback.TryGetValue(key, out var english))
            {
                return english;
            }

            return key;
        }

        public static void RegisterLanguage(string languageTag, IReadOnlyDictionary<string, string> entries)
        {
            if (string.IsNullOrWhiteSpace(languageTag) || entries == null || entries.Count == 0)
            {
                return;
            }

            if (!Resources.TryGetValue(languageTag, out var table))
            {
                table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Resources[languageTag] = table;
            }

            foreach (var entry in entries)
            {
                table[entry.Key] = entry.Value;
            }

            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}

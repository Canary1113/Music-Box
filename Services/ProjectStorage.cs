using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MusicBox.Models;

namespace MusicBox.Services
{
    public sealed class ProjectStorage
    {
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public string DataRoot { get; }
        public string ProjectsFolder => Path.Combine(DataRoot, "projects");
        public string ExportsFolder => Path.Combine(DataRoot, "exports");
        public string RecoveryFolder => Path.Combine(DataRoot, "recovery");

        public ProjectStorage()
        {
            DataRoot = ResolveDataRoot();
        }

        public string GetDefaultProjectPath()
        {
            return Path.Combine(ProjectsFolder, "Untitled.json");
        }

        public (ScoreProject project, string path) LoadOrCreateDefault()
        {
            var path = GetDefaultProjectPath();
            if (!File.Exists(path))
            {
                return (ProjectFactory.CreateDefault(), path);
            }

            return (Load(path), path);
        }

        public ScoreProject Load(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                var project = JsonSerializer.Deserialize<ScoreProject>(json, _jsonOptions);
                if (project == null) return ProjectFactory.CreateDefault();

                project.Notes ??= new();
                project.ExpressionMarks ??= new();
                project.TimeSignatureChanges ??= new();
                project.KeySignatureChanges ??= new();
                project.StaffClefs ??= new();
                project.LayoutSystemMeasureCounts ??= new();
                project.LayoutBarlineOffsets ??= new();
                return project;
            }
            catch (Exception)
            {
                return ProjectFactory.CreateDefault();
            }
        }

        public void Save(ScoreProject project, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(project, _jsonOptions);
            File.WriteAllText(path, json);
        }

        public void SaveRecovery(ScoreProject project)
        {
            Directory.CreateDirectory(RecoveryFolder);
            var path = Path.Combine(RecoveryFolder, "auto-save.json");
            var json = JsonSerializer.Serialize(project, _jsonOptions);
            File.WriteAllText(path, json);
        }

        private static string ResolveDataRoot()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicMagic"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MusicMagic"),
                Path.Combine(AppContext.BaseDirectory, "AppData", "MusicMagic")
            };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (TryEnsureWritable(candidate))
                {
                    return candidate;
                }
            }

            string fallback = Path.Combine(Path.GetTempPath(), "MusicMagic");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private static bool TryEnsureWritable(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                string probePath = Path.Combine(path, ".write-test");
                File.WriteAllText(probePath, "ok");
                File.Delete(probePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

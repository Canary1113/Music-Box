using System;
using System.IO;
using MusicBox.Models;
using MusicBox.Services;

namespace MusicBox.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private readonly ProjectStorage _storage = new();
        private readonly MusicXmlExporter _musicXmlExporter = new();
        private readonly MusicXmlImporter _musicXmlImporter = new();
        private readonly MidiExporter _midiExporter = new();
        private readonly AudioExportService _audioExporter = new();

        private ScoreProject _project = ProjectFactory.CreateDefault();
        private string _statusText = "准备就绪";
        private string _currentProjectPath = string.Empty;
        private bool _isDirty;
        private int _snapDivision = 8;
        private NoteLength _selectedNoteLength = NoteLength.Quarter;
        private bool _showBeatGrid = true;
        private double _inputFrequency = 440.0;
        private string _frequencyResult = "A4 (+0 cents)";

        public MainViewModel()
        {
            NewCommand = new RelayCommand(NewProject);
            OpenCommand = new RelayCommand(OpenProject);
            SaveCommand = new RelayCommand(SaveProject);
            ExportMusicXmlCommand = new RelayCommand(ExportMusicXml);
            ExportMidiCommand = new RelayCommand(ExportMidi);

            LoadDefault();
        }

        public RelayCommand NewCommand { get; }
        public RelayCommand OpenCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand ExportMusicXmlCommand { get; }
        public RelayCommand ExportMidiCommand { get; }

        public ScoreProject Project
        {
            get => _project;
            private set
            {
                if (SetProperty(ref _project, value))
                {
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(Bpm));
                    OnPropertyChanged(nameof(TimeSigNumerator));
                    OnPropertyChanged(nameof(TimeSigDenominator));
                    OnPropertyChanged(nameof(KeySignatureFifths));
                }
            }
        }

        public string Title
        {
            get => Project.Title;
            set
            {
                if (Project.Title != value)
                {
                    Project.Title = value;
                    MarkDirty();
                    OnPropertyChanged();
                }
            }
        }

        public int Bpm
        {
            get => Project.Bpm;
            set
            {
                int clamped = Math.Clamp(value, 20, 300);
                if (Project.Bpm != clamped)
                {
                    Project.Bpm = clamped;
                    MarkDirty();
                    OnPropertyChanged();
                }
            }
        }

        public int TimeSigNumerator
        {
            get => Project.TimeSignature.Numerator;
            set
            {
                int clamped = Math.Clamp(value, 1, 12);
                if (Project.TimeSignature.Numerator != clamped)
                {
                    Project.TimeSignature.Numerator = clamped;
                    MarkDirty();
                    OnPropertyChanged();
                }
            }
        }

        public int TimeSigDenominator
        {
            get => Project.TimeSignature.Denominator;
            set
            {
                int sanitized = value is 1 or 2 or 4 or 8 or 16 ? value : 4;
                if (Project.TimeSignature.Denominator != sanitized)
                {
                    Project.TimeSignature.Denominator = sanitized;
                    MarkDirty();
                    OnPropertyChanged();
                }
            }
        }

        public int KeySignatureFifths
        {
            get => Project.KeySignature.Fifths;
            set
            {
                int clamped = Math.Clamp(value, -7, 7);
                if (Project.KeySignature.Fifths != clamped)
                {
                    Project.KeySignature.Fifths = clamped;
                    MarkDirty();
                    OnPropertyChanged();
                }
            }
        }

        public int SnapDivision
        {
            get => _snapDivision;
            set
            {
                if (SetProperty(ref _snapDivision, value))
                {
                    OnPropertyChanged();
                }
            }
        }

        public NoteLength SelectedNoteLength
        {
            get => _selectedNoteLength;
            set => SetProperty(ref _selectedNoteLength, value);
        }

        public bool ShowBeatGrid
        {
            get => _showBeatGrid;
            set => SetProperty(ref _showBeatGrid, value);
        }

        public double InputFrequency
        {
            get => _inputFrequency;
            set
            {
                if (SetProperty(ref _inputFrequency, value))
                {
                    UpdateFrequencyResult();
                }
            }
        }

        public string FrequencyResult
        {
            get => _frequencyResult;
            private set => SetProperty(ref _frequencyResult, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string CurrentProjectPath
        {
            get => _currentProjectPath;
            private set => SetProperty(ref _currentProjectPath, value);
        }

        public bool IsDirty
        {
            get => _isDirty;
            private set => SetProperty(ref _isDirty, value);
        }

        public void TouchProject()
        {
            Project.UpdatedAt = DateTimeOffset.Now;
            MarkDirty();
            _storage.SaveRecovery(Project);
        }

        public void SetStatus(string status)
        {
            StatusText = status ?? string.Empty;
        }

        public void LoadProjectSnapshot(ScoreProject snapshot)
        {
            if (snapshot == null) return;

            snapshot.Notes ??= new System.Collections.Generic.List<NoteEvent>();
            snapshot.ExpressionMarks ??= new System.Collections.Generic.List<ExpressionMark>();
            snapshot.TimeSignatureChanges ??= new System.Collections.Generic.List<TimeSignatureChange>();
            snapshot.KeySignatureChanges ??= new System.Collections.Generic.List<KeySignatureChange>();
            snapshot.StaffClefs ??= new System.Collections.Generic.Dictionary<string, string>();
            snapshot.LayoutSystemMeasureCounts ??= new System.Collections.Generic.List<int>();
            snapshot.LayoutBarlineOffsets ??= new System.Collections.Generic.Dictionary<int, float>();
            snapshot.UpdatedAt = DateTimeOffset.Now;
            Project = snapshot;
            IsDirty = true;
            _storage.SaveRecovery(Project);
        }

        public void CreateNewProject()
        {
            NewProject();
        }

        public void LoadProjectFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var project = _storage.Load(path);
            Project = project;
            CurrentProjectPath = path;
            IsDirty = false;
            StatusText = $"已打开: {Path.GetFileName(path)}";
        }

        public void ImportProjectFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var project = _storage.Load(path);
            Project = project;
            CurrentProjectPath = string.Empty;
            IsDirty = true;
            StatusText = $"已导入: {Path.GetFileName(path)}";
        }

        public void SaveProjectToPath(string path, bool updateCurrentPath)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            _storage.Save(Project, path);
            if (updateCurrentPath)
            {
                CurrentProjectPath = path;
            }

            IsDirty = false;
            StatusText = $"已保存: {Path.GetFileName(path)}";
        }

        public void ExportProjectToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            _storage.Save(Project, path);
            StatusText = $"已导出工程: {Path.GetFileName(path)}";
        }

        public void ExportMusicXmlToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            _musicXmlExporter.Export(Project, path);
            StatusText = $"已导出 MusicXML: {Path.GetFileName(path)}";
        }

        public void ImportMusicXmlFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var imported = _musicXmlImporter.Import(path);
            Project = imported;
            CurrentProjectPath = string.Empty;
            IsDirty = true;
            StatusText = $"已导入 MusicXML: {Path.GetFileName(path)}";
        }

        public void ExportMidiToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            _midiExporter.Export(Project, path);
            StatusText = $"已导出 MIDI: {Path.GetFileName(path)}";
        }

        public void ExportAudioToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            _audioExporter.ExportWav(Project, path);
            StatusText = $"已导出 WAV: {Path.GetFileName(path)}";
        }

        private void MarkDirty()
        {
            IsDirty = true;
            Project.UpdatedAt = DateTimeOffset.Now;
            _storage.SaveRecovery(Project);
        }

        private void LoadDefault()
        {
            var (project, path) = _storage.LoadOrCreateDefault();
            Project = project;
            CurrentProjectPath = path;
            IsDirty = false;
            StatusText = $"已加载: {Path.GetFileName(path)}";
            UpdateFrequencyResult();
        }

        private void NewProject()
        {
            Project = ProjectFactory.CreateDefault();
            CurrentProjectPath = _storage.GetDefaultProjectPath();
            IsDirty = true;
            StatusText = "新工程已创建";
        }

        private void OpenProject()
        {
            var (project, path) = _storage.LoadOrCreateDefault();
            Project = project;
            CurrentProjectPath = path;
            IsDirty = false;
            StatusText = $"已打开: {Path.GetFileName(path)}";
        }

        private void SaveProject()
        {
            if (string.IsNullOrWhiteSpace(CurrentProjectPath))
            {
                CurrentProjectPath = _storage.GetDefaultProjectPath();
            }

            SaveProjectToPath(CurrentProjectPath, updateCurrentPath: true);
        }

        private void ExportMusicXml()
        {
            var name = string.IsNullOrWhiteSpace(Project.Title) ? "Untitled" : Project.Title;
            var fileName = $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.musicxml";
            var path = Path.Combine(_storage.ExportsFolder, fileName);

            ExportMusicXmlToPath(path);
        }

        private void ExportMidi()
        {
            try
            {
                var name = string.IsNullOrWhiteSpace(Project.Title) ? "Untitled" : Project.Title;
                var fileName = $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.mid";
                var path = Path.Combine(_storage.ExportsFolder, fileName);

                ExportMidiToPath(path);
            }
            catch (Exception)
            {
                StatusText = "MIDI 导出尚未实现";
            }
        }

        private void UpdateFrequencyResult()
        {
            if (InputFrequency <= 0)
            {
                FrequencyResult = "--";
                return;
            }

            var (midi, cents) = PitchUtils.FrequencyToMidiWithCents(InputFrequency);
            var name = PitchUtils.MidiToName(midi);
            FrequencyResult = $"{name} ({cents:+0;-0;0} cents)";
        }
    }
}

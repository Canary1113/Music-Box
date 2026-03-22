using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Printing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Windows.Devices.Midi;
using Windows.Foundation;
using Windows.Graphics.Printing;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Text;
using MusicBox.Models;
using MusicBox.Services;
using MusicBox.ViewModels;

namespace MusicBox
{
    public sealed partial class EditorPage : Page
    {
        private sealed class NoteDrawInfo
        {
            public NoteEvent Note { get; set; } = null!;
            public float X { get; init; }
            public float Y { get; init; }
            public bool PreferTrebleStaff { get; init; }
            public int OttavaShiftOctaves { get; init; }
            public float HeadWidth { get; init; }
            public int VisualDurationTicks { get; init; }
            public int Beams { get; init; }
            public int DotCount { get; init; }
            public bool IsWhole { get; init; }
            public bool IsHalf { get; init; }
            public bool FillHead { get; init; }
            public bool StemUp { get; set; }
            public int MeasureIndex { get; init; }
            public bool IsBeamed { get; set; }
        }

        private sealed class BeamGroup
        {
            public List<NoteDrawInfo> Notes { get; set; } = new();
            public int Beams { get; init; }
            public bool StemUp { get; init; }
            public float BeamSlope { get; init; }
            public float BeamIntercept { get; init; }
        }

        private sealed class EditorHistoryState
        {
            public string ProjectJson { get; set; } = string.Empty;
            public int ManualAdditionalSystems { get; init; }
            public int ManualMeasureCount { get; init; }
            public Dictionary<int, float> BarlineOffsets { get; init; } = new();
            public List<int> SystemMeasureCounts { get; init; } = new();
        }

        private sealed class ClipboardNoteItem
        {
            public NoteEvent Note { get; init; } = new();
            public int StartOffset { get; init; }
        }

        private sealed class ClipboardExpressionItem
        {
            public ExpressionMark Mark { get; init; } = new();
            public int StartOffset { get; init; }
        }

        private sealed class EditorClipboardState
        {
            public List<ClipboardNoteItem> Notes { get; init; } = new();
            public List<ClipboardExpressionItem> Marks { get; init; } = new();
        }

        private readonly struct OrnamentHitTarget
        {
            public OrnamentHitTarget(Rect bounds, bool isGrace, float baseY)
            {
                Bounds = bounds;
                IsGrace = isGrace;
                BaseY = baseY;
            }

            public Rect Bounds { get; }
            public bool IsGrace { get; }
            public float BaseY { get; }
        }

        private readonly struct ClefHitTarget
        {
            public ClefHitTarget(int systemIndex, bool topStaff, Rect bounds, float anchorX, float anchorY)
            {
                SystemIndex = systemIndex;
                TopStaff = topStaff;
                Bounds = bounds;
                AnchorX = anchorX;
                AnchorY = anchorY;
            }

            public int SystemIndex { get; }
            public bool TopStaff { get; }
            public Rect Bounds { get; }
            public float AnchorX { get; }
            public float AnchorY { get; }
        }

        private MainViewModel? _viewModel;

        private const float HitRadius = 14f;
        private const float BaseNoteHeadWidth = 9f;
        private const float BaseNoteHeadHeight = 6f;
        private const float NoteHeadScale = 2.2f;
        private const float TrebleClefYOffset = 0.0f;
        private const float BassClefYOffset = 0.0f;
        private const float KeySignatureYOffset = 0.75f;
        private const float KeySignatureSharpYOffsetAdjust = -0.75f;
        private const float KeySignatureFlatYOffsetAdjust = -0.75f;
        private const float KeySignatureBassFlatYOffsetAdjust = 0.0f;
        private const float KeySignatureAdvance = 0.82f;
        private const float ClefSpaceFactor = 2.6f;
        private const float KeyGapAfterClef = 1.368f;
        private const float MusicGapAfterKey = 0.45f;
        private const float SymbolSizeGap = 10.5f;
        private const float ClefVerticalStretch = 1.0f;
        private const float TimeSigGapAfterKey = 1.085f;
        private const float TimeSigVerticalOffset = 0.0f;
        private const int FallbackAutoMeasuresPerSystem = 6;
        private const float StaffMiddleGapFactor = 5.16f;
        private const float SystemSpacingFactor = 10.8f;
        private const float PrintCompactMeasureWidthScale = 0.84f;
        private const float PrintVerticalLayoutScale = 0.90f;
        private const float PrintSideMarginScale = 0.95f;
        private const double PrintPageSidePaddingScale = 0.44d;
        private const float MaxBeamSlopeAbs = 0.36f;
        private const float DefaultExpressionStaffStepOffset = 18f;
        private const float ExpressionHitPadding = 8f;
        private const float ExpressionScale = 1.5f;
        private const float ExpressionGlyphUniformSpacingFactor = 0.06f;
        private const float OrnamentLeftShiftByAdvanceFactor = 0.8f;
        private const float GraceOrnamentLeftShiftByAdvanceFactor = 0.35f;
        private const float OrnamentSharedOffsetXSpaces = -0.18f;
        private const float OrnamentDistanceFromNoteSpaces = 0.72f;
        private const float TrillExtraDistanceSpaces = 0.34f;
        private const float GraceOrnamentOffsetXSpaces = 0f;
        private const float GraceOrnamentOffsetYSpaces = 0.22f;
        private const float GraceOrnamentStemUpYOffsetSpaces = 0.28f;
        private const float GraceOrnamentStemDownYOffsetSpaces = 0.36f;
        private const float BravuraStaffLineThicknessSpaces = 0.13f;
        private const float BravuraThinBarlineThicknessSpaces = 0.16f;
        private const float BravuraThickBarlineThicknessSpaces = 0.5f;
        private const float BravuraLedgerLineThicknessSpaces = 0.13f;
        private const float BravuraLedgerLineExtensionSpaces = 0.4f;
        private readonly bool _hideCustomNotationFallback = true;
        private const float StaffInteriorPaddingFactor = 0.08f;
        private const int TrebleLowerSwitchDiatonic = 22; // D3: treble down to 4 ledger lines
        private const int BassUpperSwitchDiatonic = 34; // B4: bass up to 4 ledger lines
        private const int TrebleBottomDiatonic = 30; // E4
        private const int BassBottomDiatonic = 18; // G2
        private const float MinCanvasHeight = 3600f;
        private const string MusicFontRelativeFolder = "Assets\\Fonts";
        private const string ForcedMusicFontFamily = "Bravura";
        private const string ForcedMusicFontFile = "Bravura.otf";
        private const string MusicTextFontFile = "BravuraText.otf";
        private const string MusicTextFontFamily = "Bravura Text";

        private const int SmuflGClef = 0xE050;
        private const int SmuflFClef = 0xE062;
        private const int SmuflAccidentalFlat = 0xE260;
        private const int SmuflAccidentalSharp = 0xE262;
        private const int SmuflTimeSig0 = 0xE080;
        private const int SmuflFlag8thUp = 0xE240;
        private const int SmuflFlag8thDown = 0xE241;
        private const int SmuflFlag16thUp = 0xE242;
        private const int SmuflFlag16thDown = 0xE243;
        private const int SmuflFlag32thUp = 0xE244;
        private const int SmuflFlag32thDown = 0xE245;
        private const int SmuflNoteheadWhole = 0xE0A2;
        private const int SmuflNoteheadHalf = 0xE0A3;
        private const int SmuflNoteheadBlack = 0xE0A4;
        private const int SmuflAccidentalNatural = 0xE261;
        private const int SmuflDynamicP = 0xE520;
        private const int SmuflDynamicM = 0xE521;
        private const int SmuflDynamicF = 0xE522;
        private const int SmuflDynamicS = 0xE524;
        private const int SmuflDynamicPP = 0xE52B;
        private const int SmuflDynamicPPP = 0xE52A;
        private const int SmuflDynamicMP = 0xE52C;
        private const int SmuflDynamicMF = 0xE52D;
        private const int SmuflDynamicFF = 0xE52F;
        private const int SmuflDynamicFFF = 0xE530;
        private const int SmuflArticStaccato = 0xE4A2;
        private const int SmuflArticAccentAbove = 0xE4A0;
        private const int SmuflArticAccentBelow = 0xE4A1;
        private const int SmuflArticStaccatissimoAbove = 0xE4A6;
        private const int SmuflArticStaccatissimoBelow = 0xE4A7;
        private const int SmuflAugmentationDot = 0xE1E7;
        private const int SmuflOrnamentTrill = 0xE566;
        private const int SmuflOrnamentShortTrill = 0xE56C;
        private const int SmuflOrnamentMordent = 0xE56D;
        private const int SmuflOrnamentTurn = 0xE567;
        private const int SmuflOrnamentTurnInverted = 0xE568;
        private const int SmuflGraceNoteAcciaccaturaStemUp = 0xE560;
        private const int SmuflGraceNoteAcciaccaturaStemDown = 0xE561;
        private const int SmuflGraceNoteAppoggiaturaStemUp = 0xE562;
        private const int SmuflGraceNoteAppoggiaturaStemDown = 0xE563;
        private const int SmuflTremolo3 = 0xE222;
        private const int SmuflUnmeasuredTremoloSimple = 0xE22D;
        private const int SmuflBrace = 0xE000;
        private const int SmuflPedalMark = 0xE650;
        private const int SmuflStaff5LinesWide = 0xE01A;
        private const int SmuflLegerLine = 0xE022;
        private const int SmuflBarlineSingle = 0xE030;
        private const int SmuflBarlineFinal = 0xE032;
        private const int SmuflBarlineRepeatRight = 0xE041;
        private const int SmuflSegno = 0xE047;
        private const int SmuflNoteWhole = 0xE1D2;
        private const int SmuflNoteHalfUp = 0xE1D3;
        private const int SmuflNoteHalfDown = 0xE1D4;
        private const int SmuflNoteQuarterUp = 0xE1D5;
        private const int SmuflNoteQuarterDown = 0xE1D6;
        private const int SmuflNote8thUp = 0xE1D7;
        private const int SmuflNote8thDown = 0xE1D8;
        private const int SmuflNote16thUp = 0xE1D9;
        private const int SmuflNote16thDown = 0xE1DA;
        private const int SmuflNote32thUp = 0xE1DB;
        private const int SmuflNote32thDown = 0xE1DC;
        private const int SmuflRestWhole = 0xE4E3;
        private const int SmuflRestHalf = 0xE4E4;
        private const int SmuflRestQuarter = 0xE4E5;
        private const int SmuflRest8th = 0xE4E6;
        private const int SmuflRest16th = 0xE4E7;
        private const int SmuflRest32th = 0xE4E8;
        private const int SmuflPedalUpMark = 0xE655;
        private const int SmuflMetNoteWhole = 0xECA2;
        private const int SmuflMetNoteHalfUp = 0xECA3;
        private const int SmuflMetNoteQuarterUp = 0xECA5;
        private const int SmuflMetNote8thUp = 0xECA7;
        private const int SmuflMetNote16thUp = 0xECA9;
        private const int SmuflMetNote32thUp = 0xECAB;
        private const float InlineSignatureStartOffsetFactor = 0.56f;
        private const string ScoreMarkGClef = "score_gclef";
        private const string ScoreMarkFClef = "score_fclef";
        private const string ScoreMarkFinalBarline = "score_final_barline";
        private const string ScoreMarkRepeatBarline = "score_repeat_barline";
        private const string ScoreMarkSegno = "score_segno";
        private const string ScoreMarkEnding1 = "score_ending_1";
        private const string ScoreMarkEnding2 = "score_ending_2";
        private const float SlurMaxSlopeSteps = 20f;
        private const float SlurAutoSlopeClampSteps = 18f;
        private static readonly TimeSpan InsertionAnchorMaxAge = TimeSpan.FromSeconds(5);
        private static readonly NoteLength[] NoteLengthCycleOrder =
        {
            NoteLength.Whole,
            NoteLength.Half,
            NoteLength.Quarter,
            NoteLength.Eighth,
            NoteLength.Sixteenth,
            NoteLength.ThirtySecond
        };

        private float _staffLeft = 36f;
        private float _staffGap = 12f;
        private float _staffMiddleGapFactor = StaffMiddleGapFactor;
        private float _beatWidth = 60f;
        private float _measureWidth = 240f;
        private float _staffWidth = 600f;
        private float _staffContentWidth = 600f;
        private float _primaryBottomLineY = 0f;
        private float _primaryTrebleTop = 0f;
        private float _primaryTrebleBottom = 0f;
        private float _primaryBassTop = 0f;
        private float _primaryBassBottom = 0f;
        private float _musicStartX = 36f;
        private int _beatsPerBar = 4;
        private int _ticksPerBeat = 480;
        private int _measuresPerSystem = 1;
        private int _autoMeasuresPerSystem = FallbackAutoMeasuresPerSystem;
        private int _displayMeasuresPerSystemOverride;
        private int _systemCount = 1;
        private float _systemStride = 0f;
        private float _systemTopMargin = 0f;
        private float _clefSpace = 0f;
        private float _keySpace = 0f;
        private float _timeSignatureBlockWidth = 0f;
        private int _manualAdditionalSystems;
        private int _manualMeasureCount;
        private int _totalMeasureCount = 1;
        private readonly List<int> _systemMeasureCounts = new();
        private readonly List<int> _measureTickBoundaries = new();
        private bool _allowAutoMeasureRatioAdjust;
        private bool _musicFontAvailable;
        private string _musicFontStatus = string.Empty;
        private bool _musicFontInstallPromptShown;
        private string _musicFontFamily = string.Empty;
        private string _musicFontUri = string.Empty;
        private CanvasFontSet? _musicFontSet;
        private CanvasFontFace? _musicFontFace;
        private CanvasFontSet? _musicTextFontSet;
        private CanvasFontFace? _musicTextFontFace;

        private bool _dragging;
        private bool _isSelectingRect;
        private Point _selectionStart;
        private Point _selectionEnd;
        private bool _pendingCanvasClickAction;
        private Point _pendingCanvasPressPoint;
        private bool _pendingCanvasPressShift;
        private bool _isDraggingSelectionGroup;
        private readonly Dictionary<NoteEvent, (int StartTick, int Midi, bool? PreferTrebleStaff)> _selectionGroupNoteBaseline = new();
        private readonly Dictionary<ExpressionMark, (int StartTick, float StaffStepOffset)> _selectionGroupMarkBaseline = new();
        private int _selectionGroupAnchorStartTick;
        private int _selectionGroupAnchorMidi;
        private bool _isTitleEditing;
        private Rect _titleHitRect;
        private NoteEvent? _activeNote;
        private NoteEvent? _beamLinkAnchorNote;
        private NoteEvent? _beamLinkDraggingNote;
        private float _beamLinkAnchorPointerY = float.NaN;
        private int _measurePanelMeasureIndex = -1;
        private int _measurePanelSystemIndex = -1;
        private int _measurePanelBoundaryLocalIndex = -1;
        private float _measurePanelBarlineX;
        private float _measurePanelTrebleTop;
        private readonly Dictionary<int, float> _barlineOffsets = new();
        private readonly Dictionary<int, float> _measureDemandFactorCache = new();
        private bool _isDraggingBarline;
        private int _dragBarlineSystemIndex = -1;
        private int _dragBarlineLocalIndex = -1;
        private int _dragBarlineMeasureIndex = -1;
        private float _dragBarlineTrebleTop;
        private float _dragBarlinePointerStartX;
        private bool _dragBarlineMoved;
        private int _highlightBarlineSystemIndex = -1;
        private int _highlightBarlineLocalIndex = -1;
        private ExpressionMark? _activeExpressionMark;
        private string? _pendingExpressionCode;
        private NoteAccidental _pendingAccidental = NoteAccidental.None;
        private bool _pendingStaccatissimo;
        private bool _pendingStaccato;
        private bool _pendingAccent;
        private bool _pendingAugmentationDot;
        private NoteOrnament _pendingOrnament = NoteOrnament.None;
        private bool _syncingNoteTypeControls;
        private bool _syncingDurationModeControls;
        private bool _isRestInputMode;
        private ExpressionDragMode _expressionDragMode = ExpressionDragMode.Move;
        private float _expressionDragOffsetX;
        private float _expressionDragOffsetY;
        private MenuFlyout? _contextMenu;
        private MenuFlyoutItem? _contextCopy;
        private MenuFlyoutItem? _contextCut;
        private MenuFlyoutItem? _contextPaste;
        private MenuFlyoutItem? _contextDelete;
        private float _pendingSlurSlopeSteps;
        private readonly MidiExporter _midiExporter = new();
        private MidiSynthesizer? _midiSynth;
        private DispatcherTimer? _playbackTimer;
        private readonly List<PlaybackEvent> _playbackEvents = new();
        private readonly List<PlaybackNoteSpan> _playbackNoteSpans = new();
        private readonly List<PlaybackCursorPoint> _playbackCursorPoints = new();
        private readonly Dictionary<int, int> _activePlaybackNotes = new();
        private int _playbackEventIndex;
        private int _playbackCurrentTick;
        private int _playbackTotalTicks;
        private double _playbackTicksPerSecond;
        private double _playbackVolume = 0.80d;
        private bool _isPlaybackRunning;
        private bool _isPlaybackPaused;
        private DateTimeOffset _playbackStartTimeUtc;
        private readonly string _playbackMidiPath = Path.Combine(Path.GetTempPath(), "musicbox-preview.mid");
        private bool _isDraggingPlaybackSlider;
        private bool _resumePlaybackAfterSliderDrag;
        private bool _syncingPlaybackSlider;
        private bool _playbackSliderPointerHandlersRegistered;
        private bool _isPlaybackOverlayPointerOver;
        private bool _isPlaybackOverlayExpanded;
        private bool _playbackOverlayScaleInitialized;
        private Storyboard? _playbackOverlayScaleStoryboard;
        private readonly DispatcherTimer _playbackOverlayCollapseTimer;
        private static bool _hasPersistedEditorViewState;
        private static double _persistedHorizontalOffset;
        private static double _persistedVerticalOffset;
        private static int _persistedPlaybackTick;
        private bool _isPreparingPrintPreview;
        private bool _forcePrintInkOnWhite;
        private readonly RasterPdfExportService _pdfExporter = new();
        private PrintManager? _printManager;
        private PrintDocument? _printDocument;
        private IPrintDocumentSource? _printDocumentSource;
        private readonly List<UIElement> _printPages = new();
        private readonly List<UIElement> _pendingPrintPages = new();
        private readonly List<RasterPdfPage> _pendingPdfPages = new();
        private readonly List<EditorHistoryState> _historyStates = new();
        private int _historyIndex = -1;
        private bool _isApplyingHistory;
        private bool _pendingHistoryCommitFromDrag;
        private EditorClipboardState? _clipboard;
        private Point _lastPointerCanvasPoint = new(-1, -1);
        private bool _hasPendingInsertionAnchor;
        private Point _pendingInsertionAnchorPoint = new(-1, -1);
        private DateTimeOffset _pendingInsertionAnchorTimestampUtc = DateTimeOffset.MinValue;
        private bool _pendingSlurGesture;
        private bool _pendingSlurGestureRectMode;
        private Point _pendingSlurGestureStart;
        private readonly Dictionary<NoteEvent, OrnamentHitTarget> _ornamentHitTargets = new();
        private NoteEvent? _activeOrnamentNote;
        private bool _activeOrnamentIsGrace;
        private float _activeOrnamentBaseY;
        private float _ornamentDragStartPointerX;
        private float _ornamentDragStartPointerY;
        private float _ornamentDragStartOffsetX;
        private float _ornamentDragStartOffsetY;
        private readonly List<ClefHitTarget> _clefHitTargets = new();
        private int _clefPanelSystemIndex = -1;
        private bool _clefPanelTopStaff;
        private float _clefPanelAnchorX;
        private float _clefPanelAnchorY;

        private readonly struct PlaybackEvent
        {
            public PlaybackEvent(int tick, int order, int midi, bool isOn, int velocity = 100)
            {
                Tick = tick;
                Order = order;
                Midi = midi;
                IsOn = isOn;
                Velocity = velocity;
            }

            public int Tick { get; }
            public int Order { get; }
            public int Midi { get; }
            public bool IsOn { get; }
            public int Velocity { get; }
        }

        private readonly struct PlaybackNoteSpan
        {
            public PlaybackNoteSpan(int startTick, int endTick, int midi, int velocity = 100)
            {
                StartTick = startTick;
                EndTick = endTick;
                Midi = midi;
                Velocity = velocity;
            }

            public int StartTick { get; }
            public int EndTick { get; }
            public int Midi { get; }
            public int Velocity { get; }
        }

        private readonly struct PlaybackCursorPoint
        {
            public PlaybackCursorPoint(int playbackTick, int sourceTick)
            {
                PlaybackTick = playbackTick;
                SourceTick = sourceTick;
            }

            public int PlaybackTick { get; }
            public int SourceTick { get; }
        }

        private readonly struct PlaybackRepeatSegment
        {
            public PlaybackRepeatSegment(int startTick, int endTick)
            {
                StartTick = Math.Max(0, startTick);
                EndTick = Math.Max(StartTick + 1, endTick);
            }

            public int StartTick { get; }
            public int EndTick { get; }
            public int DurationTicks => Math.Max(1, EndTick - StartTick);
        }

        private readonly struct PlaybackPedalRange
        {
            public PlaybackPedalRange(int startTick, int endTick)
            {
                StartTick = Math.Max(0, startTick);
                EndTick = Math.Max(StartTick + 1, endTick);
            }

            public int StartTick { get; }
            public int EndTick { get; }
        }

        private enum ExpressionDragMode
        {
            Move,
            ResizeSpan,
            ResizeHeight,
            ResizeSlope
        }

        private enum StaffClefType
        {
            Treble,
            Bass
        }

        private readonly CanvasTextFormat _clefFormat = new()
        {
            FontSize = 48
        };

        private readonly CanvasTextFormat _keyFormat = new()
        {
            FontSize = 22
        };

        private readonly CanvasTextFormat _timeSigFormat = new()
        {
            FontSize = 22
        };

        private readonly CanvasTextFormat _expressionTextFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 20
        };

        private readonly CanvasTextFormat _titleFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 26,
            HorizontalAlignment = CanvasHorizontalAlignment.Center
        };

        private readonly CanvasTextFormat _tempoFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 16
        };

        private readonly CanvasTextFormat _measureNumberFormat = new()
        {
            FontFamily = "Times New Roman",
            FontSize = 14
        };

        private static readonly JsonSerializerOptions HistoryJsonOptions = new()
        {
            WriteIndented = false
        };

        private float GetNoteHeadWidth() => BaseNoteHeadWidth * NoteHeadScale;
        private float GetNoteHeadHeight() => BaseNoteHeadHeight * NoteHeadScale;

        public EditorPage()
        {
            InitializeComponent();
            Loaded += EditorPage_Loaded;
            Unloaded += EditorPage_Unloaded;
            ActualThemeChanged += EditorPage_ActualThemeChanged;
            if (Resources.TryGetValue("ToolbarShadow", out var shadowResource) &&
                shadowResource is ThemeShadow themeShadow)
            {
                themeShadow.Receivers.Add(ToolbarShadowReceiver);
            }
            _playbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _playbackTimer.Tick += PlaybackTimer_Tick;
            _playbackOverlayCollapseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _playbackOverlayCollapseTimer.Tick += PlaybackOverlayCollapseTimer_Tick;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is MainViewModel vm)
            {
                AttachViewModel(vm);
            }

            if (_viewModel != null && _viewModel.SnapDivision <= 0)
            {
                _viewModel.SnapDivision = 8;
            }
            _manualAdditionalSystems = 0;
            _manualMeasureCount = 0;
            _autoMeasuresPerSystem = GetDefaultMeasuresPerSystemForTimeSignature(_viewModel?.TimeSigNumerator ?? 4, _viewModel?.TimeSigDenominator ?? 4);
            _barlineOffsets.Clear();
            _systemMeasureCounts.Clear();
            if (AccidentalSharpItem != null) AccidentalSharpItem.IsChecked = false;
            if (AccidentalFlatItem != null) AccidentalFlatItem.IsChecked = false;
            if (AccidentalNaturalItem != null) AccidentalNaturalItem.IsChecked = false;
            if (StaccatissimoMenuItem != null) StaccatissimoMenuItem.IsChecked = false;
            if (StaccatoMenuItem != null) StaccatoMenuItem.IsChecked = false;
            if (AccentMenuItem != null) AccentMenuItem.IsChecked = false;
            if (AugmentationDotMenuItem != null) AugmentationDotMenuItem.IsChecked = false;
            if (OrnamentNoneMenuItem != null) OrnamentNoneMenuItem.IsChecked = true;
            HideTitleInlineEditor(commitChanges: false);
            HideClefEditPanel();
            SyncPendingNoteTypeFromControls();
            SetTimeSignatureSelection(_viewModel?.TimeSigNumerator ?? 4, _viewModel?.TimeSigDenominator ?? 4);
            SetKeySignatureSelection(_viewModel?.KeySignatureFifths ?? 0);
            SetTempoSelection(_viewModel?.Bpm ?? 0);
            SetSnapSelection(_viewModel?.SnapDivision ?? 8);
            if (_viewModel != null)
            {
                _viewModel.SelectedNoteLength = NoteLength.None;
            }
            EnsureDefaultNoteLengthSelection();
            SetDurationInputModeSelection(_isRestInputMode);
            UpdateNoteTypeMenuEnabledState();
            ApplyLocalizedEditorText();
            ResetHistoryState();
            UpdatePlaybackProgressBar();
            try
            {
                InitializeMusicFontSelection();
            }
            catch (Exception ex)
            {
                _musicFontAvailable = false;
                _musicFontStatus = $"Music font init failed: {ex.Message}";
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HideTitleInlineEditor(commitChanges: false);
            HideClefEditPanel();
            if (ScoreScrollViewer != null)
            {
                _persistedHorizontalOffset = ScoreScrollViewer.HorizontalOffset;
                _persistedVerticalOffset = ScoreScrollViewer.VerticalOffset;
                _hasPersistedEditorViewState = true;
            }
            _persistedPlaybackTick = _playbackCurrentTick;
            StopPlaybackInternal(resetPosition: false);
            UnregisterPrintManager();
            DetachViewModel();
        }

        private void EditorPage_Loaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            if (!_playbackSliderPointerHandlersRegistered && PlaybackProgressSlider != null)
            {
                PlaybackProgressSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PlaybackProgressSlider_PointerPressed), true);
                PlaybackProgressSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PlaybackProgressSlider_PointerReleased), true);
                PlaybackProgressSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PlaybackProgressSlider_PointerCaptureLost), true);
                _playbackSliderPointerHandlersRegistered = true;
            }
            ApplyLocalizedEditorText();
            UpdatePlaybackOverlayTheme();
            UpdateTopToolbarLayout();
            _isPlaybackOverlayExpanded = true;
            UpdatePlaybackOverlayLayout();
            UpdatePlaybackOverlayScale(expanded: true, animate: false);
            if (_hasPersistedEditorViewState && ScoreScrollViewer != null)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    ScoreScrollViewer.ChangeView(_persistedHorizontalOffset, _persistedVerticalOffset, null, true);
                });
            }
            if (_persistedPlaybackTick > 0)
            {
                _playbackCurrentTick = _persistedPlaybackTick;
                UpdatePlaybackProgressBar();
            }
            StartPlaybackOverlayCollapseDelay();
        }

        private void EditorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            if (_playbackSliderPointerHandlersRegistered && PlaybackProgressSlider != null)
            {
                PlaybackProgressSlider.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PlaybackProgressSlider_PointerPressed));
                PlaybackProgressSlider.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PlaybackProgressSlider_PointerReleased));
                PlaybackProgressSlider.RemoveHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PlaybackProgressSlider_PointerCaptureLost));
                _playbackSliderPointerHandlersRegistered = false;
            }
            _playbackOverlayCollapseTimer.Stop();
            DisablePlaybackRefractionEffect();
        }

        private void EditorPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdatePlaybackOverlayTheme();
            StaffCanvas?.Invalidate();
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTopToolbarLayout();
            UpdatePlaybackOverlayLayout();
        }

        private void ScoreScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTopToolbarLayout();
            UpdatePlaybackOverlayLayout();
            StaffCanvas?.Invalidate();
        }

        private void UpdateTopToolbarLayout()
        {
            if (TopToolbarOverlay == null)
            {
                return;
            }

            double viewportWidth = ScoreScrollViewer?.ActualWidth > 1d
                ? ScoreScrollViewer.ActualWidth
                : (RootGrid?.ActualWidth ?? 0d);
            if (viewportWidth <= 1d)
            {
                return;
            }

            double targetMaxWidth = Math.Clamp(viewportWidth * 0.90d, 520d, 2200d);
            targetMaxWidth = Math.Min(targetMaxWidth, Math.Max(420d, viewportWidth - 24d));
            TopToolbarOverlay.Width = double.NaN;
            TopToolbarOverlay.MaxWidth = targetMaxWidth;
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            ApplyLocalizedEditorText();
            StaffCanvas?.Invalidate();
        }

        private void AttachViewModel(MainViewModel vm)
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _viewModel = vm;
            DataContext = vm;
            _viewModel.Project.Notes ??= new List<NoteEvent>();
            _viewModel.Project.ExpressionMarks ??= new List<ExpressionMark>();
            _viewModel.Project.TimeSignatureChanges ??= new List<TimeSignatureChange>();
            _viewModel.Project.KeySignatureChanges ??= new List<KeySignatureChange>();
            _viewModel.Project.StaffClefs ??= new Dictionary<string, string>();
            _viewModel.Project.LayoutSystemMeasureCounts ??= new List<int>();
            _viewModel.Project.LayoutBarlineOffsets ??= new Dictionary<int, float>();
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            RestoreLayoutFromProject();
        }

        private void DetachViewModel()
        {
            if (_viewModel == null) return;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.KeySignatureFifths))
            {
                SetKeySignatureSelection(_viewModel?.KeySignatureFifths ?? 0);
            }
            else if (e.PropertyName == nameof(MainViewModel.TimeSigNumerator)
                || e.PropertyName == nameof(MainViewModel.TimeSigDenominator))
            {
                SetTimeSignatureSelection(_viewModel?.TimeSigNumerator ?? 4, _viewModel?.TimeSigDenominator ?? 4);
            }
            else if (e.PropertyName == nameof(MainViewModel.Bpm))
            {
                SetTempoSelection(_viewModel?.Bpm ?? 0);
            }
            else if (e.PropertyName == nameof(MainViewModel.SnapDivision))
            {
                SetSnapSelection(_viewModel?.SnapDivision ?? 8);
            }
            else if (e.PropertyName == nameof(MainViewModel.SelectedNoteLength))
            {
                EnsureDefaultNoteLengthSelection();
            }

            if (!_isApplyingHistory
                && (e.PropertyName == nameof(MainViewModel.Title)
                    || e.PropertyName == nameof(MainViewModel.Bpm)
                    || e.PropertyName == nameof(MainViewModel.KeySignatureFifths)
                    || e.PropertyName == nameof(MainViewModel.TimeSigNumerator)
                    || e.PropertyName == nameof(MainViewModel.TimeSigDenominator)))
            {
                PushHistorySnapshot();
            }

            StaffCanvas.Invalidate();
        }

        private void StaffCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (_viewModel == null) return;

            var ds = args.DrawingSession;
            Color paperColor = GetScorePaperColor();
            ds.Clear(paperColor);
            try
            {

            var width = (float)sender.ActualWidth;
            var height = (float)sender.ActualHeight;
            DrawScoreToSession(ds, width, height, _isSelectingRect);
            }
            catch (Exception ex)
            {
                _musicFontAvailable = false;
                _musicFontStatus = $"Render error: {ex.Message}";
                DrawMissingFontNotice(ds, (float)sender.ActualWidth);
            }

            UpdateNoteStepPanel();
        }

        private void DrawScoreToSession(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float width,
            float height,
            bool drawSelectionOverlay,
            float? layoutWidthOverride = null,
            bool? suppressSelectionVisualsOverride = null,
            bool? suppressBeatGridOverride = null,
            bool compactForPrintLayout = false)
        {
            if (_viewModel == null) return;

            _staffGap = 12f;
            float sizeGap = SymbolSizeGap;
            _beatWidth = GetBeatWidth();
            float layoutWidth = layoutWidthOverride.HasValue && layoutWidthOverride.Value > 1f
                ? layoutWidthOverride.Value
                : (float)(ScoreScrollViewer?.ActualWidth > 1d ? ScoreScrollViewer.ActualWidth : width);
            bool suppressSelectionVisuals = suppressSelectionVisualsOverride ?? false;
            bool suppressBeatGrid = suppressBeatGridOverride ?? false;
            _measureDemandFactorCache.Clear();
            float minSidePadding = compactForPrintLayout ? 12f : 25f;
            float maxSidePadding = compactForPrintLayout ? 20f : 40f;
            const float paddingWidthMin = 980f;
            const float paddingWidthMax = 2100f;
            float widthRatio = Math.Clamp((layoutWidth - paddingWidthMin) / Math.Max(1f, paddingWidthMax - paddingWidthMin), 0f, 1f);
            float sidePadding = minSidePadding + (maxSidePadding - minSidePadding) * widthRatio;
            float rawWidth = Math.Max(0, layoutWidth - sidePadding * 2f);

            int beatsPerBar = Math.Max(1, _viewModel.TimeSigNumerator);
            _beatsPerBar = beatsPerBar;
            _ticksPerBeat = GetTicksPerBeat();
            int fifths = _viewModel.Project.KeySignature.Fifths;
            int maxKeyAbs = Math.Abs(fifths);
            if (_viewModel.Project.KeySignatureChanges != null && _viewModel.Project.KeySignatureChanges.Count > 0)
            {
                int changeMax = _viewModel.Project.KeySignatureChanges
                    .Where(c => c != null)
                    .Select(c => Math.Abs(Math.Clamp(c.Fifths, -7, 7)))
                    .DefaultIfEmpty(0)
                    .Max();
                maxKeyAbs = Math.Max(maxKeyAbs, changeMax);
            }

            int keyCount = Math.Min(maxKeyAbs, 7);
            float keyAdvance = sizeGap * KeySignatureAdvance;
            _clefSpace = sizeGap * ClefSpaceFactor;
            _keySpace = keyCount > 0 ? keyCount * keyAdvance + sizeGap * 0.6f : 0f;
            _clefFormat.FontSize = sizeGap * 4.2f;
            _keyFormat.FontSize = sizeGap * 2.2f * 1.25f;
            _timeSigFormat.FontSize = sizeGap * 3.6f;
            int timeDigits = Math.Max(_viewModel.TimeSigNumerator.ToString().Length, _viewModel.TimeSigDenominator.ToString().Length);
            float timeSpace = Math.Max(sizeGap * (1.1f * timeDigits + 0.4f), _timeSigFormat.FontSize * 0.9f);
            _timeSignatureBlockWidth = sizeGap * TimeSigGapAfterKey + timeSpace;
            float reservedLeft = _clefSpace + _keySpace + sizeGap * TimeSigGapAfterKey + timeSpace + sizeGap * MusicGapAfterKey;

            float baseMeasureWidth = beatsPerBar * _beatWidth;
            float musicWidth = Math.Max(0, rawWidth - reservedLeft);
            int autoMeasuresPerSystem = GetDefaultMeasuresPerSystemForTimeSignature(_viewModel.TimeSigNumerator, _viewModel.TimeSigDenominator);
            int previousAutoMeasuresPerSystem = _autoMeasuresPerSystem;
            _autoMeasuresPerSystem = autoMeasuresPerSystem;
            if (_displayMeasuresPerSystemOverride <= 0 && previousAutoMeasuresPerSystem != autoMeasuresPerSystem)
            {
                _systemMeasureCounts.Clear();
                _barlineOffsets.Clear();
            }

            int targetMeasuresPerSystem = _displayMeasuresPerSystemOverride > 0
                ? Math.Max(1, _displayMeasuresPerSystemOverride)
                : Math.Max(1, autoMeasuresPerSystem);

            float widthScale = compactForPrintLayout ? PrintCompactMeasureWidthScale : 0.965f;
            float targetMusicWidth = Math.Max(1f, musicWidth * widthScale);
            float targetMeasureWidthByWindow = targetMusicWidth / Math.Max(1, targetMeasuresPerSystem);
            float minMeasureWidth = Math.Max(_staffGap * 1.7f, baseMeasureWidth * 0.35f);
            float measureWidth = Math.Max(minMeasureWidth, targetMeasureWidthByWindow);
            _measureWidth = measureWidth;
            _measuresPerSystem = Math.Max(1, targetMeasuresPerSystem);
            _staffContentWidth = _measuresPerSystem * measureWidth;
            float contentWidth = reservedLeft + _staffContentWidth;
            _staffWidth = Math.Min(rawWidth, contentWidth);
            _staffLeft = Math.Max(6f, (layoutWidth - _staffWidth) / 2f);
            _musicStartX = _staffLeft + reservedLeft;
            if (_musicStartX > _staffLeft + _staffWidth)
            {
                _musicStartX = _staffLeft + _staffWidth;
            }

            float middleGapFactor = compactForPrintLayout ? StaffMiddleGapFactor * PrintVerticalLayoutScale : StaffMiddleGapFactor;
            _staffMiddleGapFactor = middleGapFactor;
            float systemHeight = (4f + middleGapFactor + 4f) * _staffGap;
            float systemSpacingFactor = compactForPrintLayout ? SystemSpacingFactor * PrintVerticalLayoutScale : SystemSpacingFactor;
            float systemSpacing = _staffGap * systemSpacingFactor;
            float systemStride = systemHeight + systemSpacing;
            float headerReserveFactor = compactForPrintLayout ? 21.5f * PrintVerticalLayoutScale : 21.5f;
            float headerReserve = _staffGap * headerReserveFactor;
            int measureTicks = Math.Max(1, _ticksPerBeat * beatsPerBar);
            int desiredTotalMeasureCount = _displayMeasuresPerSystemOverride > 0
                ? GetFixedModeTotalMeasureCount(measureTicks, _measuresPerSystem)
                : GetTotalMeasureCount(measureTicks, _measuresPerSystem);
            if (_displayMeasuresPerSystemOverride > 0)
            {
                EnsureFixedSystemMeasureCounts(_measuresPerSystem, desiredTotalMeasureCount);
            }
            else
            {
                int autoSystemCount = GetRequiredSystemCount(_ticksPerBeat, beatsPerBar, _measuresPerSystem);
                int baselineSystemCount = _systemMeasureCounts.Count > 0
                    ? _systemMeasureCounts.Count
                    : autoSystemCount;
                int minSystemCount = Math.Max(baselineSystemCount, 1 + Math.Max(0, _manualAdditionalSystems));
                EnsureSystemMeasureCounts(_measuresPerSystem, minSystemCount, desiredTotalMeasureCount);
                if (ReflowDenseMeasuresAcrossSystems())
                {
                    _barlineOffsets.Clear();
                }
            }
            _totalMeasureCount = Math.Max(1, _systemMeasureCounts.Sum());
            int systemCount = Math.Max(1, _systemMeasureCounts.Count);
            RebuildMeasureTickBoundaries(_totalMeasureCount);
            float topMarginFloorFactor = compactForPrintLayout ? 21.7f * PrintVerticalLayoutScale : 21.7f;
            float topMargin = Math.Max(18f + headerReserve, _staffGap * topMarginFloorFactor);
            float staffLineThickness = GetStaffLineThickness();
            topMargin = AlignAxisForStroke(topMargin, staffLineThickness, ds.Dpi);
            systemStride = AlignDistanceToPixelGrid(systemStride, ds.Dpi);
            float contentHeight = topMargin + Math.Max(0, systemCount - 1) * systemStride + systemHeight + _staffGap * 3.2f;
            float maxMusicWidth = Math.Max(1f, _staffContentWidth);
            float desiredCanvasWidth = Math.Max(layoutWidth, reservedLeft + maxMusicWidth + sidePadding * 2f + _staffGap * 0.6f);
            EnsureStaffCanvasExtent(desiredCanvasWidth, contentHeight);
            _systemCount = systemCount;
            _systemStride = systemStride;
            _systemTopMargin = topMargin;
            _ornamentHitTargets.Clear();
            _clefHitTargets.Clear();

            DrawScoreHeader(ds, topMargin);

            for (int systemIndex = 0; systemIndex < systemCount; systemIndex++)
            {
                float systemTop = topMargin + systemIndex * systemStride;
                float trebleTop = systemTop;
                float trebleBottom = trebleTop + 4f * _staffGap;
                float bassTop = trebleBottom + middleGapFactor * _staffGap;
                float staffBottom = bassTop + 4f * _staffGap;
                int measuresInSystem = GetMeasuresInSystem(systemIndex);

                DrawStaff(ds, systemIndex, trebleTop, bassTop);
                DrawGrid(ds, systemIndex, trebleTop, staffBottom, beatsPerBar, measuresInSystem, systemIndex == systemCount - 1, suppressBeatGrid);
                DrawSystemMeasureNumber(ds, systemIndex, trebleTop);

                if (systemIndex == 0)
                {
                    _primaryTrebleTop = trebleTop;
                    _primaryTrebleBottom = trebleTop + 4f * _staffGap;
                    _primaryBassTop = bassTop;
                    _primaryBassBottom = bassTop + 4f * _staffGap;
                    _primaryBottomLineY = _primaryTrebleBottom;
                }

                DrawNotes(ds, systemIndex, measuresInSystem, trebleTop, bassTop, suppressSelectionVisuals);
                DrawExpressionMarks(ds, systemIndex, measuresInSystem, trebleTop, suppressSelectionVisuals);
                DrawPlaybackCursor(ds, systemIndex, measuresInSystem, trebleTop, staffBottom);
            }

            if (drawSelectionOverlay && _isSelectingRect)
            {
                DrawSelectionRectangle(ds);
            }

            if (!_musicFontAvailable)
            {
                DrawMissingFontNotice(ds, width);
            }
        }

        private int GetRequiredSystemCount(int ticksPerBeat, int beatsPerBar, int measuresPerSystem)
        {
            if (_viewModel == null) return 1;

            int safeTicksPerBeat = Math.Max(1, ticksPerBeat);
            int safeBeats = Math.Max(1, beatsPerBar);
            int safeMeasuresPerSystem = Math.Max(1, measuresPerSystem);
            int measureTicks = Math.Max(1, safeTicksPerBeat * safeBeats);
            int measureCount = GetTotalMeasureCount(measureTicks, safeMeasuresPerSystem);
            int autoSystems = Math.Max(1, (int)Math.Ceiling(measureCount / (double)safeMeasuresPerSystem));
            return Math.Max(1, autoSystems);
        }

        private int GetTotalMeasureCount(int measureTicks, int measuresPerSystem)
        {
            int perSystem = Math.Max(1, measuresPerSystem);
            int contentMeasureCount = GetContentMeasureCount(measureTicks);
            int manualMeasureCount = Math.Max(1, _manualMeasureCount);
            int total = Math.Max(contentMeasureCount, manualMeasureCount);
            int minimumRows = Math.Max(1, 1 + Math.Max(0, _manualAdditionalSystems));
            int minimumMeasureCount = Math.Max(1, minimumRows * perSystem);
            if (_systemMeasureCounts.Count > 0)
            {
                int preserved = _systemMeasureCounts.Sum();
                if (_systemMeasureCounts.Count < minimumRows)
                {
                    preserved += (minimumRows - _systemMeasureCounts.Count) * perSystem;
                }

                minimumMeasureCount = Math.Max(minimumMeasureCount, Math.Max(1, preserved));
            }

            return Math.Max(total, minimumMeasureCount);
        }

        private int GetFixedModeTotalMeasureCount(int measureTicks, int measuresPerSystem)
        {
            int perSystem = Math.Max(1, measuresPerSystem);
            int contentMeasureCount = GetContentMeasureCount(measureTicks);
            int manualMeasureCount = Math.Max(1, _manualMeasureCount);
            int minimumRows = Math.Max(1, 1 + Math.Max(0, _manualAdditionalSystems));
            int minimumMeasureCount = Math.Max(1, minimumRows * perSystem);
            return Math.Max(Math.Max(contentMeasureCount, manualMeasureCount), minimumMeasureCount);
        }

        private static int GetDefaultMeasuresPerSystemForTimeSignature(int numerator, int denominator)
        {
            int safeNumerator = Math.Clamp(numerator, 1, 12);
            int safeDenominator = denominator is 1 or 2 or 4 or 8 or 16 ? denominator : 4;

            if (safeNumerator == 4 && safeDenominator == 4) return 4;
            if (safeNumerator == 3 && safeDenominator == 4) return 5;
            if (safeNumerator == 6 && safeDenominator == 8) return 5;

            double quarterPerMeasure = safeNumerator * (4d / safeDenominator);
            if (quarterPerMeasure <= 0.0)
            {
                return FallbackAutoMeasuresPerSystem;
            }

            int estimated = (int)Math.Floor(20d / quarterPerMeasure);
            return Math.Clamp(estimated, 3, 10);
        }

        private int GetMeasuresInSystem(int systemIndex)
        {
            int safeSystemIndex = Math.Max(0, systemIndex);
            if (_systemMeasureCounts.Count > safeSystemIndex)
            {
                return Math.Max(1, _systemMeasureCounts[safeSystemIndex]);
            }

            return Math.Max(1, _measuresPerSystem);
        }

        private int GetSystemStartMeasureIndex(int systemIndex)
        {
            int safeSystemIndex = Math.Max(0, systemIndex);
            int start = 0;
            for (int i = 0; i < safeSystemIndex && i < _systemMeasureCounts.Count; i++)
            {
                start += Math.Max(1, _systemMeasureCounts[i]);
            }

            if (_systemMeasureCounts.Count == 0)
            {
                start = safeSystemIndex * Math.Max(1, _measuresPerSystem);
            }

            return Math.Max(0, start);
        }

        private int GetSystemIndexForMeasureIndex(int measureIndex)
        {
            int safeMeasureIndex = Math.Max(0, measureIndex);
            if (_systemMeasureCounts.Count == 0)
            {
                return Math.Max(0, safeMeasureIndex / Math.Max(1, _measuresPerSystem));
            }

            int cursor = 0;
            for (int system = 0; system < _systemMeasureCounts.Count; system++)
            {
                int count = Math.Max(1, _systemMeasureCounts[system]);
                if (safeMeasureIndex < cursor + count)
                {
                    return system;
                }

                cursor += count;
            }

            return Math.Max(0, _systemMeasureCounts.Count - 1);
        }

        private void EnsureSystemMeasureCounts(int defaultPerSystem, int minimumSystems, int minimumTotalMeasures)
        {
            int safeDefault = Math.Max(1, defaultPerSystem);
            int safeMinSystems = Math.Max(1, minimumSystems);
            int safeTotal = Math.Max(1, minimumTotalMeasures);

            if (_systemMeasureCounts.Count == 0)
            {
                for (int i = 0; i < safeMinSystems; i++)
                {
                    _systemMeasureCounts.Add(safeDefault);
                }
            }

            while (_systemMeasureCounts.Count < safeMinSystems)
            {
                _systemMeasureCounts.Add(safeDefault);
            }

            for (int i = 0; i < _systemMeasureCounts.Count; i++)
            {
                _systemMeasureCounts[i] = Math.Max(1, _systemMeasureCounts[i]);
            }

            int total = _systemMeasureCounts.Sum();
            while (total < safeTotal)
            {
                int last = Math.Max(0, _systemMeasureCounts.Count - 1);
                _systemMeasureCounts[last]++;
                total++;
            }
        }

        private void EnsureFixedSystemMeasureCounts(int fixedPerSystem, int minimumTotalMeasures)
        {
            int safePerSystem = Math.Max(1, fixedPerSystem);
            int safeTotal = Math.Max(1, minimumTotalMeasures);
            int minSystemsByContent = Math.Max(1, (int)Math.Ceiling(safeTotal / (double)safePerSystem));
            int minSystemsByManual = Math.Max(1, 1 + Math.Max(0, _manualAdditionalSystems));
            int requiredSystems = Math.Max(minSystemsByContent, minSystemsByManual);

            _systemMeasureCounts.Clear();
            for (int i = 0; i < requiredSystems; i++)
            {
                _systemMeasureCounts.Add(safePerSystem);
            }
        }

        private bool ReflowDenseMeasuresAcrossSystems()
        {
            if (_viewModel?.Project == null || _systemMeasureCounts.Count == 0)
            {
                return false;
            }

            bool changed = false;
            int safety = 0;
            while (safety++ < 256)
            {
                bool movedAny = false;
                int systemStartMeasure = 0;
                for (int systemIndex = 0; systemIndex < _systemMeasureCounts.Count; systemIndex++)
                {
                    int measuresInSystem = Math.Max(1, _systemMeasureCounts[systemIndex]);
                    while (measuresInSystem > 1
                           && !CanSystemSatisfyMeasureDemand(systemIndex, systemStartMeasure, measuresInSystem))
                    {
                        measuresInSystem--;
                        _systemMeasureCounts[systemIndex] = measuresInSystem;
                        if (systemIndex + 1 >= _systemMeasureCounts.Count)
                        {
                            _systemMeasureCounts.Add(1);
                        }
                        else
                        {
                            _systemMeasureCounts[systemIndex + 1] = Math.Max(1, _systemMeasureCounts[systemIndex + 1] + 1);
                        }

                        changed = true;
                        movedAny = true;
                    }

                    systemStartMeasure += _systemMeasureCounts[systemIndex];
                }

                if (!movedAny)
                {
                    break;
                }
            }

            return changed;
        }

        private bool CanSystemSatisfyMeasureDemand(int systemIndex, int systemStartMeasure, int measuresInSystem)
        {
            int count = Math.Max(1, measuresInSystem);
            float startX = GetSystemMusicStartX(systemIndex);
            float endX = GetSystemContentRightX();
            float availableWidth = Math.Max(1f, endX - startX);
            float preferredSum = 0f;
            float minimumSum = 0f;

            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                int globalMeasure = Math.Max(0, systemStartMeasure + localIndex);
                float demand = GetMeasureVisualDemandFactor(globalMeasure);
                float preferred = Math.Max(_measureWidth * 0.42f, _measureWidth * (0.54f + demand * 0.62f));
                if (HasMeasureBarlineScoreMarks(globalMeasure))
                {
                    preferred += Math.Max(_staffGap * 1.6f, _measureWidth * 0.10f);
                }

                preferredSum += preferred;
                minimumSum += Math.Min(availableWidth, GetDynamicMeasureMinWidth(systemIndex, localIndex, count));
            }

            if (preferredSum <= availableWidth)
            {
                return true;
            }

            return minimumSum <= availableWidth;
        }

        private int GetBarlineOffsetKey(int systemIndex, int localBoundaryIndex)
        {
            return systemIndex * 1000 + localBoundaryIndex;
        }

        private float GetSystemRightX(int systemIndex)
        {
            int measuresInSystem = GetMeasuresInSystem(systemIndex);
            float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
            return boundaries[Math.Max(1, measuresInSystem)];
        }

        private float GetSystemContentRightX()
        {
            return _musicStartX + Math.Max(1f, _staffContentWidth);
        }

        private bool ShouldDrawTimeSignatureAtSystemStart(int systemIndex)
        {
            if (_viewModel == null)
            {
                return systemIndex <= 0;
            }

            int safeSystem = Math.Max(0, systemIndex);
            int systemStartMeasure = GetSystemStartMeasureIndex(safeSystem);
            int systemStartTick = GetMeasureBoundaryTick(systemStartMeasure);
            if (safeSystem == 0)
            {
                return true;
            }

            return _viewModel.Project.TimeSignatureChanges.Any(c => c != null && Math.Max(0, c.Tick) == systemStartTick);
        }

        private float GetSystemMusicStartX(int systemIndex)
        {
            float startX = _musicStartX;
            if (systemIndex <= 0 || _timeSignatureBlockWidth <= 0f)
            {
                return startX;
            }

            if (!ShouldDrawTimeSignatureAtSystemStart(systemIndex))
            {
                float minStart = _staffLeft + _clefSpace + _keySpace + SymbolSizeGap * MusicGapAfterKey;
                startX = Math.Max(minStart, startX - _timeSignatureBlockWidth);
            }

            return startX;
        }

        private float GetBaseBarlineX(int systemIndex, int localBoundaryIndex, int measuresInSystem)
        {
            int safeMeasures = Math.Max(1, measuresInSystem);
            int clampedBoundary = Math.Clamp(localBoundaryIndex, 0, safeMeasures);
            float startX = GetSystemMusicStartX(systemIndex);
            float endX = GetSystemContentRightX();
            float[] baseBoundaries = GetSystemBaseBarlinePositions(systemIndex, safeMeasures, startX, endX);
            return baseBoundaries[clampedBoundary];
        }

        private float[] GetSystemBarlinePositions(int systemIndex, int measuresInSystem)
        {
            int count = Math.Max(1, measuresInSystem);
            var boundaries = new float[count + 1];
            float startX = GetSystemMusicStartX(systemIndex);
            // Keep each system's terminal barline at a stable X position.
            float endX = GetSystemContentRightX();
            float defaultMeasureWidth = _allowAutoMeasureRatioAdjust
                ? Math.Max(1f, (endX - startX) / count)
                : Math.Max(1f, _measureWidth);
            float availableWidth = Math.Max(1f, endX - startX);
            float[] baseBoundaries = GetSystemBaseBarlinePositions(systemIndex, count, startX, endX);
            int systemStartMeasure = GetSystemStartMeasureIndex(systemIndex);
            bool tailMeasureEmpty = IsMeasureEmpty(systemStartMeasure + count - 1);
            var minMeasureWidths = new float[count];
            for (int i = 0; i < count; i++)
            {
                int globalMeasure = systemStartMeasure + i;
                float demand = GetMeasureVisualDemandFactor(globalMeasure);
                float baseSegmentWidth = Math.Max(1f, baseBoundaries[i + 1] - baseBoundaries[i]);
                float minWidth = Math.Max(_staffGap * 3f, Math.Max(defaultMeasureWidth * 0.30f, baseSegmentWidth * 0.30f));
                if (demand > 1.45f)
                {
                    minWidth = Math.Max(minWidth, Math.Min(defaultMeasureWidth * 0.52f, baseSegmentWidth * 0.58f));
                }

                if (HasMeasureBarlineScoreMarks(globalMeasure))
                {
                    minWidth = Math.Max(minWidth, Math.Max(_staffGap * 3.5f, defaultMeasureWidth * 0.40f));
                }

                minMeasureWidths[i] = Math.Min(minWidth, availableWidth);
            }

            float minSum = minMeasureWidths.Sum();
            if (minSum > availableWidth && minSum > 1f)
            {
                float downScale = availableWidth / minSum;
                for (int i = 0; i < minMeasureWidths.Length; i++)
                {
                    minMeasureWidths[i] = Math.Max(1f, minMeasureWidths[i] * downScale);
                }
            }

            var suffixMinWidths = new float[count + 1];
            suffixMinWidths[count] = 0f;
            for (int i = count - 1; i >= 0; i--)
            {
                suffixMinWidths[i] = suffixMinWidths[i + 1] + minMeasureWidths[i];
            }

            boundaries[0] = startX;
            boundaries[count] = endX;

            for (int i = 1; i < count; i++)
            {
                float baseX = baseBoundaries[i];
                int key = GetBarlineOffsetKey(systemIndex, i);
                if (_barlineOffsets.TryGetValue(key, out float offset))
                {
                    baseX += offset;
                }

                float minX = boundaries[i - 1] + minMeasureWidths[i - 1];
                bool allowTailCollapse = i == count - 1 && tailMeasureEmpty;
                float rightReserve = allowTailCollapse ? 0f : suffixMinWidths[i];
                float maxX = endX - rightReserve;
                if (maxX < minX)
                {
                    maxX = minX;
                }

                boundaries[i] = Math.Clamp(baseX, minX, maxX);
            }

            return boundaries;
        }

        private float[] GetSystemBaseBarlinePositions(int systemIndex, int measuresInSystem, float startX, float endX)
        {
            int count = Math.Max(1, measuresInSystem);
            var boundaries = new float[count + 1];
            boundaries[0] = startX;
            boundaries[count] = endX;
            if (count == 1)
            {
                return boundaries;
            }

            float availableWidth = Math.Max(1f, endX - startX);
            var demandFactors = new float[count];
            int systemStartMeasure = GetSystemStartMeasureIndex(systemIndex);
            float sumDemand = 0f;
            for (int i = 0; i < count; i++)
            {
                float demand = GetMeasureVisualDemandFactor(systemStartMeasure + i);
                demandFactors[i] = demand;
                sumDemand += demand;
            }

            if (sumDemand <= 0.0001f)
            {
                sumDemand = count;
                for (int i = 0; i < count; i++)
                {
                    demandFactors[i] = 1f;
                }
            }

            float unit = availableWidth / sumDemand;
            float cursor = startX;
            for (int i = 1; i < count; i++)
            {
                cursor += demandFactors[i - 1] * unit;
                boundaries[i] = cursor;
            }

            boundaries[count] = endX;
            return boundaries;
        }

        private float GetMeasureVisualDemandFactor(int measureIndex)
        {
            int safeMeasure = Math.Max(0, measureIndex);
            if (_measureDemandFactorCache.TryGetValue(safeMeasure, out float cached))
            {
                return cached;
            }

            if (_viewModel?.Project == null)
            {
                return 1f;
            }

            int measureStartTick = GetMeasureBoundaryTick(safeMeasure);
            int measureEndTick = GetMeasureBoundaryTick(safeMeasure + 1);
            if (measureEndTick <= measureStartTick)
            {
                return 1f;
            }

            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            int shortThreshold = Math.Max(1, ppq / 2);
            int tinyThreshold = Math.Max(1, ppq / 8);
            int soundingCount = 0;
            int shortCount = 0;
            int tinyCount = 0;
            int accidentalCount = 0;
            int chordExtra = 0;
            var onsets = new Dictionary<int, int>();

            foreach (var note in _viewModel.Project.Notes)
            {
                if (note == null || note.IsRest)
                {
                    continue;
                }

                int tick = Math.Max(0, note.StartTick);
                if (tick < measureStartTick || tick >= measureEndTick)
                {
                    continue;
                }

                soundingCount++;
                int baseTicks = note.BaseDurationTicks > 0 ? note.BaseDurationTicks : note.DurationTicks;
                baseTicks = Math.Max(1, baseTicks);
                if (baseTicks <= shortThreshold)
                {
                    shortCount++;
                }

                if (baseTicks <= tinyThreshold)
                {
                    tinyCount++;
                }

                if (note.Accidental != NoteAccidental.None)
                {
                    accidentalCount++;
                }

                if (onsets.TryGetValue(tick, out int existing))
                {
                    onsets[tick] = existing + 1;
                }
                else
                {
                    onsets[tick] = 1;
                }
            }

            foreach (int count in onsets.Values)
            {
                chordExtra += Math.Max(0, count - 1);
            }

            int markWeight = 0;
            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                if (mark == null) continue;
                string code = NormalizeExpressionCode(mark.Code);
                int tick = Math.Max(0, mark.StartTick);
                bool inMeasure = tick >= measureStartTick && tick < measureEndTick;
                bool onMeasureEndBoundary = tick == measureEndTick && IsBarlineAnchoredScoreMark(code);
                if (!inMeasure && !onMeasureEndBoundary)
                {
                    continue;
                }

                markWeight += code switch
                {
                    ScoreMarkFinalBarline => 3,
                    ScoreMarkRepeatBarline => 3,
                    ScoreMarkEnding1 or ScoreMarkEnding2 => 2,
                    ScoreMarkSegno => 2,
                    _ => 1
                };
            }

            float factor = 1f
                + soundingCount * 0.028f
                + shortCount * 0.032f
                + tinyCount * 0.07f
                + accidentalCount * 0.024f
                + chordExtra * 0.045f
                + markWeight * 0.14f;
            factor = Math.Clamp(factor, 1f, 3.2f);
            _measureDemandFactorCache[safeMeasure] = factor;
            return factor;
        }

        private bool HasMeasureBarlineScoreMarks(int measureIndex)
        {
            if (_viewModel?.Project == null)
            {
                return false;
            }

            int safeMeasure = Math.Max(0, measureIndex);
            int measureStartTick = GetMeasureBoundaryTick(safeMeasure);
            int measureEndTick = GetMeasureBoundaryTick(safeMeasure + 1);
            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                if (mark == null)
                {
                    continue;
                }

                string code = NormalizeExpressionCode(mark.Code);
                int tick = Math.Max(0, mark.StartTick);
                bool inMeasure = tick >= measureStartTick && tick < measureEndTick;
                bool onMeasureEndBoundary = tick == measureEndTick && IsBarlineAnchoredScoreMark(code);
                if (!inMeasure && !onMeasureEndBoundary)
                {
                    continue;
                }

                if (code is ScoreMarkRepeatBarline or ScoreMarkFinalBarline or ScoreMarkEnding1 or ScoreMarkEnding2 or ScoreMarkSegno)
                {
                    return true;
                }
            }

            return false;
        }

        private float GetEndBoundarySplitMinWidth(float previousBoundaryX, float finalBoundaryX)
        {
            float tailWidth = Math.Max(1f, finalBoundaryX - previousBoundaryX);
            float preferredMin = Math.Max(_measureWidth * 0.35f, _staffGap * 3f);
            float maxAllowedByTail = Math.Max(1f, tailWidth * 0.45f);
            return Math.Max(1f, Math.Min(preferredMin, maxAllowedByTail));
        }

        private float GetDynamicMeasureMinWidth(int systemIndex, int localMeasureIndex, int measuresInSystem)
        {
            int safeMeasures = Math.Max(1, measuresInSystem);
            int clampedLocal = Math.Clamp(localMeasureIndex, 0, Math.Max(0, safeMeasures - 1));
            int globalMeasure = GetSystemStartMeasureIndex(systemIndex) + clampedLocal;
            float demand = GetMeasureVisualDemandFactor(globalMeasure);
            float minWidth = Math.Max(_measureWidth * 0.35f, _staffGap * 3f);
            if (demand > 1.45f)
            {
                minWidth = Math.Max(minWidth, _measureWidth * 0.45f);
            }

            if (HasMeasureBarlineScoreMarks(globalMeasure))
            {
                minWidth = Math.Max(minWidth, Math.Max(_staffGap * 3.8f, _measureWidth * 0.40f));
            }

            return minWidth;
        }

        private void SetSystemBoundaryAbsolutePosition(int systemIndex, int localBoundaryIndex, int measuresInSystem, float targetX)
        {
            if (localBoundaryIndex <= 0 || localBoundaryIndex >= Math.Max(1, measuresInSystem))
            {
                return;
            }

            float baseX = GetBaseBarlineX(systemIndex, localBoundaryIndex, measuresInSystem);
            int key = GetBarlineOffsetKey(systemIndex, localBoundaryIndex);
            float offset = targetX - baseX;
            if (Math.Abs(offset) < 0.5f)
            {
                _barlineOffsets.Remove(key);
            }
            else
            {
                _barlineOffsets[key] = offset;
            }
        }

        private void RemoveStaleBarlineOffsetsForSystem(int systemIndex, int measuresInSystem)
        {
            int safeSystem = Math.Max(0, systemIndex);
            int safeCount = Math.Max(1, measuresInSystem);
            var staleKeys = _barlineOffsets.Keys
                .Where(k => k / 1000 == safeSystem && (k % 1000) >= safeCount)
                .ToList();
            foreach (int key in staleKeys)
            {
                _barlineOffsets.Remove(key);
            }
        }

        private void EnsureStaffCanvasExtent(float targetWidth, float targetHeight)
        {
            if (StaffCanvas == null) return;

            double desiredWidth = Math.Max(1d, Math.Ceiling(targetWidth));
            double desired = Math.Max(MinCanvasHeight, Math.Ceiling(targetHeight));
            if (double.IsNaN(StaffCanvas.Width) || Math.Abs(StaffCanvas.Width - desiredWidth) > 0.5d)
            {
                StaffCanvas.Width = desiredWidth;
            }

            if (double.IsNaN(StaffCanvas.Height) || Math.Abs(StaffCanvas.Height - desired) > 0.5d)
            {
                StaffCanvas.Height = desired;
            }

            if (NoteStepOverlayCanvas != null)
            {
                if (double.IsNaN(NoteStepOverlayCanvas.Width) || Math.Abs(NoteStepOverlayCanvas.Width - desiredWidth) > 0.5d)
                {
                    NoteStepOverlayCanvas.Width = desiredWidth;
                }

                if (double.IsNaN(NoteStepOverlayCanvas.Height) || Math.Abs(NoteStepOverlayCanvas.Height - desired) > 0.5d)
                {
                    NoteStepOverlayCanvas.Height = desired;
                }
            }
        }

        private float GetBeatWidth()
        {
            if (_viewModel == null) return 60f;

            int denominator = _viewModel.TimeSigDenominator;
            float baseWidth = 54f;
            float scale = 4f / Math.Max(1, denominator);
            return Math.Clamp(baseWidth * scale, 24f, 120f);
        }

        private float GetStaffLineThickness()
        {
            float legacyThin = SymbolSizeGap * BravuraStaffLineThicknessSpaces * 0.8f;
            float barlineThin = SymbolSizeGap * BravuraThinBarlineThicknessSpaces;
            return Math.Max(0.8f, (legacyThin + barlineThin) * 0.5f);
        }

        private float GetThinBarlineThickness()
        {
            return Math.Max(GetStaffLineThickness(), SymbolSizeGap * BravuraThinBarlineThicknessSpaces);
        }

        private float GetFinalBarlineThickThickness(float thinThickness)
        {
            // Keep a clear visual contrast, but avoid the previous overly heavy ending barline.
            return Math.Max(thinThickness * 2.2f, SymbolSizeGap * BravuraThickBarlineThicknessSpaces * 0.8f);
        }

        private static float AlignAxisForStroke(float positionDip, float strokeThicknessDip, float dpi)
        {
            float scale = Math.Max(0.01f, dpi / 96f);
            float positionPx = positionDip * scale;
            float strokePx = Math.Max(0.1f, strokeThicknessDip * scale);
            bool oddStroke = ((int)Math.Round(strokePx)) % 2 != 0;
            float alignedPx = oddStroke
                ? (float)(Math.Round(positionPx - 0.5f) + 0.5f)
                : (float)Math.Round(positionPx);
            return alignedPx / scale;
        }

        private static float AlignDistanceToPixelGrid(float distanceDip, float dpi)
        {
            float scale = Math.Max(0.01f, dpi / 96f);
            float distancePx = Math.Max(0.1f, distanceDip * scale);
            return Math.Max(0.01f, (float)Math.Round(distancePx) / scale);
        }

        private void DrawStaff(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, int systemIndex, float trebleTop, float bassTop)
        {
            if (_viewModel == null) return;
            Color ink = GetNotationInkColor();
            int measuresInSystem = GetMeasuresInSystem(systemIndex);
            float rightX = GetSystemRightX(systemIndex);
            float staffLineThickness = GetStaffLineThickness();
            for (int i = 0; i < 5; i++)
            {
                float y = AlignAxisForStroke(trebleTop + i * _staffGap, staffLineThickness, ds.Dpi);
                ds.DrawLine(_staffLeft, y, rightX, y, ink, staffLineThickness);
            }

            for (int i = 0; i < 5; i++)
            {
                float y = AlignAxisForStroke(bassTop + i * _staffGap, staffLineThickness, ds.Dpi);
                ds.DrawLine(_staffLeft, y, rightX, y, ink, staffLineThickness);
            }

            StaffClefType topClef = GetSystemStaffClefType(systemIndex, topStaff: true);
            StaffClefType bottomClef = GetSystemStaffClefType(systemIndex, topStaff: false);
            float topClefLineY = GetStaffClefAnchorLineY(trebleTop, topClef);
            float bottomClefLineY = GetStaffClefAnchorLineY(bassTop, bottomClef);

            float clefX = _staffLeft + SymbolSizeGap * 0.6f;
            if (_musicFontAvailable && _musicFontFace != null)
            {
                DrawClefGlyph(ds, GetClefGlyphCode(topClef), clefX, topClefLineY, GetStaffClefYOffset(topClef), _clefFormat.FontSize);
                DrawClefGlyph(ds, GetClefGlyphCode(bottomClef), clefX, bottomClefLineY, GetStaffClefYOffset(bottomClef), _clefFormat.FontSize);
            }
            RegisterClefHitTarget(systemIndex, topStaff: true, clefX, topClefLineY, _clefFormat.FontSize);
            RegisterClefHitTarget(systemIndex, topStaff: false, clefX, bottomClefLineY, _clefFormat.FontSize);

            int systemMeasureStart = GetSystemStartMeasureIndex(systemIndex);
            int systemStartTick = GetMeasureBoundaryTick(systemMeasureStart);
            int systemEndTick = GetMeasureBoundaryTick(systemMeasureStart + measuresInSystem);
            TimeSignature startTimeSig = GetEffectiveTimeSignatureAtTick(systemStartTick);
            int startKeyFifths = GetEffectiveKeySignatureFifthsAtTick(systemStartTick);
            DrawKeySignature(ds, systemIndex, trebleTop, bassTop, startKeyFifths);
            bool drawTimeSigAtSystemStart = ShouldDrawTimeSignatureAtSystemStart(systemIndex);
            if (drawTimeSigAtSystemStart)
            {
                DrawTimeSignatureAtSystemStart(ds, trebleTop, bassTop, startTimeSig.Numerator, startTimeSig.Denominator, startKeyFifths);
            }
            DrawInlineSignatureChanges(ds, systemIndex, trebleTop, bassTop, systemStartTick, systemEndTick);
            // Keep brace slightly lower for optical balance between staves.
            float braceShift = SymbolSizeGap * 5.5f + 6f;
            DrawGrandStaffBrace(ds, trebleTop - SymbolSizeGap * 0.35f + braceShift, bassTop + 4.35f * SymbolSizeGap + braceShift);
        }

        private bool TryDrawStaffLinesGlyph(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float leftX, float topY, float width)
        {
            if (!_musicFontAvailable || _musicFontFace == null) return false;
            if (!_musicFontFace.HasCharacter((uint)SmuflStaff5LinesWide)) return false;

            float size = Math.Max(SymbolSizeGap * 4.2f, 24f);
            float advance = GetGlyphAdvanceRaw(SmuflStaff5LinesWide, size);
            if (advance < 1f) return false;

            float baselineY = topY + _staffGap * 4f;
            float scaleX = Math.Max(0.01f, width / advance);
            var old = ds.Transform;
            var origin = new System.Numerics.Vector2(leftX, baselineY);
            ds.Transform = System.Numerics.Matrix3x2.CreateScale(scaleX, 1f, origin) * old;
            DrawGlyph(ds, SmuflStaff5LinesWide, leftX, baselineY, size);
            ds.Transform = old;
            return true;
        }

        private void DrawSystemMeasureNumber(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, int systemIndex, float trebleTop)
        {
            if (_viewModel == null) return;
            if (systemIndex <= 0) return;

            int firstMeasureNumber = GetSystemStartMeasureIndex(systemIndex) + 1;
            string text = firstMeasureNumber.ToString();
            float x = _staffLeft - _staffGap * 0.2f;
            float y = trebleTop - _staffGap * 2.74f;
            _measureNumberFormat.FontSize = Math.Max(14f, SymbolSizeGap * 1.34f);
            _measureNumberFormat.FontFamily = _expressionTextFormat.FontFamily;
            bool isActiveDragSystem = _isDraggingBarline && _dragBarlineSystemIndex == systemIndex;
            _measureNumberFormat.FontWeight = isActiveDragSystem ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            ds.DrawText(text, x, y, isActiveDragSystem ? GetAccentColor() : GetNotationInkColor(), _measureNumberFormat);
        }

        private void DrawScoreHeader(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float firstSystemTop)
        {
            if (_viewModel == null) return;
            Color ink = GetNotationInkColor();

            _titleFormat.FontSize = Math.Max(29f, SymbolSizeGap * 3.0f);
            _tempoFormat.FontSize = Math.Max(15f, SymbolSizeGap * 1.32f);
            _titleFormat.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            _tempoFormat.FontWeight = Microsoft.UI.Text.FontWeights.Normal;

            string title = string.IsNullOrWhiteSpace(_viewModel.Title) ? "Untitled" : _viewModel.Title.Trim();
            bool hasChinese = title.Any(c => c >= '\u4E00' && c <= '\u9FFF');
            string headerFont = hasChinese ? "Microsoft YaHei UI" : "Times New Roman";
            _titleFormat.FontFamily = headerFont;
            _tempoFormat.FontFamily = "Times New Roman";
            float titleRectX = _staffLeft;
            float titleRectY = firstSystemTop - _staffGap * 17.9f;
            float titleRectWidth = _staffWidth;
            float titleRectHeight = _staffGap * 3.2f;
            _titleHitRect = new Rect(titleRectX, titleRectY - _staffGap * 0.35f, titleRectWidth, titleRectHeight + _staffGap * 0.7f);
            ds.DrawText(title, titleRectX, titleRectY, titleRectWidth, titleRectHeight, ink, _titleFormat);

            int bpm = (int)Math.Round(Math.Max(0d, _viewModel.Bpm));
            if (bpm > 0)
            {
                float tempoX = _staffLeft + _staffGap * 0.4f;
                float tempoY = firstSystemTop - _staffGap * 4.65f;
                DrawTempoHeader(ds, tempoX, tempoY, bpm, _viewModel.TimeSigDenominator, ink);
            }
        }

        private void DrawTempoHeader(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float x, float y, int bpm, int denominator, Color ink)
        {
            string prefix = $"{GetTempoItalianName(bpm)} (";
            ds.DrawText(prefix, x, y, ink, _tempoFormat);

            float prefixWidth = GetTextWidth(prefix, _tempoFormat);
            float cursor = x + prefixWidth;
            int beatGlyphCode = GetTempoBeatGlyphCode(denominator);
            if (_musicFontAvailable && _musicFontFace != null && beatGlyphCode != 0 && _musicFontFace.HasCharacter((uint)beatGlyphCode))
            {
                float glyphSize = Math.Max(_tempoFormat.FontSize * 1.08f, SymbolSizeGap * 1.42f);
                float beatBaseline = y + _tempoFormat.FontSize * 0.88f;
                float advance = DrawGlyph(ds, beatGlyphCode, cursor, beatBaseline, glyphSize, ink);
                cursor += Math.Max(advance, glyphSize * 0.42f);
            }
            else
            {
                string beatSymbol = GetTempoBeatFallback(denominator);
                var fallbackFormat = new CanvasTextFormat
                {
                    FontFamily = "Segoe UI Symbol",
                    FontSize = _tempoFormat.FontSize,
                    FontStyle = FontStyle.Normal,
                    FontWeight = _tempoFormat.FontWeight
                };
                ds.DrawText(beatSymbol, cursor, y, ink, fallbackFormat);
                cursor += GetTextWidth(beatSymbol, fallbackFormat);
            }

            string suffix = $" = {bpm})";
            ds.DrawText(suffix, cursor + SymbolSizeGap * 0.16f, y, ink, _tempoFormat);
        }

        private float GetTextWidth(string text, CanvasTextFormat format)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            using var layout = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), text, format, 2000f, 200f);
            return (float)Math.Max(0, layout.DrawBounds.Width);
        }

        private static string GetTempoItalianName(int bpm)
        {
            (int Bpm, string Name)[] map =
            {
                (24, "Larghissimo"),
                (35, "Grave"),
                (50, "Largo"),
                (56, "Adagio"),
                (60, "Lento"),
                (63, "Larghetto"),
                (66, "Adagietto"),
                (76, "Andante"),
                (84, "Andantino"),
                (88, "Maestoso"),
                (96, "Moderato"),
                (112, "Allegretto"),
                (120, "Allegro"),
                (132, "Allegro vivace"),
                (144, "Vivace"),
                (168, "Presto"),
                (200, "Prestissimo")
            };

            string best = map[0].Name;
            int minDiff = Math.Abs(bpm - map[0].Bpm);
            foreach (var item in map)
            {
                int diff = Math.Abs(bpm - item.Bpm);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    best = item.Name;
                }
            }

            return best;
        }

        private static int GetTempoBeatGlyphCode(int denominator)
        {
            return denominator switch
            {
                8 => SmuflNote8thUp,
                4 => SmuflNoteQuarterUp,
                2 => SmuflNoteHalfUp,
                1 => SmuflNoteWhole,
                16 => SmuflNote16thUp,
                32 => SmuflNote32thUp,
                _ => SmuflNoteQuarterUp
            };
        }

        private static string GetTempoBeatFallback(int denominator)
        {
            return denominator switch
            {
                8 => "Eighth",
                2 => "Half",
                16 => "16th",
                _ => "Quarter"
            };
        }

        private void DrawGrandStaffBrace(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float topY, float bottomY)
        {
            float x = _staffLeft - SymbolSizeGap * 1.71f;
            float height = Math.Max(SymbolSizeGap * 8f, bottomY - topY);
            float centerY = (topY + bottomY) * 0.5f;

            if (_musicFontAvailable && _musicFontFace != null && _musicFontFace.HasCharacter((uint)SmuflBrace))
            {
                float size = Math.Max(SymbolSizeGap * 7.6f, height * 0.94f);
                DrawGlyph(ds, SmuflBrace, x - SymbolSizeGap * 0.18f, centerY + SymbolSizeGap * 1.15f, size);
                return;
            }

            if (_hideCustomNotationFallback)
            {
                return;
            }

            float w = SymbolSizeGap * 0.55f;
            float t = Math.Max(1.2f, SymbolSizeGap * 0.11f);
            var p0 = new System.Numerics.Vector2(x + w, topY);
            var p1 = new System.Numerics.Vector2(x - w * 0.1f, topY + height * 0.12f);
            var p2 = new System.Numerics.Vector2(x - w * 0.15f, centerY - height * 0.16f);
            var p3 = new System.Numerics.Vector2(x + w * 0.06f, centerY - height * 0.02f);
            DrawCurve(ds, p0, p1, p2, p3, t);

            p0 = p3;
            p1 = new System.Numerics.Vector2(x - w * 0.12f, centerY + height * 0.06f);
            p2 = new System.Numerics.Vector2(x - w * 0.1f, bottomY - height * 0.15f);
            p3 = new System.Numerics.Vector2(x + w, bottomY);
            DrawCurve(ds, p0, p1, p2, p3, t);
        }

        private static void DrawCurve(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            System.Numerics.Vector2 p0,
            System.Numerics.Vector2 p1,
            System.Numerics.Vector2 p2,
            System.Numerics.Vector2 p3,
            float thickness)
        {
            const int segments = 20;
            var prev = p0;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                var next = EvaluateCubicBezier(p0, p1, p2, p3, t);
                ds.DrawLine(prev.X, prev.Y, next.X, next.Y, Colors.Black, thickness);
                prev = next;
            }
        }

        private static void DrawDashedHorizontalLine(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float startX,
            float endX,
            float y,
            float dashLength,
            float gapLength,
            float thickness,
            Windows.UI.Color? color = null)
        {
            if (endX <= startX || dashLength <= 0f) return;
            float x = startX;
            float gap = Math.Max(0f, gapLength);
            Windows.UI.Color drawColor = color ?? Colors.Black;
            while (x < endX)
            {
                float segmentEnd = Math.Min(endX, x + dashLength);
                ds.DrawLine(x, y, segmentEnd, y, drawColor, thickness);
                x = segmentEnd + gap;
            }
        }

        private void DrawKeySignature(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, int systemIndex, float trebleTop, float bassTop, int fifths)
        {
            if (fifths == 0 || !_musicFontAvailable) return;

            bool sharps = fifths > 0;
            int count = Math.Min(Math.Abs(fifths), 7);

            int symbolCode = sharps ? SmuflAccidentalSharp : SmuflAccidentalFlat;
            StaffClefType topClef = GetSystemStaffClefType(systemIndex, topStaff: true);
            StaffClefType bottomClef = GetSystemStaffClefType(systemIndex, topStaff: false);
            float[] topSteps = GetKeySignatureSteps(topClef, sharps);
            float[] bottomSteps = GetKeySignatureSteps(bottomClef, sharps);

            float startX = _staffLeft + _clefSpace + SymbolSizeGap * KeyGapAfterClef;
            DrawKeySignatureAtX(ds, topSteps, bottomSteps, sharps, count, startX, trebleTop, bassTop, topClef, bottomClef);
        }

        private void DrawKeySignatureAtX(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float[] topSteps,
            float[] bottomSteps,
            bool sharps,
            int count,
            float startX,
            float trebleTop,
            float bassTop,
            StaffClefType topClef,
            StaffClefType bottomClef)
        {
            if (!_musicFontAvailable) return;
            int safeCount = Math.Clamp(count, 0, Math.Min(Math.Min(topSteps.Length, bottomSteps.Length), 7));
            int symbolCode = sharps ? SmuflAccidentalSharp : SmuflAccidentalFlat;
            float advance = SymbolSizeGap * KeySignatureAdvance;
            float yAdjustTop = GetKeySignatureYOffset(topClef, sharps);
            float yAdjustBottom = GetKeySignatureYOffset(bottomClef, sharps);

            for (int i = 0; i < safeCount; i++)
            {
                float x = startX + i * advance;
                float topLineY = trebleTop + topSteps[i] * (_staffGap / 2f);
                float bottomLineY = bassTop + bottomSteps[i] * (_staffGap / 2f);

                DrawGlyph(ds, symbolCode, x, topLineY + yAdjustTop * _staffGap, _keyFormat.FontSize);
                DrawGlyph(ds, symbolCode, x, bottomLineY + yAdjustBottom * _staffGap, _keyFormat.FontSize);
            }
        }

        private void DrawInlineSignatureChanges(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            int systemIndex,
            float trebleTop,
            float bassTop,
            int systemStartTick,
            int systemEndTick)
        {
            if (_viewModel == null) return;
            var ticks = new SortedSet<int>();
            foreach (var ts in _viewModel.Project.TimeSignatureChanges)
            {
                if (ts == null) continue;
                int tick = Math.Max(0, ts.Tick);
                if (tick > systemStartTick && tick < systemEndTick)
                {
                    ticks.Add(tick);
                }
            }

            foreach (var ks in _viewModel.Project.KeySignatureChanges)
            {
                if (ks == null) continue;
                int tick = Math.Max(0, ks.Tick);
                if (tick > systemStartTick && tick < systemEndTick)
                {
                    ticks.Add(tick);
                }
            }

            foreach (int tick in ticks)
            {
                if (!TryGetSystemBoundaryXForTick(systemIndex, tick, out float boundaryX, out int localBoundary))
                {
                    continue;
                }

                if (localBoundary <= 0)
                {
                    continue;
                }

                float x = boundaryX + GetInlineSignatureLeadOffsetAtTick(tick);
                bool hasKey = TryGetInlineKeySignatureAtTick(tick, out int newFifths);
                bool hasTime = TryGetInlineTimeSignatureAtTick(tick, out int numerator, out int denominator);
                if (!hasKey && !hasTime)
                {
                    continue;
                }

                if (hasKey)
                {
                    int previousFifths = GetEffectiveKeySignatureFifthsAtTick(Math.Max(0, tick - 1));
                    float keyWidth = DrawInlineKeySignatureChange(ds, systemIndex, trebleTop, bassTop, previousFifths, newFifths, x);
                    x += keyWidth;
                }

                if (hasTime)
                {
                    if (hasKey)
                    {
                        x += SymbolSizeGap * 0.42f;
                    }

                    DrawTimeSignatureAtX(ds, trebleTop, bassTop, numerator, denominator, x);
                }
            }
        }

        private bool TryGetSystemBoundaryXForTick(int systemIndex, int tick, out float boundaryX, out int localBoundary)
        {
            boundaryX = GetSystemMusicStartX(systemIndex);
            localBoundary = 0;
            int safeTick = Math.Max(0, tick);
            int boundaryIndex = GetNearestMeasureBoundaryIndexForTick(safeTick);
            if (GetMeasureBoundaryTick(boundaryIndex) != safeTick)
            {
                return false;
            }

            int measuresInSystem = GetMeasuresInSystem(systemIndex);
            localBoundary = boundaryIndex - GetSystemStartMeasureIndex(systemIndex);
            if (localBoundary < 0 || localBoundary > measuresInSystem)
            {
                return false;
            }

            float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
            boundaryX = boundaries[localBoundary];
            return true;
        }

        private bool TryGetInlineTimeSignatureAtTick(int tick, out int numerator, out int denominator)
        {
            numerator = 4;
            denominator = 4;
            if (_viewModel == null || tick <= 0)
            {
                return false;
            }

            TimeSignatureChange? found = null;
            foreach (var ts in _viewModel.Project.TimeSignatureChanges)
            {
                if (ts == null) continue;
                if (Math.Max(0, ts.Tick) == tick)
                {
                    found = ts;
                }
            }

            if (found == null)
            {
                return false;
            }

            numerator = Math.Clamp(found.Numerator, 1, 12);
            denominator = found.Denominator is 1 or 2 or 4 or 8 or 16 ? found.Denominator : 4;
            return true;
        }

        private bool TryGetInlineKeySignatureAtTick(int tick, out int fifths)
        {
            fifths = 0;
            if (_viewModel == null || tick <= 0)
            {
                return false;
            }

            KeySignatureChange? found = null;
            foreach (var ks in _viewModel.Project.KeySignatureChanges)
            {
                if (ks == null) continue;
                if (Math.Max(0, ks.Tick) == tick)
                {
                    found = ks;
                }
            }

            if (found == null)
            {
                return false;
            }

            fifths = Math.Clamp(found.Fifths, -7, 7);
            return true;
        }

        private float DrawInlineKeySignatureChange(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            int systemIndex,
            float trebleTop,
            float bassTop,
            int previousFifths,
            int newFifths,
            float startX)
        {
            if (!_musicFontAvailable) return 0f;
            if (previousFifths == newFifths) return 0f;

            StaffClefType topClef = GetSystemStaffClefType(systemIndex, topStaff: true);
            StaffClefType bottomClef = GetSystemStaffClefType(systemIndex, topStaff: false);
            float advance = SymbolSizeGap * KeySignatureAdvance;
            float x = startX;

            int previousCount = Math.Min(Math.Abs(previousFifths), 7);
            if (previousCount > 0)
            {
                bool previousSharps = previousFifths > 0;
                float[] prevTopSteps = GetKeySignatureSteps(topClef, previousSharps);
                float[] prevBottomSteps = GetKeySignatureSteps(bottomClef, previousSharps);
                float yAdjustTop = GetKeySignatureYOffset(topClef, previousSharps);
                float yAdjustBottom = GetKeySignatureYOffset(bottomClef, previousSharps);
                DrawKeyAccidentalRunAtX(ds, prevTopSteps, prevBottomSteps, previousCount, SmuflAccidentalNatural, x, trebleTop, bassTop, yAdjustTop, yAdjustBottom);
                x += previousCount * advance + SymbolSizeGap * 0.28f;
            }

            int newCount = Math.Min(Math.Abs(newFifths), 7);
            if (newCount > 0)
            {
                bool sharps = newFifths > 0;
                float[] topSteps = GetKeySignatureSteps(topClef, sharps);
                float[] bottomSteps = GetKeySignatureSteps(bottomClef, sharps);
                DrawKeySignatureAtX(ds, topSteps, bottomSteps, sharps, newCount, x, trebleTop, bassTop, topClef, bottomClef);
                x += newCount * advance;
            }

            return Math.Max(0f, x - startX);
        }

        private void DrawKeyAccidentalRunAtX(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float[] topSteps,
            float[] bottomSteps,
            int count,
            int symbolCode,
            float startX,
            float trebleTop,
            float bassTop,
            float yAdjustTop,
            float yAdjustBottom)
        {
            int safeCount = Math.Clamp(count, 0, Math.Min(Math.Min(topSteps.Length, bottomSteps.Length), 7));
            float advance = SymbolSizeGap * KeySignatureAdvance;
            for (int i = 0; i < safeCount; i++)
            {
                float x = startX + i * advance;
                float topLineY = trebleTop + topSteps[i] * (_staffGap / 2f);
                float bottomLineY = bassTop + bottomSteps[i] * (_staffGap / 2f);
                DrawGlyph(ds, symbolCode, x, topLineY + yAdjustTop * _staffGap, _keyFormat.FontSize);
                DrawGlyph(ds, symbolCode, x, bottomLineY + yAdjustBottom * _staffGap, _keyFormat.FontSize);
            }
        }

        private float EstimateInlineTimeSignatureWidth(int numerator, int denominator)
        {
            int digits = Math.Max(numerator.ToString().Length, denominator.ToString().Length);
            float raw = Math.Max(SymbolSizeGap * (1.1f * digits + 0.4f), _timeSigFormat.FontSize * 0.9f);
            return raw * 1.12f;
        }

        private float EstimateInlineKeySignatureWidth(int previousFifths, int newFifths)
        {
            if (previousFifths == newFifths)
            {
                return 0f;
            }

            float advance = SymbolSizeGap * KeySignatureAdvance;
            float width = 0f;
            int previousCount = Math.Min(Math.Abs(previousFifths), 7);
            int newCount = Math.Min(Math.Abs(newFifths), 7);
            if (previousCount > 0)
            {
                width += previousCount * advance + SymbolSizeGap * 0.28f;
            }

            if (newCount > 0)
            {
                width += newCount * advance;
            }

            return width > 0f ? width * 1.08f + SymbolSizeGap * 0.24f : 0f;
        }

        private float GetInlineSignatureReserveWidthAtTick(int systemIndex, int boundaryTick)
        {
            if (_viewModel == null || boundaryTick <= 0)
            {
                return 0f;
            }

            if (!TryGetSystemBoundaryXForTick(systemIndex, boundaryTick, out _, out int localBoundary))
            {
                return 0f;
            }

            if (localBoundary <= 0)
            {
                return 0f;
            }

            bool hasKey = TryGetInlineKeySignatureAtTick(boundaryTick, out int newFifths);
            bool hasTime = TryGetInlineTimeSignatureAtTick(boundaryTick, out int numerator, out int denominator);
            if (!hasKey && !hasTime)
            {
                return 0f;
            }

            float width = 0f;
            if (hasKey)
            {
                int previousFifths = GetEffectiveKeySignatureFifthsAtTick(Math.Max(0, boundaryTick - 1));
                width += EstimateInlineKeySignatureWidth(previousFifths, newFifths);
            }

            if (hasTime)
            {
                if (width > 0f)
                {
                    width += SymbolSizeGap * 0.42f;
                }

                width += EstimateInlineTimeSignatureWidth(numerator, denominator);
            }

            float lead = GetInlineSignatureLeadOffsetAtTick(boundaryTick);
            float trailing = SymbolSizeGap * 0.72f;
            return Math.Max(0f, lead + width + trailing);
        }

        private float GetInlineSignatureLeadOffsetAtTick(int boundaryTick)
        {
            float lead = SymbolSizeGap * InlineSignatureStartOffsetFactor;
            if (HasRepeatBarlineMarkAtTick(boundaryTick))
            {
                // When a repeat barline sits on the same boundary, push inline signatures right.
                lead += Math.Max(SymbolSizeGap * 0.64f, _staffGap * 0.52f);
            }

            return lead;
        }

        private static float[] GetKeySignatureSteps(StaffClefType clef, bool sharps)
        {
            return clef switch
            {
                StaffClefType.Bass when sharps => new[] { 2f, 5f, 1f, 4f, 7f, 3f, 6f },
                StaffClefType.Bass => new[] { 6f, 3f, 7f, 4f, 8f, 5f, 9f },
                StaffClefType.Treble when sharps => new[] { 0f, 3f, -1f, 2f, 5f, 1f, 4f },
                _ => new[] { 4f, 1f, 5f, 2f, 6f, 3f, 7f }
            };
        }

        private static float GetKeySignatureYOffset(StaffClefType clef, bool sharps)
        {
            float baseAdjust = KeySignatureYOffset + (sharps ? KeySignatureSharpYOffsetAdjust : KeySignatureFlatYOffsetAdjust);
            if (!sharps && clef == StaffClefType.Bass)
            {
                baseAdjust += KeySignatureBassFlatYOffsetAdjust;
            }

            return baseAdjust;
        }

        private bool HasSignatureChangeAtTick(int tick)
        {
            if (_viewModel == null || tick <= 0)
            {
                return false;
            }

            bool hasTime = _viewModel.Project.TimeSignatureChanges.Any(c => c != null && Math.Max(0, c.Tick) == tick);
            if (hasTime) return true;
            return _viewModel.Project.KeySignatureChanges.Any(c => c != null && Math.Max(0, c.Tick) == tick);
        }

        private bool HasRepeatBarlineMarkAtTick(int tick)
        {
            if (_viewModel?.Project?.ExpressionMarks == null || tick < 0)
            {
                return false;
            }

            int safeTick = Math.Max(0, tick);
            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                if (mark == null || Math.Max(0, mark.StartTick) != safeTick)
                {
                    continue;
                }

                if (NormalizeExpressionCode(mark.Code) == ScoreMarkRepeatBarline)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsInlineSignatureBoundary(int systemIndex, int localBoundary)
        {
            if (localBoundary <= 0) return false;
            int measuresInSystem = GetMeasuresInSystem(systemIndex);
            if (localBoundary >= measuresInSystem) return false;
            int systemStartMeasure = GetSystemStartMeasureIndex(systemIndex);
            int boundaryTick = GetMeasureBoundaryTick(systemStartMeasure + localBoundary);
            int systemStartTick = GetMeasureBoundaryTick(systemStartMeasure);
            if (boundaryTick <= systemStartTick)
            {
                return false;
            }

            return HasSignatureChangeAtTick(boundaryTick);
        }

        private void DrawGrid(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            int systemIndex,
            float trebleTop,
            float staffBottom,
            int beatsPerBar,
            int measuresPerSystem,
            bool isLastSystem,
            bool suppressBeatGrid)
        {
            if (_viewModel == null) return;
            Color ink = GetNotationInkColor();

            int measures = Math.Max(1, measuresPerSystem);
            float thinBarlineThickness = GetThinBarlineThickness();
            float finalThickBarlineThickness = GetFinalBarlineThickThickness(thinBarlineThickness);
            float[] boundaries = GetSystemBarlinePositions(systemIndex, measures);
            float barlineTop = trebleTop - GetStaffLineThickness() * 0.5f;
            float barlineBottom = staffBottom + GetStaffLineThickness() * 0.5f;
            bool isDraggingCurrentSystem = _isDraggingBarline && _dragBarlineSystemIndex == systemIndex;

            bool isStartHighlighted = _highlightBarlineSystemIndex == systemIndex && _highlightBarlineLocalIndex == 0;
            ds.DrawLine(_staffLeft, barlineTop, _staffLeft, barlineBottom, isStartHighlighted ? GetAccentColor() : ink, thinBarlineThickness);

            if (_viewModel.ShowBeatGrid && !suppressBeatGrid)
            {
                Color subtleGrid = ActualTheme == ElementTheme.Dark
                    ? Color.FromArgb(42, 255, 255, 255)
                    : Color.FromArgb(30, 0, 0, 0);
                for (int measure = 0; measure < measures; measure++)
                {
                    float measureStartX = boundaries[measure];
                    float measureWidth = boundaries[measure + 1] - boundaries[measure];
                    for (int beat = 0; beat < beatsPerBar; beat++)
                    {
                        float x = measureStartX + (beat + 0.5f) / Math.Max(1, beatsPerBar) * measureWidth;
                        ds.DrawLine(x, trebleTop, x, staffBottom, subtleGrid, 0.9f);
                    }
                }
            }

            for (int measure = 1; measure < measures; measure++)
            {
                float barX = boundaries[measure];
                bool isActiveDragBarline = isDraggingCurrentSystem && _dragBarlineLocalIndex == measure;
                bool isPanelHighlight = _highlightBarlineSystemIndex == systemIndex && _highlightBarlineLocalIndex == measure;
                Windows.UI.Color barlineColor = (isActiveDragBarline || isPanelHighlight) ? GetAccentColor() : ink;
                float barlineThickness = isActiveDragBarline ? thinBarlineThickness * 1.18f : thinBarlineThickness;
                if (IsInlineSignatureBoundary(systemIndex, measure))
                {
                    float sep = Math.Max(SymbolSizeGap * 0.28f, barlineThickness * 1.5f);
                    ds.DrawLine(barX - sep * 0.5f, barlineTop, barX - sep * 0.5f, barlineBottom, barlineColor, barlineThickness);
                    ds.DrawLine(barX + sep * 0.5f, barlineTop, barX + sep * 0.5f, barlineBottom, barlineColor, barlineThickness);
                }
                else
                {
                    ds.DrawLine(barX, barlineTop, barX, barlineBottom, barlineColor, barlineThickness);
                }
            }

            if (isDraggingCurrentSystem && _dragBarlineLocalIndex == measures)
            {
                float minMeasureWidth = GetDynamicMeasureMinWidth(systemIndex, Math.Max(0, measures - 1), measures);
                float previewLeft = boundaries[Math.Max(0, measures - 1)] + minMeasureWidth;
                float previewRight = boundaries[measures] - minMeasureWidth;
                if (previewRight < previewLeft)
                {
                    previewRight = previewLeft;
                }

                float previewX = Math.Clamp(_measurePanelBarlineX, previewLeft, previewRight);
                if (previewX < boundaries[measures] - Math.Max(SymbolSizeGap * 0.2f, 1.2f))
                {
                    ds.DrawLine(previewX, barlineTop, previewX, barlineBottom, GetAccentColor(), thinBarlineThickness * 1.15f);
                }
            }

            float endX = boundaries[measures];
            bool isActiveDragEndBarline = isDraggingCurrentSystem && _dragBarlineLocalIndex == measures;
            bool isPanelEndHighlight = _highlightBarlineSystemIndex == systemIndex && _highlightBarlineLocalIndex == measures;
            Color endBarlineColor = (isActiveDragEndBarline || isPanelEndHighlight) ? GetAccentColor() : ink;
            if (isLastSystem)
            {
                float minSeparation = thinBarlineThickness * 0.5f + finalThickBarlineThickness * 0.5f + SymbolSizeGap * 0.22f;
                float thinX = endX - Math.Max(SymbolSizeGap * 0.72f, minSeparation);
                ds.DrawLine(thinX, barlineTop, thinX, barlineBottom, endBarlineColor, thinBarlineThickness);
                ds.DrawLine(endX, barlineTop, endX, barlineBottom, endBarlineColor, finalThickBarlineThickness);
            }
            else
            {
                ds.DrawLine(endX, barlineTop, endX, barlineBottom, endBarlineColor, thinBarlineThickness);
            }
        }

        private bool TryDrawBarlineGlyph(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float x, float topY, float bottomY, bool isFinal, Color? color = null)
        {
            if (!_musicFontAvailable || _musicFontFace == null) return false;
            int code = isFinal ? SmuflBarlineFinal : SmuflBarlineSingle;
            if (!_musicFontFace.HasCharacter((uint)code)) return false;

            float size = Math.Max(SymbolSizeGap * 4.5f, 24f);
            float advance = GetGlyphAdvanceRaw(code, size);
            if (advance < 0.6f) return false;

            float height = Math.Max(1f, bottomY - topY);
            float refHeight = _staffGap * 4f;
            float scaleY = Math.Clamp(height / Math.Max(1f, refHeight), 0.2f, 6f);
            float baselineY = bottomY;
            float startX = x - advance * 0.5f;
            var old = ds.Transform;
            var origin = new System.Numerics.Vector2(startX, baselineY);
            ds.Transform = System.Numerics.Matrix3x2.CreateScale(1f, scaleY, origin) * old;
            DrawGlyph(ds, code, startX, baselineY, size, color ?? GetNotationInkColor());
            ds.Transform = old;
            return true;
        }

        private void DrawNotes(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            int systemIndex,
            int measuresInSystem,
            float trebleTop,
            float bassTop,
            bool suppressSelectionVisuals)
        {
            if (_viewModel == null) return;

            int ticksPerBeat = _ticksPerBeat > 0 ? _ticksPerBeat : GetTicksPerBeat();
            if (ticksPerBeat <= 0) return;

            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            int halfTicks = 2 * ppq;
            int wholeTicks = 4 * ppq;
            int eighthTicks = ppq / 2;
            int sixteenthTicks = ppq / 4;
            int thirtySecondTicks = Math.Max(1, ppq / 8);
            int tolerance = Math.Max(1, ppq / 64);
            float stemThickness = Math.Max(1.2f, SymbolSizeGap * 0.12f * 1.3f);
            float stemLength = SymbolSizeGap * 3.6f;
            float flagLength = SymbolSizeGap * 1.4f;
            float flagDrop = SymbolSizeGap * 0.9f;
            float flagSpacing = SymbolSizeGap * 0.7f;
            float trebleBottom = trebleTop + 4f * _staffGap;
            float bassBottom = bassTop + 4f * _staffGap;
            float maxX = GetSystemRightX(systemIndex);

            var noteInfos = new List<NoteDrawInfo>();
            foreach (var note in _viewModel.Project.Notes)
            {
                int noteSystem = GetSystemIndexForTick(note.StartTick);
                if (noteSystem != systemIndex) continue;

                float x = GetNoteX(note.StartTick);
                if (x > maxX) continue;

                float y = GetNoteVisualY(note, systemIndex);
                int ottavaShiftOctaves = 0;
                if (!note.IsRest)
                {
                    y = GetRenderedNoteY(note, systemIndex, out ottavaShiftOctaves);
                }

                int visualDurationTicks = note.BaseDurationTicks > 0 ? note.BaseDurationTicks : note.DurationTicks;
                int dotCount = Math.Clamp(note.AugmentationDots, 0, 2);
                if (dotCount > 0 && note.BaseDurationTicks <= 0)
                {
                    visualDurationTicks = InferBaseDurationFromDot(note.DurationTicks, dotCount);
                }
                else if (dotCount == 0 && TryGetSingleDottedBaseDuration(note.DurationTicks, ppq, tolerance, out int dottedBaseTicks))
                {
                    visualDurationTicks = dottedBaseTicks;
                    dotCount = 1;
                }

                bool isWhole = visualDurationTicks >= wholeTicks - tolerance;
                bool isHalf = visualDurationTicks >= halfTicks - tolerance && visualDurationTicks < wholeTicks - tolerance;
                bool fillHead = !isWhole && !isHalf;

                float headWidth = GetNoteHeadWidth();
                if (_musicFontAvailable && _musicFontFace != null)
                {
                    int code = isWhole
                        ? SmuflNoteheadWhole
                        : (isHalf ? SmuflNoteheadHalf : SmuflNoteheadBlack);
                    float size = GetNoteheadGlyphSize();
                    float advance = GetGlyphAdvance(code, size);
                    headWidth = Math.Max(headWidth, advance / 2f);
                }

                int beams = 0;
                if (!note.IsRest)
                {
                    if (visualDurationTicks <= thirtySecondTicks + tolerance)
                    {
                        beams = 3;
                    }
                    else if (visualDurationTicks <= sixteenthTicks + tolerance)
                    {
                        beams = 2;
                    }
                    else if (visualDurationTicks <= eighthTicks + tolerance)
                    {
                        beams = 1;
                    }
                }

                noteInfos.Add(new NoteDrawInfo
                {
                    Note = note,
                    X = x + (isWhole && !note.IsRest ? SymbolSizeGap * 0.375f : 0f),
                    Y = y,
                    PreferTrebleStaff = note.PreferTrebleStaff ?? ShouldPreferTrebleByPosition(systemIndex, note.Midi, note.Accidental),
                    OttavaShiftOctaves = ottavaShiftOctaves,
                    HeadWidth = headWidth,
                    VisualDurationTicks = Math.Max(1, visualDurationTicks),
                    Beams = beams,
                    DotCount = dotCount,
                    IsWhole = isWhole,
                    IsHalf = isHalf,
                    FillHead = fillHead,
                    StemUp = note.IsRest ? true : (note.StemUpOverride ?? (GetEffectiveNoteMidi(note) < 71)),
                    MeasureIndex = GetMeasureIndex(note.StartTick)
                });
            }

            if (noteInfos.Count == 0) return;

            noteInfos = noteInfos
                .OrderBy(n => n.Note.StartTick)
                .ThenBy(n => n.Y)
                .ToList();
            var drawnOttavaAnchors = new HashSet<(int Tick, bool Treble, int Shift)>();

            var eligibleChordGroups = noteInfos
                .Where(n => !n.Note.IsRest
                    && !n.IsWhole
                    && !n.IsHalf
                    && n.Beams > 0)
                .GroupBy(n => new
                {
                    Tick = n.Note.StartTick,
                    Voice = Math.Max(1, n.Note.Voice),
                    Staff = n.PreferTrebleStaff
                })
                // Chords require same onset + same voice/staff + same visual duration.
                // Mixed durations (e.g., quarter + eighth) at one onset are not chorded.
                .SelectMany(g => g
                    .GroupBy(n => Math.Max(1, n.VisualDurationTicks))
                    .Select(h => h.OrderBy(n => n.Y).ToList()))
                .Where(g => g.Count > 1)
                .ToList();

            var chordGroupByNote = new Dictionary<NoteDrawInfo, List<NoteDrawInfo>>();
            var chordStemAnchorByNote = new Dictionary<NoteDrawInfo, NoteDrawInfo>();
            var chordBoundsByNote = new Dictionary<NoteDrawInfo, (float Top, float Bottom)>();
            foreach (var chord in eligibleChordGroups)
            {
                var forced = chord
                    .Select(n => n.Note.StemUpOverride)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                bool stemUp = forced.Count > 0
                    ? forced.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key
                    : chord.Average(n => GetEffectiveNoteMidi(n.Note)) < 71;
                foreach (var chordNote in chord)
                {
                    chordNote.StemUp = chordNote.Note.StemUpOverride ?? stemUp;
                }

                var anchor = stemUp
                    ? chord.OrderBy(n => n.Y).First()
                    : chord.OrderByDescending(n => n.Y).First();
                var bounds = (Top: chord.Min(n => n.Y), Bottom: chord.Max(n => n.Y));
                foreach (var chordNote in chord)
                {
                    chordGroupByNote[chordNote] = chord;
                    chordStemAnchorByNote[chordNote] = anchor;
                    chordBoundsByNote[chordNote] = bounds;
                }
            }

            var groups = BuildBeamGroups(
                noteInfos,
                tolerance);
            var groupByNote = new Dictionary<NoteDrawInfo, BeamGroup>();
            foreach (var group in groups)
            {
                foreach (var info in group.Notes)
                {
                    info.StemUp = group.StemUp;
                    groupByNote[info] = group;
                }
            }

            foreach (var info in noteInfos)
            {
                bool isChordTone = chordGroupByNote.ContainsKey(info);
                bool isChordStemAnchor = isChordTone
                    && chordStemAnchorByNote.TryGetValue(info, out var anchorInfo)
                    && ReferenceEquals(anchorInfo, info);
                bool isBeamedGroupNote = groupByNote.ContainsKey(info);
                bool isShortFlagChordTone = isChordTone && info.Beams > 0;
                bool isBeamedChordTone = isChordTone && isBeamedGroupNote && info.Beams > 0;
                // Chord stems are provided by glyphs, not custom stem-line drawing.
                bool drawStemForThisNote = !isChordTone;
                // Legacy behavior requested:
                // for 8th/16th/32nd chords, only one anchor keeps tails; the others become quarter glyphs.
                bool shouldUseChordQuarterGlyph = !info.Note.IsRest
                    && isShortFlagChordTone
                    && !isChordStemAnchor
                    && CanDrawBeamedQuarterGlyph(info);
                bool shouldUseChordBeamedAnchorGlyph = !info.Note.IsRest
                    && isBeamedChordTone
                    && isChordStemAnchor
                    && CanDrawBeamedQuarterGlyph(info);
                bool shouldUseChordAnchorDurationGlyph = !info.Note.IsRest
                    && isChordTone
                    && isChordStemAnchor
                    && !isBeamedChordTone
                    && CanDrawFullNoteGlyph(info);
                bool shouldUseBeamedQuarterGlyph = !info.Note.IsRest
                    && !isChordTone
                    && isBeamedGroupNote
                    && info.Beams > 0
                    && CanDrawBeamedQuarterGlyph(info);
                bool shouldUseCompleteGlyph = !info.Note.IsRest
                    && (shouldUseChordAnchorDurationGlyph
                        || (!isBeamedGroupNote
                            && drawStemForThisNote
                            && CanDrawFullNoteGlyph(info)));
                info.IsBeamed = isBeamedGroupNote;
                float noteheadScale = 1f;
                Color noteColor = info.Note.IsSelected && !suppressSelectionVisuals ? GetAccentColor() : GetNotationInkColor();
                if (!info.Note.IsRest)
                {
                    float ledgerCenterX = GetLedgerCenterX(
                        info,
                        shouldUseCompleteGlyph || shouldUseBeamedQuarterGlyph || shouldUseChordQuarterGlyph,
                        noteheadScale);
                    DrawLedgerLines(ds, ledgerCenterX, info.Y, info.HeadWidth * noteheadScale, trebleTop, trebleBottom, bassTop, bassBottom, info.PreferTrebleStaff, noteColor);
                }

                if (info.Note.IsRest)
                {
                    DrawRest(ds, info, noteColor);
                    DrawNoteDots(ds, info, noteColor, 1f);
                }
                else
                {
                    DrawNoteAccidental(ds, info, noteColor);
                    bool drawnAsCompleteGlyph = false;
                    if (shouldUseBeamedQuarterGlyph || shouldUseChordQuarterGlyph || shouldUseChordBeamedAnchorGlyph)
                    {
                        drawnAsCompleteGlyph = TryDrawBeamedQuarterGlyph(ds, info, noteColor);
                    }
                    else if (shouldUseCompleteGlyph)
                    {
                        drawnAsCompleteGlyph = TryDrawFullNoteGlyph(ds, info, noteColor);
                    }
                    if (!drawnAsCompleteGlyph)
                    {
                        DrawNotehead(ds, info, noteColor, noteheadScale);
                    }
                    DrawNoteDots(ds, info, noteColor, noteheadScale);
                    DrawNoteArticulation(ds, info, noteColor);
                DrawNoteOrnament(ds, info, noteColor);
                    DrawAutoOttavaHint(ds, info, noteColor, drawnOttavaAnchors);

                    if (!info.IsWhole && (!drawnAsCompleteGlyph || isBeamedGroupNote))
                    {
                        if (!drawStemForThisNote)
                        {
                            continue;
                        }

                        bool stemUp = info.StemUp;
                        float stemX = GetStemX(info, stemUp);
                        float chordSpan = 0f;
                        if (isChordTone && chordBoundsByNote.TryGetValue(info, out var chordBounds))
                        {
                            chordSpan = Math.Max(0f, chordBounds.Bottom - chordBounds.Top);
                        }

                        float effectiveStemLength = stemLength + chordSpan;
                        float stemYTop = stemUp ? info.Y - effectiveStemLength : info.Y;
                        float stemYBottom = stemUp ? info.Y : info.Y + effectiveStemLength;
                        bool isBeamed = groupByNote.ContainsKey(info);
                        float effectiveStemThickness = isBeamed
                            ? Math.Max(GetStaffLineThickness(), stemThickness * 0.7f)
                            : stemThickness;

                        if (groupByNote.TryGetValue(info, out var group))
                        {
                            float stemDrawX = stemX;
                            float stemStartY = info.Y;
                            if (drawnAsCompleteGlyph && shouldUseBeamedQuarterGlyph)
                            {
                                // Avoid doubling the quarter-glyph stem near the notehead:
                                // only draw the extension segment from glyph stem tip to beam.
                                float glyphStemLength = SymbolSizeGap * 2.95f;
                                stemDrawX += stemUp ? 0.4f : 0.3f;
                                stemStartY = stemUp
                                    ? info.Y - glyphStemLength - 3f
                                    : info.Y + glyphStemLength + 3f;
                            }
                            float beamY = GetBeamYAtX(group, stemDrawX);

                            ds.DrawLine(stemDrawX, stemStartY, stemDrawX, beamY, noteColor, effectiveStemThickness);
                        }
                        else
                        {
                            ds.DrawLine(stemX, stemYTop, stemX, stemYBottom, noteColor, effectiveStemThickness);

                            int flags = info.Beams;
                            if (flags > 0)
                            {
                                if (_musicFontAvailable)
                                {
                                    float flagSize = SymbolSizeGap * 2.2f;
                                    int flagCode = flags switch
                                    {
                                        1 => (stemUp ? SmuflFlag8thUp : SmuflFlag8thDown),
                                        2 => (stemUp ? SmuflFlag16thUp : SmuflFlag16thDown),
                                        _ => (stemUp ? SmuflFlag32thUp : SmuflFlag32thDown)
                                    };
                                    float fy = stemUp ? stemYTop : stemYBottom;
                                    DrawGlyph(ds, flagCode, stemX, fy + (stemUp ? SymbolSizeGap * 0.3f : -SymbolSizeGap * 0.1f), flagSize, noteColor);
                                }
                                else
                                {
                                    if (stemUp)
                                    {
                                        for (int i = 0; i < flags; i++)
                                        {
                                            float fy = stemYTop + i * flagSpacing;
                                            ds.DrawLine(stemX, fy, stemX + flagLength, fy + flagDrop, noteColor, effectiveStemThickness);
                                        }
                                    }
                                    else
                                    {
                                        for (int i = 0; i < flags; i++)
                                        {
                                            float fy = stemYBottom - i * flagSpacing;
                                            ds.DrawLine(stemX, fy, stemX - flagLength, fy - flagDrop, noteColor, effectiveStemThickness);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            foreach (var group in groups)
            {
                DrawBeamGroup(ds, group, suppressSelectionVisuals);
            }
        }

        private void DrawExpressionMarks(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            int systemIndex,
            int measuresInSystem,
            float trebleTop,
            bool suppressSelectionVisuals)
        {
            if (_viewModel == null) return;

            _expressionTextFormat.FontSize = GetExpressionFontSize();
            _expressionTextFormat.FontFamily = MusicTextFontFamily;
            _expressionTextFormat.FontStyle = FontStyle.Italic;
            _expressionTextFormat.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            float bottomLineY = trebleTop + 4f * _staffGap;
            float[] systemBoundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
            float systemStartX = systemBoundaries[0];
            float maxX = systemBoundaries[Math.Max(1, measuresInSystem)];

            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                string code = NormalizeExpressionCode(mark.Code);
                float x = GetNoteX(mark.StartTick);
                float y = StaffStepOffsetToY(mark.StaffStepOffset, bottomLineY);

                if (code == "ottava")
                {
                    Windows.UI.Color ottavaColor = mark.IsSelected && !suppressSelectionVisuals
                        ? GetAccentColor()
                        : GetNotationInkColor();
                    if (!DrawOttavaExpressionSegment(ds, mark, systemIndex, measuresInSystem, ottavaColor))
                    {
                        continue;
                    }
                }
                else
                {
                    if (GetSystemIndexForTick(mark.StartTick) != systemIndex) continue;
                    if (x < systemStartX - _staffGap * 2f || x > maxX + _staffGap * 2f) continue;
                    DrawExpressionMark(ds, mark, x, y, suppressSelectionVisuals);
                }

                if (mark.IsSelected && !suppressSelectionVisuals)
                {
                    DrawExpressionResizeHandles(ds, mark, x, y);
                }
            }
        }

        private void DrawPlaybackCursor(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, int systemIndex, int measuresInSystem, float trebleTop, float staffBottom)
        {
            if (!_isPlaybackRunning && !_isPlaybackPaused) return;
            if (_playbackCurrentTick < 0) return;

            double sourceTick = GetSourceTickForPlaybackCursor(_playbackCurrentTick);
            int cursorSourceTick = (int)Math.Round(sourceTick);
            if (cursorSourceTick < 0) cursorSourceTick = 0;
            int cursorSystem = GetSystemIndexForTick(cursorSourceTick);
            if (cursorSystem != systemIndex) return;

            float x = GetNoteX(sourceTick);
            float width = Math.Max(4f, SymbolSizeGap * 0.92f);
            float maxX = GetSystemRightX(systemIndex) - width;
            float minX = GetSystemMusicStartX(systemIndex);
            float left = Math.Clamp(x - width * 0.5f, minX, maxX);
            float top = trebleTop - SymbolSizeGap * 0.18f;
            float height = (staffBottom - trebleTop) + SymbolSizeGap * 0.36f;

            ds.FillRectangle(left, top, width, height, Color.FromArgb(76, 64, 156, 255));
            ds.DrawRectangle(left, top, width, height, Colors.DodgerBlue, 1.4f);
        }

        private double GetSourceTickForPlaybackCursor(double playbackTick)
        {
            if (_playbackCursorPoints.Count == 0)
            {
                return Math.Max(0, playbackTick);
            }

            double clampedTick = Math.Max(0d, playbackTick);
            if (clampedTick <= _playbackCursorPoints[0].PlaybackTick)
            {
                return Math.Max(0, _playbackCursorPoints[0].SourceTick);
            }

            int lastIndex = _playbackCursorPoints.Count - 1;
            if (clampedTick >= _playbackCursorPoints[lastIndex].PlaybackTick)
            {
                return Math.Max(0, _playbackCursorPoints[lastIndex].SourceTick);
            }

            int low = 0;
            int high = lastIndex;
            int upperIndex = lastIndex;

            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                int pointTick = _playbackCursorPoints[mid].PlaybackTick;
                if (pointTick < clampedTick)
                {
                    low = mid + 1;
                }
                else
                {
                    upperIndex = mid;
                    high = mid - 1;
                }
            }

            int lowerIndex = Math.Max(0, upperIndex - 1);
            var lower = _playbackCursorPoints[lowerIndex];
            var upper = _playbackCursorPoints[upperIndex];
            double span = Math.Max(1, upper.PlaybackTick - lower.PlaybackTick);
            double t = Math.Clamp((clampedTick - lower.PlaybackTick) / span, 0d, 1d);
            return Math.Max(0d, lower.SourceTick + (upper.SourceTick - lower.SourceTick) * t);
        }

        private void DrawSelectionRectangle(Microsoft.Graphics.Canvas.CanvasDrawingSession ds)
        {
            Rect rect = NormalizeRect(_selectionStart, _selectionEnd);
            if (rect.Width < 1 || rect.Height < 1) return;

            ds.FillRectangle((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, Color.FromArgb(46, 0, 120, 215));
            ds.DrawRectangle((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, Colors.DodgerBlue, 1.5f);
        }

        private static bool IsOttavaUp(ExpressionMark mark)
        {
            return mark.ShapeHeightSteps >= 0f;
        }

        private int GetOttavaEndTick(ExpressionMark mark)
        {
            int ticksPerBeat = Math.Max(1, _ticksPerBeat > 0 ? _ticksPerBeat : GetTicksPerBeat());
            int spanTicks = Math.Max(1, (int)Math.Round(Math.Max(0.2f, mark.SpanBeats) * ticksPerBeat));
            return Math.Max(mark.StartTick + 1, mark.StartTick + spanTicks);
        }

        private bool DrawOttavaExpressionSegment(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            ExpressionMark mark,
            int systemIndex,
            int measuresInSystem,
            Windows.UI.Color color)
        {
            int startTick = Math.Max(0, mark.StartTick);
            int endTick = GetOttavaEndTick(mark);
            int systemStartMeasure = GetSystemStartMeasureIndex(systemIndex);
            int systemStartTick = GetMeasureBoundaryTick(systemStartMeasure);
            int systemEndTick = GetMeasureBoundaryTick(systemStartMeasure + Math.Max(1, measuresInSystem));

            if (endTick <= systemStartTick || startTick >= systemEndTick)
            {
                return false;
            }

            bool isFirstSegment = startTick >= systemStartTick && startTick < systemEndTick;
            bool isLastSegment = endTick > systemStartTick && endTick <= systemEndTick;
            int segmentStartTick = Math.Max(startTick, systemStartTick);
            int segmentEndTick = Math.Min(endTick, systemEndTick);
            if (segmentEndTick <= segmentStartTick)
            {
                segmentEndTick = segmentStartTick + 1;
            }

            float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
            float systemStartX = boundaries[0];
            float systemEndX = boundaries[Math.Max(1, measuresInSystem)];
            float segmentStartX = GetNoteX(segmentStartTick);
            float segmentEndX = GetNoteX(segmentEndTick);
            if (!isFirstSegment)
            {
                segmentStartX = systemStartX;
            }
            if (!isLastSegment)
            {
                segmentEndX = systemEndX;
            }
            if (segmentEndX <= segmentStartX)
            {
                segmentEndX = segmentStartX + Math.Max(_staffGap, _beatWidth * 0.3f);
            }

            float bottomLineY = GetSystemBottomLineY(systemIndex);
            float y = StaffStepOffsetToY(mark.StaffStepOffset, bottomLineY);
            var (dx, dy) = GetExpressionAnchorOffset(mark.Code);
            float drawX = segmentStartX + dx;
            float drawY = y + dy;
            bool up = IsOttavaUp(mark);
            float textSize = Math.Max(14.5f, SymbolSizeGap * 1.28f * ExpressionScale);
            float textWidth = GetOttavaNumberWidth(textSize);
            float gapAfterText = SymbolSizeGap * 0.2f;
            float lineY = drawY - SymbolSizeGap * 0.12f;
            float textBaselineY = lineY - SymbolSizeGap * 0.18f;
            float numberX = drawX - SymbolSizeGap * 1.08f;
            float numberBaselineY = textBaselineY + SymbolSizeGap * 0.58f;

            if (isFirstSegment)
            {
                DrawOttavaNumber(ds, numberX, numberBaselineY, textSize, color);
            }

            float lineStartX = isFirstSegment ? drawX + textWidth + gapAfterText : segmentStartX;
            float lineEndX = segmentEndX;
            if (lineEndX > lineStartX)
            {
                DrawDashedHorizontalLine(ds, lineStartX, lineEndX, lineY, SymbolSizeGap * 0.34f, SymbolSizeGap * 0.17f, Math.Max(1.0f, SymbolSizeGap * 0.09f), color);
            }

            if (isLastSegment)
            {
                float hookHeight = Math.Max(1.8f, SymbolSizeGap * 0.32f);
                float hookEndY = up ? lineY + hookHeight : lineY - hookHeight;
                ds.DrawLine(lineEndX, lineY, lineEndX, hookEndY, color, Math.Max(1.0f, SymbolSizeGap * 0.09f));
            }

            return true;
        }

        private float GetOttavaNumberWidth(float textSize)
        {
            int code = SmuflTimeSig0 + 8;
            if (_musicFontAvailable && _musicFontFace != null && _musicFontFace.HasCharacter((uint)code))
            {
                return Math.Max(SymbolSizeGap * 0.38f, GetGlyphAdvance(code, textSize));
            }

            return Math.Max(SymbolSizeGap * 0.38f, textSize * 0.24f);
        }

        private void DrawOttavaNumber(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float x,
            float baselineY,
            float textSize,
            Windows.UI.Color color)
        {
            int code = SmuflTimeSig0 + 8;
            if (_musicFontAvailable && _musicFontFace != null && _musicFontFace.HasCharacter((uint)code))
            {
                DrawGlyph(ds, code, x, baselineY, textSize, color);
                return;
            }

            if (!TryDrawMusicText(ds, "8", x, baselineY, textSize, color))
            {
                var fallbackFormat = new CanvasTextFormat
                {
                    FontFamily = _expressionTextFormat.FontFamily,
                    FontSize = textSize,
                    FontStyle = FontStyle.Normal,
                    FontWeight = Microsoft.UI.Text.FontWeights.Normal
                };
                ds.DrawText("8", x, baselineY, color, fallbackFormat);
            }
        }

        private void DrawExpressionMark(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            ExpressionMark mark,
            float x,
            float y,
            bool suppressSelectionVisuals)
        {
            string code = NormalizeExpressionCode(mark.Code);
            float thickness = Math.Max(1.2f, SymbolSizeGap * 0.1f);
            var (dx, dy) = GetExpressionAnchorOffset(code);
            float drawX = x + dx;
            float drawY = y + dy;
            Color ink = mark.IsSelected && !suppressSelectionVisuals ? GetAccentColor() : GetNotationInkColor();

            if (IsScoreClefExpression(code))
            {
                DrawScoreClefMark(ds, mark, code, ink);
                return;
            }

            if (IsScoreBarlineExpression(code))
            {
                DrawScoreBarlineMark(ds, mark, code, ink);
                return;
            }

            if (IsScoreEndingExpression(code))
            {
                DrawScoreEndingMark(ds, mark, code, ink);
                return;
            }

            if (IsSlurExpression(code))
            {
                var (width, arch, direction, slopeDelta) = GetSlurGeometry(mark);
                var p0 = new System.Numerics.Vector2(drawX, drawY);
                var p1 = new System.Numerics.Vector2(drawX + width * 0.22f, drawY + direction * arch * 0.96f + slopeDelta * 0.2f);
                var p2 = new System.Numerics.Vector2(drawX + width * 0.78f, drawY + direction * arch * 0.96f + slopeDelta * 0.8f);
                var p3 = new System.Numerics.Vector2(drawX + width, drawY + slopeDelta);
                float maxThickness = Math.Max(2.4f, thickness * 2.0f);
                float minThickness = Math.Max(1.0f, maxThickness * 0.3f);
                const int segments = 40;
                var prev = p0;
                for (int i = 1; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float tMid = (i - 0.5f) / segments;
                    float profile = (float)Math.Sin(Math.PI * tMid);
                    float slurThickness = minThickness + (maxThickness - minThickness) * MathF.Pow(Math.Max(0f, profile), 0.85f);
                    var next = EvaluateCubicBezier(p0, p1, p2, p3, t);
                    ds.DrawLine(prev.X, prev.Y, next.X, next.Y, ink, slurThickness);
                    prev = next;
                }
                return;
            }

            if (IsHairpinExpression(code))
            {
                float width = Math.Max(SymbolSizeGap * 3f, _beatWidth * Math.Max(0.8f, mark.SpanBeats));
                float height = Math.Max(SymbolSizeGap * 0.8f, SymbolSizeGap * 1.2f);
                float half = height * 0.5f;

                if (code == "cresc")
                {
                    ds.DrawLine(drawX, drawY, drawX + width, drawY - half, ink, thickness);
                    ds.DrawLine(drawX, drawY, drawX + width, drawY + half, ink, thickness);
                }
                else
                {
                    ds.DrawLine(drawX, drawY - half, drawX + width, drawY, ink, thickness);
                    ds.DrawLine(drawX, drawY + half, drawX + width, drawY, ink, thickness);
                }

                return;
            }

            if (code == "ped_line")
            {
                float width = Math.Max(SymbolSizeGap * 2.4f, _beatWidth * Math.Max(0.8f, mark.SpanBeats));
                float lineY = drawY + SymbolSizeGap * 0.06f;
                float hookHeight = Math.Max(2f, SymbolSizeGap * 0.6f);
                float pedThickness = GetStaffLineThickness();
                bool hasPrevConnection = HasConnectedPedalNeighbor(mark, checkAtStart: true);
                bool hasNextConnection = HasConnectedPedalNeighbor(mark, checkAtStart: false);
                float lineStartX = drawX;
                if (hasPrevConnection)
                {
                    float notchWidth = Math.Max(6f, SymbolSizeGap * 0.95f);
                    float notchHeight = Math.Max(2.6f, SymbolSizeGap * 0.42f);
                    float apexX = drawX + notchWidth * 0.5f;
                    ds.DrawLine(drawX, lineY, apexX, lineY - notchHeight, ink, pedThickness);
                    ds.DrawLine(apexX, lineY - notchHeight, drawX + notchWidth, lineY, ink, pedThickness);
                    lineStartX = drawX + notchWidth;
                }
                else
                {
                    ds.DrawLine(drawX, lineY - hookHeight, drawX, lineY, ink, pedThickness);
                }

                float lineEndX = drawX + width;
                ds.DrawLine(lineStartX, lineY, lineEndX, lineY, ink, pedThickness);
                if (!hasNextConnection)
                {
                    ds.DrawLine(lineEndX, lineY, lineEndX, lineY - hookHeight, ink, pedThickness);
                }
                return;
            }

            if (code == "stacc")
            {
                if (TryGetExpressionGlyphSequence(code, out var staccGlyph, out float staccSizeFactor, out float staccSpacing)
                    && CanDrawGlyphSequence(staccGlyph))
                {
                    float size = Math.Max(12f, SymbolSizeGap * staccSizeFactor * ExpressionScale);
                    DrawExpressionGlyphSequence(ds, staccGlyph, drawX, drawY, size, staccSpacing, ink);
                    return;
                }

                if (_hideCustomNotationFallback)
                {
                    return;
                }

                float r = Math.Max(1.6f, SymbolSizeGap * 0.18f * 3f);
                ds.FillCircle(drawX + r, drawY + r, r, ink);
                return;
            }

            if (code == "ottava")
            {
                _expressionTextFormat.FontSize = GetExpressionFontSize();
                Windows.UI.Color ottavaColor = mark.IsSelected && !suppressSelectionVisuals ? GetAccentColor() : GetNotationInkColor();
                float ottavaTextSize = Math.Max(14.5f, SymbolSizeGap * 1.28f * ExpressionScale);
                float ottavaTextY = drawY - SymbolSizeGap * 0.28f;
                DrawOttavaNumber(ds, drawX, ottavaTextY, ottavaTextSize, ottavaColor);

                float textWidth = GetOttavaNumberWidth(ottavaTextSize);
                float spanWidth = Math.Max(SymbolSizeGap * 1.8f, _beatWidth * Math.Max(0.8f, mark.SpanBeats));
                float lineStartX = drawX + textWidth + SymbolSizeGap * 0.04f;
                float lineY = drawY + SymbolSizeGap * 0.02f;
                float lineEndX = lineStartX + spanWidth;
                DrawDashedHorizontalLine(ds, lineStartX, lineEndX, lineY, SymbolSizeGap * 0.34f, SymbolSizeGap * 0.17f, Math.Max(1.0f, SymbolSizeGap * 0.09f), ottavaColor);
                float hookHeight = Math.Max(1.8f, SymbolSizeGap * 0.32f);
                ds.DrawLine(lineEndX, lineY, lineEndX, lineY + hookHeight, ottavaColor, Math.Max(1.0f, SymbolSizeGap * 0.09f));
                return;
            }

            if (TryGetExpressionGlyphSequence(code, out var glyphCodes, out float glyphSizeFactor, out float spacingFactor)
                && CanDrawGlyphSequence(glyphCodes))
            {
                float size = Math.Max(12f, SymbolSizeGap * glyphSizeFactor * ExpressionScale);
                DrawExpressionGlyphSequence(ds, glyphCodes, drawX, drawY, size, spacingFactor, ink);
                return;
            }

            string text = GetExpressionDisplayText(code);
            _expressionTextFormat.FontSize = GetExpressionFontSize();
            _expressionTextFormat.FontFamily = MusicTextFontFamily;
            _expressionTextFormat.FontStyle = FontStyle.Italic;
            _expressionTextFormat.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            float musicTextSize = Math.Max(12f, SymbolSizeGap * 2.0f * ExpressionScale);
            if (TryDrawLibraryTextExpression(ds, text, drawX, drawY, musicTextSize, code, ink))
            {
                return;
            }
            ds.DrawText(text, drawX, drawY, ink, _expressionTextFormat);
        }

        private Rect GetExpressionMarkBounds(ExpressionMark mark, float x, float y)
        {
            string code = NormalizeExpressionCode(mark.Code);
            var (dx, dy) = GetExpressionAnchorOffset(code);
            float drawX = x + dx;
            float drawY = y + dy;
            if (IsScoreClefExpression(code))
            {
                return GetScoreClefBounds(mark);
            }

            if (IsScoreBarlineExpression(code))
            {
                return GetScoreBarlineBounds(mark);
            }

            if (IsScoreEndingExpression(code))
            {
                return GetScoreEndingBounds(mark);
            }

            if (IsSlurExpression(code))
            {
                var (width, arch, direction, slopeDelta) = GetSlurGeometry(mark);
                float endY = drawY + slopeDelta;
                float controlTop = direction < 0f ? Math.Min(drawY, endY) - arch : Math.Min(drawY, endY);
                float controlBottom = direction > 0f ? Math.Max(drawY, endY) + arch : Math.Max(drawY, endY);
                return new Rect(
                    drawX - ExpressionHitPadding,
                    controlTop - ExpressionHitPadding,
                    width + ExpressionHitPadding * 2f,
                    (controlBottom - controlTop) + ExpressionHitPadding * 2f);
            }

            if (IsHairpinExpression(code))
            {
                float width = Math.Max(SymbolSizeGap * 3f, _beatWidth * Math.Max(0.8f, mark.SpanBeats));
                float height = Math.Max(SymbolSizeGap * 0.8f, SymbolSizeGap * 1.2f);
                return new Rect(
                    drawX - ExpressionHitPadding,
                    drawY - height * 0.5f - ExpressionHitPadding,
                    width + ExpressionHitPadding * 2f,
                    height + ExpressionHitPadding * 2f);
            }

            if (code == "stacc")
            {
                float r = Math.Max(1.6f, SymbolSizeGap * 0.18f * 3f);
                return new Rect(
                    drawX - ExpressionHitPadding,
                    drawY - ExpressionHitPadding,
                    r * 2f + ExpressionHitPadding * 2f,
                    r * 2f + ExpressionHitPadding * 2f);
            }

            if (code == "ottava")
            {
                int systemIndex = GetSystemIndexForTick(mark.StartTick);
                int measuresInSystem = GetMeasuresInSystem(systemIndex);
                if (TryGetOttavaSegmentBounds(mark, systemIndex, measuresInSystem, out Rect segmentBounds))
                {
                    return segmentBounds;
                }
            }

            if (code == "ped_line")
            {
                float width = Math.Max(SymbolSizeGap * 2.4f, _beatWidth * Math.Max(0.8f, mark.SpanBeats));
                float height = Math.Max(SymbolSizeGap * 0.9f, SymbolSizeGap * 1.25f);
                return new Rect(
                    drawX - ExpressionHitPadding,
                    drawY - height - ExpressionHitPadding,
                    width + ExpressionHitPadding * 2f,
                    height + ExpressionHitPadding * 2f);
            }

            if (TryGetExpressionGlyphSequence(code, out var glyphCodes, out float glyphSizeFactor, out float spacingFactor)
                && CanDrawGlyphSequence(glyphCodes))
            {
                float size = Math.Max(12f, SymbolSizeGap * glyphSizeFactor * ExpressionScale);
                float width = GetExpressionGlyphSequenceWidth(glyphCodes, size, spacingFactor);
                float height = size;
                return new Rect(
                    drawX - ExpressionHitPadding,
                    drawY - size - ExpressionHitPadding * 0.4f,
                    width + ExpressionHitPadding * 2f,
                    height + ExpressionHitPadding * 2f);
            }

            string text = GetExpressionDisplayText(code);
            float fontSize = GetExpressionFontSize();
            float textWidth = Math.Max(SymbolSizeGap * 1.2f, text.Length * fontSize * 0.58f);
            float textHeight = fontSize * 1.15f;
            return new Rect(
                drawX - ExpressionHitPadding,
                drawY - ExpressionHitPadding,
                textWidth + ExpressionHitPadding * 2f,
                textHeight + ExpressionHitPadding * 2f);
        }

        private bool TryGetOttavaSegmentBounds(ExpressionMark mark, int systemIndex, int measuresInSystem, out Rect bounds)
        {
            bounds = Rect.Empty;
            if (NormalizeExpressionCode(mark.Code) != "ottava")
            {
                return false;
            }

            int startTick = Math.Max(0, mark.StartTick);
            int endTick = GetOttavaEndTick(mark);
            int systemStartMeasure = GetSystemStartMeasureIndex(systemIndex);
            int systemStartTick = GetMeasureBoundaryTick(systemStartMeasure);
            int systemEndTick = GetMeasureBoundaryTick(systemStartMeasure + Math.Max(1, measuresInSystem));
            if (endTick <= systemStartTick || startTick >= systemEndTick)
            {
                return false;
            }

            bool isFirstSegment = startTick >= systemStartTick && startTick < systemEndTick;
            bool isLastSegment = endTick > systemStartTick && endTick <= systemEndTick;
            int segmentStartTick = Math.Max(startTick, systemStartTick);
            int segmentEndTick = Math.Min(endTick, systemEndTick);
            if (segmentEndTick <= segmentStartTick)
            {
                segmentEndTick = segmentStartTick + 1;
            }

            float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
            float systemStartX = boundaries[0];
            float systemEndX = boundaries[Math.Max(1, measuresInSystem)];
            float segmentStartX = GetNoteX(segmentStartTick);
            float segmentEndX = GetNoteX(segmentEndTick);
            if (!isFirstSegment)
            {
                segmentStartX = systemStartX;
            }
            if (!isLastSegment)
            {
                segmentEndX = systemEndX;
            }
            if (segmentEndX <= segmentStartX)
            {
                segmentEndX = segmentStartX + Math.Max(SymbolSizeGap, _beatWidth * 0.3f);
            }

            float bottomLineY = GetSystemBottomLineY(systemIndex);
            float y = StaffStepOffsetToY(mark.StaffStepOffset, bottomLineY);
            var (dx, dy) = GetExpressionAnchorOffset(mark.Code);
            float drawX = segmentStartX + dx;
            float drawY = y + dy;
            bool up = IsOttavaUp(mark);
            float textSize = Math.Max(14.5f, SymbolSizeGap * 1.28f * ExpressionScale);
            float textWidth = GetOttavaNumberWidth(textSize);
            float gapAfterText = SymbolSizeGap * 0.2f;
            float lineY = drawY - SymbolSizeGap * 0.12f;
            float textBaselineY = lineY - SymbolSizeGap * 0.18f;
            float numberX = drawX - SymbolSizeGap * 1.08f;
            float numberBaselineY = textBaselineY + SymbolSizeGap * 0.58f;
            float lineStartX = isFirstSegment ? drawX + textWidth + gapAfterText : segmentStartX;
            float lineEndX = segmentEndX;
            float hookHeight = Math.Max(1.8f, SymbolSizeGap * 0.32f);
            float hookEndY = up ? lineY + hookHeight : lineY - hookHeight;

            float left = Math.Min(numberX, lineStartX) - ExpressionHitPadding;
            float right = Math.Max(lineEndX, numberX + textWidth) + ExpressionHitPadding;
            float top = Math.Min(Math.Min(numberBaselineY - textSize, lineY), hookEndY) - ExpressionHitPadding;
            float bottom = Math.Max(Math.Max(numberBaselineY, lineY), hookEndY) + ExpressionHitPadding;
            bounds = new Rect(left, top, Math.Max(1f, right - left), Math.Max(1f, bottom - top));
            return true;
        }

        private (float DrawX, float DrawY) GetExpressionDrawAnchor(ExpressionMark mark)
        {
            string code = NormalizeExpressionCode(mark.Code);
            if (IsScoreMarkExpression(code))
            {
                if (code == ScoreMarkSegno)
                {
                    int segnoSystem = GetSystemIndexForTick(mark.StartTick);
                    float segnoX = GetNoteX(mark.StartTick);
                    float segnoBottomLineY = GetSystemBottomLineY(segnoSystem);
                    float segnoY = StaffStepOffsetToY(mark.StaffStepOffset, segnoBottomLineY);
                    var (segnoDx, segnoDy) = GetExpressionAnchorOffset(code);
                    return (segnoX + segnoDx, segnoY + segnoDy);
                }

                if (IsScoreClefExpression(code))
                {
                    int clefSystemIndex = GetSystemIndexForTick(mark.StartTick);
                    float clefAnchorX = GetNoteX(mark.StartTick);
                    StaffClefType clef = code == ScoreMarkFClef ? StaffClefType.Bass : StaffClefType.Treble;
                    bool topStaff = ResolveScoreMarkTopStaff(mark, clefSystemIndex);
                    float staffTop = topStaff ? GetSystemTrebleTop(clefSystemIndex) : GetSystemBassTop(clefSystemIndex);
                    float anchorLineY = GetStaffClefAnchorLineY(staffTop, clef);
                    float clefY = anchorLineY + GetStaffClefYOffset(clef) * _staffGap;
                    return (clefAnchorX + SymbolSizeGap * 0.08f, clefY);
                }

                bool preferCurrentBoundary = IsScoreEndingExpression(code)
                    || (code == ScoreMarkRepeatBarline && IsStartRepeatBarline(mark));
                if (!TryResolveBarlineAnchorForTick(mark.StartTick, out int systemIndex, out float anchorX, preferCurrentBoundary))
                {
                    systemIndex = GetSystemIndexForTick(mark.StartTick);
                    anchorX = GetNoteX(mark.StartTick);
                }

                if (IsScoreEndingExpression(code))
                {
                    return (anchorX, GetSystemTrebleTop(systemIndex) - _staffGap * 2.78f);
                }

                return (anchorX, (GetSystemTrebleTop(systemIndex) + GetSystemBassBottom(systemIndex)) * 0.5f);
            }

            int markSystem = GetSystemIndexForTick(mark.StartTick);
            float bottomLineY = GetSystemBottomLineY(markSystem);
            float x = GetNoteX(mark.StartTick);
            float y = StaffStepOffsetToY(mark.StaffStepOffset, bottomLineY);
            var (dx, dy) = GetExpressionAnchorOffset(mark.Code);
            return (x + dx, y + dy);
        }

        private float GetExpressionFontSize()
        {
            return Math.Max(14f, SymbolSizeGap * 1.5f) * ExpressionScale;
        }

        private static string GetExpressionDisplayText(string code)
        {
            return code switch
            {
                "rit" or "rall" => "rit.",
                "cresc_text" => "cresc.",
                "dim_text" => "dim.",
                "ottava" => "8",
                "ped" => "Ped.",
                "ped_release" => "*",
                "ped_line" => "_",
                ScoreMarkSegno => "饾剫",
                "tune" => "\u266E",
                "stacc" => "\u2022",
                _ => code
            };
        }

        private static bool IsSlurExpression(string code)
        {
            return code == "slur";
        }

        private static bool IsHairpinExpression(string code)
        {
            return code is "cresc" or "dim";
        }

        private static bool IsScoreClefExpression(string code)
        {
            return code is ScoreMarkGClef or ScoreMarkFClef;
        }

        private static bool IsScoreBarlineExpression(string code)
        {
            return code is ScoreMarkFinalBarline or ScoreMarkRepeatBarline;
        }

        private static bool IsScoreEndingExpression(string code)
        {
            return code is ScoreMarkEnding1 or ScoreMarkEnding2;
        }

        private static bool IsScoreMarkExpression(string code)
        {
            return IsScoreClefExpression(code) || IsScoreBarlineExpression(code) || IsScoreEndingExpression(code) || code == ScoreMarkSegno;
        }

        private static bool IsBarlineAnchoredScoreMark(string code)
        {
            return IsScoreBarlineExpression(code) || IsScoreEndingExpression(code);
        }

        private static string GetScoreEndingLabel(string code)
        {
            return code == ScoreMarkEnding2 ? "2" : "1";
        }

        private static string NormalizeExpressionCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "mf";
            string normalized = code.Trim().ToLowerInvariant();
            return normalized switch
            {
                "rall" => "rit",
                "cresc." or "cresctext" => "cresc_text",
                "dim." or "dimtext" => "dim_text",
                "pedup" => "ped_release",
                _ => normalized
            };
        }

        private float StaffStepOffsetToY(float stepOffset, float bottomLineY)
        {
            return bottomLineY + stepOffset * (_staffGap / 2f);
        }

        private float YToStaffStepOffset(float y, float bottomLineY)
        {
            if (_staffGap <= 0f) return DefaultExpressionStaffStepOffset;
            return (y - bottomLineY) / (_staffGap / 2f);
        }

        private static float ClampExpressionStaffStepOffset(float value)
        {
            return Math.Clamp(value, -24f, 56f);
        }

        private (float Dx, float Dy) GetExpressionAnchorOffset(string code)
        {
            string normalized = NormalizeExpressionCode(code);
            if (normalized == "tune")
            {
                // Place natural sign closer to the note head: up by 1/2 gap and slightly left.
                return (-SymbolSizeGap * 0.24f, -SymbolSizeGap * 0.5f);
            }
            if (normalized == "ottava")
            {
                return (-SymbolSizeGap * 2.45f, -SymbolSizeGap * 0.48f);
            }
            if (normalized == ScoreMarkSegno)
            {
                return (-SymbolSizeGap * 0.12f, -SymbolSizeGap * 0.34f);
            }
            if (normalized == "ped")
            {
                return (0f, SymbolSizeGap * 2.2f);
            }
            if (normalized == "ped_release" || normalized == "ped_line")
            {
                return (0f, SymbolSizeGap * 2.35f);
            }
            return (0f, 0f);
        }

        private static float GetDefaultExpressionSpanBeats(string code)
        {
            string normalized = NormalizeExpressionCode(code);
            return normalized switch
            {
                "slur" => 2.2f,
                "cresc" or "dim" => 1.8f,
                "ottava" => 2.8f,
                "ped_line" => 2.4f,
                ScoreMarkEnding1 or ScoreMarkEnding2 => 4f,
                _ => 1.2f
            };
        }

        private int GetExpressionSpanEndTick(ExpressionMark mark)
        {
            int ticksPerBeat = Math.Max(1, _ticksPerBeat > 0 ? _ticksPerBeat : GetTicksPerBeat());
            int spanTicks = Math.Max(1, (int)Math.Round(Math.Max(0.2f, mark.SpanBeats) * ticksPerBeat));
            return Math.Max(mark.StartTick + 1, mark.StartTick + spanTicks);
        }

        private int SnapBarlineTickForSystem(double x, int systemIndex)
        {
            int safeSystem = Math.Clamp(systemIndex, 0, Math.Max(0, _systemCount - 1));
            int measuresInSystem = GetMeasuresInSystem(safeSystem);
            float[] boundaries = GetSystemBarlinePositions(safeSystem, measuresInSystem);
            int nearestBoundary = 0;
            float nearestDistance = float.MaxValue;
            float targetX = (float)x;
            for (int i = 0; i <= measuresInSystem; i++)
            {
                float distance = Math.Abs(boundaries[i] - targetX);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestBoundary = i;
                }
            }

            int globalBoundary = GetSystemStartMeasureIndex(safeSystem) + nearestBoundary;
            return GetMeasureBoundaryTick(globalBoundary);
        }

        private bool TryResolveBarlineAnchorForTick(int tick, out int systemIndex, out float x, bool preferCurrentSystemAtSystemBoundary = false)
        {
            systemIndex = 0;
            x = _musicStartX;
            if (_systemCount <= 0)
            {
                return false;
            }

            int safeTick = Math.Max(0, tick);
            int globalBoundary = GetNearestMeasureBoundaryIndexForTick(safeTick);
            bool onBoundary = safeTick == GetMeasureBoundaryTick(globalBoundary);
            if (onBoundary && preferCurrentSystemAtSystemBoundary)
            {
                int preferredSystem = Math.Clamp(GetSystemIndexForMeasureIndex(globalBoundary), 0, Math.Max(0, _systemCount - 1));
                int preferredLocalBoundary = globalBoundary - GetSystemStartMeasureIndex(preferredSystem);
                if (preferredLocalBoundary == 0)
                {
                    int preferredMeasures = GetMeasuresInSystem(preferredSystem);
                    float[] preferredBoundaries = GetSystemBarlinePositions(preferredSystem, preferredMeasures);
                    systemIndex = preferredSystem;
                    x = preferredBoundaries[0];
                    return true;
                }
            }

            int sampleTick = onBoundary ? Math.Max(0, safeTick - 1) : safeTick;
            systemIndex = Math.Clamp(GetSystemIndexForTick(sampleTick), 0, Math.Max(0, _systemCount - 1));
            int localBoundary = globalBoundary - GetSystemStartMeasureIndex(systemIndex);
            int measuresInSystem = GetMeasuresInSystem(systemIndex);
            localBoundary = Math.Clamp(localBoundary, 0, measuresInSystem);
            float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
            x = boundaries[localBoundary];
            return true;
        }

        private bool ResolveScoreMarkTopStaff(ExpressionMark mark, int systemIndex)
        {
            float bottomLineY = GetSystemBottomLineY(systemIndex);
            float y = StaffStepOffsetToY(mark.StaffStepOffset, bottomLineY);
            float splitY = (GetSystemTrebleBottom(systemIndex) + GetSystemBassTop(systemIndex)) * 0.5f;
            return y <= splitY;
        }

        private float GetScoreClefStaffStepOffset(int systemIndex, bool topStaff, StaffClefType clef)
        {
            float staffTop = topStaff ? GetSystemTrebleTop(systemIndex) : GetSystemBassTop(systemIndex);
            float anchorLineY = GetStaffClefAnchorLineY(staffTop, clef);
            float y = anchorLineY + GetStaffClefYOffset(clef) * _staffGap;
            return ClampExpressionStaffStepOffset(YToStaffStepOffset(y, GetSystemBottomLineY(systemIndex)));
        }

        private void DrawScoreClefMark(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, ExpressionMark mark, string code, Color ink)
        {
            StaffClefType clef = code == ScoreMarkFClef ? StaffClefType.Bass : StaffClefType.Treble;
            int systemIndex = GetSystemIndexForTick(mark.StartTick);
            float anchorX = GetNoteX(mark.StartTick);
            bool topStaff = ResolveScoreMarkTopStaff(mark, systemIndex);
            float staffTop = topStaff ? GetSystemTrebleTop(systemIndex) : GetSystemBassTop(systemIndex);
            float anchorLineY = GetStaffClefAnchorLineY(staffTop, clef);
            float x = anchorX + SymbolSizeGap * 0.08f;
            if (_musicFontAvailable && _musicFontFace != null)
            {
                DrawClefGlyph(ds, GetClefGlyphCode(clef), x, anchorLineY, GetStaffClefYOffset(clef), _clefFormat.FontSize * 0.94f);
            }
        }

        private bool TryDrawRepeatBarlineGlyph(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float x, float topY, float bottomY, Color color)
        {
            if (!_musicFontAvailable || _musicFontFace == null || !_musicFontFace.HasCharacter((uint)SmuflBarlineRepeatRight))
            {
                return false;
            }

            float size = Math.Max(SymbolSizeGap * 4.5f, 24f);
            float advance = GetGlyphAdvanceRaw(SmuflBarlineRepeatRight, size);
            if (advance < 0.6f) return false;

            float height = Math.Max(1f, bottomY - topY);
            float refHeight = _staffGap * 4f;
            float scaleY = Math.Clamp(height / Math.Max(1f, refHeight), 0.2f, 6f);
            float baselineY = bottomY;
            float startX = x - advance * 0.5f;
            var old = ds.Transform;
            var origin = new System.Numerics.Vector2(startX, baselineY);
            ds.Transform = System.Numerics.Matrix3x2.CreateScale(1f, scaleY, origin) * old;
            DrawGlyph(ds, SmuflBarlineRepeatRight, startX, baselineY, size, color);
            ds.Transform = old;
            return true;
        }

        private static bool IsStartRepeatBarline(ExpressionMark mark)
        {
            return mark.ShapeHeightSteps < 0f;
        }

        private bool IsSystemHeadBoundaryTick(int tick, int systemIndex)
        {
            int safeTick = Math.Max(0, tick);
            int boundaryIndex = GetNearestMeasureBoundaryIndexForTick(safeTick);
            if (GetMeasureBoundaryTick(boundaryIndex) != safeTick)
            {
                return false;
            }

            int safeSystem = Math.Max(0, systemIndex);
            int localBoundary = boundaryIndex - GetSystemStartMeasureIndex(safeSystem);
            return localBoundary == 0;
        }

        private float GetStartRepeatHeadOffset(int tick, int systemIndex, bool isStartRepeat)
        {
            if (!IsSystemHeadBoundaryTick(tick, systemIndex))
            {
                return 0f;
            }

            int keyFifths = Math.Abs(GetEffectiveKeySignatureFifthsAtTick(Math.Max(0, tick)));
            float keyExtra = keyFifths > 0
                ? Math.Min(SymbolSizeGap * 0.46f, keyFifths * SymbolSizeGap * 0.07f)
                : 0f;
            float timeExtra = ShouldDrawTimeSignatureAtSystemStart(systemIndex)
                ? SymbolSizeGap * 0.22f
                : 0f;
            // Keep a clear buffer from system-head key/time signature block.
            float baseOffset = Math.Max(SymbolSizeGap * 0.98f, _staffGap * 0.72f);
            if (isStartRepeat)
            {
                baseOffset += Math.Max(SymbolSizeGap * 0.12f, _staffGap * 0.08f);
            }
            return baseOffset + keyExtra + timeExtra;
        }

        private float GetRepeatInlineSignatureNudge(int tick, int systemIndex, bool isStartRepeat)
        {
            if (!isStartRepeat || tick <= 0)
            {
                return 0f;
            }

            if (!TryGetSystemBoundaryXForTick(systemIndex, tick, out _, out int localBoundary) || localBoundary <= 0)
            {
                return 0f;
            }

            if (!HasSignatureChangeAtTick(tick))
            {
                return 0f;
            }

            // Mid-system start-repeat: move repeat glyph slightly left to clear inline meter/key.
            return -Math.Max(SymbolSizeGap * 0.26f, _staffGap * 0.22f);
        }

        private void DrawRepeatDots(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float dotX,
            float staffTop,
            Color ink)
        {
            float dotR = Math.Max(1.5f, SymbolSizeGap * 0.16f);
            ds.FillCircle(dotX, staffTop + 1.5f * _staffGap, dotR, ink);
            ds.FillCircle(dotX, staffTop + 2.5f * _staffGap, dotR, ink);
        }

        private void DrawScoreBarlineMark(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, ExpressionMark mark, string code, Color ink)
        {
            bool preferCurrentBoundary = true;
            if (!TryResolveBarlineAnchorForTick(mark.StartTick, out int systemIndex, out float x, preferCurrentBoundary))
            {
                return;
            }

            float thinBarlineThickness = GetThinBarlineThickness();
            float thickBarlineThickness = GetFinalBarlineThickThickness(thinBarlineThickness);
            float lineTop = GetSystemTrebleTop(systemIndex);
            float lineBottom = GetSystemBassBottom(systemIndex);
            if (code == ScoreMarkFinalBarline)
            {
                float finalSeparation = Math.Max(SymbolSizeGap * 0.56f, thinBarlineThickness * 0.5f + thickBarlineThickness * 0.5f + SymbolSizeGap * 0.18f);
                float finalThinX = x - finalSeparation;
                ds.DrawLine(finalThinX, lineTop, finalThinX, lineBottom, ink, thinBarlineThickness);
                ds.DrawLine(x, lineTop, x, lineBottom, ink, thickBarlineThickness);
                return;
            }

            bool isStartRepeat = IsStartRepeatBarline(mark);
            x += GetStartRepeatHeadOffset(mark.StartTick, systemIndex, isStartRepeat);
            x += GetRepeatInlineSignatureNudge(mark.StartTick, systemIndex, isStartRepeat);
            float separation = Math.Max(SymbolSizeGap * 0.52f, thinBarlineThickness * 0.5f + thickBarlineThickness * 0.5f + SymbolSizeGap * 0.18f);
            float dotOffset = Math.Max(SymbolSizeGap * 0.48f, 4.5f);
            float thickX = x;
            float thinX = isStartRepeat ? x + separation : x - separation;
            float dotX = isStartRepeat ? thinX + dotOffset : thinX - dotOffset;

            ds.DrawLine(thickX, lineTop, thickX, lineBottom, ink, thickBarlineThickness);
            ds.DrawLine(thinX, lineTop, thinX, lineBottom, ink, thinBarlineThickness);
            DrawRepeatDots(ds, dotX, GetSystemTrebleTop(systemIndex), ink);
            DrawRepeatDots(ds, dotX, GetSystemBassTop(systemIndex), ink);
        }

        private void DrawScoreEndingMark(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, ExpressionMark mark, string code, Color ink)
        {
            if (!TryResolveBarlineAnchorForTick(mark.StartTick, out int systemIndex, out float startX, preferCurrentSystemAtSystemBoundary: true))
            {
                return;
            }

            startX += GetStartRepeatHeadOffset(mark.StartTick, systemIndex, isStartRepeat: false);
            float width = Math.Max(_beatWidth * Math.Max(1f, mark.SpanBeats), _staffGap * 3.2f);
            float endX = startX + width;
            float y = GetSystemTrebleTop(systemIndex) - _staffGap * 2.78f;
            float hookY = y + _staffGap * 1.24f;
            float thickness = GetStaffLineThickness();
            ds.DrawLine(startX, y, endX, y, ink, thickness);
            ds.DrawLine(startX, y, startX, hookY, ink, thickness);
            ds.DrawLine(endX, y, endX, hookY, ink, thickness);

            string label = GetScoreEndingLabel(code);
            float labelX = startX + SymbolSizeGap * 0.41f;
            float labelY = y + _staffGap * 0.84f;
            float labelSize = Math.Max(16.5f, SymbolSizeGap * 1.46f);
            if (!TryDrawScoreEndingDigit(ds, label, labelX, labelY, labelSize, ink)
                && !TryDrawMusicText(ds, label, labelX, labelY, labelSize, ink))
            {
                var labelFormat = new CanvasTextFormat
                {
                    FontFamily = "Times New Roman",
                    FontSize = Math.Max(16f, SymbolSizeGap * 1.33f),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                ds.DrawText(label, labelX, labelY - _staffGap * 0.7f, ink, labelFormat);
            }
        }

        private bool TryDrawScoreEndingDigit(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            string label,
            float x,
            float baselineY,
            float size,
            Color ink)
        {
            if (!_musicFontAvailable || _musicFontFace == null || string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            char ch = label[0];
            if (!char.IsDigit(ch))
            {
                return false;
            }

            int digit = ch - '0';
            int glyphCode = SmuflTimeSig0 + digit;
            if (!_musicFontFace.HasCharacter((uint)glyphCode))
            {
                return false;
            }

            DrawGlyph(ds, glyphCode, x, baselineY, size, ink);
            return true;
        }

        private Rect GetScoreClefBounds(ExpressionMark mark)
        {
            string code = NormalizeExpressionCode(mark.Code);
            StaffClefType clef = code == ScoreMarkFClef ? StaffClefType.Bass : StaffClefType.Treble;
            int systemIndex = GetSystemIndexForTick(mark.StartTick);
            float anchorX = GetNoteX(mark.StartTick);
            bool topStaff = ResolveScoreMarkTopStaff(mark, systemIndex);
            float staffTop = topStaff ? GetSystemTrebleTop(systemIndex) : GetSystemBassTop(systemIndex);
            float anchorLineY = GetStaffClefAnchorLineY(staffTop, clef);
            float y = anchorLineY + GetStaffClefYOffset(clef) * _staffGap;
            float width = Math.Max(SymbolSizeGap * 2.5f, _clefFormat.FontSize * 0.68f);
            float height = Math.Max(SymbolSizeGap * 5.2f, _clefFormat.FontSize * 1.2f);
            return new Rect(anchorX - width * 0.42f, y - height * 0.82f, width, height);
        }

        private Rect GetScoreBarlineBounds(ExpressionMark mark)
        {
            bool preferCurrentBoundary = true;
            if (!TryResolveBarlineAnchorForTick(mark.StartTick, out int systemIndex, out float x, preferCurrentBoundary))
            {
                return Rect.Empty;
            }

            float top = GetSystemTrebleTop(systemIndex) - _staffGap * 0.55f;
            float bottom = GetSystemBassBottom(systemIndex) + _staffGap * 0.55f;
            string code = NormalizeExpressionCode(mark.Code);
            if (code == ScoreMarkRepeatBarline)
            {
                bool isStartRepeat = IsStartRepeatBarline(mark);
                x += GetStartRepeatHeadOffset(mark.StartTick, systemIndex, isStartRepeat);
                x += GetRepeatInlineSignatureNudge(mark.StartTick, systemIndex, isStartRepeat);
                float separation = Math.Max(SymbolSizeGap * 0.52f, SymbolSizeGap * 0.4f);
                float dotOffset = Math.Max(SymbolSizeGap * 0.48f, 4.5f);
                float dotR = Math.Max(1.5f, SymbolSizeGap * 0.16f);
                float left = isStartRepeat
                    ? x - SymbolSizeGap * 0.45f
                    : x - separation - dotOffset - dotR * 2.1f;
                float right = isStartRepeat
                    ? x + separation + dotOffset + dotR * 2.1f
                    : x + SymbolSizeGap * 0.45f;
                return new Rect(left - ExpressionHitPadding * 0.4f, top, Math.Max(1f, right - left), Math.Max(1f, bottom - top));
            }

            float width = Math.Max(SymbolSizeGap * 1.2f, 12f);
            return new Rect(x - width * 0.5f, top, width, Math.Max(1f, bottom - top));
        }

        private Rect GetScoreEndingBounds(ExpressionMark mark)
        {
            if (!TryResolveBarlineAnchorForTick(mark.StartTick, out int systemIndex, out float startX, preferCurrentSystemAtSystemBoundary: true))
            {
                return Rect.Empty;
            }

            startX += GetStartRepeatHeadOffset(mark.StartTick, systemIndex, isStartRepeat: false);
            float width = Math.Max(_beatWidth * Math.Max(1f, mark.SpanBeats), _staffGap * 3.2f);
            float y = GetSystemTrebleTop(systemIndex) - _staffGap * 3.34f;
            float height = _staffGap * 2.2f;
            return new Rect(startX - ExpressionHitPadding, y - ExpressionHitPadding, width + ExpressionHitPadding * 2f, height + ExpressionHitPadding * 2f);
        }

        private bool HasConnectedPedalNeighbor(ExpressionMark mark, bool checkAtStart)
        {
            if (_viewModel == null || NormalizeExpressionCode(mark.Code) != "ped_line")
            {
                return false;
            }

            int ticksPerBeat = Math.Max(1, _ticksPerBeat > 0 ? _ticksPerBeat : GetTicksPerBeat());
            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            int snapTicks = Math.Max(1, ppq / Math.Max(1, _viewModel.SnapDivision));
            int tolerance = Math.Max(1, Math.Min(snapTicks, ticksPerBeat / 4));
            int targetTick = checkAtStart ? mark.StartTick : GetExpressionSpanEndTick(mark);

            foreach (var other in _viewModel.Project.ExpressionMarks)
            {
                if (ReferenceEquals(other, mark)) continue;
                if (NormalizeExpressionCode(other.Code) != "ped_line") continue;
                if (Math.Abs(other.StaffStepOffset - mark.StaffStepOffset) > 1.1f) continue;

                int compareTick = checkAtStart ? GetExpressionSpanEndTick(other) : other.StartTick;
                if (Math.Abs(compareTick - targetTick) <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private void SnapPedalLineMark(ExpressionMark mark, int systemIndex)
        {
            if (_viewModel == null || NormalizeExpressionCode(mark.Code) != "ped_line")
            {
                return;
            }

            int ticksPerBeat = Math.Max(1, _ticksPerBeat > 0 ? _ticksPerBeat : GetTicksPerBeat());
            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            int snapTicks = Math.Max(1, ppq / Math.Max(1, _viewModel.SnapDivision));
            int tolerance = Math.Max(1, Math.Min(snapTicks, ticksPerBeat / 2));

            ExpressionMark? bestPrev = null;
            int bestPrevDelta = int.MaxValue;
            ExpressionMark? bestNext = null;
            int bestNextDelta = int.MaxValue;
            int currentEndTick = GetExpressionSpanEndTick(mark);

            foreach (var other in _viewModel.Project.ExpressionMarks)
            {
                if (ReferenceEquals(other, mark)) continue;
                if (NormalizeExpressionCode(other.Code) != "ped_line") continue;
                if (GetSystemIndexForTick(other.StartTick) != systemIndex) continue;

                int otherEndTick = GetExpressionSpanEndTick(other);
                int prevDelta = Math.Abs(mark.StartTick - otherEndTick);
                if (prevDelta <= tolerance && prevDelta < bestPrevDelta)
                {
                    bestPrev = other;
                    bestPrevDelta = prevDelta;
                }

                int nextDelta = Math.Abs(currentEndTick - other.StartTick);
                if (nextDelta <= tolerance && nextDelta < bestNextDelta)
                {
                    bestNext = other;
                    bestNextDelta = nextDelta;
                }
            }

            if (bestPrev != null)
            {
                mark.StartTick = GetExpressionSpanEndTick(bestPrev);
                mark.StaffStepOffset = bestPrev.StaffStepOffset;
            }

            if (bestNext != null)
            {
                int targetSpanTicks = Math.Max(1, bestNext.StartTick - mark.StartTick);
                mark.SpanBeats = Math.Clamp(targetSpanTicks / (float)ticksPerBeat, 0.2f, 24f);
                mark.StaffStepOffset = bestNext.StaffStepOffset;
            }
        }

        private static float GetDefaultExpressionShapeHeightSteps(string code)
        {
            string normalized = NormalizeExpressionCode(code);
            return normalized switch
            {
                "slur" => 6f,
                "cresc" or "dim" => 4f,
                _ => 6f
            };
        }

        private (float Width, float Arch, float Direction, float SlopeDelta) GetSlurGeometry(ExpressionMark mark)
        {
            float width = Math.Max(SymbolSizeGap * 1.6f, _beatWidth * Math.Max(0.2f, mark.SpanBeats));
            float absSteps = Math.Abs(mark.ShapeHeightSteps);
            if (absSteps < 0.8f)
            {
                absSteps = GetDefaultExpressionShapeHeightSteps(mark.Code);
            }

            float arch = Math.Max(SymbolSizeGap * 1.1f, absSteps * (SymbolSizeGap / 2f));
            float direction = mark.ShapeHeightSteps >= 0f ? -1f : 1f;
            float slopeDelta = Math.Clamp(mark.SlopeSteps, -SlurMaxSlopeSteps, SlurMaxSlopeSteps) * (SymbolSizeGap / 2f);
            return (width, arch, direction, slopeDelta);
        }

        private void DrawExpressionResizeHandles(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, ExpressionMark mark, float x, float y)
        {
            string code = NormalizeExpressionCode(mark.Code);
            float handleSize = Math.Max(5.5f, SymbolSizeGap * 0.6f);
            Windows.UI.Color handleColor = GetAccentColor();

            if (code == "ottava")
            {
                int endTick = GetOttavaEndTick(mark);
                int endSystem = GetSystemIndexForTick(endTick);
                float handleX = GetNoteX(endTick);
                float handleY = StaffStepOffsetToY(mark.StaffStepOffset, GetSystemBottomLineY(endSystem));
                DrawResizeHandleSquare(ds, handleX, handleY, handleSize, handleColor);
                return;
            }

            if (IsHairpinExpression(code))
            {
                float hairpinWidth = Math.Max(SymbolSizeGap * 3f, _beatWidth * Math.Max(0.8f, mark.SpanBeats));
                float hairpinSpanHandleX = x + hairpinWidth;
                float hairpinSpanHandleY = y;
                DrawResizeHandleSquare(ds, hairpinSpanHandleX, hairpinSpanHandleY, handleSize, handleColor);
                return;
            }

            if (code == "ped_line")
            {
                float pedWidth = Math.Max(SymbolSizeGap * 2.4f, _beatWidth * Math.Max(0.8f, mark.SpanBeats));
                float handleX = x + pedWidth;
                float handleY = y;
                DrawResizeHandleSquare(ds, handleX, handleY, handleSize, handleColor);
                return;
            }

            if (IsScoreEndingExpression(code))
            {
                if (TryResolveBarlineAnchorForTick(mark.StartTick, out int systemIndex, out float startX, preferCurrentSystemAtSystemBoundary: true))
                {
                    float endingWidth = Math.Max(_beatWidth * Math.Max(1f, mark.SpanBeats), _staffGap * 3.2f);
                    float handleX = startX + endingWidth;
                    float handleY = GetSystemTrebleTop(systemIndex) - _staffGap * 2.78f;
                    DrawResizeHandleSquare(ds, handleX, handleY, handleSize, handleColor);
                }
                return;
            }

            if (!IsSlurExpression(code)) return;

            var (width, arch, direction, slopeDelta) = GetSlurGeometry(mark);
            float spanHandleX = x + width;
            float spanHandleY = y + slopeDelta;
            float heightHandleX = x + width;
            float heightHandleY = y + slopeDelta + direction * arch;
            float slopeHandleX = x + width * 0.5f;
            float slopeHandleY = y + slopeDelta * 0.5f;

            DrawResizeHandleSquare(ds, spanHandleX, spanHandleY, handleSize, handleColor);
            DrawResizeHandleSquare(ds, heightHandleX, heightHandleY, handleSize, handleColor);
            DrawResizeHandleSquare(ds, slopeHandleX, slopeHandleY, handleSize, handleColor);
        }

        private static void DrawResizeHandleSquare(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float centerX, float centerY, float size, Color color)
        {
            float half = size * 0.5f;
            ds.FillRectangle(centerX - half, centerY - half, size, size, Colors.White);
            ds.DrawRectangle(centerX - half, centerY - half, size, size, color, 1.6f);
        }

        private bool CanDrawGlyphSequence(IReadOnlyList<int> glyphCodes)
        {
            if (!_musicFontAvailable || _musicFontFace == null) return false;
            foreach (int code in glyphCodes)
            {
                if (!_musicFontFace.HasCharacter((uint)code))
                {
                    return false;
                }
            }
            return true;
        }

        private void DrawExpressionGlyphSequence(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            IReadOnlyList<int> glyphCodes,
            float x,
            float baselineY,
            float glyphSize,
            float spacingFactor,
            Color color)
        {
            float cursor = x;
            float spacing = SymbolSizeGap * spacingFactor;
            for (int i = 0; i < glyphCodes.Count; i++)
            {
                float advance = DrawGlyph(ds, glyphCodes[i], cursor, baselineY, glyphSize, color);
                float unifiedAdvance = Math.Max(advance, glyphSize * 0.36f);
                cursor += unifiedAdvance + spacing;
            }
        }

        private float GetExpressionGlyphSequenceWidth(IReadOnlyList<int> glyphCodes, float glyphSize, float spacingFactor)
        {
            float spacing = SymbolSizeGap * spacingFactor;
            float width = 0f;
            for (int i = 0; i < glyphCodes.Count; i++)
            {
                float advance = Math.Max(GetGlyphAdvance(glyphCodes[i], glyphSize), glyphSize * 0.36f);

                width += advance;
                if (i < glyphCodes.Count - 1)
                {
                    width += spacing;
                }
            }
            return width;
        }

        private static bool TryGetExpressionGlyphSequence(string code, out int[] glyphCodes, out float glyphSizeFactor, out float spacingFactor)
        {
            glyphCodes = Array.Empty<int>();
            glyphSizeFactor = 2.2f;
            spacingFactor = ExpressionGlyphUniformSpacingFactor;
            const float dynamicGlyphSizeFactor = 2.05f;

            switch (code)
            {
                case "p":
                    glyphCodes = new[] { SmuflDynamicP };
                    glyphSizeFactor = dynamicGlyphSizeFactor;
                    spacingFactor = 0f;
                    return true;
                case "pp":
                    glyphCodes = new[] { SmuflDynamicPP };
                    glyphSizeFactor = dynamicGlyphSizeFactor;
                    spacingFactor = 0f;
                    return true;
                case "ppp":
                    glyphCodes = new[] { SmuflDynamicPPP };
                    glyphSizeFactor = dynamicGlyphSizeFactor;
                    spacingFactor = 0f;
                    return true;
                case "mp":
                    glyphCodes = new[] { SmuflDynamicMP };
                    glyphSizeFactor = dynamicGlyphSizeFactor;
                    spacingFactor = 0f;
                    return true;
                case "mf":
                    glyphCodes = new[] { SmuflDynamicMF };
                    glyphSizeFactor = dynamicGlyphSizeFactor;
                    spacingFactor = 0f;
                    return true;
                case "f":
                    glyphCodes = new[] { SmuflDynamicF };
                    glyphSizeFactor = dynamicGlyphSizeFactor;
                    spacingFactor = 0f;
                    return true;
                case "ff":
                    glyphCodes = new[] { SmuflDynamicFF };
                    glyphSizeFactor = dynamicGlyphSizeFactor;
                    spacingFactor = 0f;
                    return true;
                case "fff":
                    glyphCodes = new[] { SmuflDynamicFFF };
                    glyphSizeFactor = dynamicGlyphSizeFactor;
                    spacingFactor = 0f;
                    return true;
                case "sf":
                    glyphCodes = new[] { SmuflDynamicS, SmuflDynamicF };
                    glyphSizeFactor = dynamicGlyphSizeFactor;
                    spacingFactor = -0.42f;
                    return true;
                case "tune":
                    glyphCodes = new[] { SmuflAccidentalNatural };
                    glyphSizeFactor = 2.5f;
                    spacingFactor = 0f;
                    return true;
                case "ped":
                    glyphCodes = new[] { SmuflPedalMark };
                    glyphSizeFactor = 1.45f;
                    spacingFactor = 0f;
                    return true;
                case "ped_release":
                    glyphCodes = new[] { SmuflPedalUpMark };
                    glyphSizeFactor = 1.67f;
                    spacingFactor = 0f;
                    return true;
                case "stacc":
                    glyphCodes = new[] { SmuflArticStaccato };
                    glyphSizeFactor = 1.8f;
                    spacingFactor = 0f;
                    return true;
                case ScoreMarkSegno:
                    glyphCodes = new[] { SmuflSegno };
                    glyphSizeFactor = 1.5f;
                    spacingFactor = 0f;
                    return true;
                default:
                    return false;
            }
        }

        private void DrawLedgerLines(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float x,
            float y,
            float headWidth,
            float trebleTop,
            float trebleBottom,
            float bassTop,
            float bassBottom,
            bool preferTrebleStaff,
            Color color)
        {
            if (_staffGap <= 0f) return;

            float visualHeadHalf = Math.Clamp(headWidth * 0.56f, SymbolSizeGap * 0.48f, SymbolSizeGap * 0.62f);
            float halfLength = visualHeadHalf + SymbolSizeGap * 0.06f;
            float thickness = GetStaffLineThickness();

            if (y < trebleTop)
            {
                DrawLedgerLinesAbove(ds, x, trebleTop, y, halfLength, thickness, color);
                return;
            }

            if (y > bassBottom)
            {
                DrawLedgerLinesBelow(ds, x, bassBottom, y, halfLength, thickness, color);
                return;
            }

            if (y > trebleBottom && y < bassTop)
            {
                if (preferTrebleStaff)
                {
                    DrawLedgerLinesBelow(ds, x, trebleBottom, y, halfLength, thickness, color);
                }
                else
                {
                    DrawLedgerLinesAbove(ds, x, bassTop, y, halfLength, thickness, color);
                }
            }
        }

        private void DrawLedgerLinesAbove(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float x, float topLineY, float noteY, float halfLength, float thickness, Color color)
        {
            float offset = topLineY - noteY;
            float epsilon = _staffGap * 0.03f;
            if (offset < _staffGap - epsilon) return;

            int lines = (int)Math.Floor((offset + epsilon) / _staffGap);
            for (int i = 1; i <= lines; i++)
            {
                float ly = topLineY - i * _staffGap;
                ds.DrawLine(x - halfLength, ly, x + halfLength * 2.027f, ly, color, thickness);
            }
        }

        private void DrawLedgerLinesBelow(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float x, float bottomLineY, float noteY, float halfLength, float thickness, Color color)
        {
            float offset = noteY - bottomLineY;
            float epsilon = _staffGap * 0.03f;
            if (offset < _staffGap - epsilon) return;

            int lines = (int)Math.Floor((offset + epsilon) / _staffGap);
            for (int i = 1; i <= lines; i++)
            {
                float ly = bottomLineY + i * _staffGap;
                ds.DrawLine(x - halfLength, ly, x + halfLength * 2.027f, ly, color, thickness);
            }
        }

        private bool TryDrawLedgerLineGlyph(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float centerX, float y, float halfLength, Color color)
        {
            if (!_musicFontAvailable || _musicFontFace == null) return false;
            if (!_musicFontFace.HasCharacter((uint)SmuflLegerLine)) return false;

            float size = Math.Max(12f, SymbolSizeGap * 2.24f);
            float advance = GetGlyphAdvance(SmuflLegerLine, size);
            if (advance < 0.5f) return false;

            float targetWidth = Math.Max(2f, halfLength * 2f);
            float scaleX = Math.Clamp(targetWidth / advance, 0.05f, 12f);
            float baselineY = y;
            float left = centerX - advance * 0.5f;
            var old = ds.Transform;
            var origin = new System.Numerics.Vector2(left, baselineY);
            ds.Transform = System.Numerics.Matrix3x2.CreateScale(scaleX, 1f, origin) * old;
            DrawGlyph(ds, SmuflLegerLine, left, baselineY, size, color);
            ds.Transform = old;
            return true;
        }

        private float GetNoteheadGlyphSize() => SymbolSizeGap * 4.15f;

        private float GetStemAttachOffset(float headWidth)
        {
            float offset = headWidth - SymbolSizeGap * 0.9f;
            float attach = Math.Max(headWidth * 0.45f, offset);
            return Math.Max(headWidth * 0.3f, attach - SymbolSizeGap * 0.125f);
        }

        private float GetStemX(NoteDrawInfo info, bool stemUp)
        {
            float stemAttach = GetStemAttachOffset(info.HeadWidth);
            float baseX = stemUp ? info.X + stemAttach - 1f : info.X - stemAttach + 1f;
            float inwardShift = SymbolSizeGap * 0.125f;
            float rightShift = info.IsBeamed
                ? 0.3f
                : 0f;
            return (stemUp ? baseX - inwardShift : baseX + inwardShift) + rightShift;
        }

        private static float GetBeamYAtX(BeamGroup group, float x)
        {
            return group.BeamSlope * x + group.BeamIntercept;
        }

        private static int GetFullNoteGlyphCode(NoteDrawInfo info)
        {
            return info switch
            {
                { IsWhole: true } => SmuflNoteWhole,
                { IsHalf: true, StemUp: true } => SmuflNoteHalfUp,
                { IsHalf: true, StemUp: false } => SmuflNoteHalfDown,
                { Beams: 0, StemUp: true } => SmuflNoteQuarterUp,
                { Beams: 0, StemUp: false } => SmuflNoteQuarterDown,
                { Beams: 1, StemUp: true } => SmuflNote8thUp,
                { Beams: 1, StemUp: false } => SmuflNote8thDown,
                { Beams: >= 3, StemUp: true } => SmuflNote32thUp,
                { Beams: >= 3, StemUp: false } => SmuflNote32thDown,
                { Beams: >= 2, StemUp: true } => SmuflNote16thUp,
                { Beams: >= 2, StemUp: false } => SmuflNote16thDown,
                _ => 0
            };
        }

        private bool CanDrawFullNoteGlyph(NoteDrawInfo info)
        {
            if (!_musicFontAvailable || _musicFontFace == null) return false;
            int code = GetFullNoteGlyphCode(info);
            return code != 0 && _musicFontFace.HasCharacter((uint)code);
        }

        private static int GetBeamedQuarterGlyphCode(NoteDrawInfo info)
        {
            return info.StemUp ? SmuflNoteQuarterUp : SmuflNoteQuarterDown;
        }

        private bool CanDrawBeamedQuarterGlyph(NoteDrawInfo info)
        {
            if (!_musicFontAvailable || _musicFontFace == null) return false;
            int code = GetBeamedQuarterGlyphCode(info);
            return code != 0 && _musicFontFace.HasCharacter((uint)code);
        }

        private float GetLedgerCenterX(NoteDrawInfo info, bool usingCompleteGlyph, float headScale = 1f)
        {
            if (info.IsWhole)
            {
                return info.X;
            }

            // For beamed-note quarter glyphs, keep ledger lines centered on the note head.
            if (info.IsBeamed)
            {
                return info.X - SymbolSizeGap * 0.15f;
            }

            bool applyVisualOffset = usingCompleteGlyph;
            if (!applyVisualOffset)
            {
                return info.X;
            }

            float widthScale = Math.Max(0.8f, headScale);
            float effectiveHeadWidth = info.HeadWidth * widthScale;
            float headCenterOffset = Math.Max(SymbolSizeGap * 0.1f, effectiveHeadWidth * 0.16f);
            if (info.StemUp)
            {
                return info.X - headCenterOffset;
            }

            // Shift ledger lines left a bit more for stem-down notes to keep visual centering.
            float stemDownFactor = 0.88f;
            return info.X - headCenterOffset * stemDownFactor;
        }

        private bool TryDrawFullNoteGlyph(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, NoteDrawInfo info, Color color)
        {
            if (!_musicFontAvailable || _musicFontFace == null) return false;

            int code = GetFullNoteGlyphCode(info);

            if (code == 0 || !_musicFontFace.HasCharacter((uint)code))
            {
                return false;
            }

            float size = Math.Max(16f, SymbolSizeGap * 4.28f);
            float advance = GetGlyphAdvance(code, size);
            float startX = info.X - advance * 0.5f;
            float baselineY = info.Y - SymbolSizeGap * 0.04f;
            DrawGlyph(ds, code, startX, baselineY, size, color);
            return true;
        }

        private bool TryDrawBeamedQuarterGlyph(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, NoteDrawInfo info, Color color)
        {
            if (!_musicFontAvailable || _musicFontFace == null) return false;

            int code = GetBeamedQuarterGlyphCode(info);
            if (code == 0 || !_musicFontFace.HasCharacter((uint)code))
            {
                return false;
            }

            float size = Math.Max(16f, SymbolSizeGap * 4.28f);
            float advance = GetGlyphAdvance(code, size);
            float startX = info.X - advance * 0.5f;
            float baselineY = info.Y - SymbolSizeGap * 0.04f;
            DrawGlyph(ds, code, startX, baselineY, size, color);
            return true;
        }

        private void DrawNotehead(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, NoteDrawInfo info, Color color, float headScale = 1f)
        {
            float scale = Math.Max(0.8f, headScale);
            if (_musicFontAvailable && _musicFontFace != null)
            {
                int code = info.IsWhole
                    ? SmuflNoteheadWhole
                    : (info.IsHalf ? SmuflNoteheadHalf : SmuflNoteheadBlack);

                float size = GetNoteheadGlyphSize() * scale;
                float advance = GetGlyphAdvance(code, size);
                float startX = info.X - advance / 2f;
                if (info.IsWhole)
                {
                    // Whole-note glyph side-bearings make it look slightly right-shifted.
                    startX -= SymbolSizeGap * 0.42f;
                }
                float baselineY = info.Y - SymbolSizeGap * 0.05f;
                DrawGlyph(ds, code, startX, baselineY, size, color);
            }
            else
            {
                if (_hideCustomNotationFallback)
                {
                    return;
                }

                if (info.FillHead)
                {
                    ds.FillEllipse(info.X, info.Y, GetNoteHeadWidth() * scale, GetNoteHeadHeight() * scale, color);
                }
                else
                {
                    float outline = info.IsWhole ? 2.6f : 1.8f;
                    ds.DrawEllipse(info.X, info.Y, GetNoteHeadWidth() * scale, GetNoteHeadHeight() * scale, color, outline);
                }
            }
        }

        private void DrawNoteDots(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, NoteDrawInfo info, Color color, float headScale = 1f)
        {
            if (info.DotCount <= 0) return;

            float spacing = SymbolSizeGap * 0.5f;
            float scale = Math.Max(0.8f, headScale);
            float startX = info.X + info.HeadWidth * scale - SymbolSizeGap * 0.34f - 3f;
            float dotY = info.Y + SymbolSizeGap * 0.05f;

            for (int i = 0; i < info.DotCount; i++)
            {
                float x = startX + i * spacing;
                if (_musicFontAvailable && _musicFontFace != null && _musicFontFace.HasCharacter((uint)SmuflAugmentationDot))
                {
                    float size = Math.Max(10f, SymbolSizeGap * 1.45f * 3f);
                    DrawGlyph(ds, SmuflAugmentationDot, x, dotY, size, color);
                }
                else
                {
                    if (_hideCustomNotationFallback)
                    {
                        continue;
                    }

                    float dotRadius = Math.Max(1.5f, SymbolSizeGap * 0.16f * 3f);
                    ds.FillCircle(x, dotY, dotRadius, color);
                }
            }
        }

        private void DrawNoteAccidental(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, NoteDrawInfo info, Color color)
        {
            if (info.Note.Accidental == NoteAccidental.None) return;

            float x = info.X - info.HeadWidth + 3f;
            float y = info.Y;

            int code = info.Note.Accidental switch
            {
                NoteAccidental.Sharp => SmuflAccidentalSharp,
                NoteAccidental.Flat => SmuflAccidentalFlat,
                NoteAccidental.Natural => SmuflAccidentalNatural,
                _ => 0
            };

            if (_musicFontAvailable && _musicFontFace != null && code != 0 && _musicFontFace.HasCharacter((uint)code))
            {
                float size = Math.Max(12f, SymbolSizeGap * 2f * 1.25f);
                DrawGlyph(ds, code, x, y, size, color);
                return;
            }

            if (_hideCustomNotationFallback)
            {
                return;
            }

            string fallback = info.Note.Accidental switch
            {
                NoteAccidental.Sharp => "#",
                NoteAccidental.Flat => "b",
                NoteAccidental.Natural => "?",
                _ => ""
            };

            if (!string.IsNullOrEmpty(fallback))
            {
                var fallbackFormat = new CanvasTextFormat
                {
                    FontFamily = "Segoe UI",
                    FontSize = Math.Max(12f, SymbolSizeGap * 1.4f * 1.25f)
                };
                ds.DrawText(fallback, x, y - SymbolSizeGap * 0.8f, color, fallbackFormat);
            }
        }

        private static int GetRestGlyphCode(int baseDurationTicks, int ppq)
        {
            int whole = Math.Max(1, 4 * ppq);
            int half = Math.Max(1, 2 * ppq);
            int quarter = Math.Max(1, ppq);
            int eighth = Math.Max(1, ppq / 2);
            int sixteenth = Math.Max(1, ppq / 4);
            int thirtySecond = Math.Max(1, ppq / 8);

            if (baseDurationTicks >= whole) return SmuflRestWhole;
            if (baseDurationTicks >= half) return SmuflRestHalf;
            if (baseDurationTicks >= quarter) return SmuflRestQuarter;
            if (baseDurationTicks >= eighth) return SmuflRest8th;
            if (baseDurationTicks >= sixteenth) return SmuflRest16th;
            return SmuflRest32th;
        }

        private void DrawRest(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, NoteDrawInfo info, Color color)
        {
            int ppq = Math.Max(1, _viewModel?.Project.Ppq ?? 480);
            int baseDuration = info.Note.BaseDurationTicks > 0 ? info.Note.BaseDurationTicks : info.Note.DurationTicks;
            int code = GetRestGlyphCode(Math.Max(1, baseDuration), ppq);

            if (_musicFontAvailable && _musicFontFace != null && _musicFontFace.HasCharacter((uint)code))
            {
                float size = Math.Max(15f, SymbolSizeGap * (code is SmuflRestWhole or SmuflRestHalf ? 4.35f : 4.15f));
                float advance = GetGlyphAdvance(code, size);
                float startX = info.X - advance * 0.5f;
                float baselineY = code switch
                {
                    SmuflRestWhole => info.Y - SymbolSizeGap * 0.98f,
                    SmuflRestHalf => info.Y - SymbolSizeGap * 0.84f,
                    SmuflRestQuarter => info.Y - SymbolSizeGap * 1.00f,
                    SmuflRest8th => info.Y - SymbolSizeGap * 1.04f,
                    SmuflRest16th => info.Y - SymbolSizeGap * 1.08f,
                    _ => info.Y - SymbolSizeGap * 1.12f
                };
                DrawGlyph(ds, code, startX, baselineY, size, color);
                return;
            }

            if (_hideCustomNotationFallback)
            {
                return;
            }

            ds.DrawText("rest", info.X - SymbolSizeGap * 0.6f, info.Y - SymbolSizeGap * 0.4f, color);
        }

        private void DrawNoteArticulation(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, NoteDrawInfo info, Color color)
        {
            if (info.Note.IsRest) return;
            if (!info.Note.IsStaccato && !info.Note.IsStaccatissimo && !info.Note.IsAccent) return;

            var (x, y, above) = GetNoteArticulationAnchor(info);
            if (_musicFontAvailable && _musicFontFace != null)
            {
                if (info.Note.IsStaccato && _musicFontFace.HasCharacter((uint)SmuflArticStaccato))
                {
                    float size = Math.Max(10f, SymbolSizeGap * 1.8f * 3f);
                    DrawGlyph(ds, SmuflArticStaccato, x - SymbolSizeGap * 0.18f, y + SymbolSizeGap * 0.3f, size, color);
                    return;
                }
            }

            if (info.Note.IsStaccatissimo)
            {
                float side = Math.Max(3.8f, SymbolSizeGap * 0.78f);
                float height = side * 0.866f;
                // Staccatissimo triangle should point toward the note head.
                float tipY = above ? y + height * 0.5f : y - height * 0.5f;
                float baseY = above ? y - height * 0.5f : y + height * 0.5f;
                var p1 = new System.Numerics.Vector2(x, tipY);
                var p2 = new System.Numerics.Vector2(x - side * 0.5f, baseY);
                var p3 = new System.Numerics.Vector2(x + side * 0.5f, baseY);
                using var tri = CanvasGeometry.CreatePolygon(ds.Device, new[] { p1, p2, p3 });
                ds.FillGeometry(tri, color);
                return;
            }

            if (info.Note.IsAccent)
            {
                float width = Math.Max(7.0f, SymbolSizeGap * 1.18f);
                float height = Math.Max(2.4f, SymbolSizeGap * 0.45f);
                float stroke = Math.Max(1.0f, SymbolSizeGap * 0.1f);
                float accentY = above ? y - SymbolSizeGap * 0.2f : y + SymbolSizeGap * 0.2f;
                float cx = x + SymbolSizeGap * 0.04f;
                ds.DrawLine(cx - width * 0.55f, accentY - height * 0.65f, cx + width * 0.45f, accentY, color, stroke);
                ds.DrawLine(cx - width * 0.55f, accentY + height * 0.65f, cx + width * 0.45f, accentY, color, stroke);
                return;
            }

            if (_hideCustomNotationFallback)
            {
                return;
            }

            float r = Math.Max(1.5f, SymbolSizeGap * 0.16f * 3f);
            ds.FillCircle(x, y, r, color);
        }

        private static (float X, float Y, bool Above) GetNoteArticulationAnchor(NoteDrawInfo info)
        {
            bool above = !info.StemUp;
            float y = above ? info.Y - SymbolSizeGap * 1.15f : info.Y + SymbolSizeGap * 1.15f;
            float x = info.X + SymbolSizeGap * 0.02f;
            return (x, y, above);
        }

        private void DrawNoteOrnament(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, NoteDrawInfo info, Color color)
        {
            if (info.Note.IsRest || info.Note.Ornament == NoteOrnament.None) return;
            bool stemUp = info.StemUp;
            if (!TryGetNoteOrnamentGlyph(info.Note.Ornament, stemUp, out int code)) return;

            bool isGrace = IsGraceOrnament(info.Note.Ornament);

            if (_musicFontAvailable && _musicFontFace != null && _musicFontFace.HasCharacter((uint)code))
            {
                float size = isGrace ? Math.Max(13f, SymbolSizeGap * 3.85f) : Math.Max(13f, SymbolSizeGap * 3.05f);
                float advance = GetGlyphAdvance(code, size);

                float x;
                float y;
                if (isGrace)
                {
                    float leftPadding = SymbolSizeGap * 0.46f;
                    if (info.Note.Accidental != NoteAccidental.None)
                    {
                        leftPadding += SymbolSizeGap * 1.05f;
                    }

                    x = info.X
                        - advance
                        - leftPadding
                        - advance * GraceOrnamentLeftShiftByAdvanceFactor
                        + SymbolSizeGap * GraceOrnamentOffsetXSpaces;
                    y = info.Y
                        + SymbolSizeGap * (stemUp ? GraceOrnamentStemUpYOffsetSpaces : GraceOrnamentStemDownYOffsetSpaces)
                        + SymbolSizeGap * GraceOrnamentOffsetYSpaces;
                }
                else
                {
                    var (anchorX, anchorY, above) = GetNoteArticulationAnchor(info);
                    x = anchorX
                        - advance * OrnamentLeftShiftByAdvanceFactor
                        + SymbolSizeGap * OrnamentSharedOffsetXSpaces;
                    y = anchorY + (above ? -SymbolSizeGap * OrnamentDistanceFromNoteSpaces : SymbolSizeGap * OrnamentDistanceFromNoteSpaces);
                    if (IsTurnOrnament(info.Note.Ornament))
                    {
                        x += SymbolSizeGap * 0.32f;
                    }

                    if (info.Note.Ornament == NoteOrnament.Trill)
                    {
                        y += above ? -SymbolSizeGap * TrillExtraDistanceSpaces : SymbolSizeGap * TrillExtraDistanceSpaces;
                    }
                }

                var (manualDx, manualDy) = GetManualOrnamentOffset(info.Note, isGrace);
                x += manualDx * SymbolSizeGap;
                y += manualDy * SymbolSizeGap;

                DrawGlyph(ds, code, x, y, size, color);
                RegisterOrnamentHitTarget(info.Note, x, y, size, advance, isGrace);
                return;
            }

            if (_hideCustomNotationFallback)
            {
                return;
            }

            string fallback = info.Note.Ornament switch
            {
                NoteOrnament.Trill => "tr",
                NoteOrnament.Appoggiatura => "gr",
                NoteOrnament.Acciaccatura => "sl",
                NoteOrnament.TremoloSingle or NoteOrnament.TremoloDouble => "///",
                _ => "~"
            };
            float fallbackX;
            float fallbackY;
            if (isGrace)
            {
                fallbackX = info.X - SymbolSizeGap * 1.5f;
                fallbackY = info.Y - SymbolSizeGap * 0.4f;
            }
            else
            {
                var (anchorX, anchorY, _) = GetNoteArticulationAnchor(info);
                fallbackX = anchorX - SymbolSizeGap * 0.5f;
                fallbackY = anchorY - SymbolSizeGap * 0.85f;
            }
            var fallbackFormat = new CanvasTextFormat
            {
                FontFamily = "Segoe UI",
                FontSize = Math.Max(10f, SymbolSizeGap * (isGrace ? 0.92f : 1.05f))
            };
            ds.DrawText(fallback, fallbackX, fallbackY, color, fallbackFormat);
        }

        private void DrawAutoOttavaHint(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            NoteDrawInfo info,
            Color color,
            ISet<(int Tick, bool Treble, int Shift)> drawnAnchors)
        {
            if (info.Note.IsRest || info.OttavaShiftOctaves == 0)
            {
                return;
            }

            var key = (info.Note.StartTick, info.PreferTrebleStaff, info.OttavaShiftOctaves);
            if (!drawnAnchors.Add(key))
            {
                return;
            }

            bool up = info.OttavaShiftOctaves > 0;
            float textSize = Math.Max(13.5f, SymbolSizeGap * 0.96f);
            float numberX = info.X - SymbolSizeGap * 0.42f;
            float baselineY = up
                ? info.Y - SymbolSizeGap * 3.15f
                : info.Y + SymbolSizeGap * 3.55f;

            DrawOttavaNumber(ds, numberX, baselineY, textSize, color);

            string suffix = up ? "va" : "vb";
            var suffixFormat = new CanvasTextFormat
            {
                FontFamily = _expressionTextFormat.FontFamily,
                FontSize = Math.Max(8.5f, SymbolSizeGap * 0.42f),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };

            float numberWidth = GetOttavaNumberWidth(textSize);
            float suffixX = numberX + numberWidth + SymbolSizeGap * 0.1f;
            float suffixY = baselineY - SymbolSizeGap * 0.62f;
            ds.DrawText(suffix, suffixX, suffixY, color, suffixFormat);

            float suffixWidth = GetTextWidth(suffix, suffixFormat);
            float lineY = up
                ? baselineY - SymbolSizeGap * 0.18f
                : baselineY + SymbolSizeGap * 0.12f;
            float lineStartX = suffixX + suffixWidth + SymbolSizeGap * 0.18f;
            float lineEndX = info.X + SymbolSizeGap * 1.85f;
            if (lineEndX > lineStartX)
            {
                DrawDashedHorizontalLine(
                    ds,
                    lineStartX,
                    lineEndX,
                    lineY,
                    SymbolSizeGap * 0.26f,
                    SymbolSizeGap * 0.14f,
                    Math.Max(0.9f, SymbolSizeGap * 0.08f),
                    color);
            }

            float hookHeight = Math.Max(1.6f, SymbolSizeGap * 0.26f);
            float hookEndY = up ? lineY + hookHeight : lineY - hookHeight;
            ds.DrawLine(lineEndX, lineY, lineEndX, hookEndY, color, Math.Max(0.9f, SymbolSizeGap * 0.08f));
        }

        private static bool IsGraceOrnament(NoteOrnament ornament)
        {
            return ornament is NoteOrnament.Appoggiatura or NoteOrnament.Acciaccatura;
        }

        private static bool IsTurnOrnament(NoteOrnament ornament)
        {
            return ornament is NoteOrnament.Turn or NoteOrnament.InvertedTurn;
        }

        private static (float X, float Y) GetManualOrnamentOffset(NoteEvent note, bool isGrace)
        {
            return isGrace
                ? (note.GraceOrnamentOffsetX, note.GraceOrnamentOffsetY)
                : (note.OrnamentOffsetX, note.OrnamentOffsetY);
        }

        private static void SetManualOrnamentOffset(NoteEvent note, bool isGrace, float x, float y)
        {
            if (isGrace)
            {
                note.GraceOrnamentOffsetX = x;
                note.GraceOrnamentOffsetY = y;
            }
            else
            {
                note.OrnamentOffsetX = x;
                note.OrnamentOffsetY = y;
            }
        }

        private void RegisterOrnamentHitTarget(NoteEvent note, float x, float y, float size, float advance, bool isGrace)
        {
            float width = Math.Max(advance, SymbolSizeGap * 0.8f);
            float height = Math.Max(size * 0.9f, SymbolSizeGap * 1.05f);
            float padding = Math.Max(4f, SymbolSizeGap * 0.35f);
            var bounds = new Rect(
                x - padding,
                y - size - padding,
                width + padding * 2f,
                height + padding * 2f);
            var (_, manualDy) = GetManualOrnamentOffset(note, isGrace);
            float baseY = y - manualDy * SymbolSizeGap;
            _ornamentHitTargets[note] = new OrnamentHitTarget(bounds, isGrace, baseY);
        }

        private static bool TryGetNoteOrnamentGlyph(NoteOrnament ornament, bool stemUp, out int code)
        {
            code = ornament switch
            {
                NoteOrnament.Trill => SmuflOrnamentTrill,
                NoteOrnament.UpperMordent => SmuflOrnamentShortTrill,
                NoteOrnament.LowerMordent => SmuflOrnamentMordent,
                NoteOrnament.Turn => SmuflOrnamentTurn,
                NoteOrnament.InvertedTurn => SmuflOrnamentTurnInverted,
                NoteOrnament.Appoggiatura => stemUp ? SmuflGraceNoteAppoggiaturaStemUp : SmuflGraceNoteAppoggiaturaStemDown,
                NoteOrnament.Acciaccatura => stemUp ? SmuflGraceNoteAcciaccaturaStemUp : SmuflGraceNoteAcciaccaturaStemDown,
                NoteOrnament.TremoloSingle => SmuflTremolo3,
                NoteOrnament.TremoloDouble => SmuflUnmeasuredTremoloSimple,
                _ => 0
            };
            return code != 0;
        }

        private static System.Numerics.Vector2 EvaluateCubicBezier(
            System.Numerics.Vector2 p0,
            System.Numerics.Vector2 p1,
            System.Numerics.Vector2 p2,
            System.Numerics.Vector2 p3,
            float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            var point = uuu * p0;
            point += 3f * uu * t * p1;
            point += 3f * u * tt * p2;
            point += ttt * p3;
            return point;
        }

        private static bool TryGetSingleDottedBaseDuration(int durationTicks, int ppq, int tolerance, out int baseDurationTicks)
        {
            baseDurationTicks = 0;
            int half = 2 * ppq;
            int quarter = ppq;
            int eighth = ppq / 2;
            int sixteenth = ppq / 4;

            if (Math.Abs(durationTicks - (half + quarter)) <= tolerance)
            {
                baseDurationTicks = half;
                return true;
            }

            if (Math.Abs(durationTicks - (quarter + eighth)) <= tolerance)
            {
                baseDurationTicks = quarter;
                return true;
            }

            if (Math.Abs(durationTicks - (eighth + sixteenth)) <= tolerance)
            {
                baseDurationTicks = eighth;
                return true;
            }

            return false;
        }

        private List<BeamGroup> BuildBeamGroups(List<NoteDrawInfo> notes, int tolerance)
        {
            var groups = new List<BeamGroup>();
            if (notes.Count < 2) return groups;

            float stemLength = SymbolSizeGap * 4.2f;
            var manualNotes = new HashSet<NoteDrawInfo>();

            foreach (var manualGroup in notes
                .Where(n => n.Beams > 0 && n.Note.BeamGroupId > 0)
                .GroupBy(n => n.Note.BeamGroupId))
            {
                foreach (var measureGroup in manualGroup
                    .OrderBy(n => n.Note.StartTick)
                    .ThenBy(n => n.Y)
                    .GroupBy(n => n.MeasureIndex))
                {
                    var manualList = measureGroup.ToList();
                    if (manualList.Count < 2)
                    {
                        continue;
                    }

                    var segment = new List<NoteDrawInfo>();
                    NoteDrawInfo? prevManual = null;
                    foreach (var note in manualList)
                    {
                        if (segment.Count == 0)
                        {
                            segment.Add(note);
                            prevManual = note;
                            continue;
                        }

                        segment.Add(note);

                        prevManual = note;
                    }

                    AddManualSegment(segment);
                }
            }

            int index = 0;
            while (index < notes.Count)
            {
                int measure = notes[index].MeasureIndex;
                var measureNotes = new List<NoteDrawInfo>();
                while (index < notes.Count && notes[index].MeasureIndex == measure)
                {
                    measureNotes.Add(notes[index]);
                    index++;
                }

                foreach (var lane in measureNotes
                    .GroupBy(n => (Voice: Math.Max(1, n.Note.Voice), n.PreferTrebleStaff))
                    .OrderBy(g => g.Key.PreferTrebleStaff ? 0 : 1)
                    .ThenBy(g => g.Key.Voice))
                {
                    var current = new List<NoteDrawInfo>();
                    NoteDrawInfo? prevRepresentative = null;

                    foreach (var onsetGroup in lane
                        .OrderBy(n => n.Note.StartTick)
                        .ThenBy(n => n.Y)
                        .GroupBy(n => n.Note.StartTick))
                    {
                        List<NoteDrawInfo> onsetNotes = onsetGroup.ToList();
                        if (onsetNotes.Any(manualNotes.Contains))
                        {
                            FinalizeGroup(current);
                            current.Clear();
                            prevRepresentative = null;
                            continue;
                        }

                        List<NoteDrawInfo> beamableNotes = onsetNotes
                            .Where(note => note.Beams > 0 && (note.Note.BeamGroupId > 0 || CanAutoBeamNote(note)))
                            .ToList();
                        if (beamableNotes.Count == 0)
                        {
                            FinalizeGroup(current);
                            current.Clear();
                            prevRepresentative = null;
                            continue;
                        }

                        NoteDrawInfo representative = beamableNotes[0];
                        if (current.Count > 0
                            && prevRepresentative != null
                            && CanShareBeamGroup(prevRepresentative, representative, tolerance))
                        {
                            current.AddRange(beamableNotes);
                        }
                        else
                        {
                            FinalizeGroup(current);
                            current.AddRange(beamableNotes);
                        }

                        prevRepresentative = representative;
                    }

                    FinalizeGroup(current);
                }
            }

            return groups;

            void FinalizeGroup(List<NoteDrawInfo> list)
            {
                AddGroup(list);
                list.Clear();
            }

            void AddManualSegment(List<NoteDrawInfo> list)
            {
                if (list.Count < 2)
                {
                    return;
                }

                AddGroup(list);
                foreach (var note in list)
                {
                    manualNotes.Add(note);
                }
            }

            void AddGroup(List<NoteDrawInfo> list)
            {
                if (list.Count < 2) return;

                var forced = list
                    .Select(n => n.Note.StemUpOverride)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();
                bool stemUp = forced.Count > 0
                    ? forced.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key
                    : list.Average(n => GetEffectiveNoteMidi(n.Note)) < 71;
                var collapsed = list
                    .GroupBy(n => n.Note.StartTick)
                    .OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        var onset = g.ToList();
                        return stemUp
                            ? onset.OrderBy(n => n.Y).First()
                            : onset.OrderByDescending(n => n.Y).First();
                    })
                    .ToList();

                if (collapsed.Count < 2) return;

                var first = collapsed[0];
                var last = collapsed[collapsed.Count - 1];
                float x1 = GetStemX(first, stemUp);
                float x2 = GetStemX(last, stemUp);
                float y1 = stemUp ? first.Y - stemLength : first.Y + stemLength;
                float y2 = stemUp ? last.Y - stemLength : last.Y + stemLength;

                float slope = 0f;
                if (Math.Abs(x2 - x1) > 0.01f)
                {
                    slope = (y2 - y1) / (x2 - x1);
                }
                slope = Math.Clamp(slope, -MaxBeamSlopeAbs, MaxBeamSlopeAbs);

                float intercept = y1 - slope * x1;
                float shift = 0f;
                foreach (var note in collapsed)
                {
                    float stemX = GetStemX(note, stemUp);
                    float beamY = slope * stemX + intercept;
                    float minStemEndY = stemUp ? note.Y - stemLength : note.Y + stemLength;

                    if (stemUp)
                    {
                        float delta = beamY - minStemEndY;
                        if (delta > shift) shift = delta;
                    }
                    else
                    {
                        float delta = minStemEndY - beamY;
                        if (delta > shift) shift = delta;
                    }
                }

                if (stemUp)
                {
                    intercept -= shift;
                }
                else
                {
                    intercept += shift;
                }

                groups.Add(new BeamGroup
                {
                    Notes = new List<NoteDrawInfo>(collapsed),
                    Beams = collapsed.Max(n => n.Beams),
                    StemUp = stemUp,
                    BeamSlope = slope,
                    BeamIntercept = intercept
                });
            }
        }

        private static bool AreAdjacent(NoteDrawInfo prev, NoteDrawInfo next, int tolerance)
        {
            int delta = next.Note.StartTick - prev.Note.StartTick;
            if (delta <= 0) return false;

            int prevSpan = Math.Max(1, prev.Note.BaseDurationTicks > 0 ? prev.Note.BaseDurationTicks : prev.Note.DurationTicks);
            int maxAcceptedDelta = Math.Max(1, prevSpan + Math.Max(1, tolerance * 2));
            return delta <= maxAcceptedDelta;
        }

        private static bool CanAutoBeamNote(NoteDrawInfo note)
        {
            return note.Beams > 0 && !note.Note.IsRest;
        }

        private bool CanShareBeamGroup(NoteDrawInfo prev, NoteDrawInfo next, int tolerance)
        {
            if (!AreAdjacent(prev, next, tolerance))
            {
                return false;
            }

            if (prev.MeasureIndex != next.MeasureIndex)
            {
                return false;
            }

            if (Math.Max(1, prev.Note.Voice) != Math.Max(1, next.Note.Voice))
            {
                return false;
            }

            if (prev.PreferTrebleStaff != next.PreferTrebleStaff)
            {
                return false;
            }

            int groupA = prev.Note.BeamGroupId;
            int groupB = next.Note.BeamGroupId;
            if (groupA > 0 || groupB > 0)
            {
                return groupA > 0 && groupA == groupB;
            }

            return CanAutoBeamTogether(prev, next);
        }

        private bool CanAutoBeamTogether(NoteDrawInfo prev, NoteDrawInfo next)
        {
            if (!CanAutoBeamNote(prev) || !CanAutoBeamNote(next))
            {
                return false;
            }

            int measureStartTick = GetMeasureBoundaryTick(prev.MeasureIndex);
            int beamUnitTicks = GetAutoBeamUnitTicks(prev.Note.StartTick);
            if (beamUnitTicks <= 0)
            {
                return false;
            }

            int prevBucket = Math.Max(0, prev.Note.StartTick - measureStartTick) / beamUnitTicks;
            int nextBucket = Math.Max(0, next.Note.StartTick - measureStartTick) / beamUnitTicks;
            return prevBucket == nextBucket;
        }

        private int GetAutoBeamUnitTicks(int tick)
        {
            if (_viewModel == null)
            {
                return Math.Max(1, _ticksPerBeat > 0 ? _ticksPerBeat : 480);
            }

            TimeSignature timeSignature = GetEffectiveTimeSignatureAtTick(tick);
            int ticksPerBeat = Math.Max(1, timeSignature.TicksPerBeat(Math.Max(1, _viewModel.Project.Ppq)));
            bool compoundMeter = timeSignature.Denominator == 8 && timeSignature.Numerator % 3 == 0 && timeSignature.Numerator >= 6;
            return compoundMeter ? ticksPerBeat * 3 : ticksPerBeat;
        }

        private void DrawBeamGroup(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            BeamGroup group,
            bool suppressSelectionVisuals)
        {
            if (group.Notes.Count < 2) return;

            // Re-tuned to standard engraving proportions for this staff size.
            float beamThickness = Math.Max(1.72f, SymbolSizeGap * 0.31f) * 1.1f * 1.6f;
            float beamSpacing = SymbolSizeGap * 0.78f;
            bool isSelected = !suppressSelectionVisuals && group.Notes.Any(n => n.Note.IsSelected);
            Color ink = isSelected ? GetAccentColor() : GetNotationInkColor();

            for (int i = 0; i < group.Notes.Count - 1; i++)
            {
                var a = group.Notes[i];
                var b = group.Notes[i + 1];
                float x1 = GetStemX(a, group.StemUp);
                float x2 = GetStemX(b, group.StemUp);
                float y1 = GetBeamYAtX(group, x1);
                float y2 = GetBeamYAtX(group, x2);

                ds.DrawLine(x1, y1, x2, y2, ink, beamThickness);

                float secondaryOffset = group.StemUp ? beamSpacing : -beamSpacing;
                if (group.Beams >= 2 && Math.Min(a.Beams, b.Beams) >= 2)
                {
                    ds.DrawLine(x1, y1 + secondaryOffset, x2, y2 + secondaryOffset, ink, beamThickness);
                }
                else if (group.Beams >= 2)
                {
                    bool aHasSecondary = a.Beams >= 2;
                    bool bHasSecondary = b.Beams >= 2;
                    if (aHasSecondary ^ bHasSecondary)
                    {
                        bool secondaryAtA = aHasSecondary;
                        float sx = secondaryAtA ? x1 : x2;
                        float sy = (secondaryAtA ? y1 : y2) + secondaryOffset;
                        float tx = secondaryAtA ? x2 : x1;
                        float dir = MathF.Sign(tx - sx);
                        if (Math.Abs(dir) < 0.5f) dir = 1f;
                        int anchorIndex = secondaryAtA ? i : i + 1;
                        bool isolatedMiddle = anchorIndex > 0
                            && anchorIndex < group.Notes.Count - 1
                            && group.Notes[anchorIndex - 1].Beams < 2
                            && group.Notes[anchorIndex + 1].Beams < 2;
                        bool drawSecondaryPartial = true;
                        int continuationIndex = secondaryAtA ? anchorIndex - 1 : anchorIndex + 1;
                        bool hasSameLevelContinuation = continuationIndex >= 0
                            && continuationIndex < group.Notes.Count
                            && group.Notes[continuationIndex].Beams >= 2;
                        if (hasSameLevelContinuation)
                        {
                            drawSecondaryPartial = false;
                        }
                        if (isolatedMiddle)
                        {
                            float desiredDir = group.StemUp ? -1f : 1f;
                            if (MathF.Sign(dir) != MathF.Sign(desiredDir))
                            {
                                drawSecondaryPartial = false;
                            }
                        }
                        if (drawSecondaryPartial)
                        {
                            float partialLen = Math.Min(Math.Abs(tx - sx) * 0.58f, SymbolSizeGap * 1.72f) * 0.6f;
                            float ex = sx + dir * partialLen;
                            float ey = sy + group.BeamSlope * (ex - sx);
                            ds.DrawLine(sx, sy, ex, ey, ink, beamThickness);
                        }
                    }
                }

                if (group.Beams >= 3 && Math.Min(a.Beams, b.Beams) >= 3)
                {
                    float thirdOffset = secondaryOffset * 2f;
                    ds.DrawLine(x1, y1 + thirdOffset, x2, y2 + thirdOffset, ink, beamThickness);
                }
                else if (group.Beams >= 3)
                {
                    bool aHasThird = a.Beams >= 3;
                    bool bHasThird = b.Beams >= 3;
                    if (aHasThird ^ bHasThird)
                    {
                        bool thirdAtA = aHasThird;
                        float sx = thirdAtA ? x1 : x2;
                        float sy = (thirdAtA ? y1 : y2) + secondaryOffset * 2f;
                        float tx = thirdAtA ? x2 : x1;
                        float dir = MathF.Sign(tx - sx);
                        if (Math.Abs(dir) < 0.5f) dir = 1f;
                        int anchorIndex = thirdAtA ? i : i + 1;
                        bool isolatedMiddle = anchorIndex > 0
                            && anchorIndex < group.Notes.Count - 1
                            && group.Notes[anchorIndex - 1].Beams < 3
                            && group.Notes[anchorIndex + 1].Beams < 3;
                        bool drawThirdPartial = true;
                        int continuationIndex = thirdAtA ? anchorIndex - 1 : anchorIndex + 1;
                        bool hasSameLevelContinuation = continuationIndex >= 0
                            && continuationIndex < group.Notes.Count
                            && group.Notes[continuationIndex].Beams >= 3;
                        if (hasSameLevelContinuation)
                        {
                            drawThirdPartial = false;
                        }
                        if (isolatedMiddle)
                        {
                            float desiredDir = group.StemUp ? -1f : 1f;
                            if (MathF.Sign(dir) != MathF.Sign(desiredDir))
                            {
                                drawThirdPartial = false;
                            }
                        }
                        if (drawThirdPartial)
                        {
                            float partialLen = Math.Min(Math.Abs(tx - sx) * 0.58f, SymbolSizeGap * 1.62f) * 0.6f;
                            float ex = sx + dir * partialLen;
                            float ey = sy + group.BeamSlope * (ex - sx);
                            ds.DrawLine(sx, sy, ex, ey, ink, beamThickness);
                        }
                    }
                }
            }
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_viewModel == null || RootGrid == null || StaffCanvas == null)
            {
                return;
            }

            var point = e.GetCurrentPoint(RootGrid);
            if ((point.Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.RightButtonPressed)
                || (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0)
            {
                return;
            }

            GeneralTransform transform = StaffCanvas.TransformToVisual(RootGrid);
            Point origin = transform.TransformPoint(new Point(0, 0));
            var staffBounds = new Rect(origin.X, origin.Y, StaffCanvas.ActualWidth, StaffCanvas.ActualHeight);
            if (staffBounds.Contains(point.Position))
            {
                return;
            }

            if (!HasAnySelection())
            {
                HideClefEditPanel();
                return;
            }

            ClearSelection();
            HideMeasureEditPanel();
            HideClefEditPanel();
            SyncSlurSlopeControlFromSelection();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private void StaffCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_viewModel == null) return;

            var point = e.GetCurrentPoint(StaffCanvas);
            _lastPointerCanvasPoint = point.Position;
            bool shift = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
            EndSelectionGroupDrag();
            _pendingCanvasClickAction = false;
            if (point.Properties.IsLeftButtonPressed)
            {
                _hasPendingInsertionAnchor = true;
                _pendingInsertionAnchorPoint = point.Position;
                _pendingInsertionAnchorTimestampUtc = DateTimeOffset.UtcNow;
            }

            if (point.Properties.IsRightButtonPressed)
            {
                HideMeasureEditPanel();
                HideClefEditPanel();
                if (!shift)
                {
                    ClearSelection();
                }

                var rightHitMark = HitTestExpressionMark(point.Position);
                if (rightHitMark != null)
                {
                    rightHitMark.IsSelected = true;
                    SyncSlurSlopeControlFromSelection();
                }
                else
                {
                    var rightHitNote = HitTest(point.Position);
                    if (rightHitNote != null)
                    {
                        rightHitNote.IsSelected = true;
                        SyncNoteTypeControlsFromSelectedNotes();
                    }
                }

                SyncSlurSlopeControlFromSelection();

                EnsureContextMenu();
                UpdateContextMenuState();
                _contextMenu!.ShowAt(StaffCanvas, point.Position);
                StaffCanvas.Invalidate();
                e.Handled = true;
                return;
            }

            if (point.Properties.IsLeftButtonPressed
                && string.Equals(NormalizeExpressionCode(_pendingExpressionCode), "slur", StringComparison.Ordinal))
            {
                HideMeasureEditPanel();
                HideClefEditPanel();
                if (!shift)
                {
                    ClearSelection();
                }

                _pendingSlurGesture = true;
                _pendingSlurGestureRectMode = false;
                _pendingSlurGestureStart = point.Position;
                _selectionStart = point.Position;
                _selectionEnd = point.Position;
                _dragging = false;
                _activeExpressionMark = null;
                StaffCanvas.Invalidate();
                e.Handled = true;
                return;
            }

            if (point.Properties.IsLeftButtonPressed)
            {
                if (_isTitleEditing && !IsTitleEditorPoint(point.Position))
                {
                    HideTitleInlineEditor(commitChanges: true);
                }

                if (_titleHitRect.Contains(point.Position))
                {
                    ShowTitleInlineEditor();
                    e.Handled = true;
                    return;
                }

                if (!shift && TryHitSystemClef(point.Position, out int clefSystemIndex, out bool clefTopStaff, out float clefAnchorX, out float clefAnchorY))
                {
                    ClearSelection();
                    HideMeasureEditPanel();
                    ShowClefEditPanel(clefSystemIndex, clefTopStaff, clefAnchorX, clefAnchorY);
                    SyncSlurSlopeControlFromSelection();
                    StaffCanvas.Invalidate();
                    UpdateNoteStepPanel();
                    e.Handled = true;
                    return;
                }
            }

            HideClefEditPanel();
            _activeNote = null;
            _activeOrnamentNote = null;
            _activeExpressionMark = null;
            _expressionDragMode = ExpressionDragMode.Move;
            _expressionDragOffsetX = 0f;
            _expressionDragOffsetY = 0f;
            _beamLinkAnchorNote = null;
            _beamLinkDraggingNote = null;
            _beamLinkAnchorPointerY = float.NaN;
            SyncSlurSlopeControlFromSelection();

            if (!string.IsNullOrWhiteSpace(_pendingExpressionCode))
            {
                if (!shift)
                {
                    ClearSelection();
                }

                HideMeasureEditPanel();
                HideClefEditPanel();
                var addedMark = AddExpressionMark(point.Position, _pendingExpressionCode);
                _pendingExpressionCode = null;
                ClearPendingInsertionAnchor();
                _activeExpressionMark = addedMark;
                _expressionDragMode = ExpressionDragMode.Move;
                _expressionDragOffsetX = 0f;
                _expressionDragOffsetY = 0f;
                _dragging = true;
                SyncSlurSlopeControlFromSelection();
                return;
            }

            if (TryHitExpressionResizeHandle(point.Position, out var resizeMark, out var resizeMode) && resizeMark != null)
            {
                if (!shift)
                {
                    ClearSelection();
                }

                resizeMark.IsSelected = true;
                _activeExpressionMark = resizeMark;
                _expressionDragMode = resizeMode;
                _expressionDragOffsetX = 0f;
                _expressionDragOffsetY = 0f;
                _dragging = true;
                HideMeasureEditPanel();
                HideClefEditPanel();
                SyncSlurSlopeControlFromSelection();
                StaffCanvas.Invalidate();
                return;
            }

            var hitMark = HitTestExpressionMark(point.Position);
            if (hitMark != null)
            {
                if (!shift)
                {
                    ClearSelection();
                }

                hitMark.IsSelected = true;
                _activeExpressionMark = hitMark;
                _expressionDragMode = ExpressionDragMode.Move;
                var (drawX, drawY) = GetExpressionDrawAnchor(hitMark);
                _expressionDragOffsetX = (float)point.Position.X - drawX;
                _expressionDragOffsetY = (float)point.Position.Y - drawY;
                _dragging = true;
                HideMeasureEditPanel();
                HideClefEditPanel();
                SyncSlurSlopeControlFromSelection();
                StaffCanvas.Invalidate();
                return;
            }

            if (!shift && TryHitMeasureBarline(point.Position, out int measureIndex, out int measureSystem, out int localBoundaryIndex, out float barlineX, out float trebleTop))
            {
                if (TryHitDraggableBarline(point.Position, out int dragSystem, out int dragLocalIndex, out _))
                {
                    _isDraggingBarline = true;
                    _dragBarlineSystemIndex = dragSystem;
                    _dragBarlineLocalIndex = dragLocalIndex;
                    _dragBarlineMeasureIndex = measureIndex;
                    _dragBarlineTrebleTop = trebleTop;
                    _dragBarlinePointerStartX = (float)point.Position.X;
                    _dragBarlineMoved = false;
                    _measurePanelBarlineX = barlineX;
                    HideMeasureEditPanel();
                    HideClefEditPanel();
                    StaffCanvas.Invalidate();
                    return;
                }

                ClearSelection();
                ShowMeasureEditPanel(measureIndex, measureSystem, localBoundaryIndex, barlineX, trebleTop);
                HideClefEditPanel();
                StaffCanvas.Invalidate();
                return;
            }

            if (TryHitNoteOrnament(point.Position, out var ornamentNote, out bool ornamentIsGrace) && ornamentNote != null)
            {
                if (!shift)
                {
                    ClearSelection();
                }

                ornamentNote.IsSelected = true;
                _activeOrnamentNote = ornamentNote;
                _activeOrnamentIsGrace = ornamentIsGrace;
                var (offsetX, offsetY) = GetManualOrnamentOffset(ornamentNote, ornamentIsGrace);
                _ornamentDragStartOffsetX = offsetX;
                _ornamentDragStartOffsetY = offsetY;
                _ornamentDragStartPointerX = (float)point.Position.X;
                _ornamentDragStartPointerY = (float)point.Position.Y;
                _dragging = true;
                HideMeasureEditPanel();
                HideClefEditPanel();
                SyncNoteTypeControlsFromSelectedNotes();
                SyncSlurSlopeControlFromSelection();
                StaffCanvas.Invalidate();
                UpdateNoteStepPanel();
                return;
            }

            var hit = HitTest(point.Position);

            if (point.Properties.IsLeftButtonPressed
                && (_isPlaybackRunning || _isPlaybackPaused)
                && hit != null
                && !shift)
            {
                ClearSelection();
                hit.IsSelected = true;
                _activeNote = hit;
                _dragging = false;
                SeekPlaybackToSourceTick(hit.StartTick, keepRunningIfWasRunning: _isPlaybackRunning);
                SyncNoteTypeControlsFromSelectedNotes();
                SyncSlurSlopeControlFromSelection();
                UpdateNoteStepPanel();
                e.Handled = true;
                return;
            }

            if (hit == null)
            {
                bool hadSelection = HasAnySelection();
                HideMeasureEditPanel();
                HideClefEditPanel();
                if (point.Properties.IsLeftButtonPressed)
                {
                    _pendingCanvasClickAction = true;
                    _pendingCanvasPressPoint = point.Position;
                    _pendingCanvasPressShift = shift;
                    _selectionStart = point.Position;
                    _selectionEnd = point.Position;
                    _isSelectingRect = false;
                    if (!shift && hadSelection)
                    {
                        ClearSelection();
                        ResetNoteTypeControlsForNewInput();
                    }
                }
                else
                {
                    StaffCanvas.Invalidate();
                }
                return;
            }

            bool preserveCurrentSelectionForDrag = !shift && hit.IsSelected && GetSelectedItemCount() > 1;
            if (!shift && !preserveCurrentSelectionForDrag)
            {
                ClearSelection();
            }

            HideMeasureEditPanel();
            HideClefEditPanel();
            hit.IsSelected = true;
            _activeNote = hit;
            _dragging = true;
            BeginSelectionGroupDrag(hit);
            SyncNoteTypeControlsFromSelectedNotes();
            SyncSlurSlopeControlFromSelection();
            StaffCanvas.Invalidate();
        }

        private void StaffCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var pos = e.GetCurrentPoint(StaffCanvas).Position;
            _lastPointerCanvasPoint = pos;

            if (_pendingSlurGesture)
            {
                float dragThreshold = Math.Max(6f, _staffGap * 0.45f);
                if (!_pendingSlurGestureRectMode)
                {
                    float dx = (float)Math.Abs(pos.X - _pendingSlurGestureStart.X);
                    float dy = (float)Math.Abs(pos.Y - _pendingSlurGestureStart.Y);
                    if (dx > dragThreshold || dy > dragThreshold)
                    {
                        _pendingSlurGestureRectMode = true;
                        _isSelectingRect = true;
                        _selectionStart = _pendingSlurGestureStart;
                    }
                }

                if (_pendingSlurGestureRectMode)
                {
                    _selectionEnd = pos;
                    StaffCanvas.Invalidate();
                }

                return;
            }

            if (_pendingCanvasClickAction)
            {
                float dragThreshold = Math.Max(6f, _staffGap * 0.45f);
                float dx = (float)Math.Abs(pos.X - _pendingCanvasPressPoint.X);
                float dy = (float)Math.Abs(pos.Y - _pendingCanvasPressPoint.Y);
                if (!_isSelectingRect && (dx > dragThreshold || dy > dragThreshold))
                {
                    _isSelectingRect = true;
                    _selectionStart = _pendingCanvasPressPoint;
                }

                if (_isSelectingRect)
                {
                    _selectionEnd = pos;
                    StaffCanvas.Invalidate();
                }

                return;
            }

            if (_isSelectingRect)
            {
                _selectionEnd = pos;
                StaffCanvas.Invalidate();
                return;
            }

            if (_isDraggingBarline && _viewModel != null)
            {
                if (_dragBarlineSystemIndex >= 0 && _dragBarlineLocalIndex > 0)
                {
                    int measuresInSystem = GetMeasuresInSystem(_dragBarlineSystemIndex);
                    if (_dragBarlineLocalIndex < measuresInSystem)
                    {
                        float[] boundaries = GetSystemBarlinePositions(_dragBarlineSystemIndex, measuresInSystem);
                        float leftMinWidth = GetDynamicMeasureMinWidth(_dragBarlineSystemIndex, _dragBarlineLocalIndex - 1, measuresInSystem);
                        float rightMinWidth = GetDynamicMeasureMinWidth(_dragBarlineSystemIndex, _dragBarlineLocalIndex, measuresInSystem);
                        float leftBound = boundaries[_dragBarlineLocalIndex - 1] + leftMinWidth;
                        float rightBound = boundaries[_dragBarlineLocalIndex + 1] - rightMinWidth;
                        if (_dragBarlineLocalIndex == measuresInSystem - 1)
                        {
                            int lastMeasureIndex = GetSystemStartMeasureIndex(_dragBarlineSystemIndex) + measuresInSystem - 1;
                            if (IsMeasureEmpty(lastMeasureIndex))
                            {
                                rightBound = boundaries[_dragBarlineLocalIndex + 1];
                            }
                        }

                        if (rightBound < leftBound)
                        {
                            rightBound = leftBound;
                        }

                        float targetX = Math.Clamp((float)pos.X, leftBound, rightBound);
                        float defaultX = GetBaseBarlineX(_dragBarlineSystemIndex, _dragBarlineLocalIndex, measuresInSystem);
                        float offset = targetX - defaultX;
                        int key = GetBarlineOffsetKey(_dragBarlineSystemIndex, _dragBarlineLocalIndex);
                        if (Math.Abs(offset) < 0.5f)
                        {
                            _barlineOffsets.Remove(key);
                        }
                        else
                        {
                            _barlineOffsets[key] = offset;
                        }

                        if (Math.Abs((float)pos.X - _dragBarlinePointerStartX) > 2f)
                        {
                            _dragBarlineMoved = true;
                        }

                        _measurePanelBarlineX = targetX;
                        StaffCanvas.Invalidate();
                    }
                    else if (_dragBarlineLocalIndex == measuresInSystem)
                    {
                        float[] boundaries = GetSystemBarlinePositions(_dragBarlineSystemIndex, measuresInSystem);
                        float previousBoundaryX = boundaries[Math.Max(0, measuresInSystem - 1)];
                        float finalBoundaryX = boundaries[measuresInSystem];
                        float minMeasureWidth = GetEndBoundarySplitMinWidth(previousBoundaryX, finalBoundaryX);
                        float leftBound = previousBoundaryX + minMeasureWidth;
                        float rightBound = finalBoundaryX - minMeasureWidth;
                        if (rightBound < leftBound)
                        {
                            float center = (leftBound + rightBound) * 0.5f;
                            leftBound = center;
                            rightBound = center;
                        }

                        float targetX = Math.Clamp((float)pos.X, leftBound, rightBound);
                        if (Math.Abs((float)pos.X - _dragBarlinePointerStartX) > Math.Max(4f, _staffGap * 0.45f))
                        {
                            _dragBarlineMoved = true;
                        }

                        _measurePanelBarlineX = targetX;
                        StaffCanvas.Invalidate();
                    }
                }

                return;
            }

            if (_viewModel == null || !_dragging) return;

            if (_activeOrnamentNote != null)
            {
                float dx = (float)pos.X - _ornamentDragStartPointerX;
                float dy = (float)pos.Y - _ornamentDragStartPointerY;
                float offsetX = _ornamentDragStartOffsetX + dx / Math.Max(1f, SymbolSizeGap);
                float offsetY = _ornamentDragStartOffsetY + dy / Math.Max(1f, SymbolSizeGap);
                if (_activeOrnamentIsGrace)
                {
                    int systemIndex = GetSystemIndexForTick(_activeOrnamentNote.StartTick);
                    float bottomLineY = GetSystemBottomLineY(systemIndex);
                    float desiredY = _activeOrnamentBaseY + offsetY * SymbolSizeGap;
                    float snappedStaffStep = MathF.Round(YToStaffStepOffset(desiredY, bottomLineY));
                    float snappedY = StaffStepOffsetToY(snappedStaffStep, bottomLineY);
                    offsetY = (snappedY - _activeOrnamentBaseY) / Math.Max(1f, SymbolSizeGap);
                }
                SetManualOrnamentOffset(_activeOrnamentNote, _activeOrnamentIsGrace, offsetX, offsetY);
                UpdateNoteStepPanel();
            }
            else if (_activeNote != null)
            {
                int systemIndex = GetSystemIndexFromY((float)pos.Y);
                bool preferTreble = _activeNote.PreferTrebleStaff
                    ?? ShouldPreferTrebleByPosition(systemIndex, _activeNote.Midi, _activeNote.Accidental);
                preferTreble = ResolveDragStaffPreference(preferTreble, (float)pos.Y, systemIndex);
                if (_isDraggingSelectionGroup && TryApplySelectionGroupDrag(pos))
                {
                    UpdateNoteStepPanel();
                }
                else
                {
                    if (!_activeNote.IsRest)
                    {
                        _activeNote.Midi = YToMidi((float)pos.Y, systemIndex, _activeNote.Accidental, preferTreble);
                    }
                    _activeNote.PreferTrebleStaff = preferTreble;
                    _activeNote.StartTick = SnapNoteTick(pos.X, systemIndex, _activeNote);
                    TryTrackBeamLinkGesture(_activeNote, systemIndex, (float)pos.Y);
                    UpdateNoteStepPanel();
                }
            }
            else if (_activeExpressionMark != null)
            {
                if (_expressionDragMode == ExpressionDragMode.Move)
                {
                    string code = NormalizeExpressionCode(_activeExpressionMark.Code);
                    var (dx, dy) = GetExpressionAnchorOffset(code);
                    float desiredDrawX = (float)pos.X - _expressionDragOffsetX;
                    float desiredDrawY = (float)pos.Y - _expressionDragOffsetY;
                    float baseX = desiredDrawX - dx;
                    float baseY = desiredDrawY - dy;
                    int systemIndex = GetSystemIndexFromY(baseY);
                    float bottomLineY = GetSystemBottomLineY(systemIndex);
                    if (IsScoreClefExpression(code))
                    {
                        _activeExpressionMark.StartTick = SnapTickAllowBarline(baseX, systemIndex);
                        _activeExpressionMark.StaffStepOffset = ClampExpressionStaffStepOffset(MathF.Round(YToStaffStepOffset(baseY, bottomLineY)));
                    }
                    else if (IsBarlineAnchoredScoreMark(code))
                    {
                        _activeExpressionMark.StartTick = SnapBarlineTickForSystem(baseX, systemIndex);
                        _activeExpressionMark.StaffStepOffset = ClampExpressionStaffStepOffset(YToStaffStepOffset(baseY, bottomLineY));
                    }
                    else if (code == ScoreMarkSegno)
                    {
                        _activeExpressionMark.StartTick = SnapTickAllowBarline(baseX, systemIndex);
                        _activeExpressionMark.StaffStepOffset = ClampExpressionStaffStepOffset(YToStaffStepOffset(baseY, bottomLineY));
                    }
                    else
                    {
                        _activeExpressionMark.StartTick = SnapTick(baseX, systemIndex);
                        _activeExpressionMark.StaffStepOffset = ClampExpressionStaffStepOffset(YToStaffStepOffset(baseY, bottomLineY));
                        if (code == "ped_line")
                        {
                            SnapPedalLineMark(_activeExpressionMark, systemIndex);
                        }
                    }
                }
                else
                {
                    int markSystem = GetSystemIndexForTick(_activeExpressionMark.StartTick);
                    float markBottomLineY = GetSystemBottomLineY(markSystem);
                    float markX = GetNoteX(_activeExpressionMark.StartTick);
                    float markY = StaffStepOffsetToY(_activeExpressionMark.StaffStepOffset, markBottomLineY);

                    if (_expressionDragMode == ExpressionDragMode.ResizeSpan)
                    {
                        string code = NormalizeExpressionCode(_activeExpressionMark.Code);
                        if (code == "ottava")
                        {
                            int targetSystem = GetSystemIndexFromY((float)pos.Y);
                            int targetTick = SnapTick(pos.X, targetSystem);
                            int deltaTicks = Math.Max(1, targetTick - _activeExpressionMark.StartTick);
                            int ticksPerBeat = Math.Max(1, _ticksPerBeat > 0 ? _ticksPerBeat : GetTicksPerBeat());
                            _activeExpressionMark.SpanBeats = Math.Clamp(deltaTicks / (float)ticksPerBeat, 0.2f, 512f);
                        }
                        else
                        {
                            float spanStartX = markX;
                            if (IsScoreEndingExpression(code)
                                && TryResolveBarlineAnchorForTick(_activeExpressionMark.StartTick, out int endingSystem, out float endingStartX, preferCurrentSystemAtSystemBoundary: true))
                            {
                                markSystem = endingSystem;
                                spanStartX = endingStartX;
                            }

                            float width = Math.Max(_staffGap * 0.9f, (float)pos.X - spanStartX);
                            _activeExpressionMark.SpanBeats = Math.Clamp(width / Math.Max(1f, _beatWidth), 0.2f, 24f);
                            if (code == "ped_line")
                            {
                                SnapPedalLineMark(_activeExpressionMark, markSystem);
                            }
                        }
                    }
                    else if (_expressionDragMode == ExpressionDragMode.ResizeHeight)
                    {
                        float slopeDelta = Math.Clamp(_activeExpressionMark.SlopeSteps, -SlurMaxSlopeSteps, SlurMaxSlopeSteps) * (_staffGap / 2f);
                        float endBaseY = markY + slopeDelta;
                        float steps = (endBaseY - (float)pos.Y) / Math.Max(0.001f, _staffGap / 2f);
                        if (Math.Abs(steps) < 0.8f)
                        {
                            steps = steps < 0f ? -0.8f : 0.8f;
                        }
                        _activeExpressionMark.ShapeHeightSteps = Math.Clamp(steps, -28f, 28f);
                    }
                    else if (_expressionDragMode == ExpressionDragMode.ResizeSlope)
                    {
                        float newSlopeDelta = ((float)pos.Y - markY) * 2f;
                        float slopeSteps = newSlopeDelta / Math.Max(0.001f, _staffGap / 2f);
                        _activeExpressionMark.SlopeSteps = Math.Clamp(slopeSteps, -SlurMaxSlopeSteps, SlurMaxSlopeSteps);
                        _pendingSlurSlopeSteps = _activeExpressionMark.SlopeSteps;
                    }
                }
            }
            else
            {
                return;
            }

            MarkProjectChanged(pushHistory: false);
            StaffCanvas.Invalidate();
        }

        private void ApplyLocalizedEditorText()
        {
            bool isEnglish = AppSettingsService.Instance.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
            if (FileMenu != null) FileMenu.Title = LocalizationService.Translate("editor.menu.file");
            if (FileNewMenuItem != null) FileNewMenuItem.Text = LocalizationService.Translate("editor.menu.new");
            if (FileImportMusicXmlMenuItem != null) FileImportMusicXmlMenuItem.Text = LocalizationService.Translate("editor.menu.import_musicxml");
            if (FileExportMusicXmlMenuItem != null) FileExportMusicXmlMenuItem.Text = LocalizationService.Translate("editor.menu.export_musicxml");
            if (FileExportPdfMenuItem != null) FileExportPdfMenuItem.Text = LocalizationService.Translate("editor.menu.export_pdf");
            if (FilePrintMenuItem != null) FilePrintMenuItem.Text = LocalizationService.Translate("editor.menu.print");
            if (TimeSignatureMenu != null) TimeSignatureMenu.Title = LocalizationService.Translate("editor.menu.time_signature");
            if (KeySignatureMenu != null) KeySignatureMenu.Title = LocalizationService.Translate("editor.menu.key_signature");
            if (TempoMenu != null) TempoMenu.Title = LocalizationService.Translate("editor.menu.tempo");
            if (SnapMenu != null) SnapMenu.Title = LocalizationService.Translate("editor.menu.note_snap");
            if (SymbolFontMenu != null) SymbolFontMenu.Title = LocalizationService.Translate("editor.menu.display");
            if (ShowGridMenuItem != null) ShowGridMenuItem.Text = LocalizationService.Translate("editor.menu.grid");
            if (AreaSelectMenuItem != null) AreaSelectMenuItem.Text = LocalizationService.Translate("editor.menu.area_select");
            if (ClearAllMenuItem != null) ClearAllMenuItem.Text = LocalizationService.Translate("editor.menu.clear");
            if (UndoButton != null) ToolTipService.SetToolTip(UndoButton, LocalizationService.Translate("editor.toolbar.undo"));
            if (RedoButton != null) ToolTipService.SetToolTip(RedoButton, LocalizationService.Translate("editor.toolbar.redo"));
            if (ExpressionMarkButton != null) ExpressionMarkButton.Content = LocalizationService.Translate("editor.toolbar.expressions");
            if (ScoreMarkButton != null) ScoreMarkButton.Content = isEnglish ? "Score Marks" : "\u8c31\u9762\u8bb0\u53f7";
            if (PedalMarkButton != null) PedalMarkButton.Content = LocalizationService.Translate("editor.toolbar.pedal");
            if (SlurToolButton != null) SlurToolButton.Content = LocalizationService.Translate("editor.toolbar.slur");
            if (NoteLengthLabelText != null) NoteLengthLabelText.Text = LocalizationService.Translate("editor.toolbar.duration");
            if (NoteTypeButton != null) NoteTypeButton.Content = "...";
            if (NoteTypeButton != null) ToolTipService.SetToolTip(NoteTypeButton, LocalizationService.Translate("editor.toolbar.note_type"));
            if (PlayMidiButton != null) ToolTipService.SetToolTip(PlayMidiButton, LocalizationService.Translate("editor.toolbar.play"));
            if (PauseMidiButton != null) ToolTipService.SetToolTip(PauseMidiButton, LocalizationService.Translate("editor.toolbar.pause"));
            if (StopMidiButton != null) ToolTipService.SetToolTip(StopMidiButton, LocalizationService.Translate("editor.toolbar.stop"));
            if (MeasureAddSystemButton != null)
            {
                MeasureAddSystemButton.Content = new TextBlock
                {
                    Text = LocalizationService.Translate("editor.toolbar.add_system"),
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ToolTipService.SetToolTip(MeasureAddSystemButton, LocalizationService.Translate("editor.toolbar.add_system_tooltip"));
            }

            if (DurationModeToggleSwitch != null)
            {
                DurationModeToggleSwitch.OffContent = string.Empty;
                DurationModeToggleSwitch.OnContent = string.Empty;
            }
            UpdateDurationModeStateText();
            EnsureContextMenu();
            UpdateContextMenuLocalization(isEnglish);

            if (NoteLengthButtons != null)
            {
                foreach (var item in NoteLengthButtons.Children.OfType<RadioButton>())
                {
                    if (item.Tag is not string tag) continue;
                    item.Content = tag switch
                    {
                        "None" => LocalizationService.Translate("editor.note_length.none"),
                        "Whole" => LocalizationService.Translate("editor.note_length.whole"),
                        "Half" => LocalizationService.Translate("editor.note_length.half"),
                        "Quarter" => LocalizationService.Translate("editor.note_length.quarter"),
                        "Eighth" => LocalizationService.Translate("editor.note_length.eighth"),
                        "Sixteenth" => LocalizationService.Translate("editor.note_length.sixteenth"),
                        "ThirtySecond" => LocalizationService.Translate("editor.note_length.thirty_second"),
                        _ => item.Content
                    };
                }
            }

            if (AccidentalSharpItem != null) AccidentalSharpItem.Text = LocalizationService.Translate("editor.note_type.sharp");
            if (AccidentalFlatItem != null) AccidentalFlatItem.Text = LocalizationService.Translate("editor.note_type.flat");
            if (AccidentalNaturalItem != null) AccidentalNaturalItem.Text = LocalizationService.Translate("editor.note_type.natural");
            if (StaccatoMenuItem != null) StaccatoMenuItem.Text = LocalizationService.Translate("editor.note_type.staccato");
            if (StaccatissimoMenuItem != null) StaccatissimoMenuItem.Text = LocalizationService.Translate("editor.note_type.staccatissimo");
            if (AccentMenuItem != null) AccentMenuItem.Text = LocalizationService.Translate("editor.note_type.accent");
            if (AugmentationDotMenuItem != null) AugmentationDotMenuItem.Text = LocalizationService.Translate("editor.note_type.dot");
            if (OrnamentSubMenuItem != null) OrnamentSubMenuItem.Text = isEnglish ? "Ornaments" : "\u88c5\u9970\u97f3";
            if (OrnamentNoneMenuItem != null) OrnamentNoneMenuItem.Text = isEnglish ? "None" : "\u65e0";
            if (OrnamentTrillMenuItem != null) OrnamentTrillMenuItem.Text = isEnglish ? "Trill" : "\u98a4\u97f3";
            if (OrnamentUpperMordentMenuItem != null) OrnamentUpperMordentMenuItem.Text = isEnglish ? "Upper Mordent" : "\u4e0a\u6ce2\u97f3";
            if (OrnamentLowerMordentMenuItem != null) OrnamentLowerMordentMenuItem.Text = isEnglish ? "Lower Mordent" : "\u4e0b\u6ce2\u97f3";
            if (OrnamentTurnMenuItem != null) OrnamentTurnMenuItem.Text = isEnglish ? "Turn" : "\u56de\u97f3";
            if (OrnamentInvertedTurnMenuItem != null) OrnamentInvertedTurnMenuItem.Text = isEnglish ? "Inverted Turn" : "\u9006\u56de\u97f3";
            if (OrnamentAppoggiaturaMenuItem != null) OrnamentAppoggiaturaMenuItem.Text = isEnglish ? "Appoggiatura" : "\u501a\u97f3";
            if (OrnamentAcciaccaturaMenuItem != null) OrnamentAcciaccaturaMenuItem.Text = isEnglish ? "Acciaccatura" : "\u77ed\u501a\u97f3";

            if (ExpressionMarkButton?.Flyout is MenuFlyout expressionFlyout)
            {
                foreach (var entry in expressionFlyout.Items.OfType<MenuFlyoutItem>())
                {
                    string code = NormalizeExpressionCode(entry.Tag?.ToString());
                    entry.Text = code switch
                    {
                        "cresc" => LocalizationService.Translate("editor.expression.cresc_symbol"),
                        "dim" => LocalizationService.Translate("editor.expression.dim_symbol"),
                        "ottava" => LocalizationService.Translate("editor.expression.ottava"),
                        "ped" => LocalizationService.Translate("editor.expression.pedal"),
                        "ped_release" => LocalizationService.Translate("editor.expression.pedal_release"),
                        "ped_line" => LocalizationService.Translate("editor.expression.pedal_line"),
                        "tune" => LocalizationService.Translate("editor.expression.tune"),
                        "stacc" => LocalizationService.Translate("editor.expression.stacc"),
                        _ => entry.Text
                    };
                }
            }

            if (PedalMarkButton?.Flyout is MenuFlyout pedalFlyout)
            {
                foreach (var entry in pedalFlyout.Items.OfType<MenuFlyoutItem>())
                {
                    string code = NormalizeExpressionCode(entry.Tag?.ToString());
                    entry.Text = code switch
                    {
                        "ped" => LocalizationService.Translate("editor.expression.pedal"),
                        "ped_release" => LocalizationService.Translate("editor.expression.pedal_release"),
                        "ped_line" => LocalizationService.Translate("editor.expression.pedal_line"),
                        _ => entry.Text
                    };
                }
            }

            if (ScoreMarkButton?.Flyout is MenuFlyout scoreFlyout)
            {
                foreach (var entry in scoreFlyout.Items.OfType<MenuFlyoutItem>())
                {
                    string code = NormalizeExpressionCode(entry.Tag?.ToString());
                    entry.Text = code switch
                    {
                        "score_gclef" => isEnglish ? "Treble Clef" : "\u9ad8\u97f3\u8c31\u53f7",
                        "score_fclef" => isEnglish ? "Bass Clef" : "\u4f4e\u97f3\u8c31\u53f7",
                        "score_final_barline" => isEnglish ? "Final Barline" : "\u7ec8\u6b62\u7ebf",
                        "score_repeat_barline" => isEnglish ? "Repeat Barline" : "\u53cd\u590d\u8bb0\u53f7",
                        "score_segno" => isEnglish ? "Segno" : "\u56de\u5230\u6807\u8bb0",
                        "ottava" => LocalizationService.Translate("editor.expression.ottava"),
                        "score_ending_1" => isEnglish ? "1st Ending" : "\u7b2c\u4e00\u7ed3\u5c3e",
                        "score_ending_2" => isEnglish ? "2nd Ending" : "\u7b2c\u4e8c\u7ed3\u5c3e",
                        _ => entry.Text
                    };
                }
            }
        }

        private void EnsureDefaultNoteLengthSelection()
        {
            if (_viewModel == null)
            {
                return;
            }

            SetNoteLengthSelection(_viewModel.SelectedNoteLength);
        }
        private void SwitchToNoNoteLengthIfNeeded()
        {
            // Keep current note length; do not force-switch to "None".
        }

        private void StaffCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_pendingSlurGesture)
            {
                bool useRectMode = _pendingSlurGestureRectMode;
                Point gestureStart = _pendingSlurGestureStart;
                Point gestureEnd = _selectionEnd;
                _pendingSlurGesture = false;
                _pendingSlurGestureRectMode = false;

                if (useRectMode)
                {
                    _isSelectingRect = false;
                    SelectByRectangle(NormalizeRect(_selectionStart, gestureEnd));
                    if (TryInsertSlurForSelectedNotes())
                    {
                        _pendingExpressionCode = null;
                    }
                }
                else
                {
                    ClearSelection();
                    HideMeasureEditPanel();
                    HideClefEditPanel();
                    var addedMark = AddExpressionMark(gestureStart, "slur");
                    _pendingExpressionCode = null;
                    _activeExpressionMark = addedMark;
                }

                ClearPendingInsertionAnchor();
                StaffCanvas.Invalidate();
                UpdateNoteStepPanel();
                return;
            }

            if (_pendingCanvasClickAction)
            {
                bool useRectMode = _isSelectingRect;
                Point pressPoint = _pendingCanvasPressPoint;
                bool keepSelection = _pendingCanvasPressShift;
                _pendingCanvasClickAction = false;

                if (useRectMode)
                {
                    _isSelectingRect = false;
                    if (!keepSelection)
                    {
                        ClearSelection();
                    }
                    SelectByRectangle(NormalizeRect(_selectionStart, _selectionEnd));
                    StaffCanvas.Invalidate();
                    UpdateNoteStepPanel();
                    return;
                }

                HideMeasureEditPanel();
                HideClefEditPanel();
                if (!keepSelection)
                {
                    ClearSelection();
                }

                var note = AddNote(pressPoint);
                if (note != null)
                {
                    _activeNote = note;
                }
                ClearPendingInsertionAnchor();
                StaffCanvas.Invalidate();
                UpdateNoteStepPanel();
                return;
            }

            if (_isSelectingRect)
            {
                _isSelectingRect = false;
                SelectByRectangle(NormalizeRect(_selectionStart, _selectionEnd));
                StaffCanvas.Invalidate();
                UpdateNoteStepPanel();
                return;
            }

            if (_isDraggingBarline)
            {
                bool moved = _dragBarlineMoved;
                int measureIndex = _dragBarlineMeasureIndex;
                int systemIndex = _dragBarlineSystemIndex;
                int localBoundaryIndex = _dragBarlineLocalIndex;
                float barlineX = _measurePanelBarlineX;
                float trebleTop = _dragBarlineTrebleTop;
                _isDraggingBarline = false;
                _dragBarlineSystemIndex = -1;
                _dragBarlineLocalIndex = -1;
                _dragBarlineMeasureIndex = -1;
                _dragBarlineMoved = false;
                if (moved)
                {
                    try
                    {
                        bool handledEndBoundary = false;
                        if (systemIndex >= 0)
                        {
                            int measuresInSystem = GetMeasuresInSystem(systemIndex);
                            if (localBoundaryIndex == measuresInSystem)
                            {
                                float threshold = Math.Max(8f, _staffGap * 0.8f);
                                float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
                                float finalBarlineX = boundaries[measuresInSystem];
                                float pullDistance = finalBarlineX - _measurePanelBarlineX;
                                float pullTrigger = Math.Max(18f, _staffGap * 1.4f);
                                if (pullDistance >= Math.Max(threshold, pullTrigger))
                                {
                                    if (TryExpandSystemByPullingEndBarline(systemIndex, _measurePanelBarlineX))
                                    {
                                        handledEndBoundary = true;
                                        _viewModel?.SetStatus("\u5df2\u5411\u5de6\u62c9\u51fa\u4e00\u6761\u65b0\u5c0f\u8282\u7ebf");
                                    }
                                }
                            }
                            else if (localBoundaryIndex == Math.Max(1, measuresInSystem - 1) && measuresInSystem > 1)
                            {
                                // Collapse only when the dragged barline reaches the final barline and the tail measure is empty.
                                float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
                                float finalBarlineX = boundaries[measuresInSystem];
                                float nearFinalThreshold = Math.Max(2.5f, _staffGap * 0.2f);
                                bool draggedNearFinal = Math.Abs(_measurePanelBarlineX - finalBarlineX) <= nearFinalThreshold;
                                if (draggedNearFinal && TryShrinkSystemByOneEmptyMeasure(systemIndex))
                                {
                                    handledEndBoundary = true;
                                    _viewModel?.SetStatus("\u5df2\u5220\u9664\u672c\u884c\u672b\u5c3e\u7a7a\u767d\u5c0f\u8282");
                                }
                            }
                        }

                        if (!handledEndBoundary)
                        {
                            _viewModel?.SetStatus("\u5df2\u8c03\u6574\u5c0f\u8282\u7ebf\u4f4d\u7f6e\uff08\u76f4\u63a5\u62d6\u62fd\u5c0f\u8282\u7ebf\u5373\u53ef\u8c03\u6574\uff09");
                        }
                        MarkProjectChanged();
                    }
                    catch (Exception ex)
                    {
                        _viewModel?.SetStatus($"鎷栨嫿灏忚妭绾垮け璐? {ex.Message}");
                    }
                }
                else if (measureIndex >= 0 && systemIndex >= 0)
                {
                    ShowMeasureEditPanel(measureIndex, systemIndex, localBoundaryIndex, barlineX, trebleTop);
                }
                StaffCanvas.Invalidate();
                return;
            }

            _dragging = false;
            _activeNote = null;
            _activeOrnamentNote = null;
            _activeExpressionMark = null;
            _expressionDragMode = ExpressionDragMode.Move;
            _expressionDragOffsetX = 0f;
            _expressionDragOffsetY = 0f;
            EndSelectionGroupDrag();
            _beamLinkAnchorNote = null;
            _beamLinkDraggingNote = null;
            _beamLinkAnchorPointerY = float.NaN;
            if (_pendingHistoryCommitFromDrag)
            {
                _pendingHistoryCommitFromDrag = false;
                PushHistorySnapshot();
                UpdateUndoRedoButtons();
            }
            UpdateNoteStepPanel();
        }

        private Color GetScorePaperColor()
        {
            // Keep the score canvas transparent so window Mica can show through.
            return Color.FromArgb(0, 0, 0, 0);
        }

        private void EnsureContextMenu()
        {
            if (_contextMenu != null) return;

            _contextMenu = new MenuFlyout();
            _contextCopy = new MenuFlyoutItem
            {
                Text = "\u590d\u5236",
                Icon = new FontIcon { Glyph = "\uE8C8", FontSize = 11 }
            };
            _contextCopy.Click += (_, __) => CopySelection();

            _contextCut = new MenuFlyoutItem
            {
                Text = "\u526a\u5207",
                Icon = new FontIcon { Glyph = "\uE8C6", FontSize = 11 }
            };
            _contextCut.Click += (_, __) => CutSelection();

            _contextPaste = new MenuFlyoutItem
            {
                Text = "\u7c98\u8d34",
                Icon = new FontIcon { Glyph = "\uE77F", FontSize = 11 }
            };
            _contextPaste.Click += (_, __) => PasteSelection(_lastPointerCanvasPoint);

            _contextDelete = new MenuFlyoutItem
            {
                Text = "\u5220\u9664",
                Icon = new FontIcon
                {
                    Glyph = "\uE74D",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 198, 40, 40))
                }
            };
            _contextDelete.Click += (_, __) => DeleteSelected();

            _contextMenu.Items.Add(_contextCopy);
            _contextMenu.Items.Add(_contextCut);
            _contextMenu.Items.Add(_contextPaste);
            _contextMenu.Items.Add(new MenuFlyoutSeparator());
            _contextMenu.Items.Add(_contextDelete);
        }

        private void UpdateContextMenuLocalization(bool isEnglish)
        {
            if (_contextCopy != null) _contextCopy.Text = isEnglish ? "Copy" : "\u590d\u5236";
            if (_contextCut != null) _contextCut.Text = isEnglish ? "Cut" : "\u526a\u5207";
            if (_contextPaste != null) _contextPaste.Text = isEnglish ? "Paste" : "\u7c98\u8d34";
            if (_contextDelete != null) _contextDelete.Text = isEnglish ? "Delete" : "\u5220\u9664";
        }

        private bool HasClipboardSelection()
        {
            return _clipboard != null
                && (_clipboard.Notes.Count > 0 || _clipboard.Marks.Count > 0);
        }

        private void UpdateContextMenuState()
        {
            bool hasSelection = HasAnySelection();
            if (_contextCopy != null)
            {
                _contextCopy.IsEnabled = hasSelection;
            }

            if (_contextPaste != null)
            {
                _contextPaste.IsEnabled = HasClipboardSelection();
            }

            if (_contextCut != null)
            {
                _contextCut.IsEnabled = hasSelection;
            }

            if (_contextDelete != null)
            {
                _contextDelete.IsEnabled = hasSelection;
            }
        }

        private bool HasAnySelection()
        {
            if (_viewModel == null) return false;
            return _viewModel.Project.Notes.Any(n => n.IsSelected)
                || _viewModel.Project.ExpressionMarks.Any(m => m.IsSelected);
        }

        private int GetSelectedItemCount()
        {
            if (_viewModel == null)
            {
                return 0;
            }

            return _viewModel.Project.Notes.Count(n => n.IsSelected)
                + _viewModel.Project.ExpressionMarks.Count(m => m.IsSelected);
        }

        private void BeginSelectionGroupDrag(NoteEvent anchor)
        {
            if (_viewModel == null)
            {
                EndSelectionGroupDrag();
                return;
            }

            if (GetSelectedItemCount() <= 1)
            {
                EndSelectionGroupDrag();
                return;
            }

            _selectionGroupNoteBaseline.Clear();
            foreach (var note in _viewModel.Project.Notes.Where(n => n.IsSelected))
            {
                _selectionGroupNoteBaseline[note] = (note.StartTick, note.Midi, note.PreferTrebleStaff);
            }

            _selectionGroupMarkBaseline.Clear();
            foreach (var mark in _viewModel.Project.ExpressionMarks.Where(m => m.IsSelected))
            {
                _selectionGroupMarkBaseline[mark] = (mark.StartTick, mark.StaffStepOffset);
            }

            _selectionGroupAnchorStartTick = anchor.StartTick;
            _selectionGroupAnchorMidi = anchor.Midi;
            _isDraggingSelectionGroup = _selectionGroupNoteBaseline.Count + _selectionGroupMarkBaseline.Count > 1;
        }

        private void EndSelectionGroupDrag()
        {
            _isDraggingSelectionGroup = false;
            _selectionGroupAnchorStartTick = 0;
            _selectionGroupAnchorMidi = 0;
            _selectionGroupNoteBaseline.Clear();
            _selectionGroupMarkBaseline.Clear();
        }

        private bool TryApplySelectionGroupDrag(Point pointer)
        {
            if (!_isDraggingSelectionGroup || _viewModel == null || _activeNote == null)
            {
                return false;
            }

            int systemIndex = GetSystemIndexFromY((float)pointer.Y);
            bool preferTreble = _activeNote.PreferTrebleStaff
                ?? ShouldPreferTrebleByPosition(systemIndex, _activeNote.Midi, _activeNote.Accidental);
            preferTreble = ResolveDragStaffPreference(preferTreble, (float)pointer.Y, systemIndex);

            int anchorTick = SnapNoteTick(pointer.X, systemIndex, _activeNote);
            int anchorMidi = _activeNote.IsRest
                ? _selectionGroupAnchorMidi
                : YToMidi((float)pointer.Y, systemIndex, _activeNote.Accidental, preferTreble);

            int tickDelta = anchorTick - _selectionGroupAnchorStartTick;
            int midiDelta = anchorMidi - _selectionGroupAnchorMidi;

            foreach (var kvp in _selectionGroupNoteBaseline)
            {
                var note = kvp.Key;
                var baseline = kvp.Value;
                note.StartTick = Math.Max(0, baseline.StartTick + tickDelta);
                if (!note.IsRest)
                {
                    note.Midi = Math.Clamp(baseline.Midi + midiDelta, 24, 108);
                    note.PreferTrebleStaff = preferTreble;
                }
            }

            foreach (var kvp in _selectionGroupMarkBaseline)
            {
                kvp.Key.StartTick = Math.Max(0, kvp.Value.StartTick + tickDelta);
            }

            return true;
        }

        private void UpdateNoteStepPanel()
        {
            if (_viewModel == null || NoteStepPanel == null || StaffCanvas == null)
            {
                return;
            }

            var selectedNotes = _viewModel.Project.Notes.Where(n => n.IsSelected).ToList();
            if (selectedNotes.Count == 0)
            {
                NoteStepPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                var note = selectedNotes
                    .OrderBy(n => n.StartTick)
                    .ThenByDescending(n => n.Midi)
                    .First();
                int systemIndex = GetSystemIndexForTick(note.StartTick);
                float noteX = GetNoteX(note.StartTick);
                if (_staffContentWidth > 0 && noteX > _musicStartX + _staffContentWidth)
                {
                    NoteStepPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    float noteY = GetNoteVisualY(note, systemIndex);

                    double panelWidth = NoteStepPanel.ActualWidth > 0 ? NoteStepPanel.ActualWidth : 36d;
                    double panelHeight = NoteStepPanel.ActualHeight > 0 ? NoteStepPanel.ActualHeight : 168d;

                    double targetX = noteX + Math.Max(_staffGap * 2.3f, 26f);
                    double targetY = noteY - panelHeight * 0.5d;
                    double maxX = Math.Max(0d, StaffCanvas.ActualWidth - panelWidth - 2d);
                    double maxY = Math.Max(0d, StaffCanvas.ActualHeight - panelHeight - 2d);
                    if (targetX > maxX)
                    {
                        targetX = noteX - panelWidth - Math.Max(_staffGap * 1.6f, 16f);
                    }

                    Canvas.SetLeft(NoteStepPanel, Math.Clamp(targetX, 0d, maxX));
                    Canvas.SetTop(NoteStepPanel, Math.Clamp(targetY, 0d, maxY));
                    if (NoteStepUnbeamButton != null)
                    {
                        bool hasBeamGroup = selectedNotes.Any(n => n.BeamGroupId > 0);
                        NoteStepUnbeamButton.Visibility = hasBeamGroup ? Visibility.Visible : Visibility.Collapsed;
                        NoteStepUnbeamButton.IsEnabled = hasBeamGroup;
                    }
                    NoteStepPanel.Visibility = Visibility.Visible;
                }
            }

            if (ExpressionDeletePanel != null)
            {
                var selectedMark = _viewModel.Project.ExpressionMarks
                    .Where(m =>
                    {
                        if (!m.IsSelected) return false;
                        string code = NormalizeExpressionCode(m.Code);
                        return code != "ottava" && code != ScoreMarkRepeatBarline;
                    })
                    .OrderBy(m => m.StartTick)
                    .FirstOrDefault();

                if (selectedMark == null)
                {
                    ExpressionDeletePanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    int markSystem = GetSystemIndexForTick(selectedMark.StartTick);
                    float bottomLineY = GetSystemBottomLineY(markSystem);
                    float markX = GetNoteX(selectedMark.StartTick);
                    float markY = StaffStepOffsetToY(selectedMark.StaffStepOffset, bottomLineY);
                    Rect markBounds = GetExpressionMarkBounds(selectedMark, markX, markY);

                    double panelWidth = ExpressionDeletePanel.ActualWidth > 0 ? ExpressionDeletePanel.ActualWidth : 32d;
                    double panelHeight = ExpressionDeletePanel.ActualHeight > 0 ? ExpressionDeletePanel.ActualHeight : 32d;
                    double targetX = markBounds.X + markBounds.Width + Math.Max(_staffGap * 1.1f, 10f);
                    double targetY = markBounds.Y - panelHeight * 0.22d;
                    double maxX = Math.Max(0d, StaffCanvas.ActualWidth - panelWidth - 2d);
                    double maxY = Math.Max(0d, StaffCanvas.ActualHeight - panelHeight - 2d);

                    Canvas.SetLeft(ExpressionDeletePanel, Math.Clamp(targetX, 0d, maxX));
                    Canvas.SetTop(ExpressionDeletePanel, Math.Clamp(targetY, 0d, maxY));
                    ExpressionDeletePanel.Visibility = Visibility.Visible;
                }
            }

            if (OttavaEditPanel != null)
            {
                var ottavaMark = _viewModel.Project.ExpressionMarks
                    .Where(m => m.IsSelected && NormalizeExpressionCode(m.Code) == "ottava")
                    .OrderBy(m => m.StartTick)
                    .FirstOrDefault();

                if (ottavaMark == null)
                {
                    OttavaEditPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    int startSystem = GetSystemIndexForTick(ottavaMark.StartTick);
                    float startBottom = GetSystemBottomLineY(startSystem);
                    float startX = GetNoteX(ottavaMark.StartTick);
                    float startY = StaffStepOffsetToY(ottavaMark.StaffStepOffset, startBottom);
                    Rect ottavaBounds = GetExpressionMarkBounds(ottavaMark, startX, startY);

                    double panelWidth = OttavaEditPanel.ActualWidth > 0 ? OttavaEditPanel.ActualWidth : 64d;
                    double panelHeight = OttavaEditPanel.ActualHeight > 0 ? OttavaEditPanel.ActualHeight : 32d;
                    double targetX = ottavaBounds.X + ottavaBounds.Width + Math.Max(_staffGap * 1.9f, 20f);
                    double targetY = ottavaBounds.Y - panelHeight * 0.2d;
                    double maxX = Math.Max(0d, StaffCanvas.ActualWidth - panelWidth - 2d);
                    double maxY = Math.Max(0d, StaffCanvas.ActualHeight - panelHeight - 2d);

                    if (OttavaDirectionButton?.Content is TextBlock block)
                    {
                        block.Text = IsOttavaUp(ottavaMark) ? "↓" : "↑";
                    }

                    Canvas.SetLeft(OttavaEditPanel, Math.Clamp(targetX, 0d, maxX));
                    Canvas.SetTop(OttavaEditPanel, Math.Clamp(targetY, 0d, maxY));
                    OttavaEditPanel.Visibility = Visibility.Visible;
                }
            }

            if (RepeatEditPanel != null)
            {
                var repeatMark = _viewModel.Project.ExpressionMarks
                    .Where(m => m.IsSelected && NormalizeExpressionCode(m.Code) == ScoreMarkRepeatBarline)
                    .OrderBy(m => m.StartTick)
                    .FirstOrDefault();

                if (repeatMark == null)
                {
                    RepeatEditPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    int repeatSystem = GetSystemIndexForTick(repeatMark.StartTick);
                    float repeatBottom = GetSystemBottomLineY(repeatSystem);
                    float repeatX = GetNoteX(repeatMark.StartTick);
                    float repeatY = StaffStepOffsetToY(repeatMark.StaffStepOffset, repeatBottom);
                    Rect repeatBounds = GetExpressionMarkBounds(repeatMark, repeatX, repeatY);

                    double panelWidth = RepeatEditPanel.ActualWidth > 0 ? RepeatEditPanel.ActualWidth : 32d;
                    double panelHeight = RepeatEditPanel.ActualHeight > 0 ? RepeatEditPanel.ActualHeight : 32d;
                    double targetX = repeatBounds.X + repeatBounds.Width + Math.Max(_staffGap * 1.2f, 12f);
                    double targetY = repeatBounds.Y - panelHeight * 0.18d;
                    double maxX = Math.Max(0d, StaffCanvas.ActualWidth - panelWidth - 2d);
                    double maxY = Math.Max(0d, StaffCanvas.ActualHeight - panelHeight - 2d);

                    if (RepeatDirectionButton?.Content is TextBlock repeatText)
                    {
                        repeatText.Text = IsStartRepeatBarline(repeatMark) ? "\u2192:" : ":\u2190";
                    }

                    Canvas.SetLeft(RepeatEditPanel, Math.Clamp(targetX, 0d, maxX));
                    Canvas.SetTop(RepeatEditPanel, Math.Clamp(targetY, 0d, maxY));
                    RepeatEditPanel.Visibility = Visibility.Visible;
                }
            }

            UpdateMeasureEditPanelPosition();
            UpdateClefEditPanelPosition();
        }

        private bool IsTitleEditorPoint(Point point)
        {
            if (TitleInlineEditor == null || TitleInlineEditor.Visibility != Visibility.Visible)
            {
                return false;
            }

            double left = Canvas.GetLeft(TitleInlineEditor);
            double top = Canvas.GetTop(TitleInlineEditor);
            double width = Math.Max(1d, TitleInlineEditor.ActualWidth > 0d ? TitleInlineEditor.ActualWidth : TitleInlineEditor.Width);
            double height = Math.Max(1d, TitleInlineEditor.ActualHeight > 0d ? TitleInlineEditor.ActualHeight : 44d);
            return point.X >= left && point.X <= left + width && point.Y >= top && point.Y <= top + height;
        }

        private void ShowTitleInlineEditor()
        {
            if (_viewModel == null || TitleInlineEditor == null || NoteStepOverlayCanvas == null)
            {
                return;
            }

            _isTitleEditing = true;
            TitleInlineEditor.Text = string.IsNullOrWhiteSpace(_viewModel.Title) ? "Untitled" : _viewModel.Title.Trim();
            TitleInlineEditor.Width = Math.Clamp(_titleHitRect.Width * 0.42, 220d, 520d);
            TitleInlineEditor.Height = 44d;
            bool hasChinese = (TitleInlineEditor.Text ?? string.Empty).Any(c => c >= '\u4E00' && c <= '\u9FFF');
            string editorFont = hasChinese ? "Microsoft YaHei UI" : "Times New Roman";
            TitleInlineEditor.FontFamily = new FontFamily(editorFont);

            double x = _titleHitRect.X + (_titleHitRect.Width - TitleInlineEditor.Width) * 0.5;
            double y = _titleHitRect.Y + Math.Max(0.0, (_titleHitRect.Height - 44.0) * 0.5);
            double maxX = Math.Max(0d, StaffCanvas.ActualWidth - TitleInlineEditor.Width - 8d);
            Canvas.SetLeft(TitleInlineEditor, Math.Clamp(x, 0d, maxX));
            Canvas.SetTop(TitleInlineEditor, Math.Max(0d, y));
            TitleInlineEditor.Visibility = Visibility.Visible;
            TitleInlineEditor.SelectAll();
            TitleInlineEditor.Focus(FocusState.Programmatic);
        }

        private void HideTitleInlineEditor(bool commitChanges)
        {
            if (TitleInlineEditor == null)
            {
                _isTitleEditing = false;
                return;
            }

            if (_isTitleEditing && commitChanges && _viewModel != null)
            {
                string updated = (TitleInlineEditor.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(updated))
                {
                    updated = "Untitled";
                }

                if (!string.Equals(updated, _viewModel.Title, StringComparison.Ordinal))
                {
                    _viewModel.Title = updated;
                }
            }

            TitleInlineEditor.Visibility = Visibility.Collapsed;
            _isTitleEditing = false;
        }

        private void TitleInlineEditor_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                HideTitleInlineEditor(commitChanges: true);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                HideTitleInlineEditor(commitChanges: false);
                e.Handled = true;
            }
        }

        private void TitleInlineEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            HideTitleInlineEditor(commitChanges: true);
        }

        private NoteEvent? HitTest(Point pt)
        {
            if (_viewModel == null) return null;

            int ticksPerBeat = GetTicksPerBeat();
            if (ticksPerBeat <= 0) return null;

            for (int i = _viewModel.Project.Notes.Count - 1; i >= 0; i--)
            {
                var note = _viewModel.Project.Notes[i];
                int noteSystem = GetSystemIndexForTick(note.StartTick);
                float x = GetNoteX(note.StartTick);
                if (_staffContentWidth > 0 && x > _musicStartX + _staffContentWidth) continue;
                float y = GetNoteVisualY(note, noteSystem);
                float dx = (float)pt.X - x;
                float dy = (float)pt.Y - y;
                if (dx * dx + dy * dy <= HitRadius * HitRadius)
                {
                    return note;
                }
            }

            return null;
        }

        private ExpressionMark? HitTestExpressionMark(Point pt)
        {
            if (_viewModel == null) return null;

            for (int i = _viewModel.Project.ExpressionMarks.Count - 1; i >= 0; i--)
            {
                var mark = _viewModel.Project.ExpressionMarks[i];
                string code = NormalizeExpressionCode(mark.Code);
                if (code == "ottava")
                {
                    bool hitAnySegment = false;
                    for (int systemIndex = 0; systemIndex < Math.Max(1, _systemCount); systemIndex++)
                    {
                        int measuresInSystem = GetMeasuresInSystem(systemIndex);
                        if (!TryGetOttavaSegmentBounds(mark, systemIndex, measuresInSystem, out Rect segmentBounds))
                        {
                            continue;
                        }

                        if (pt.X >= segmentBounds.X && pt.X <= segmentBounds.X + segmentBounds.Width
                            && pt.Y >= segmentBounds.Y && pt.Y <= segmentBounds.Y + segmentBounds.Height)
                        {
                            hitAnySegment = true;
                            break;
                        }
                    }

                    if (hitAnySegment)
                    {
                        return mark;
                    }
                }
                else
                {
                    int systemIndex = GetSystemIndexForTick(mark.StartTick);
                    float bottomLineY = GetSystemBottomLineY(systemIndex);
                    float x = GetNoteX(mark.StartTick);
                    float y = StaffStepOffsetToY(mark.StaffStepOffset, bottomLineY);
                    Rect bounds = GetExpressionMarkBounds(mark, x, y);

                    if (pt.X >= bounds.X && pt.X <= bounds.X + bounds.Width
                        && pt.Y >= bounds.Y && pt.Y <= bounds.Y + bounds.Height)
                    {
                        return mark;
                    }
                }
            }

            return null;
        }

        private bool TryHitSystemClef(Point pt, out int systemIndex, out bool topStaff, out float anchorX, out float anchorY)
        {
            systemIndex = -1;
            topStaff = true;
            anchorX = 0f;
            anchorY = 0f;
            if (_clefHitTargets.Count == 0)
            {
                return false;
            }

            for (int i = _clefHitTargets.Count - 1; i >= 0; i--)
            {
                var target = _clefHitTargets[i];
                Rect bounds = target.Bounds;
                if (pt.X < bounds.X || pt.X > bounds.X + bounds.Width
                    || pt.Y < bounds.Y || pt.Y > bounds.Y + bounds.Height)
                {
                    continue;
                }

                systemIndex = target.SystemIndex;
                topStaff = target.TopStaff;
                anchorX = target.AnchorX;
                anchorY = target.AnchorY;
                return true;
            }

            return false;
        }

        private bool TryHitNoteOrnament(Point pt, out NoteEvent? note, out bool isGrace)
        {
            note = null;
            isGrace = false;
            if (_viewModel == null || _ornamentHitTargets.Count == 0) return false;

            for (int i = _viewModel.Project.Notes.Count - 1; i >= 0; i--)
            {
                var candidate = _viewModel.Project.Notes[i];
                if (!_ornamentHitTargets.TryGetValue(candidate, out var hit)) continue;
                Rect bounds = hit.Bounds;
                if (pt.X >= bounds.X && pt.X <= bounds.X + bounds.Width
                    && pt.Y >= bounds.Y && pt.Y <= bounds.Y + bounds.Height)
                {
                    note = candidate;
                    isGrace = hit.IsGrace;
                    _activeOrnamentBaseY = hit.BaseY;
                    return true;
                }
            }

            return false;
        }

        private bool TryHitExpressionResizeHandle(Point pt, out ExpressionMark? mark, out ExpressionDragMode mode)
        {
            mark = null;
            mode = ExpressionDragMode.Move;
            if (_viewModel == null) return false;

            float handleRadius = Math.Max(5f, SymbolSizeGap * 0.35f);
            float hitRadius = handleRadius + 4f;
            float hitSq = hitRadius * hitRadius;

            for (int i = _viewModel.Project.ExpressionMarks.Count - 1; i >= 0; i--)
            {
                var candidate = _viewModel.Project.ExpressionMarks[i];
                string code = NormalizeExpressionCode(candidate.Code);
                if (!IsSlurExpression(code) && !IsHairpinExpression(code) && code != "ottava" && code != "ped_line" && !IsScoreEndingExpression(code)) continue;

                int systemIndex = GetSystemIndexForTick(candidate.StartTick);
                float bottomLineY = GetSystemBottomLineY(systemIndex);
                float x = GetNoteX(candidate.StartTick);
                float y = StaffStepOffsetToY(candidate.StaffStepOffset, bottomLineY);
                if (code == "ottava")
                {
                    int endTick = GetOttavaEndTick(candidate);
                    int endSystem = GetSystemIndexForTick(endTick);
                    float handleX = GetNoteX(endTick);
                    float handleY = StaffStepOffsetToY(candidate.StaffStepOffset, GetSystemBottomLineY(endSystem));
                    if (DistanceSquared((float)pt.X, (float)pt.Y, handleX, handleY) <= hitSq)
                    {
                        mark = candidate;
                        mode = ExpressionDragMode.ResizeSpan;
                        return true;
                    }

                    continue;
                }

                if (code == "ped_line")
                {
                    float pedWidth = Math.Max(SymbolSizeGap * 2.4f, _beatWidth * Math.Max(0.8f, candidate.SpanBeats));
                    float handleX = x + pedWidth;
                    float handleY = y;
                    if (DistanceSquared((float)pt.X, (float)pt.Y, handleX, handleY) <= hitSq)
                    {
                        mark = candidate;
                        mode = ExpressionDragMode.ResizeSpan;
                        return true;
                    }

                    continue;
                }

                if (IsScoreEndingExpression(code))
                {
                    if (!TryResolveBarlineAnchorForTick(candidate.StartTick, out int endingSystem, out float startX, preferCurrentSystemAtSystemBoundary: true))
                    {
                        continue;
                    }

                    float endingWidth = Math.Max(_beatWidth * Math.Max(1f, candidate.SpanBeats), _staffGap * 3.2f);
                    float handleX = startX + endingWidth;
                    float handleY = GetSystemTrebleTop(endingSystem) - _staffGap * 2.78f;
                    if (DistanceSquared((float)pt.X, (float)pt.Y, handleX, handleY) <= hitSq)
                    {
                        mark = candidate;
                        mode = ExpressionDragMode.ResizeSpan;
                        return true;
                    }

                    continue;
                }

                if (IsHairpinExpression(code))
                {
                    float hairpinWidth = Math.Max(SymbolSizeGap * 3f, _beatWidth * Math.Max(0.8f, candidate.SpanBeats));
                    float hairpinSpanHandleX = x + hairpinWidth;
                    float hairpinSpanHandleY = y;
                    if (DistanceSquared((float)pt.X, (float)pt.Y, hairpinSpanHandleX, hairpinSpanHandleY) <= hitSq)
                    {
                        mark = candidate;
                        mode = ExpressionDragMode.ResizeSpan;
                        return true;
                    }

                    continue;
                }

                var (width, arch, direction, slopeDelta) = GetSlurGeometry(candidate);
                float spanHandleX = x + width;
                float spanHandleY = y + slopeDelta;
                float heightHandleX = x + width;
                float heightHandleY = y + slopeDelta + direction * arch;
                float slopeHandleX = x + width * 0.5f;
                float slopeHandleY = y + slopeDelta * 0.5f;

                if (DistanceSquared((float)pt.X, (float)pt.Y, slopeHandleX, slopeHandleY) <= hitSq)
                {
                    mark = candidate;
                    mode = ExpressionDragMode.ResizeSlope;
                    return true;
                }

                if (DistanceSquared((float)pt.X, (float)pt.Y, heightHandleX, heightHandleY) <= hitSq)
                {
                    mark = candidate;
                    mode = ExpressionDragMode.ResizeHeight;
                    return true;
                }

                if (DistanceSquared((float)pt.X, (float)pt.Y, spanHandleX, spanHandleY) <= hitSq)
                {
                    mark = candidate;
                    mode = ExpressionDragMode.ResizeSpan;
                    return true;
                }
            }

            return false;
        }

        private static float DistanceSquared(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx;
            float dy = ay - by;
            return dx * dx + dy * dy;
        }

        private int GetMeasureTicks()
        {
            int ticksPerBeat = _ticksPerBeat > 0 ? _ticksPerBeat : GetTicksPerBeat();
            return Math.Max(1, Math.Max(1, _beatsPerBar) * Math.Max(1, ticksPerBeat));
        }

        private List<TimeSignatureChange> GetNormalizedTimeSignatureChanges()
        {
            if (_viewModel == null)
            {
                return new List<TimeSignatureChange> { new TimeSignatureChange { Tick = 0, Numerator = 4, Denominator = 4 } };
            }

            var list = new List<TimeSignatureChange>
            {
                new TimeSignatureChange
                {
                    Tick = 0,
                    Numerator = Math.Clamp(_viewModel.Project.TimeSignature.Numerator, 1, 12),
                    Denominator = _viewModel.Project.TimeSignature.Denominator is 1 or 2 or 4 or 8 or 16
                        ? _viewModel.Project.TimeSignature.Denominator
                        : 4
                }
            };

            foreach (var change in _viewModel.Project.TimeSignatureChanges)
            {
                if (change == null) continue;
                int tick = Math.Max(0, change.Tick);
                int numerator = Math.Clamp(change.Numerator, 1, 12);
                int denominator = change.Denominator is 1 or 2 or 4 or 8 or 16 ? change.Denominator : 4;
                if (tick == 0)
                {
                    list[0].Numerator = numerator;
                    list[0].Denominator = denominator;
                }
                else
                {
                    list.Add(new TimeSignatureChange
                    {
                        Tick = tick,
                        Numerator = numerator,
                        Denominator = denominator
                    });
                }
            }

            return list
                .OrderBy(c => c.Tick)
                .ThenBy(c => c.Numerator)
                .GroupBy(c => c.Tick)
                .Select(g => g.Last())
                .ToList();
        }

        private List<KeySignatureChange> GetNormalizedKeySignatureChanges()
        {
            if (_viewModel == null)
            {
                return new List<KeySignatureChange> { new KeySignatureChange { Tick = 0, Fifths = 0, Mode = KeyMode.Major } };
            }

            var list = new List<KeySignatureChange>
            {
                new KeySignatureChange
                {
                    Tick = 0,
                    Fifths = Math.Clamp(_viewModel.Project.KeySignature.Fifths, -7, 7),
                    Mode = _viewModel.Project.KeySignature.Mode
                }
            };

            foreach (var change in _viewModel.Project.KeySignatureChanges)
            {
                if (change == null) continue;
                int tick = Math.Max(0, change.Tick);
                int fifths = Math.Clamp(change.Fifths, -7, 7);
                if (tick == 0)
                {
                    list[0].Fifths = fifths;
                    list[0].Mode = change.Mode;
                }
                else
                {
                    list.Add(new KeySignatureChange
                    {
                        Tick = tick,
                        Fifths = fifths,
                        Mode = change.Mode
                    });
                }
            }

            return list
                .OrderBy(c => c.Tick)
                .ThenBy(c => c.Fifths)
                .GroupBy(c => c.Tick)
                .Select(g => g.Last())
                .ToList();
        }

        private TimeSignature GetEffectiveTimeSignatureAtTick(int tick)
        {
            var changes = GetNormalizedTimeSignatureChanges();
            int safeTick = Math.Max(0, tick);
            TimeSignatureChange active = changes[0];
            for (int i = 1; i < changes.Count; i++)
            {
                if (changes[i].Tick <= safeTick)
                {
                    active = changes[i];
                }
                else
                {
                    break;
                }
            }

            return new TimeSignature(active.Numerator, active.Denominator);
        }

        private int GetEffectiveKeySignatureFifthsAtTick(int tick)
        {
            var changes = GetNormalizedKeySignatureChanges();
            int safeTick = Math.Max(0, tick);
            KeySignatureChange active = changes[0];
            for (int i = 1; i < changes.Count; i++)
            {
                if (changes[i].Tick <= safeTick)
                {
                    active = changes[i];
                }
                else
                {
                    break;
                }
            }

            return Math.Clamp(active.Fifths, -7, 7);
        }

        private void RebuildMeasureTickBoundaries(int measureCount)
        {
            if (_viewModel == null)
            {
                _measureTickBoundaries.Clear();
                _measureTickBoundaries.Add(0);
                _measureTickBoundaries.Add(GetMeasureTicks());
                return;
            }

            int targetMeasures = Math.Max(1, measureCount);
            _measureTickBoundaries.Clear();
            _measureTickBoundaries.Add(0);
            int cursorTick = 0;
            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            for (int i = 0; i < targetMeasures; i++)
            {
                TimeSignature ts = GetEffectiveTimeSignatureAtTick(cursorTick);
                int ticks = Math.Max(1, ts.TicksPerMeasure(ppq));
                cursorTick += ticks;
                _measureTickBoundaries.Add(cursorTick);
            }
        }

        private void EnsureMeasureTickBoundariesForTick(int tick)
        {
            if (_viewModel == null) return;

            int safeTick = Math.Max(0, tick);
            if (_measureTickBoundaries.Count == 0)
            {
                RebuildMeasureTickBoundaries(Math.Max(1, _totalMeasureCount));
            }

            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            while (_measureTickBoundaries.Count > 0 && _measureTickBoundaries[^1] <= safeTick)
            {
                int cursorTick = _measureTickBoundaries[^1];
                TimeSignature ts = GetEffectiveTimeSignatureAtTick(cursorTick);
                int ticks = Math.Max(1, ts.TicksPerMeasure(ppq));
                _measureTickBoundaries.Add(cursorTick + ticks);
            }
        }

        private int GetMeasureBoundaryTick(int boundaryIndex)
        {
            int safeBoundary = Math.Max(0, boundaryIndex);
            if (_measureTickBoundaries.Count == 0)
            {
                RebuildMeasureTickBoundaries(Math.Max(1, _totalMeasureCount));
            }

            while (_measureTickBoundaries.Count <= safeBoundary)
            {
                EnsureMeasureTickBoundariesForTick(_measureTickBoundaries[^1]);
            }

            return _measureTickBoundaries[safeBoundary];
        }

        private int GetMeasureTickLengthByIndex(int measureIndex)
        {
            int safeIndex = Math.Max(0, measureIndex);
            int start = GetMeasureBoundaryTick(safeIndex);
            int end = GetMeasureBoundaryTick(safeIndex + 1);
            return Math.Max(1, end - start);
        }

        private int GetMeasureIndexByTick(int tick)
        {
            int safeTick = Math.Max(0, tick);
            EnsureMeasureTickBoundariesForTick(safeTick);

            int low = 0;
            int high = Math.Max(0, _measureTickBoundaries.Count - 2);
            int best = 0;
            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                int start = _measureTickBoundaries[mid];
                int end = _measureTickBoundaries[mid + 1];
                if (safeTick < start)
                {
                    high = mid - 1;
                }
                else if (safeTick >= end)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    return mid;
                }
            }

            return best;
        }

        private int GetNearestMeasureBoundaryIndexForTick(int tick)
        {
            int safeTick = Math.Max(0, tick);
            EnsureMeasureTickBoundariesForTick(safeTick);
            int measureIndex = GetMeasureIndexByTick(safeTick);
            int left = GetMeasureBoundaryTick(measureIndex);
            int right = GetMeasureBoundaryTick(measureIndex + 1);
            return Math.Abs(safeTick - left) <= Math.Abs(right - safeTick)
                ? measureIndex
                : measureIndex + 1;
        }

        private int GetContentMeasureCount(int measureTicks)
        {
            if (_viewModel == null) return 1;

            int safeMeasureTicks = Math.Max(1, measureTicks);
            int maxNoteTick = _viewModel.Project.Notes.Count == 0
                ? 0
                : _viewModel.Project.Notes.Max(n => Math.Max(0, n.StartTick));
            int maxExpressionTick = _viewModel.Project.ExpressionMarks.Count == 0
                ? 0
                : _viewModel.Project.ExpressionMarks.Max(m =>
                {
                    int tick = Math.Max(0, m.StartTick);
                    string code = NormalizeExpressionCode(m.Code);
                    if (tick > 0 && IsBarlineAnchoredScoreMark(code))
                    {
                        tick--;
                    }

                    return tick;
                });
            int maxTimeSigTick = _viewModel.Project.TimeSignatureChanges.Count == 0
                ? 0
                : _viewModel.Project.TimeSignatureChanges.Max(c => Math.Max(0, c.Tick));
            int maxKeySigTick = _viewModel.Project.KeySignatureChanges.Count == 0
                ? 0
                : _viewModel.Project.KeySignatureChanges.Max(c => Math.Max(0, c.Tick));
            int targetTick = Math.Max(safeMeasureTicks, Math.Max(Math.Max(maxNoteTick + 1, maxExpressionTick + 1), Math.Max(maxTimeSigTick + 1, maxKeySigTick + 1)));

            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            int count = 0;
            int cursor = 0;
            while (cursor < targetTick && count < 8192)
            {
                TimeSignature ts = GetEffectiveTimeSignatureAtTick(cursor);
                int ticks = Math.Max(1, ts.TicksPerMeasure(ppq));
                cursor += ticks;
                count++;
            }

            return Math.Max(1, count);
        }

        private bool TryHitMeasureBarline(Point pt, out int measureIndex, out int systemIndex, out int localBoundaryIndex, out float barlineX, out float trebleTop)
        {
            measureIndex = -1;
            systemIndex = -1;
            localBoundaryIndex = -1;
            barlineX = 0f;
            trebleTop = 0f;

            if (_measureWidth <= 0f || _systemCount <= 0) return false;

            systemIndex = GetSystemIndexFromY((float)pt.Y);
            trebleTop = GetSystemTrebleTop(systemIndex);
            float bassBottom = GetSystemBassBottom(systemIndex);
            if (pt.Y < trebleTop - _staffGap || pt.Y > bassBottom + _staffGap) return false;

            int measuresInSystem = GetMeasuresInSystem(systemIndex);
            float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
            float systemStartX = boundaries[0];
            float totalSystemWidth = boundaries[Math.Max(1, measuresInSystem)] - systemStartX;
            float localX = (float)pt.X - systemStartX;
            if (localX < -_staffGap * 0.7f || localX > totalSystemWidth + _staffGap * 0.7f) return false;

            int boundary = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i <= measuresInSystem; i++)
            {
                float dist = Math.Abs((float)pt.X - boundaries[i]);
                if (dist < nearest)
                {
                    nearest = dist;
                    boundary = i;
                }
            }

            boundary = Math.Clamp(boundary, 0, Math.Max(1, measuresInSystem));
            barlineX = boundaries[boundary];
            localBoundaryIndex = boundary;
            float tolerance = Math.Max(6f, _staffGap * 0.55f);
            if (Math.Abs((float)pt.X - barlineX) > tolerance) return false;

            int boundaryGlobal = GetSystemStartMeasureIndex(systemIndex) + boundary;
            measureIndex = boundary == 0 ? boundaryGlobal : boundaryGlobal - 1;
            return measureIndex >= 0;
        }

        private bool TryHitDraggableBarline(Point pt, out int systemIndex, out int localBoundaryIndex, out float barlineX)
        {
            systemIndex = -1;
            localBoundaryIndex = -1;
            barlineX = 0f;

            if (_measureWidth <= 0f || _systemCount <= 0) return false;

            int hitSystem = GetSystemIndexFromY((float)pt.Y);
            float trebleTop = GetSystemTrebleTop(hitSystem);
            float bassBottom = GetSystemBassBottom(hitSystem);
            if (pt.Y < trebleTop - _staffGap || pt.Y > bassBottom + _staffGap) return false;

            int measuresInSystem = GetMeasuresInSystem(hitSystem);
            if (measuresInSystem <= 0) return false;

            float[] boundaries = GetSystemBarlinePositions(hitSystem, measuresInSystem);
            float tolerance = Math.Max(6f, _staffGap * 0.6f);
            for (int i = 1; i <= measuresInSystem; i++)
            {
                if (Math.Abs((float)pt.X - boundaries[i]) <= tolerance)
                {
                    systemIndex = hitSystem;
                    localBoundaryIndex = i;
                    barlineX = boundaries[i];
                    return true;
                }
            }

            return false;
        }

        private void ShowMeasureEditPanel(int measureIndex, int systemIndex, int localBoundaryIndex, float barlineX, float trebleTop)
        {
            _measurePanelMeasureIndex = Math.Max(0, measureIndex);
            _measurePanelSystemIndex = Math.Max(0, systemIndex);
            _measurePanelBoundaryLocalIndex = Math.Max(0, localBoundaryIndex);
            _measurePanelBarlineX = barlineX;
            _measurePanelTrebleTop = trebleTop;
            _highlightBarlineSystemIndex = _measurePanelSystemIndex;
            _highlightBarlineLocalIndex = _measurePanelBoundaryLocalIndex;
            if (MeasureAddSystemButton != null)
            {
                int measuresInSystem = GetMeasuresInSystem(_measurePanelSystemIndex);
                MeasureAddSystemButton.Visibility = _measurePanelBoundaryLocalIndex >= measuresInSystem ? Visibility.Visible : Visibility.Collapsed;
            }
            if (MeasureDeleteSystemButton != null)
            {
                int measuresInSystem = GetMeasuresInSystem(_measurePanelSystemIndex);
                bool atSystemEndBoundary = _measurePanelBoundaryLocalIndex >= measuresInSystem;
                MeasureDeleteSystemButton.Visibility = (_systemCount > 1 && atSystemEndBoundary)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            UpdateMeasureEditPanelPosition();
        }

        private void HideMeasureEditPanel()
        {
            _measurePanelMeasureIndex = -1;
            _measurePanelSystemIndex = -1;
            _measurePanelBoundaryLocalIndex = -1;
            _highlightBarlineSystemIndex = -1;
            _highlightBarlineLocalIndex = -1;
            if (MeasureEditPanel != null)
            {
                MeasureEditPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateMeasureEditPanelPosition()
        {
            if (MeasureEditPanel == null || StaffCanvas == null) return;
            if (_measurePanelMeasureIndex < 0 || _measurePanelSystemIndex < 0)
            {
                MeasureEditPanel.Visibility = Visibility.Collapsed;
                return;
            }

            double panelWidth = MeasureEditPanel.ActualWidth > 0 ? MeasureEditPanel.ActualWidth : 132d;
            double panelHeight = MeasureEditPanel.ActualHeight > 0 ? MeasureEditPanel.ActualHeight : 34d;
            double targetX = _measurePanelBarlineX - panelWidth * 0.5d;
            double targetY = _measurePanelTrebleTop - panelHeight - Math.Max(_staffGap * 0.35f, 4f);
            double maxX = Math.Max(0d, StaffCanvas.ActualWidth - panelWidth - 2d);
            double maxY = Math.Max(0d, StaffCanvas.ActualHeight - panelHeight - 2d);

            Canvas.SetLeft(MeasureEditPanel, Math.Clamp(targetX, 0d, maxX));
            Canvas.SetTop(MeasureEditPanel, Math.Clamp(targetY, 0d, maxY));
            MeasureEditPanel.Visibility = Visibility.Visible;
        }

        private void ShowClefEditPanel(int systemIndex, bool topStaff, float anchorX, float anchorY)
        {
            _clefPanelSystemIndex = Math.Max(0, systemIndex);
            _clefPanelTopStaff = topStaff;
            _clefPanelAnchorX = anchorX;
            _clefPanelAnchorY = anchorY;
            UpdateClefEditPanelButtons();
            UpdateClefEditPanelPosition();
        }

        private void HideClefEditPanel()
        {
            _clefPanelSystemIndex = -1;
            if (ClefEditPanel != null)
            {
                ClefEditPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateClefEditPanelButtons()
        {
            if (_clefPanelSystemIndex < 0)
            {
                return;
            }

            StaffClefType current = GetSystemStaffClefType(_clefPanelSystemIndex, _clefPanelTopStaff);
            if (ClefSetTrebleButton != null)
            {
                bool selected = current == StaffClefType.Treble;
                ClefSetTrebleButton.Background = selected ? new SolidColorBrush(Color.FromArgb(255, 219, 234, 254)) : new SolidColorBrush(Color.FromArgb(204, 255, 255, 255));
                ClefSetTrebleButton.BorderBrush = selected ? new SolidColorBrush(GetAccentColor()) : new SolidColorBrush(Color.FromArgb(42, 0, 0, 0));
                ClefSetTrebleButton.IsEnabled = !selected;
            }

            if (ClefSetBassButton != null)
            {
                bool selected = current == StaffClefType.Bass;
                ClefSetBassButton.Background = selected ? new SolidColorBrush(Color.FromArgb(255, 219, 234, 254)) : new SolidColorBrush(Color.FromArgb(204, 255, 255, 255));
                ClefSetBassButton.BorderBrush = selected ? new SolidColorBrush(GetAccentColor()) : new SolidColorBrush(Color.FromArgb(42, 0, 0, 0));
                ClefSetBassButton.IsEnabled = !selected;
            }
        }

        private void UpdateClefEditPanelPosition()
        {
            if (ClefEditPanel == null || StaffCanvas == null)
            {
                return;
            }

            if (_clefPanelSystemIndex < 0)
            {
                ClefEditPanel.Visibility = Visibility.Collapsed;
                return;
            }

            double panelWidth = ClefEditPanel.ActualWidth > 0 ? ClefEditPanel.ActualWidth : 142d;
            double panelHeight = ClefEditPanel.ActualHeight > 0 ? ClefEditPanel.ActualHeight : 32d;
            double targetX = _clefPanelAnchorX + Math.Max(_staffGap * 2.3f, 30f);
            double targetY = _clefPanelAnchorY - panelHeight - Math.Max(_staffGap * 1.2f, 14f);
            double maxX = Math.Max(0d, StaffCanvas.ActualWidth - panelWidth - 2d);
            double maxY = Math.Max(0d, StaffCanvas.ActualHeight - panelHeight - 2d);
            if (targetX > maxX)
            {
                targetX = _clefPanelAnchorX - panelWidth - Math.Max(_staffGap * 1.6f, 18f);
            }

            Canvas.SetLeft(ClefEditPanel, Math.Clamp(targetX, 0d, maxX));
            Canvas.SetTop(ClefEditPanel, Math.Clamp(targetY, 0d, maxY));
            ClefEditPanel.Visibility = Visibility.Visible;
        }

        private void ApplyClefPanelSelection(StaffClefType clef)
        {
            if (_clefPanelSystemIndex < 0 || _viewModel == null)
            {
                return;
            }

            StaffClefType current = GetSystemStaffClefType(_clefPanelSystemIndex, _clefPanelTopStaff);
            if (current == clef)
            {
                return;
            }

            SetSystemStaffClefType(_clefPanelSystemIndex, _clefPanelTopStaff, clef);
            MarkProjectChanged();
            UpdateClefEditPanelButtons();
            StaffCanvas.Invalidate();
        }

        private void ClefSetTrebleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyClefPanelSelection(StaffClefType.Treble);
        }

        private void ClefSetBassButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyClefPanelSelection(StaffClefType.Bass);
        }

        private bool IsMeasureEmpty(int measureIndex)
        {
            if (_viewModel == null || measureIndex < 0)
            {
                return true;
            }

            int startTick = GetMeasureBoundaryTick(measureIndex);
            int endTick = GetMeasureBoundaryTick(measureIndex + 1);
            bool hasNotes = _viewModel.Project.Notes.Any(n => n.StartTick >= startTick && n.StartTick < endTick);
            bool hasMarks = _viewModel.Project.ExpressionMarks.Any(m => m.StartTick >= startTick && m.StartTick < endTick);
            bool hasTimeSig = _viewModel.Project.TimeSignatureChanges.Any(c => c.Tick >= startTick && c.Tick < endTick);
            bool hasKeySig = _viewModel.Project.KeySignatureChanges.Any(c => c.Tick >= startTick && c.Tick < endTick);
            return !(hasNotes || hasMarks || hasTimeSig || hasKeySig);
        }

        private void ExpandSystemByOneMeasure(int systemIndex)
        {
            int safeSystem = Math.Clamp(systemIndex, 0, Math.Max(0, _systemCount - 1));
            int systemStart = GetSystemStartMeasureIndex(safeSystem);
            int systemCount = GetMeasuresInSystem(safeSystem);
            int insertAfterMeasure = Math.Max(0, systemStart + systemCount - 1);
            InsertMeasureAfter(insertAfterMeasure);

            while (_systemMeasureCounts.Count <= safeSystem)
            {
                _systemMeasureCounts.Add(Math.Max(1, _measuresPerSystem));
            }

            _systemMeasureCounts[safeSystem] = Math.Max(1, _systemMeasureCounts[safeSystem] + 1);
        }

        private bool TryExpandSystemByPullingEndBarline(int systemIndex, float targetBoundaryX)
        {
            int safeSystem = Math.Clamp(systemIndex, 0, Math.Max(0, _systemCount - 1));
            int oldCount = GetMeasuresInSystem(safeSystem);
            if (oldCount <= 0)
            {
                return false;
            }

            float[] oldBoundaries = GetSystemBarlinePositions(safeSystem, oldCount);
            float finalX = oldBoundaries[Math.Max(1, oldCount)];
            float prevX = oldBoundaries[Math.Max(0, oldCount - 1)];
            float minMeasureWidth = GetEndBoundarySplitMinWidth(prevX, finalX);
            float left = prevX + minMeasureWidth;
            float right = finalX - minMeasureWidth;
            if (right < left)
            {
                float center = (left + right) * 0.5f;
                left = center;
                right = center;
            }

            float desiredX = Math.Clamp(targetBoundaryX, left, right);

            ExpandSystemByOneMeasure(safeSystem);

            int newCount = GetMeasuresInSystem(safeSystem);
            if (newCount <= oldCount)
            {
                return false;
            }

            // Preserve existing interior barline positions and insert the new boundary at the dragged location.
            for (int i = 1; i < oldCount; i++)
            {
                SetSystemBoundaryAbsolutePosition(safeSystem, i, newCount, oldBoundaries[i]);
            }

            int insertedBoundary = oldCount;
            SetSystemBoundaryAbsolutePosition(safeSystem, insertedBoundary, newCount, desiredX);
            RemoveStaleBarlineOffsetsForSystem(safeSystem, newCount);
            return true;
        }

        private bool TryShrinkSystemByOneEmptyMeasure(int systemIndex)
        {
            int safeSystem = Math.Clamp(systemIndex, 0, Math.Max(0, _systemCount - 1));
            int systemCount = GetMeasuresInSystem(safeSystem);
            if (systemCount <= 1)
            {
                return false;
            }

            int systemStart = GetSystemStartMeasureIndex(safeSystem);
            int lastMeasureIndex = systemStart + systemCount - 1;
            if (!IsMeasureEmpty(lastMeasureIndex))
            {
                return false;
            }

            DeleteMeasure(lastMeasureIndex);
            while (_systemMeasureCounts.Count <= safeSystem)
            {
                _systemMeasureCounts.Add(Math.Max(1, _measuresPerSystem));
            }

            _systemMeasureCounts[safeSystem] = Math.Max(1, _systemMeasureCounts[safeSystem] - 1);
            RemoveStaleBarlineOffsetsForSystem(safeSystem, _systemMeasureCounts[safeSystem]);
            return true;
        }

        private void InsertMeasureAfter(int measureIndex)
        {
            if (_viewModel == null || measureIndex < 0) return;

            int measureTicks = GetMeasureTicks();
            int currentMeasureCount = Math.Max(GetContentMeasureCount(measureTicks), Math.Max(1, _manualMeasureCount));
            int insertBoundary = Math.Max(0, measureIndex + 1);
            int insertTick = GetMeasureBoundaryTick(insertBoundary);
            int insertLengthTicks = GetMeasureTickLengthByIndex(insertBoundary);
            foreach (var note in _viewModel.Project.Notes)
            {
                if (note.StartTick >= insertTick)
                {
                    note.StartTick += insertLengthTicks;
                }
            }

            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                if (mark.StartTick >= insertTick)
                {
                    mark.StartTick += insertLengthTicks;
                }
            }

            foreach (var ts in _viewModel.Project.TimeSignatureChanges)
            {
                if (ts.Tick >= insertTick)
                {
                    ts.Tick += insertLengthTicks;
                }
            }

            foreach (var ks in _viewModel.Project.KeySignatureChanges)
            {
                if (ks.Tick >= insertTick)
                {
                    ks.Tick += insertLengthTicks;
                }
            }

            int newMeasureCount = Math.Max(currentMeasureCount + 1, measureIndex + 2);
            _manualMeasureCount = Math.Max(_manualMeasureCount, newMeasureCount);
            MarkProjectChanged();
        }

        private void DeleteMeasure(int measureIndex)
        {
            if (_viewModel == null || measureIndex < 0) return;

            int measureTicks = GetMeasureTicks();
            int currentMeasureCount = Math.Max(GetContentMeasureCount(measureTicks), Math.Max(1, _manualMeasureCount));
            int startTick = GetMeasureBoundaryTick(measureIndex);
            int endTick = GetMeasureBoundaryTick(measureIndex + 1);
            int removedTicks = Math.Max(1, endTick - startTick);

            _viewModel.Project.Notes.RemoveAll(n => n.StartTick >= startTick && n.StartTick < endTick);
            foreach (var note in _viewModel.Project.Notes)
            {
                if (note.StartTick >= endTick)
                {
                    note.StartTick = Math.Max(0, note.StartTick - removedTicks);
                }
            }

            _viewModel.Project.ExpressionMarks.RemoveAll(m => m.StartTick >= startTick && m.StartTick < endTick);
            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                if (mark.StartTick >= endTick)
                {
                    mark.StartTick = Math.Max(0, mark.StartTick - removedTicks);
                }
            }

            _viewModel.Project.TimeSignatureChanges.RemoveAll(c => c.Tick >= startTick && c.Tick < endTick);
            foreach (var ts in _viewModel.Project.TimeSignatureChanges)
            {
                if (ts.Tick >= endTick)
                {
                    ts.Tick = Math.Max(0, ts.Tick - removedTicks);
                }
            }

            _viewModel.Project.KeySignatureChanges.RemoveAll(c => c.Tick >= startTick && c.Tick < endTick);
            foreach (var ks in _viewModel.Project.KeySignatureChanges)
            {
                if (ks.Tick >= endTick)
                {
                    ks.Tick = Math.Max(0, ks.Tick - removedTicks);
                }
            }

            int reducedMeasureCount = Math.Max(1, currentMeasureCount - 1);
            int contentMeasureCount = GetContentMeasureCount(measureTicks);
            _manualMeasureCount = Math.Max(contentMeasureCount, reducedMeasureCount);
            MarkProjectChanged();
        }

        private void DeleteSystemAt(int systemIndex)
        {
            if (_viewModel == null) return;
            if (_systemCount <= 1) return;

            int safeSystem = Math.Clamp(systemIndex, 0, Math.Max(0, _systemCount - 1));
            int measureTicks = GetMeasureTicks();
            int removedSystemMeasures = GetMeasuresInSystem(safeSystem);
            int startMeasureBoundary = GetSystemStartMeasureIndex(safeSystem);
            int endMeasureBoundary = startMeasureBoundary + removedSystemMeasures;
            int startTick = GetMeasureBoundaryTick(startMeasureBoundary);
            int endTick = GetMeasureBoundaryTick(endMeasureBoundary);
            int systemTicks = Math.Max(1, endTick - startTick);

            _viewModel.Project.Notes.RemoveAll(n => n.StartTick >= startTick && n.StartTick < endTick);
            foreach (var note in _viewModel.Project.Notes)
            {
                if (note.StartTick >= endTick)
                {
                    note.StartTick = Math.Max(0, note.StartTick - systemTicks);
                }
            }

            _viewModel.Project.ExpressionMarks.RemoveAll(m => m.StartTick >= startTick && m.StartTick < endTick);
            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                if (mark.StartTick >= endTick)
                {
                    mark.StartTick = Math.Max(0, mark.StartTick - systemTicks);
                }
            }

            _viewModel.Project.TimeSignatureChanges.RemoveAll(c => c.Tick >= startTick && c.Tick < endTick);
            foreach (var ts in _viewModel.Project.TimeSignatureChanges)
            {
                if (ts.Tick >= endTick)
                {
                    ts.Tick = Math.Max(0, ts.Tick - systemTicks);
                }
            }

            _viewModel.Project.KeySignatureChanges.RemoveAll(c => c.Tick >= startTick && c.Tick < endTick);
            foreach (var ks in _viewModel.Project.KeySignatureChanges)
            {
                if (ks.Tick >= endTick)
                {
                    ks.Tick = Math.Max(0, ks.Tick - systemTicks);
                }
            }

            if (_barlineOffsets.Count > 0)
            {
                var shifted = new Dictionary<int, float>();
                foreach (var kvp in _barlineOffsets)
                {
                    int keySystem = kvp.Key / 1000;
                    int local = kvp.Key % 1000;
                    if (keySystem == safeSystem)
                    {
                        continue;
                    }

                    int mappedSystem = keySystem > safeSystem ? keySystem - 1 : keySystem;
                    shifted[GetBarlineOffsetKey(mappedSystem, local)] = kvp.Value;
                }

                _barlineOffsets.Clear();
                foreach (var kvp in shifted)
                {
                    _barlineOffsets[kvp.Key] = kvp.Value;
                }
            }

            ShiftStaffClefMappingsAfterSystemDelete(safeSystem);
            if (_systemMeasureCounts.Count > safeSystem)
            {
                _systemMeasureCounts.RemoveAt(safeSystem);
                if (_systemMeasureCounts.Count == 0)
                {
                    _systemMeasureCounts.Add(Math.Max(1, _measuresPerSystem));
                }
            }

            if (_manualAdditionalSystems > 0)
            {
                _manualAdditionalSystems--;
            }

            int currentMeasureCount = Math.Max(GetContentMeasureCount(measureTicks), Math.Max(1, _manualMeasureCount));
            int reducedMeasureCount = Math.Max(1, currentMeasureCount - removedSystemMeasures);
            int contentMeasureCount = GetContentMeasureCount(measureTicks);
            _manualMeasureCount = Math.Max(contentMeasureCount, reducedMeasureCount);

            MarkProjectChanged();
        }

        private void ShiftStaffClefMappingsAfterSystemDelete(int removedSystemIndex)
        {
            if (_viewModel?.Project.StaffClefs == null || _viewModel.Project.StaffClefs.Count == 0)
            {
                return;
            }

            var shifted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _viewModel.Project.StaffClefs)
            {
                string[] parts = kvp.Key.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int keySystem))
                {
                    continue;
                }

                if (keySystem == removedSystemIndex)
                {
                    continue;
                }

                int mappedSystem = keySystem > removedSystemIndex ? keySystem - 1 : keySystem;
                string mappedKey = GetStaffClefKey(mappedSystem, string.Equals(parts[1], "top", StringComparison.OrdinalIgnoreCase));
                shifted[mappedKey] = kvp.Value;
            }

            _viewModel.Project.StaffClefs.Clear();
            foreach (var kvp in shifted)
            {
                _viewModel.Project.StaffClefs[kvp.Key] = kvp.Value;
            }
        }

        private NoteEvent? AddNote(Point pt)
        {
            if (_viewModel == null) throw new InvalidOperationException("ViewModel is not ready.");

            int systemIndex = GetSystemIndexFromY((float)pt.Y);
            bool preferTreble = GetPreferredStaffFromPointerY((float)pt.Y, systemIndex);
            int midi = YToMidi((float)pt.Y, systemIndex, _pendingAccidental, preferTreble);
            int startTick = SnapNoteTick(pt.X, systemIndex, null);
            if (IsBarlineTick(startTick))
            {
                return null;
            }
            int baseDuration = NoteLengthUtils.ToTicks(_viewModel.SelectedNoteLength, _viewModel.Project.Ppq);
            if (baseDuration <= 0) return null;
            int duration = _pendingAugmentationDot ? baseDuration + baseDuration / 2 : baseDuration;
            bool isRest = _isRestInputMode;

            var note = new NoteEvent
            {
                Midi = isRest ? 60 : midi,
                StartTick = startTick,
                BaseDurationTicks = baseDuration,
                DurationTicks = duration,
                AugmentationDots = _pendingAugmentationDot ? 1 : 0,
                IsRest = isRest,
                Voice = 1,
                Accidental = isRest ? NoteAccidental.None : _pendingAccidental,
                IsStaccato = !isRest && _pendingStaccato,
                IsStaccatissimo = !isRest && _pendingStaccatissimo,
                IsAccent = !isRest && _pendingAccent,
                Ornament = isRest ? NoteOrnament.None : _pendingOrnament,
                PreferTrebleStaff = preferTreble,
                IsSelected = false
            };

            _viewModel.Project.Notes.Add(note);
            MarkProjectChanged();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
            ResetNoteTypeControlsForNewInput();
            return note;
        }

        private static bool TryParseNoteOrnamentTag(string? tag, out NoteOrnament ornament)
        {
            ornament = NoteOrnament.None;
            if (string.IsNullOrWhiteSpace(tag)) return true;
            return Enum.TryParse(tag, ignoreCase: true, out ornament);
        }

        private NoteOrnament GetSelectedOrnamentFromControls()
        {
            if (OrnamentTrillMenuItem?.IsChecked == true) return NoteOrnament.Trill;
            if (OrnamentUpperMordentMenuItem?.IsChecked == true) return NoteOrnament.UpperMordent;
            if (OrnamentLowerMordentMenuItem?.IsChecked == true) return NoteOrnament.LowerMordent;
            if (OrnamentTurnMenuItem?.IsChecked == true) return NoteOrnament.Turn;
            if (OrnamentInvertedTurnMenuItem?.IsChecked == true) return NoteOrnament.InvertedTurn;
            if (OrnamentAppoggiaturaMenuItem?.IsChecked == true) return NoteOrnament.Appoggiatura;
            if (OrnamentAcciaccaturaMenuItem?.IsChecked == true) return NoteOrnament.Acciaccatura;
            return NoteOrnament.None;
        }

        private void ClearOrnamentSelection()
        {
            if (OrnamentNoneMenuItem != null) OrnamentNoneMenuItem.IsChecked = false;
            if (OrnamentTrillMenuItem != null) OrnamentTrillMenuItem.IsChecked = false;
            if (OrnamentUpperMordentMenuItem != null) OrnamentUpperMordentMenuItem.IsChecked = false;
            if (OrnamentLowerMordentMenuItem != null) OrnamentLowerMordentMenuItem.IsChecked = false;
            if (OrnamentTurnMenuItem != null) OrnamentTurnMenuItem.IsChecked = false;
            if (OrnamentInvertedTurnMenuItem != null) OrnamentInvertedTurnMenuItem.IsChecked = false;
            if (OrnamentAppoggiaturaMenuItem != null) OrnamentAppoggiaturaMenuItem.IsChecked = false;
            if (OrnamentAcciaccaturaMenuItem != null) OrnamentAcciaccaturaMenuItem.IsChecked = false;
        }

        private void SetOrnamentSelection(NoteOrnament ornament)
        {
            if (ornament is not (NoteOrnament.None
                or NoteOrnament.Trill
                or NoteOrnament.UpperMordent
                or NoteOrnament.LowerMordent
                or NoteOrnament.Turn
                or NoteOrnament.InvertedTurn
                or NoteOrnament.Appoggiatura
                or NoteOrnament.Acciaccatura))
            {
                ornament = NoteOrnament.None;
            }

            if (OrnamentNoneMenuItem != null) OrnamentNoneMenuItem.IsChecked = ornament == NoteOrnament.None;
            if (OrnamentTrillMenuItem != null) OrnamentTrillMenuItem.IsChecked = ornament == NoteOrnament.Trill;
            if (OrnamentUpperMordentMenuItem != null) OrnamentUpperMordentMenuItem.IsChecked = ornament == NoteOrnament.UpperMordent;
            if (OrnamentLowerMordentMenuItem != null) OrnamentLowerMordentMenuItem.IsChecked = ornament == NoteOrnament.LowerMordent;
            if (OrnamentTurnMenuItem != null) OrnamentTurnMenuItem.IsChecked = ornament == NoteOrnament.Turn;
            if (OrnamentInvertedTurnMenuItem != null) OrnamentInvertedTurnMenuItem.IsChecked = ornament == NoteOrnament.InvertedTurn;
            if (OrnamentAppoggiaturaMenuItem != null) OrnamentAppoggiaturaMenuItem.IsChecked = ornament == NoteOrnament.Appoggiatura;
            if (OrnamentAcciaccaturaMenuItem != null) OrnamentAcciaccaturaMenuItem.IsChecked = ornament == NoteOrnament.Acciaccatura;
        }

        private void ResetPendingNoteTypeState()
        {
            _pendingAccidental = NoteAccidental.None;
            _pendingStaccatissimo = false;
            _pendingStaccato = false;
            _pendingAccent = false;
            _pendingAugmentationDot = false;
            _pendingOrnament = NoteOrnament.None;
        }

        private void ResetNoteTypeControlsForNewInput()
        {
            _syncingNoteTypeControls = true;
            try
            {
                if (AccidentalSharpItem != null) AccidentalSharpItem.IsChecked = false;
                if (AccidentalFlatItem != null) AccidentalFlatItem.IsChecked = false;
                if (AccidentalNaturalItem != null) AccidentalNaturalItem.IsChecked = false;
                if (StaccatoMenuItem != null) StaccatoMenuItem.IsChecked = false;
                if (StaccatissimoMenuItem != null) StaccatissimoMenuItem.IsChecked = false;
                if (AccentMenuItem != null) AccentMenuItem.IsChecked = false;
                if (AugmentationDotMenuItem != null) AugmentationDotMenuItem.IsChecked = false;
                ClearOrnamentSelection();
            }
            finally
            {
                _syncingNoteTypeControls = false;
            }

            ResetPendingNoteTypeState();
        }

        private void SyncPendingNoteTypeFromControls()
        {
            if (_syncingNoteTypeControls) return;

            if (_isRestInputMode)
            {
                _pendingAccidental = NoteAccidental.None;
                _pendingStaccatissimo = false;
                _pendingStaccato = false;
                _pendingAccent = false;
                _pendingAugmentationDot = AugmentationDotMenuItem?.IsChecked == true;
                _pendingOrnament = NoteOrnament.None;
                return;
            }

            _pendingAccidental = AccidentalSharpItem?.IsChecked == true
                ? NoteAccidental.Sharp
                : AccidentalFlatItem?.IsChecked == true
                    ? NoteAccidental.Flat
                    : AccidentalNaturalItem?.IsChecked == true
                        ? NoteAccidental.Natural
                        : NoteAccidental.None;

            _pendingStaccatissimo = StaccatissimoMenuItem?.IsChecked == true;
            _pendingStaccato = StaccatoMenuItem?.IsChecked == true;
            _pendingAccent = AccentMenuItem?.IsChecked == true;
            _pendingAugmentationDot = AugmentationDotMenuItem?.IsChecked == true;
            _pendingOrnament = GetSelectedOrnamentFromControls();
        }

        private void SyncNoteTypeControlsFromSelectedNotes()
        {
            if (_viewModel == null) return;

            var selected = _viewModel.Project.Notes.Where(n => n.IsSelected).ToList();
            if (selected.Count != 1) return;

            var note = selected[0];
            _syncingNoteTypeControls = true;
            try
            {
                if (AccidentalSharpItem != null) AccidentalSharpItem.IsChecked = note.Accidental == NoteAccidental.Sharp;
                if (AccidentalFlatItem != null) AccidentalFlatItem.IsChecked = note.Accidental == NoteAccidental.Flat;
                if (AccidentalNaturalItem != null) AccidentalNaturalItem.IsChecked = note.Accidental == NoteAccidental.Natural;
                if (StaccatoMenuItem != null) StaccatoMenuItem.IsChecked = note.IsStaccato;
                if (StaccatissimoMenuItem != null) StaccatissimoMenuItem.IsChecked = note.IsStaccatissimo;
                if (AccentMenuItem != null) AccentMenuItem.IsChecked = note.IsAccent;
                if (AugmentationDotMenuItem != null) AugmentationDotMenuItem.IsChecked = note.AugmentationDots > 0;
                SetOrnamentSelection(note.Ornament);
            }
            finally
            {
                _syncingNoteTypeControls = false;
            }

            SyncPendingNoteTypeFromControls();
        }

        private void SyncSlurSlopeControlFromSelection()
        {
            if (_viewModel == null) return;

            var selectedSlurs = _viewModel.Project.ExpressionMarks
                .Where(m => m.IsSelected && IsSlurExpression(NormalizeExpressionCode(m.Code)))
                .ToList();

            if (selectedSlurs.Count == 1)
            {
                _pendingSlurSlopeSteps = Math.Clamp(selectedSlurs[0].SlopeSteps, -SlurMaxSlopeSteps, SlurMaxSlopeSteps);
            }
        }

        private void ApplyPendingNoteTypeToSelectedNotes()
        {
            if (_viewModel == null) return;

            var selected = _viewModel.Project.Notes.Where(n => n.IsSelected).ToList();
            if (selected.Count == 0) return;

            foreach (var note in selected)
            {
                if (!note.IsRest)
                {
                    int naturalMidi = GetNaturalMidiForDisplay(note.Midi, note.Accidental);
                    note.Accidental = _pendingAccidental;
                    note.Midi = Math.Clamp(naturalMidi + GetAccidentalSemitoneOffset(note.Accidental), 36, 96);
                    note.IsStaccato = _pendingStaccato;
                    note.IsStaccatissimo = _pendingStaccatissimo;
                    note.IsAccent = _pendingAccent;
                    note.Ornament = _pendingOrnament;
                }
                else
                {
                    note.Ornament = NoteOrnament.None;
                }
                SetNoteAugmentationDot(note, _pendingAugmentationDot);
            }

            MarkProjectChanged();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private static void SetNoteAugmentationDot(NoteEvent note, bool enabled)
        {
            int baseDuration = note.BaseDurationTicks;
            if (baseDuration <= 0)
            {
                baseDuration = note.AugmentationDots > 0
                    ? InferBaseDurationFromDot(note.DurationTicks, note.AugmentationDots)
                    : Math.Max(1, note.DurationTicks);
            }

            note.BaseDurationTicks = Math.Max(1, baseDuration);
            note.AugmentationDots = enabled ? 1 : 0;
            note.DurationTicks = enabled
                ? note.BaseDurationTicks + note.BaseDurationTicks / 2
                : note.BaseDurationTicks;
        }

        private static int InferBaseDurationFromDot(int durationTicks, int dotCount)
        {
            if (dotCount <= 0) return Math.Max(1, durationTicks);

            double factor = dotCount switch
            {
                1 => 1.5,
                2 => 1.75,
                _ => 1.5
            };
            return Math.Max(1, (int)Math.Round(durationTicks / factor));
        }

        private ExpressionMark AddExpressionMark(Point pt, string code)
        {
            if (_viewModel == null) throw new InvalidOperationException("ViewModel is not ready.");

            int systemIndex = GetSystemIndexFromY((float)pt.Y);
            string normalizedCode = NormalizeExpressionCode(code);
            float bottomLineY = GetSystemBottomLineY(systemIndex);
            int startTick = IsScoreClefExpression(normalizedCode)
                ? SnapTickAllowBarline(pt.X, systemIndex)
                : (IsBarlineAnchoredScoreMark(normalizedCode)
                    ? SnapBarlineTickForSystem(pt.X, systemIndex)
                    : (normalizedCode == ScoreMarkSegno
                        ? SnapTickAllowBarline(pt.X, systemIndex)
                        : SnapTick(pt.X, systemIndex)));
            float staffStepOffset = ClampExpressionStaffStepOffset(YToStaffStepOffset((float)pt.Y, bottomLineY));
            if (IsScoreClefExpression(normalizedCode))
            {
                staffStepOffset = ClampExpressionStaffStepOffset(MathF.Round(YToStaffStepOffset((float)pt.Y, bottomLineY)));
            }
            else if (IsScoreEndingExpression(normalizedCode))
            {
                float labelY = GetSystemTrebleTop(systemIndex) - _staffGap * 2.78f;
                staffStepOffset = ClampExpressionStaffStepOffset(YToStaffStepOffset(labelY, bottomLineY));
            }

            var mark = new ExpressionMark
            {
                Code = normalizedCode,
                StartTick = startTick,
                StaffStepOffset = staffStepOffset,
                SpanBeats = GetDefaultExpressionSpanBeats(code),
                ShapeHeightSteps = GetDefaultExpressionShapeHeightSteps(code),
                SlopeSteps = IsSlurExpression(normalizedCode)
                    ? Math.Clamp(_pendingSlurSlopeSteps, -SlurMaxSlopeSteps, SlurMaxSlopeSteps)
                    : 0f,
                IsSelected = true
            };

            if (IsScoreEndingExpression(normalizedCode))
            {
                mark.SpanBeats = Math.Max(1f, GetEffectiveTimeSignatureAtTick(mark.StartTick).Numerator);
            }

            if (normalizedCode == "ped_line")
            {
                SnapPedalLineMark(mark, systemIndex);
            }

            _viewModel.Project.ExpressionMarks.Add(mark);
            MarkProjectChanged();
            StaffCanvas.Invalidate();
            return mark;
        }

        private int SnapTick(double x, int systemIndex)
        {
            return SnapTickCore(x, systemIndex, avoidMeasureBoundary: true);
        }

        private int SnapTickAllowBarline(double x, int systemIndex)
        {
            return SnapTickCore(x, systemIndex, avoidMeasureBoundary: false);
        }

        private int SnapTickCore(double x, int systemIndex, bool avoidMeasureBoundary)
        {
            if (_viewModel == null) return 0;

            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            int snapDiv = Math.Max(1, _viewModel.SnapDivision);
            int snapTicks = Math.Max(1, ppq / snapDiv);

            double clampedX = ClampX(x, systemIndex);
            int measuresInSystem = GetMeasuresInSystem(systemIndex);
            float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
            int localMeasure = 0;
            for (int i = 0; i < measuresInSystem; i++)
            {
                if (clampedX <= boundaries[i + 1] || i == measuresInSystem - 1)
                {
                    localMeasure = i;
                    break;
                }
            }

            double measureStartX = boundaries[localMeasure];
            double measureEndX = boundaries[localMeasure + 1];
            double measureRatio = 0d;
            int globalMeasure = GetSystemStartMeasureIndex(systemIndex) + localMeasure;
            int measureStartTick = GetMeasureBoundaryTick(globalMeasure);
            int measureEndTick = GetMeasureBoundaryTick(globalMeasure + 1);
            GetMeasurePlayableRange(
                systemIndex,
                localMeasure,
                measureStartTick,
                (float)measureStartX,
                (float)measureEndX,
                out float playableStartX,
                out float playableEndX);
            measureStartX = playableStartX;
            measureEndX = playableEndX;
            double measureWidth = Math.Max(1d, measureEndX - measureStartX);
            measureRatio = Math.Clamp((clampedX - measureStartX) / measureWidth, 0d, 1d);
            int measureTickLength = Math.Max(1, measureEndTick - measureStartTick);
            int rawTicks;
            if (TryGetAccidentalSlotTickData(
                measureStartTick,
                measureEndTick,
                out var accidentalRelativeTicks,
                out int accidentalSlotTickWidth)
                && accidentalRelativeTicks.Count > 0)
            {
                int virtualLength = measureTickLength + accidentalRelativeTicks.Count * accidentalSlotTickWidth;
                double virtualTick = Math.Clamp(measureRatio * virtualLength, 0d, virtualLength);
                int mappedLocalTick = MapVirtualTickToMeasureTick(
                    virtualTick,
                    measureTickLength,
                    accidentalRelativeTicks,
                    accidentalSlotTickWidth);
                rawTicks = measureStartTick + mappedLocalTick;
            }
            else
            {
                rawTicks = measureStartTick + (int)Math.Round(measureRatio * measureTickLength);
            }

            if (rawTicks < 0) rawTicks = 0;

            int snapped = (int)Math.Round(rawTicks / (double)snapTicks) * snapTicks;
            if (avoidMeasureBoundary)
            {
                int safeSnapTicks = Math.Max(1, snapTicks);
                int interiorStart = measureStartTick + safeSnapTicks;
                int interiorEnd = measureEndTick - safeSnapTicks;
                if (interiorEnd >= interiorStart)
                {
                    snapped = Math.Clamp(snapped, interiorStart, interiorEnd);
                }
                else if (snapped >= measureEndTick)
                {
                    snapped = Math.Max(measureStartTick, measureEndTick - safeSnapTicks);
                }
            }

            return Math.Max(0, snapped);
        }

        private int SnapNoteTick(double x, int systemIndex, NoteEvent? ignoreNote)
        {
            int snappedToGrid = SnapTick(x, systemIndex);
            if (_viewModel == null) return snappedToGrid;

            float clampedX = (float)ClampX(x, systemIndex);
            float snapRadiusPx = Math.Max(_staffGap * 0.9f, _beatWidth * 0.18f);
            NoteEvent? nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (var note in _viewModel.Project.Notes)
            {
                if (ReferenceEquals(note, ignoreNote)) continue;
                if (GetSystemIndexForTick(note.StartTick) != systemIndex) continue;
                if (IsBarlineTick(note.StartTick)) continue;

                float noteX = GetNoteX(note.StartTick);
                float distance = Math.Abs(noteX - clampedX);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = note;
                }
            }

            if (nearest != null && nearestDistance <= snapRadiusPx)
            {
                return nearest.StartTick;
            }

            return snappedToGrid;
        }

        private void GetMeasurePlayableRange(
            int systemIndex,
            int localMeasureIndex,
            int measureStartTick,
            float measureLeft,
            float measureRight,
            out float playableStartX,
            out float playableEndX)
        {
            float reserve = localMeasureIndex > 0
                ? GetInlineSignatureReserveWidthAtTick(systemIndex, measureStartTick)
                : 0f;
            reserve += GetBarlineScoreMarkReserveWidthAtTick(measureStartTick);
            float maxReserve = Math.Max(0f, (measureRight - measureLeft) - Math.Max(_staffGap * 2f, _beatWidth * 0.25f));
            reserve = Math.Clamp(reserve, 0f, maxReserve);

            // Keep first/last notes away from barlines, and reserve visual room
            // for accidentals / dots in dense bars.
            float leadInset = Math.Max(SymbolSizeGap * 0.98f, _beatWidth * 0.24f);
            if (localMeasureIndex == 0)
            {
                leadInset += Math.Max(SymbolSizeGap * 0.42f, _beatWidth * 0.12f);
            }
            else
            {
                leadInset += Math.Max(SymbolSizeGap * 0.78f, _beatWidth * 0.23f);
            }

            if (reserve > 0f)
            {
                leadInset += Math.Min(reserve * 0.42f, Math.Max(SymbolSizeGap * 1.25f, _beatWidth * 0.30f));
            }

            float trailingInset = Math.Max(SymbolSizeGap * 0.84f, _beatWidth * 0.22f);
            int snapDivision = Math.Max(1, _viewModel?.SnapDivision ?? 4);
            float gridStepApprox = Math.Max(1f, (measureRight - measureLeft) / Math.Max(8f, _beatsPerBar * snapDivision));
            float edgeGridReserve = gridStepApprox * 1.0f;
            leadInset = Math.Max(leadInset, edgeGridReserve);
            trailingInset = Math.Max(trailingInset, edgeGridReserve);
            playableStartX = measureLeft + reserve + leadInset;
            playableEndX = measureRight - trailingInset;
            if (playableEndX <= playableStartX + 1f)
            {
                float center = (measureLeft + measureRight) * 0.5f;
                playableStartX = center - 0.5f;
                playableEndX = center + 0.5f;
            }
        }

        private float GetBarlineScoreMarkReserveWidthAtTick(int boundaryTick)
        {
            if (_viewModel?.Project?.ExpressionMarks == null || boundaryTick < 0)
            {
                return 0f;
            }

            float reserve = 0f;
            int safeTick = Math.Max(0, boundaryTick);
            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                if (mark == null) continue;
                if (Math.Max(0, mark.StartTick) != safeTick) continue;
                string code = NormalizeExpressionCode(mark.Code);
                reserve += code switch
                {
                    ScoreMarkFinalBarline => Math.Max(SymbolSizeGap * 1.05f, _staffGap * 1.12f),
                    ScoreMarkRepeatBarline => Math.Max(SymbolSizeGap * 0.95f, _staffGap * 0.98f),
                    ScoreMarkEnding1 or ScoreMarkEnding2 => Math.Max(SymbolSizeGap * 0.82f, _staffGap * 0.90f),
                    ScoreMarkSegno => Math.Max(SymbolSizeGap * 0.78f, _staffGap * 0.84f),
                    _ => 0f
                };
            }

            return Math.Min(reserve, Math.Max(SymbolSizeGap * 2.8f, _beatWidth * 0.65f));
        }

        private bool IsBeamable(NoteEvent note)
        {
            if (_viewModel == null) return false;
            if (note.IsRest) return false;
            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            int baseTicks = note.BaseDurationTicks > 0 ? note.BaseDurationTicks : note.DurationTicks;
            return baseTicks <= Math.Max(1, ppq / 2);
        }

        private void TryTrackBeamLinkGesture(NoteEvent movingNote, int systemIndex, float pointerY)
        {
            if (_viewModel == null) return;
            if (!IsBeamable(movingNote))
            {
                _beamLinkAnchorNote = null;
                _beamLinkAnchorPointerY = float.NaN;
                return;
            }

            if (!ReferenceEquals(_beamLinkDraggingNote, movingNote))
            {
                _beamLinkDraggingNote = movingNote;
                _beamLinkAnchorNote = null;
                _beamLinkAnchorPointerY = float.NaN;
            }

            float overlapTolerance = Math.Max(_staffGap * 0.48f, 3.5f);
            var overlappedNote = _viewModel.Project.Notes
                .Where(n =>
                    !ReferenceEquals(n, movingNote)
                    && n.StartTick == movingNote.StartTick
                    && IsBeamable(n)
                    && GetSystemIndexForTick(n.StartTick) == systemIndex)
                .OrderBy(n =>
                {
                    float ny = GetNoteVisualY(n, systemIndex);
                    return Math.Abs(ny - pointerY);
                })
                .FirstOrDefault();

            if (overlappedNote != null)
            {
                float overlapY = GetNoteVisualY(overlappedNote, systemIndex);
                if (Math.Abs(overlapY - pointerY) > overlapTolerance)
                {
                    overlappedNote = null;
                }
            }

            if (_beamLinkAnchorNote == null)
            {
                if (overlappedNote != null)
                {
                    _beamLinkAnchorNote = overlappedNote;
                    _beamLinkAnchorPointerY = pointerY;
                }
                else
                {
                    _beamLinkAnchorNote = null;
                    _beamLinkAnchorPointerY = float.NaN;
                }

                return;
            }

            if (movingNote.StartTick != _beamLinkAnchorNote.StartTick)
            {
                LinkBeamPair(_beamLinkAnchorNote, movingNote);
                _beamLinkAnchorNote = null;
                _beamLinkDraggingNote = null;
                _beamLinkAnchorPointerY = float.NaN;
                return;
            }

            float chordTriggerDistance = Math.Max(_staffGap * 0.7f, 6f);
            float anchorPointerY = float.IsNaN(_beamLinkAnchorPointerY)
                ? GetNoteVisualY(_beamLinkAnchorNote, systemIndex)
                : _beamLinkAnchorPointerY;
            if (Math.Abs(pointerY - anchorPointerY) >= chordTriggerDistance)
            {
                LinkBeamPair(_beamLinkAnchorNote, movingNote);
                _beamLinkAnchorNote = null;
                _beamLinkDraggingNote = null;
                _beamLinkAnchorPointerY = float.NaN;
            }
        }

        private void LinkBeamPair(NoteEvent a, NoteEvent b)
        {
            if (_viewModel == null) return;

            int groupA = a.BeamGroupId;
            int groupB = b.BeamGroupId;

            if (groupA > 0 && groupB > 0 && groupA != groupB)
            {
                int keep = Math.Min(groupA, groupB);
                int remove = Math.Max(groupA, groupB);
                foreach (var note in _viewModel.Project.Notes)
                {
                    if (note.BeamGroupId == remove)
                    {
                        note.BeamGroupId = keep;
                    }
                }

                a.BeamGroupId = keep;
                b.BeamGroupId = keep;
                MarkProjectChanged(pushHistory: false);
                return;
            }

            if (groupA > 0)
            {
                b.BeamGroupId = groupA;
                MarkProjectChanged(pushHistory: false);
                return;
            }

            if (groupB > 0)
            {
                a.BeamGroupId = groupB;
                MarkProjectChanged(pushHistory: false);
                return;
            }

            int newGroupId = _viewModel.Project.Notes.Count == 0
                ? 1
                : _viewModel.Project.Notes.Max(n => n.BeamGroupId) + 1;
            a.BeamGroupId = newGroupId;
            b.BeamGroupId = newGroupId;
            MarkProjectChanged(pushHistory: false);
        }

        private bool IsBarlineTick(int tick)
        {
            int safeTick = Math.Max(0, tick);
            int boundaryIndex = GetNearestMeasureBoundaryIndexForTick(safeTick);
            return safeTick > 0 && GetMeasureBoundaryTick(boundaryIndex) == safeTick;
        }

        private double ClampX(double x, int systemIndex)
        {
            double minX = GetSystemMusicStartX(systemIndex);
            if (x < minX) return minX;
            if (_measureWidth <= 0f) return x;

            double maxX = GetSystemRightX(systemIndex);
            return x > maxX ? maxX : x;
        }

        private int GetSystemIndexFromY(float y)
        {
            if (_systemCount <= 1 || _systemStride <= 0f) return 0;

            int bestIndex = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < _systemCount; i++)
            {
                float trebleTop = GetSystemTrebleTop(i);
                float bassBottom = GetSystemBassBottom(i);
                float centerY = (trebleTop + bassBottom) * 0.5f;
                float distance = Math.Abs(y - centerY);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private float GetSystemTrebleTop(int systemIndex)
        {
            return _systemTopMargin + systemIndex * _systemStride;
        }

        private float GetSystemTrebleBottom(int systemIndex)
        {
            return GetSystemTrebleTop(systemIndex) + 4f * _staffGap;
        }

        private float GetSystemBassTop(int systemIndex)
        {
            return GetSystemTrebleBottom(systemIndex) + _staffMiddleGapFactor * _staffGap;
        }

        private float GetSystemBassBottom(int systemIndex)
        {
            return GetSystemBassTop(systemIndex) + 4f * _staffGap;
        }

        private static string GetStaffClefKey(int systemIndex, bool topStaff)
        {
            return $"{Math.Max(0, systemIndex)}:{(topStaff ? "top" : "bottom")}";
        }

        private static StaffClefType GetDefaultStaffClef(bool topStaff)
        {
            return topStaff ? StaffClefType.Treble : StaffClefType.Bass;
        }

        private static StaffClefType ParseStaffClef(string? code, StaffClefType fallback)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return fallback;
            }

            return string.Equals(code.Trim(), "bass", StringComparison.OrdinalIgnoreCase)
                ? StaffClefType.Bass
                : StaffClefType.Treble;
        }

        private static string ToStaffClefCode(StaffClefType clef)
        {
            return clef == StaffClefType.Bass ? "bass" : "treble";
        }

        private StaffClefType GetSystemStaffClefType(int systemIndex, bool topStaff)
        {
            StaffClefType fallback = GetDefaultStaffClef(topStaff);
            if (_viewModel?.Project.StaffClefs == null)
            {
                return fallback;
            }

            string key = GetStaffClefKey(systemIndex, topStaff);
            if (!_viewModel.Project.StaffClefs.TryGetValue(key, out string? code))
            {
                return fallback;
            }

            return ParseStaffClef(code, fallback);
        }

        private void SetSystemStaffClefType(int systemIndex, bool topStaff, StaffClefType clef)
        {
            if (_viewModel == null)
            {
                return;
            }

            _viewModel.Project.StaffClefs ??= new Dictionary<string, string>();
            string key = GetStaffClefKey(systemIndex, topStaff);
            StaffClefType fallback = GetDefaultStaffClef(topStaff);
            if (clef == fallback)
            {
                _viewModel.Project.StaffClefs.Remove(key);
            }
            else
            {
                _viewModel.Project.StaffClefs[key] = ToStaffClefCode(clef);
            }
        }

        private static int GetBottomLineDiatonicByClef(StaffClefType clef)
        {
            return clef == StaffClefType.Bass ? BassBottomDiatonic : TrebleBottomDiatonic;
        }

        private static int GetClefGlyphCode(StaffClefType clef)
        {
            return clef == StaffClefType.Bass ? SmuflFClef : SmuflGClef;
        }

        private static float GetStaffClefYOffset(StaffClefType clef)
        {
            return clef == StaffClefType.Bass ? BassClefYOffset : TrebleClefYOffset;
        }

        private float GetStaffClefAnchorLineY(float staffTop, StaffClefType clef)
        {
            return staffTop + (clef == StaffClefType.Bass ? 1f : 3f) * _staffGap;
        }

        private void RegisterClefHitTarget(int systemIndex, bool topStaff, float x, float anchorY, float glyphSize)
        {
            float width = Math.Max(SymbolSizeGap * 2.6f, glyphSize * 0.8f);
            float height = Math.Max(SymbolSizeGap * 5.8f, glyphSize * 1.34f);
            float left = x - width * 0.46f;
            float top = anchorY - height * 0.8f;
            _clefHitTargets.Add(new ClefHitTarget(systemIndex, topStaff, new Rect(left, top, width, height), x, anchorY));
        }

        private bool IsInsideTrebleStaff(float y, int systemIndex)
        {
            float pad = _staffGap * StaffInteriorPaddingFactor;
            return y > GetSystemTrebleTop(systemIndex) + pad
                && y < GetSystemTrebleBottom(systemIndex) - pad;
        }

        private bool IsInsideBassStaff(float y, int systemIndex)
        {
            float pad = _staffGap * StaffInteriorPaddingFactor;
            return y > GetSystemBassTop(systemIndex) + pad
                && y < GetSystemBassBottom(systemIndex) - pad;
        }

        private bool GetPreferredStaffFromPointerY(float y, int systemIndex)
        {
            if (IsInsideTrebleStaff(y, systemIndex)) return true;
            if (IsInsideBassStaff(y, systemIndex)) return false;
            return y <= (GetSystemTrebleBottom(systemIndex) + GetSystemBassTop(systemIndex)) * 0.5f;
        }

        private bool ResolveDragStaffPreference(bool currentPreferTreble, float y, int systemIndex)
        {
            if (currentPreferTreble && IsInsideBassStaff(y, systemIndex))
            {
                return false;
            }

            if (!currentPreferTreble && IsInsideTrebleStaff(y, systemIndex))
            {
                return true;
            }

            return currentPreferTreble;
        }

        private float GetSystemBottomLineY(int systemIndex)
        {
            return GetSystemTrebleBottom(systemIndex);
        }

        private int GetSystemIndexForTick(int startTick)
        {
            int measureIndex = GetMeasureIndex(startTick);
            int index = GetSystemIndexForMeasureIndex(measureIndex);
            if (index < 0) index = 0;
            if (index >= _systemCount) index = Math.Max(0, _systemCount - 1);
            return index;
        }

        private int GetMeasureIndex(int startTick)
        {
            return GetMeasureIndexByTick(startTick);
        }

        private float GetNoteX(int startTick)
        {
            return GetNoteX((double)startTick);
        }

        private float GetNoteX(double startTick)
        {
            if (_measureWidth <= 0f) return _musicStartX;

            double safeTick = Math.Max(0d, startTick);
            int measureIndex = GetMeasureIndex((int)Math.Floor(safeTick));
            int measureStartTick = GetMeasureBoundaryTick(measureIndex);
            int measureEndTick = GetMeasureBoundaryTick(measureIndex + 1);
            int measureTickLength = Math.Max(1, measureEndTick - measureStartTick);
            double tickInMeasure = Math.Clamp(safeTick - measureStartTick, 0d, measureTickLength);
            int systemIndex = GetSystemIndexForMeasureIndex(measureIndex);
            int measureInSystem = measureIndex - GetSystemStartMeasureIndex(systemIndex);
            int measuresInSystem = GetMeasuresInSystem(systemIndex);
            measureInSystem = Math.Clamp(measureInSystem, 0, Math.Max(0, measuresInSystem - 1));
            float[] boundaries = GetSystemBarlinePositions(systemIndex, measuresInSystem);
            float measureLeft = boundaries[measureInSystem];
            float measureRight = boundaries[measureInSystem + 1];
            GetMeasurePlayableRange(
                systemIndex,
                measureInSystem,
                measureStartTick,
                measureLeft,
                measureRight,
                out float startX,
                out float endX);
            float width = Math.Max(1f, endX - startX);
            double adjustedTickInMeasure = tickInMeasure;
            double adjustedMeasureTickLength = measureTickLength;
            if (TryGetAccidentalSlotTickData(
                measureStartTick,
                measureEndTick,
                out var accidentalRelativeTicks,
                out int accidentalSlotTickWidth)
                && accidentalRelativeTicks.Count > 0)
            {
                int slotsBefore = CountAccidentalSlotsAtOrBefore(accidentalRelativeTicks, tickInMeasure);
                adjustedTickInMeasure = tickInMeasure + slotsBefore * accidentalSlotTickWidth;
                adjustedMeasureTickLength = measureTickLength + accidentalRelativeTicks.Count * accidentalSlotTickWidth;
            }

            float ratio = (float)(adjustedTickInMeasure / Math.Max(1d, adjustedMeasureTickLength));
            return startX + width * Math.Clamp(ratio, 0f, 1f);
        }

        private bool TryGetAccidentalSlotTickData(
            int measureStartTick,
            int measureEndTick,
            out List<int> accidentalRelativeTicks,
            out int accidentalSlotTickWidth)
        {
            accidentalRelativeTicks = new List<int>();
            accidentalSlotTickWidth = 1;

            if (_viewModel?.Project == null)
            {
                return false;
            }

            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            accidentalSlotTickWidth = Math.Max(1, ppq / 8);
            int safeStart = Math.Max(0, measureStartTick);
            int safeEnd = Math.Max(safeStart + 1, measureEndTick);

            foreach (var note in _viewModel.Project.Notes)
            {
                if (note == null || note.IsRest || note.Accidental == NoteAccidental.None)
                {
                    continue;
                }

                int tick = Math.Max(0, note.StartTick);
                if (tick < safeStart || tick >= safeEnd)
                {
                    continue;
                }

                accidentalRelativeTicks.Add(tick - safeStart);
            }

            accidentalRelativeTicks.Sort();
            return accidentalRelativeTicks.Count > 0;
        }

        private static int CountAccidentalSlotsAtOrBefore(IReadOnlyList<int> accidentalRelativeTicks, double localTick)
        {
            if (accidentalRelativeTicks == null || accidentalRelativeTicks.Count == 0)
            {
                return 0;
            }

            int count = 0;
            double threshold = localTick + 0.0001d;
            for (int i = 0; i < accidentalRelativeTicks.Count; i++)
            {
                if (accidentalRelativeTicks[i] <= threshold)
                {
                    count++;
                }
                else
                {
                    break;
                }
            }

            return count;
        }

        private static int MapVirtualTickToMeasureTick(
            double virtualTick,
            int measureTickLength,
            IReadOnlyList<int> accidentalRelativeTicks,
            int accidentalSlotTickWidth)
        {
            int low = 0;
            int high = Math.Max(1, measureTickLength);
            for (int i = 0; i < 22 && low < high; i++)
            {
                int mid = low + ((high - low) / 2);
                int slots = CountAccidentalSlotsAtOrBefore(accidentalRelativeTicks, mid);
                double mapped = mid + slots * accidentalSlotTickWidth;
                if (mapped < virtualTick)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            int candidateA = Math.Clamp(low, 0, Math.Max(1, measureTickLength));
            int candidateB = Math.Clamp(low - 1, 0, Math.Max(1, measureTickLength));
            double mappedA = candidateA + CountAccidentalSlotsAtOrBefore(accidentalRelativeTicks, candidateA) * accidentalSlotTickWidth;
            double mappedB = candidateB + CountAccidentalSlotsAtOrBefore(accidentalRelativeTicks, candidateB) * accidentalSlotTickWidth;
            return Math.Abs(mappedA - virtualTick) <= Math.Abs(mappedB - virtualTick) ? candidateA : candidateB;
        }

        private static int GetAccidentalSemitoneOffset(NoteAccidental accidental)
        {
            return accidental switch
            {
                NoteAccidental.Sharp => 1,
                NoteAccidental.Flat => -1,
                _ => 0
            };
        }

        private static int QuantizeToNaturalMidi(int midi)
        {
            int clamped = Math.Clamp(midi, 0, 127);
            int octaveBlock = clamped / 12;
            int pitchClass = clamped % 12;
            int[] naturalPitchClasses = { 0, 2, 4, 5, 7, 9, 11 };

            int best = naturalPitchClasses[0];
            int bestDiff = Math.Abs(pitchClass - best);
            for (int i = 1; i < naturalPitchClasses.Length; i++)
            {
                int candidate = naturalPitchClasses[i];
                int diff = Math.Abs(pitchClass - candidate);
                if (diff < bestDiff || (diff == bestDiff && candidate < best))
                {
                    best = candidate;
                    bestDiff = diff;
                }
            }

            int result = octaveBlock * 12 + best;
            return Math.Clamp(result, 0, 127);
        }

        private static int NaturalMidiToDiatonicIndex(int midi)
        {
            int naturalMidi = QuantizeToNaturalMidi(midi);
            int octaveNumber = (naturalMidi / 12) - 1;
            int pitchClass = naturalMidi % 12;

            int degree = pitchClass switch
            {
                0 => 0,   // C
                2 => 1,   // D
                4 => 2,   // E
                5 => 3,   // F
                7 => 4,   // G
                9 => 5,   // A
                11 => 6,  // B
                _ => 0
            };

            return octaveNumber * 7 + degree;
        }

        private static int DiatonicIndexToNaturalMidi(int diatonicIndex)
        {
            int octaveNumber = (int)Math.Floor(diatonicIndex / 7.0);
            int degree = diatonicIndex - octaveNumber * 7;

            int semitone = degree switch
            {
                0 => 0,   // C
                1 => 2,   // D
                2 => 4,   // E
                3 => 5,   // F
                4 => 7,   // G
                5 => 9,   // A
                6 => 11,  // B
                _ => 0
            };

            int midi = 12 * (octaveNumber + 1) + semitone;
            return Math.Clamp(midi, 0, 127);
        }

        private static int GetNaturalMidiForDisplay(int midi, NoteAccidental accidental)
        {
            int baseMidi = midi - GetAccidentalSemitoneOffset(accidental);
            return QuantizeToNaturalMidi(baseMidi);
        }

        private static int GetKeySignatureSemitoneOffset(int naturalMidi, int fifths)
        {
            if (fifths == 0) return 0;

            int pitchClass = QuantizeToNaturalMidi(naturalMidi) % 12;
            if (fifths > 0)
            {
                int[] sharpOrder = { 5, 0, 7, 2, 9, 4, 11 }; // F C G D A E B
                int count = Math.Min(fifths, sharpOrder.Length);
                for (int i = 0; i < count; i++)
                {
                    if (pitchClass == sharpOrder[i]) return 1;
                }
            }
            else
            {
                int[] flatOrder = { 11, 4, 9, 2, 7, 0, 5 }; // B E A D G C F
                int count = Math.Min(Math.Abs(fifths), flatOrder.Length);
                for (int i = 0; i < count; i++)
                {
                    if (pitchClass == flatOrder[i]) return -1;
                }
            }

            return 0;
        }

        private int GetEffectiveNoteMidi(NoteEvent note)
        {
            int naturalMidi = GetNaturalMidiForDisplay(note.Midi, note.Accidental);
            int keyOffset = GetKeySignatureSemitoneOffset(naturalMidi, GetEffectiveKeySignatureFifthsAtTick(note.StartTick));
            int offset = note.Accidental switch
            {
                // If key signature already sharp/flat on this pitch, explicit same accidental upgrades to double accidental.
                NoteAccidental.Sharp => keyOffset > 0 ? keyOffset + 1 : 1,
                NoteAccidental.Flat => keyOffset < 0 ? keyOffset - 1 : -1,
                NoteAccidental.Natural => 0,
                _ => keyOffset
            };
            return Math.Clamp(naturalMidi + offset, 0, 127);
        }

        private bool ResolveNoteStaffPreference(NoteEvent note, int systemIndex, int naturalMidi)
        {
            bool preferTreble = note.PreferTrebleStaff ?? ShouldPreferTrebleByPosition(systemIndex, note.Midi, note.Accidental);
            note.PreferTrebleStaff = preferTreble;
            return preferTreble;
        }

        private bool ShouldPreferTrebleByPosition(int systemIndex, int midi, NoteAccidental accidental)
        {
            int naturalMidi = GetNaturalMidiForDisplay(midi, accidental);
            int diatonicIndex = NaturalMidiToDiatonicIndex(naturalMidi);
            const int trebleBottomDiatonic = TrebleBottomDiatonic;
            const int bassBottomDiatonic = BassBottomDiatonic;

            float trebleBottom = GetSystemTrebleBottom(systemIndex);
            float bassBottom = GetSystemBassBottom(systemIndex);
            float trebleY = trebleBottom - (diatonicIndex - trebleBottomDiatonic) * (_staffGap / 2f);
            float bassY = bassBottom - (diatonicIndex - bassBottomDiatonic) * (_staffGap / 2f);
            float splitY = (GetSystemTrebleBottom(systemIndex) + GetSystemBassTop(systemIndex)) / 2f;
            float trebleDistance = Math.Abs(trebleY - splitY);
            float bassDistance = Math.Abs(bassY - splitY);
            return trebleDistance <= bassDistance;
        }

        private float GetRestY(NoteEvent note, int systemIndex)
        {
            bool preferTreble = note.PreferTrebleStaff ?? ShouldPreferTrebleByPosition(systemIndex, note.Midi, note.Accidental);
            note.PreferTrebleStaff = preferTreble;
            return preferTreble
                ? GetSystemTrebleTop(systemIndex) + 3.12f * _staffGap
                : GetSystemBassTop(systemIndex) + 3.12f * _staffGap;
        }

        private float GetNoteVisualY(NoteEvent note, int systemIndex)
        {
            return note.IsRest ? GetRestY(note, systemIndex) : MidiToY(note, systemIndex);
        }

        private float GetRenderedNoteY(NoteEvent note, int systemIndex, out int ottavaShiftOctaves)
        {
            int naturalMidi = GetNaturalMidiForDisplay(note.Midi, note.Accidental);
            bool preferTreble = ResolveNoteStaffPreference(note, systemIndex, naturalMidi);
            int displayMidi = ResolveDisplayMidiForRender(naturalMidi, preferTreble, out ottavaShiftOctaves);
            return MidiToY(displayMidi, systemIndex, preferTreble);
        }

        private static int ResolveDisplayMidiForRender(int naturalMidi, bool preferTreble, out int ottavaShiftOctaves)
        {
            int displayMidi = Math.Clamp(naturalMidi, 0, 127);
            int diatonicIndex = NaturalMidiToDiatonicIndex(displayMidi);
            int lowerThreshold = preferTreble ? TrebleLowerSwitchDiatonic : BassBottomDiatonic - 8;
            int upperThreshold = preferTreble ? TrebleBottomDiatonic + 16 : BassUpperSwitchDiatonic;
            ottavaShiftOctaves = 0;

            while (diatonicIndex > upperThreshold && displayMidi - 12 >= 0 && ottavaShiftOctaves < 1)
            {
                displayMidi -= 12;
                diatonicIndex -= 7;
                ottavaShiftOctaves++;
            }

            while (diatonicIndex < lowerThreshold && displayMidi + 12 <= 127 && ottavaShiftOctaves > -1)
            {
                displayMidi += 12;
                diatonicIndex += 7;
                ottavaShiftOctaves--;
            }

            return displayMidi;
        }

        private float MidiToY(NoteEvent note, int systemIndex)
        {
            int naturalMidi = GetNaturalMidiForDisplay(note.Midi, note.Accidental);
            bool preferTreble = ResolveNoteStaffPreference(note, systemIndex, naturalMidi);
            return MidiToY(naturalMidi, systemIndex, preferTreble);
        }

        private float MidiToY(int midi, int systemIndex, NoteAccidental accidental)
        {
            int naturalMidi = GetNaturalMidiForDisplay(midi, accidental);
            bool preferTreble = ShouldPreferTrebleByPosition(systemIndex, midi, accidental);
            return MidiToY(naturalMidi, systemIndex, preferTreble);
        }

        private float MidiToY(int naturalMidi, int systemIndex, bool preferTreble)
        {
            int diatonicIndex = NaturalMidiToDiatonicIndex(naturalMidi);
            const int trebleBottomDiatonic = TrebleBottomDiatonic;
            const int bassBottomDiatonic = BassBottomDiatonic;
            float trebleBottom = GetSystemTrebleBottom(systemIndex);
            float bassBottom = GetSystemBassBottom(systemIndex);

            float trebleY = trebleBottom - (diatonicIndex - trebleBottomDiatonic) * (_staffGap / 2f);
            float bassY = bassBottom - (diatonicIndex - bassBottomDiatonic) * (_staffGap / 2f);
            return preferTreble ? trebleY : bassY;
        }

        private int YToMidi(float y, int systemIndex, NoteAccidental accidental)
        {
            bool preferTreble = GetPreferredStaffFromPointerY(y, systemIndex);
            return YToMidi(y, systemIndex, accidental, preferTreble);
        }

        private int YToMidi(float y, int systemIndex, NoteAccidental accidental, bool preferTreble)
        {
            const int trebleBottomDiatonic = TrebleBottomDiatonic;
            const int bassBottomDiatonic = BassBottomDiatonic;
            float trebleBottom = GetSystemTrebleBottom(systemIndex);
            float bassBottom = GetSystemBassBottom(systemIndex);
            float stepHeight = _staffGap / 2f;

            float trebleStaffSteps = (trebleBottom - y) / stepHeight;
            int trebleDiatonic = trebleBottomDiatonic + (int)Math.Round(trebleStaffSteps);
            int trebleNaturalMidi = DiatonicIndexToNaturalMidi(trebleDiatonic);

            float bassStaffSteps = (bassBottom - y) / stepHeight;
            int bassDiatonic = bassBottomDiatonic + (int)Math.Round(bassStaffSteps);
            int bassNaturalMidi = DiatonicIndexToNaturalMidi(bassDiatonic);

            int naturalMidi = preferTreble ? trebleNaturalMidi : bassNaturalMidi;
            int midi = naturalMidi + GetAccidentalSemitoneOffset(accidental);
            return Math.Clamp(midi, 24, 108);
        }

        private void DrawClefGlyph(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, int codePoint, float x, float baselineY, float offsetInGap, float fontSize)
        {
            float y = baselineY + offsetInGap * _staffGap;
            var old = ds.Transform;
            var origin = new System.Numerics.Vector2(x, y);
            ds.Transform = System.Numerics.Matrix3x2.CreateScale(1f, ClefVerticalStretch, origin) * old;
            DrawGlyph(ds, codePoint, x, y, fontSize);
            ds.Transform = old;
        }

        private void DrawTimeSignature(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float trebleTop, float bassTop, int numerator, int denominator)
        {
            if (numerator <= 0 || denominator <= 0 || !_musicFontAvailable) return;

            string top = numerator.ToString();
            string bottom = denominator.ToString();

            float timeX = _staffLeft + _clefSpace + _keySpace + SymbolSizeGap * TimeSigGapAfterKey;
            DrawTimeSignatureAtX(ds, trebleTop, bassTop, numerator, denominator, timeX);
        }

        private void DrawTimeSignatureAtSystemStart(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float trebleTop,
            float bassTop,
            int numerator,
            int denominator,
            int keyFifths)
        {
            if (numerator <= 0 || denominator <= 0 || !_musicFontAvailable) return;
            int keyCount = Math.Min(Math.Abs(Math.Clamp(keyFifths, -7, 7)), 7);
            float keyAdvance = SymbolSizeGap * KeySignatureAdvance;
            float keySpace = keyCount > 0 ? keyCount * keyAdvance + SymbolSizeGap * 0.6f : 0f;
            float timeX = _staffLeft + _clefSpace + keySpace + SymbolSizeGap * TimeSigGapAfterKey;
            DrawTimeSignatureAtX(ds, trebleTop, bassTop, numerator, denominator, timeX);
        }

        private void DrawTimeSignatureAtX(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            float trebleTop,
            float bassTop,
            int numerator,
            int denominator,
            float timeX)
        {
            string top = numerator.ToString();
            string bottom = denominator.ToString();
            DrawTimeSigPair(ds, top, bottom, timeX, trebleTop, _timeSigFormat.FontSize);
            DrawTimeSigPair(ds, top, bottom, timeX, bassTop, _timeSigFormat.FontSize);
        }

        private void DrawTimeSigPair(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, string top, string bottom, float x, float staffTop, float fontSize)
        {
            float middleLineY = staffTop + 2f * _staffGap;
            float topY = middleLineY - _staffGap + TimeSigVerticalOffset * _staffGap;
            float bottomY = middleLineY + _staffGap + TimeSigVerticalOffset * _staffGap;

            DrawGlyphString(ds, top, x, topY, fontSize);
            DrawGlyphString(ds, bottom, x, bottomY, fontSize);
        }

        private float GetGlyphAdvance(int codePoint, float fontSize)
        {
            float rawAdvance = GetGlyphAdvanceRaw(codePoint, fontSize);
            if (rawAdvance <= 0f)
            {
                return fontSize * 0.6f;
            }

            return Math.Clamp(rawAdvance, fontSize * 0.3f, fontSize * 1.5f);
        }

        private float GetGlyphAdvanceRaw(int codePoint, float fontSize)
        {
            if (_musicFontFace == null) return fontSize * 0.6f;
            return GetGlyphAdvanceRaw(_musicFontFace, codePoint, fontSize);
        }

        private static float GetGlyphAdvanceRaw(CanvasFontFace? fontFace, int codePoint, float fontSize)
        {
            if (fontFace == null) return fontSize * 0.6f;

            var indices = fontFace.GetGlyphIndices(new uint[] { (uint)codePoint });
            if (indices.Length == 0) return fontSize * 0.6f;

            int glyphIndex = indices[0];
            var metrics = fontFace.GetGlyphMetrics(new int[] { glyphIndex }, false);
            return metrics.Length > 0 ? metrics[0].AdvanceWidth : fontSize * 0.6f;
        }

        private float DrawGlyph(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            int codePoint,
            float x,
            float baselineY,
            float fontSize,
            Windows.UI.Color? color = null)
        {
            if (_musicFontFace == null) return 0f;
            return DrawGlyphWithFace(ds, _musicFontFace, codePoint, x, baselineY, fontSize, color ?? GetNotationInkColor());
        }

        private static float DrawGlyphWithFace(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            CanvasFontFace fontFace,
            int codePoint,
            float x,
            float baselineY,
            float fontSize,
            Windows.UI.Color? color = null)
        {
            var indices = fontFace.GetGlyphIndices(new uint[] { (uint)codePoint });
            if (indices.Length == 0) return 0f;

            int glyphIndex = indices[0];
            float advance = GetGlyphAdvanceRaw(fontFace, codePoint, fontSize);
            if (advance <= 0f)
            {
                advance = fontSize * 0.6f;
            }

            var glyphs = new CanvasGlyph[]
            {
                new CanvasGlyph
                {
                    Index = glyphIndex,
                    Advance = advance,
                    AdvanceOffset = 0f,
                    AscenderOffset = 0f
                }
            };

            using var brush = new Microsoft.Graphics.Canvas.Brushes.CanvasSolidColorBrush(ds, color ?? Colors.Black);
            ds.DrawGlyphRun(new System.Numerics.Vector2(x, baselineY), fontFace, fontSize, glyphs, false, 0, brush);
            return advance;
        }

        private void DrawGlyphString(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, string digits, float x, float baselineY, float fontSize)
        {
            float cursor = x;
            for (int i = 0; i < digits.Length; i++)
            {
                int d = digits[i] - '0';
                if (d < 0 || d > 9) continue;
                int codePoint = SmuflTimeSig0 + d;
                float advance = DrawGlyph(ds, codePoint, cursor, baselineY, fontSize);
                cursor += Math.Max(advance, fontSize * 0.5f);
            }
        }

        private bool CanDrawMusicText(string text)
        {
            var textFace = _musicTextFontFace ?? _musicFontFace;
            if (!_musicFontAvailable || textFace == null || string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch)) continue;
                if (char.IsLetterOrDigit(ch) && !textFace.HasCharacter((uint)ch))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryDrawMusicText(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            string text,
            float x,
            float baselineY,
            float fontSize,
            Windows.UI.Color? color = null)
        {
            var textFace = _musicTextFontFace ?? _musicFontFace;
            if (textFace == null || !CanDrawMusicText(text)) return false;

            float cursor = x;
            bool drewAny = false;
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    cursor += fontSize * 0.33f;
                    continue;
                }

                float advance = 0f;
                if (textFace.HasCharacter((uint)ch))
                {
                    advance = DrawGlyphWithFace(ds, textFace, ch, cursor, baselineY, fontSize, color);
                    drewAny = true;
                }
                else if (ch == '.')
                {
                    float dotRadius = Math.Max(0.9f, fontSize * 0.06f);
                    ds.FillCircle(cursor + dotRadius, baselineY - fontSize * 0.06f, dotRadius, color ?? Colors.Black);
                    advance = dotRadius * 2.8f;
                    drewAny = true;
                }

                cursor += Math.Max(advance, fontSize * 0.34f);
            }

            return drewAny;
        }

        private bool TryDrawLibraryTextExpression(
            Microsoft.Graphics.Canvas.CanvasDrawingSession ds,
            string text,
            float x,
            float baselineY,
            float fontSize,
            string code,
            Color color)
        {
            float drawSize = code is "cresc_text" or "dim_text" or "rit"
                ? fontSize * 1.04f
                : fontSize;

            if (TryDrawMusicText(ds, text, x, baselineY, drawSize, color))
            {
                if (code is "cresc_text" or "dim_text" or "rit")
                {
                    TryDrawMusicText(ds, text, x + 0.08f, baselineY, drawSize, color);
                }
                return true;
            }

            return false;
        }

        private void InitializeMusicFontSelection()
        {
            if (SymbolFontMenu == null || SymbolFontMenu.Items.Count == 0) return;

            RadioMenuFlyoutItem? bravuraItem = null;
            foreach (var item in SymbolFontMenu.Items.OfType<RadioMenuFlyoutItem>())
            {
                if (TryParseMusicFontTag(item.Tag?.ToString(), out var candidateFile, out var candidateFamily)
                    && string.Equals(candidateFamily, ForcedMusicFontFamily, StringComparison.OrdinalIgnoreCase))
                {
                    bravuraItem = item;
                    break;
                }
            }

            if (bravuraItem == null)
            {
                _musicFontAvailable = false;
                _musicFontStatus = "\u672a\u627e\u5230 Bravura \u5b57\u4f53\u83dc\u5355\u9879\u3002";
                return;
            }

            bravuraItem.IsChecked = true;
            if (TryParseMusicFontTag(bravuraItem.Tag?.ToString(), out var selectedFile, out var selectedFamily))
            {
                ApplyMusicFont(selectedFile, selectedFamily);
            }
        }

        private void MusicFontMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioMenuFlyoutItem item) return;

            if (!TryParseMusicFontTag(item.Tag?.ToString(), out var fileName, out var familyName))
            {
                return;
            }

            if (!string.Equals(familyName, ForcedMusicFontFamily, StringComparison.OrdinalIgnoreCase))
            {
                _viewModel?.SetStatus("\u5f53\u524d\u7248\u672c\u5df2\u9501\u5b9a\u4f7f\u7528 Bravura\u3002");
                InitializeMusicFontSelection();
                return;
            }

            ApplyMusicFont(fileName, familyName);
            SwitchToNoNoteLengthIfNeeded();
        }

        private void ApplyMusicFont(string fileName, string familyName)
        {
            try
            {
                bool isEnglish = AppSettingsService.Instance.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
                if (!string.Equals(familyName, ForcedMusicFontFamily, StringComparison.OrdinalIgnoreCase))
                {
                    familyName = ForcedMusicFontFamily;
                    fileName = ForcedMusicFontFile;
                }

                if (!TryResolveFont(fileName, familyName, out var isSmufl))
                {
                    _musicFontAvailable = false;
                    _musicFontStatus = isEnglish
                        ? $"Font not installed: {familyName} (install the bundled font and restart the app)."
                        : $"\u5b57\u4f53\u672a\u5b89\u88c5\uff1a{familyName}\uff08\u8bf7\u5b89\u88c5\u968f\u5e94\u7528\u9644\u5e26\u7684\u5b57\u4f53\u540e\u91cd\u542f\uff09";
                    ShowMusicFontInstallPromptIfNeeded(familyName);
                    return;
                }

                if (!isSmufl)
                {
                    _musicFontAvailable = false;
                    _musicFontStatus = isEnglish
                        ? $"Font is not SMuFL: {familyName}"
                        : $"\u5b57\u4f53\u4e0d\u662f SMuFL\uff1a{familyName}";
                    return;
                }

                if (!TryCreateFontFace(fileName, familyName, out var fontSet, out var fontFace))
                {
                    _musicFontAvailable = false;
                    _musicFontStatus = isEnglish
                        ? $"Failed to load font: {familyName}"
                        : $"\u5b57\u4f53\u52a0\u8f7d\u5931\u8d25\uff1a{familyName}";
                    return;
                }

                DisposeMusicFont();
                _musicFontSet = fontSet;
                _musicFontFace = fontFace;
                LoadMusicTextFont();

                _musicFontFamily = familyName;
                _musicFontAvailable = true;
                _musicFontStatus = string.Empty;
            }
            catch (Exception ex)
            {
                _musicFontAvailable = false;
                _musicFontStatus = $"Music font apply failed: {ex.Message}";
            }
            finally
            {
                StaffCanvas.Invalidate();
            }
        }

        private bool TryResolveFont(string fileName, string familyName, out bool isSmufl)
        {
            isSmufl = false;

            if (TryLoadFontFromSystem(familyName, out isSmufl))
            {
                return true;
            }

            if (TryCreateFontFaceFromUri(fileName, out var bundledSet, out var bundledFace) && bundledFace != null)
            {
                try
                {
                    isSmufl = bundledFace.HasCharacter((uint)SmuflGClef)
                        && bundledFace.HasCharacter((uint)SmuflFClef)
                        && bundledFace.HasCharacter((uint)SmuflAccidentalSharp);
                    return true;
                }
                finally
                {
                    bundledSet?.Dispose();
                }
            }

            return false;
        }

        private void ShowMusicFontInstallPromptIfNeeded(string familyName)
        {
            if (_musicFontInstallPromptShown)
            {
                return;
            }

            _musicFontInstallPromptShown = true;
            if (XamlRoot == null)
            {
                RoutedEventHandler? loadedHandler = null;
                loadedHandler = (_, _) =>
                {
                    Loaded -= loadedHandler;
                    _ = ShowMusicFontInstallDialogAsync(familyName);
                };
                Loaded += loadedHandler;
                return;
            }

            _ = ShowMusicFontInstallDialogAsync(familyName);
        }

        private async System.Threading.Tasks.Task ShowMusicFontInstallDialogAsync(string familyName)
        {
            try
            {
                bool isEnglish = AppSettingsService.Instance.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = isEnglish ? "Music Font Missing" : "\u7f3a\u5c11\u97f3\u4e50\u5b57\u4f53",
                    Content = isEnglish
                        ? $"{familyName} is not installed. Open the bundled font folder and install it manually, then restart the app."
                        : $"{familyName} \u672a\u5b89\u88c5\u3002\u8bf7\u6253\u5f00\u968f\u5e94\u7528\u9644\u5e26\u7684\u5b57\u4f53\u76ee\u5f55\u624b\u52a8\u5b89\u88c5\uff0c\u7136\u540e\u91cd\u542f\u5e94\u7528\u3002",
                    PrimaryButtonText = isEnglish ? "Go to Install" : "\u524d\u5f80\u5b89\u88c5",
                    CloseButtonText = isEnglish ? "Later" : "\u7a0d\u540e",
                    DefaultButton = ContentDialogButton.Primary
                };

                ContentDialogResult result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await OpenBundledFontFolderAsync();
                }
            }
            catch
            {
            }
        }

        private async System.Threading.Tasks.Task OpenBundledFontFolderAsync()
        {
            string? fontFolderPath = GetBundledFontFolderPath();
            if (string.IsNullOrWhiteSpace(fontFolderPath) || !Directory.Exists(fontFolderPath))
            {
                return;
            }

            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(fontFolderPath);
            await Launcher.LaunchFolderAsync(folder);
        }

        private static string? GetBundledFontFolderPath()
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            return Directory.Exists(candidate) ? candidate : null;
        }

        private bool TryCreateFontFace(string fileName, string familyName, out CanvasFontSet? fontSet, out CanvasFontFace? fontFace)
        {
            fontSet = null;
            fontFace = null;
            try
            {
                if (TryCreateFontFaceFromUri(fileName, out fontSet, out fontFace))
                {
                    return true;
                }

                return TryCreateFontFaceFromSystem(familyName, out fontSet, out fontFace);
            }
            catch
            {
            }

            fontSet?.Dispose();
            fontSet = null;
            fontFace = null;
            if (TryCreateFontFaceFromUri(fileName, out fontSet, out fontFace))
            {
                return true;
            }

            return TryCreateFontFaceFromSystem(familyName, out fontSet, out fontFace);
        }

        private void LoadMusicTextFont()
        {
            if (_musicTextFontSet is IDisposable previousSet)
            {
                previousSet.Dispose();
            }

            _musicTextFontSet = null;
            _musicTextFontFace = null;

            if (TryCreateFontFaceFromUri(MusicTextFontFile, out var uriSet, out var uriFace))
            {
                _musicTextFontSet = uriSet;
                _musicTextFontFace = uriFace;
                return;
            }

            if (TryCreateFontFaceFromSystem(MusicTextFontFamily, out var systemSet, out var systemFace))
            {
                _musicTextFontSet = systemSet;
                _musicTextFontFace = systemFace;
            }
        }

        private bool TryCreateFontFaceFromUri(string fileName, out CanvasFontSet? fontSet, out CanvasFontFace? fontFace)
        {
            fontSet = null;
            fontFace = null;
            try
            {
                string relativePath = MusicFontRelativeFolder.Replace('\\', '/');
                var uri = new Uri($"ms-appx:///{relativePath}/{fileName}");
                fontSet = new CanvasFontSet(uri);
                fontFace = fontSet.Fonts.FirstOrDefault();
                return fontFace != null;
            }
            catch
            {
                fontSet = null;
                fontFace = null;
                return false;
            }
        }

        private bool TryLoadFontFromSystem(string familyName, out bool isSmufl)
        {
            isSmufl = false;
            try
            {
                var systemSet = CanvasFontSet.GetSystemFontSet();
                var face = FindSystemFontFace(systemSet, familyName);
                if (face == null) return false;

                isSmufl = face.HasCharacter((uint)SmuflGClef)
                    && face.HasCharacter((uint)SmuflFClef)
                    && face.HasCharacter((uint)SmuflAccidentalSharp);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCreateFontFaceFromSystem(string familyName, out CanvasFontSet? fontSet, out CanvasFontFace? fontFace)
        {
            fontSet = null;
            fontFace = null;
            try
            {
                fontSet = CanvasFontSet.GetSystemFontSet();
                var face = FindSystemFontFace(fontSet, familyName);
                if (face == null)
                {
                    fontSet = null;
                    return false;
                }

                fontFace = face;
                return true;
            }
            catch
            {
                fontSet = null;
                fontFace = null;
                return false;
            }
        }

        private static CanvasFontFace? FindSystemFontFace(CanvasFontSet systemSet, string familyName)
        {
            foreach (var face in systemSet.Fonts)
            {
                if (FamilyMatches(face, familyName))
                {
                    return face;
                }
            }

            return null;
        }

        private static bool FamilyMatches(CanvasFontFace face, string familyName)
        {
            foreach (var entry in face.FamilyNames)
            {
                if (string.Equals(entry.Value, familyName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void DisposeMusicFont()
        {
            if (_musicFontSet is IDisposable disposable)
            {
                disposable.Dispose();
            }

            if (_musicTextFontSet is IDisposable textDisposable)
            {
                textDisposable.Dispose();
            }

            _musicFontSet = null;
            _musicFontFace = null;
            _musicTextFontSet = null;
            _musicTextFontFace = null;
        }

        private static bool TryParseMusicFontTag(string? tag, out string fileName, out string familyName)
        {
            fileName = string.Empty;
            familyName = string.Empty;
            if (string.IsNullOrWhiteSpace(tag)) return false;

            var parts = tag.Split('|');
            if (parts.Length != 2) return false;

            fileName = parts[0].Trim();
            familyName = parts[1].Trim();
            return !string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(familyName);
        }

        private void DrawMissingFontNotice(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, float width)
        {
            if (string.IsNullOrWhiteSpace(_musicFontStatus)) return;

            var format = new CanvasTextFormat
            {
                FontFamily = "Segoe UI",
                FontSize = 14
            };

            float x = _staffLeft;
            float y = 8f;
            ds.DrawText(_musicFontStatus, x, y, Colors.IndianRed, format);
        }

        private static Color GetAccentColor()
        {
            try
            {
                if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var value) && value is Color color)
                {
                    return color;
                }
            }
            catch
            {
            }

            return Colors.DodgerBlue;
        }

        private Color GetNotationInkColor()
        {
            if (_forcePrintInkOnWhite)
            {
                return Colors.Black;
            }

            return ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        }

        private int GetTicksPerBeat()
        {
            if (_viewModel == null) return 0;
            return _viewModel.Project.TimeSignature.TicksPerBeat(_viewModel.Project.Ppq);
        }

        private void MarkProjectChanged(bool pushHistory = true)
        {
            if (_viewModel == null) return;
            SyncLayoutToProject();
            _viewModel.TouchProject();

            if (_isApplyingHistory) return;

            if (pushHistory)
            {
                PushHistorySnapshot();
            }
            else
            {
                _pendingHistoryCommitFromDrag = true;
            }
        }

        private void SyncLayoutToProject()
        {
            if (_viewModel == null) return;

            _viewModel.Project.LayoutMeasuresPerSystemOverride = Math.Max(0, _displayMeasuresPerSystemOverride);
            _viewModel.Project.LayoutAutoMeasuresPerSystem = Math.Max(1, _autoMeasuresPerSystem);
            _viewModel.Project.LayoutSystemMeasureCounts = _systemMeasureCounts
                .Select(v => Math.Max(1, v))
                .ToList();
            _viewModel.Project.LayoutBarlineOffsets = _barlineOffsets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private void RestoreLayoutFromProject()
        {
            if (_viewModel == null) return;

            _displayMeasuresPerSystemOverride = Math.Max(0, _viewModel.Project.LayoutMeasuresPerSystemOverride);
            _autoMeasuresPerSystem = Math.Max(1, _viewModel.Project.LayoutAutoMeasuresPerSystem > 0
                ? _viewModel.Project.LayoutAutoMeasuresPerSystem
                : GetDefaultMeasuresPerSystemForTimeSignature(_viewModel.TimeSigNumerator, _viewModel.TimeSigDenominator));

            _systemMeasureCounts.Clear();
            if (_viewModel.Project.LayoutSystemMeasureCounts != null)
            {
                foreach (int count in _viewModel.Project.LayoutSystemMeasureCounts)
                {
                    _systemMeasureCounts.Add(Math.Max(1, count));
                }
            }

            _barlineOffsets.Clear();
            if (_viewModel.Project.LayoutBarlineOffsets != null)
            {
                foreach (var kvp in _viewModel.Project.LayoutBarlineOffsets)
                {
                    _barlineOffsets[kvp.Key] = kvp.Value;
                }
            }
        }

        private void ResetHistoryState()
        {
            _historyStates.Clear();
            _historyIndex = -1;
            _pendingHistoryCommitFromDrag = false;
            PushHistorySnapshot(force: true);
            UpdateUndoRedoButtons();
        }

        private void PushHistorySnapshot(bool force = false)
        {
            if (_viewModel == null || _isApplyingHistory) return;

            var snapshot = CaptureHistoryState();
            if (!force && _historyIndex >= 0 && _historyIndex < _historyStates.Count)
            {
                if (HistoryStatesEqual(_historyStates[_historyIndex], snapshot))
                {
                    UpdateUndoRedoButtons();
                    return;
                }
            }

            if (_historyIndex < _historyStates.Count - 1)
            {
                _historyStates.RemoveRange(_historyIndex + 1, _historyStates.Count - (_historyIndex + 1));
            }

            _historyStates.Add(snapshot);
            if (_historyStates.Count > 128)
            {
                _historyStates.RemoveAt(0);
            }

            _historyIndex = _historyStates.Count - 1;
            UpdateUndoRedoButtons();
        }

        private EditorHistoryState CaptureHistoryState()
        {
            return new EditorHistoryState
            {
                ProjectJson = JsonSerializer.Serialize(_viewModel!.Project, HistoryJsonOptions),
                ManualAdditionalSystems = _manualAdditionalSystems,
                ManualMeasureCount = _manualMeasureCount,
                BarlineOffsets = _barlineOffsets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                SystemMeasureCounts = _systemMeasureCounts.ToList()
            };
        }

        private static bool HistoryStatesEqual(EditorHistoryState a, EditorHistoryState b)
        {
            if (!string.Equals(a.ProjectJson, b.ProjectJson, StringComparison.Ordinal))
            {
                return false;
            }

            if (a.ManualAdditionalSystems != b.ManualAdditionalSystems
                || a.ManualMeasureCount != b.ManualMeasureCount
                || a.BarlineOffsets.Count != b.BarlineOffsets.Count
                || a.SystemMeasureCounts.Count != b.SystemMeasureCounts.Count)
            {
                return false;
            }

            for (int i = 0; i < a.SystemMeasureCounts.Count; i++)
            {
                if (a.SystemMeasureCounts[i] != b.SystemMeasureCounts[i])
                {
                    return false;
                }
            }

            foreach (var kvp in a.BarlineOffsets)
            {
                if (!b.BarlineOffsets.TryGetValue(kvp.Key, out float value)) return false;
                if (Math.Abs(value - kvp.Value) > 0.0001f) return false;
            }

            return true;
        }

        private void UndoAction()
        {
            if (_historyIndex <= 0 || _historyStates.Count == 0) return;
            _historyIndex--;
            ApplyHistoryState(_historyStates[_historyIndex]);
        }

        private void RedoAction()
        {
            if (_historyIndex >= _historyStates.Count - 1 || _historyStates.Count == 0) return;
            _historyIndex++;
            ApplyHistoryState(_historyStates[_historyIndex]);
        }

        private void ApplyHistoryState(EditorHistoryState state)
        {
            if (_viewModel == null) return;

            ScoreProject? project = null;
            try
            {
                project = JsonSerializer.Deserialize<ScoreProject>(state.ProjectJson, HistoryJsonOptions);
            }
            catch
            {
            }

            if (project == null) return;

            _isApplyingHistory = true;
            try
            {
                _viewModel.LoadProjectSnapshot(project);
                _manualAdditionalSystems = Math.Max(0, state.ManualAdditionalSystems);
                _manualMeasureCount = Math.Max(0, state.ManualMeasureCount);
                _barlineOffsets.Clear();
                foreach (var kvp in state.BarlineOffsets)
                {
                    _barlineOffsets[kvp.Key] = kvp.Value;
                }
                _systemMeasureCounts.Clear();
                foreach (int count in state.SystemMeasureCounts)
                {
                    _systemMeasureCounts.Add(Math.Max(1, count));
                }

                ClearSelection();
                SyncPendingNoteTypeFromControls();
                SetTimeSignatureSelection(_viewModel.TimeSigNumerator, _viewModel.TimeSigDenominator);
                SetKeySignatureSelection(_viewModel.KeySignatureFifths);
                SetTempoSelection(_viewModel.Bpm);
                SetSnapSelection(_viewModel.SnapDivision);
                EnsureDefaultNoteLengthSelection();
            }
            finally
            {
                _isApplyingHistory = false;
            }

            StaffCanvas.Invalidate();
            UpdateUndoRedoButtons();
        }

        private void UpdateUndoRedoButtons()
        {
            if (UndoButton != null)
            {
                UndoButton.IsEnabled = _historyIndex > 0;
            }

            if (RedoButton != null)
            {
                RedoButton.IsEnabled = _historyIndex >= 0 && _historyIndex < _historyStates.Count - 1;
            }
        }

        private void ClearSelection()
        {
            if (_viewModel == null) return;

            foreach (var note in _viewModel.Project.Notes)
            {
                note.IsSelected = false;
            }
            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                mark.IsSelected = false;
            }
            _activeNote = null;
            _activeExpressionMark = null;
            _expressionDragMode = ExpressionDragMode.Move;
            _expressionDragOffsetX = 0f;
            _expressionDragOffsetY = 0f;
            UpdateNoteStepPanel();
        }

        private void DeleteSelected()
        {
            if (_viewModel == null) return;

            int noteRemoved = _viewModel.Project.Notes.RemoveAll(n => n.IsSelected);
            int markRemoved = _viewModel.Project.ExpressionMarks.RemoveAll(m => m.IsSelected);
            if (noteRemoved == 0 && markRemoved == 0) return;

            _activeNote = null;
            _activeExpressionMark = null;
            _expressionDragOffsetX = 0f;
            _expressionDragOffsetY = 0f;
            SyncSlurSlopeControlFromSelection();
            MarkProjectChanged();
            UpdateContextMenuState();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private static NoteEvent CloneNote(NoteEvent source)
        {
            return new NoteEvent
            {
                Midi = source.Midi,
                StartTick = source.StartTick,
                DurationTicks = source.DurationTicks,
                BaseDurationTicks = source.BaseDurationTicks,
                AugmentationDots = source.AugmentationDots,
                IsRest = source.IsRest,
                Voice = source.Voice,
                Accidental = source.Accidental,
                IsStaccato = source.IsStaccato,
                IsStaccatissimo = source.IsStaccatissimo,
                IsAccent = source.IsAccent,
                Ornament = source.Ornament,
                OrnamentOffsetX = source.OrnamentOffsetX,
                OrnamentOffsetY = source.OrnamentOffsetY,
                GraceOrnamentOffsetX = source.GraceOrnamentOffsetX,
                GraceOrnamentOffsetY = source.GraceOrnamentOffsetY,
                TieStart = source.TieStart,
                TieEnd = source.TieEnd,
                BeamGroupId = source.BeamGroupId,
                StemUpOverride = source.StemUpOverride,
                PreferTrebleStaff = source.PreferTrebleStaff,
                IsSelected = false
            };
        }

        private static ExpressionMark CloneExpressionMark(ExpressionMark source)
        {
            return new ExpressionMark
            {
                Code = source.Code,
                StartTick = source.StartTick,
                StaffStepOffset = source.StaffStepOffset,
                SpanBeats = source.SpanBeats,
                ShapeHeightSteps = source.ShapeHeightSteps,
                SlopeSteps = source.SlopeSteps,
                IsSelected = false
            };
        }

        private void CopySelection()
        {
            if (_viewModel == null) return;

            var selectedNotes = _viewModel.Project.Notes.Where(n => n.IsSelected).ToList();
            var selectedMarks = _viewModel.Project.ExpressionMarks.Where(m => m.IsSelected).ToList();
            if (selectedNotes.Count == 0 && selectedMarks.Count == 0)
            {
                return;
            }

            int minStartTick = int.MaxValue;
            if (selectedNotes.Count > 0)
            {
                minStartTick = Math.Min(minStartTick, selectedNotes.Min(n => n.StartTick));
            }

            if (selectedMarks.Count > 0)
            {
                minStartTick = Math.Min(minStartTick, selectedMarks.Min(m => m.StartTick));
            }

            minStartTick = Math.Max(0, minStartTick == int.MaxValue ? 0 : minStartTick);

            _clipboard = new EditorClipboardState
            {
                Notes = selectedNotes
                    .Select(n => new ClipboardNoteItem
                    {
                        Note = CloneNote(n),
                        StartOffset = n.StartTick - minStartTick
                    })
                    .OrderBy(item => item.StartOffset)
                    .ToList(),
                Marks = selectedMarks
                    .Select(m => new ClipboardExpressionItem
                    {
                        Mark = CloneExpressionMark(m),
                        StartOffset = m.StartTick - minStartTick
                    })
                    .OrderBy(item => item.StartOffset)
                    .ToList()
            };

            UpdateContextMenuState();
            _viewModel.SetStatus("宸插鍒堕€変腑鍐呭");
        }

        private void CutSelection()
        {
            if (_viewModel == null || !HasAnySelection())
            {
                return;
            }

            CopySelection();
            DeleteSelected();
            _viewModel.SetStatus("宸插壀鍒囬€変腑鍐呭");
        }

        private void PasteSelection(Point? anchorPoint)
        {
            if (_viewModel == null || !HasClipboardSelection())
            {
                return;
            }

            int anchorTick;
            if (anchorPoint.HasValue && anchorPoint.Value.X >= 0d && anchorPoint.Value.Y >= 0d)
            {
                int anchorSystem = GetSystemIndexFromY((float)anchorPoint.Value.Y);
                anchorTick = SnapTick(anchorPoint.Value.X, anchorSystem);
            }
            else
            {
                int? selectedStartTick = _viewModel.Project.Notes
                    .Where(n => n.IsSelected)
                    .Select(n => (int?)n.StartTick)
                    .Concat(_viewModel.Project.ExpressionMarks.Where(m => m.IsSelected).Select(m => (int?)m.StartTick))
                    .Min();
                anchorTick = Math.Max(0, selectedStartTick ?? 0);
            }

            ClearSelection();

            int maxBeamGroupId = _viewModel.Project.Notes.Count == 0 ? 0 : _viewModel.Project.Notes.Max(n => n.BeamGroupId);
            var beamGroupMap = new Dictionary<int, int>();
            int copiedCount = 0;

            if (_clipboard != null)
            {
                foreach (var item in _clipboard.Notes)
                {
                    var note = CloneNote(item.Note);
                    note.StartTick = Math.Max(0, anchorTick + item.StartOffset);
                    if (note.BeamGroupId > 0)
                    {
                        if (!beamGroupMap.TryGetValue(note.BeamGroupId, out int mappedId))
                        {
                            mappedId = ++maxBeamGroupId;
                            beamGroupMap[note.BeamGroupId] = mappedId;
                        }

                        note.BeamGroupId = mappedId;
                    }

                    note.IsSelected = true;
                    _viewModel.Project.Notes.Add(note);
                    copiedCount++;
                }

                foreach (var item in _clipboard.Marks)
                {
                    var mark = CloneExpressionMark(item.Mark);
                    mark.StartTick = Math.Max(0, anchorTick + item.StartOffset);
                    mark.IsSelected = true;
                    _viewModel.Project.ExpressionMarks.Add(mark);
                    copiedCount++;
                }
            }

            if (copiedCount == 0)
            {
                return;
            }

            MarkProjectChanged();
            SyncNoteTypeControlsFromSelectedNotes();
            SyncSlurSlopeControlFromSelection();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
            _viewModel.SetStatus("\u5df2\u7c98\u8d34");
        }

        private void CopyKey_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            CopySelection();
            args.Handled = true;
        }

        private void CutKey_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            CutSelection();
            args.Handled = true;
        }

        private void PasteKey_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            PasteSelection(_lastPointerCanvasPoint.X >= 0d && _lastPointerCanvasPoint.Y >= 0d
                ? _lastPointerCanvasPoint
                : null);
            args.Handled = true;
        }

        private void DeleteKey_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            DeleteSelected();
            args.Handled = true;
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            UndoAction();
        }

        private void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            RedoAction();
        }

        private void UndoKey_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            UndoAction();
            args.Handled = true;
        }

        private void RedoKey_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            RedoAction();
            args.Handled = true;
        }

        private void AKey_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (IsTextInputFocused()) return;
            if (CycleSelectedNoteLength(-1))
            {
                args.Handled = true;
            }
        }

        private void DKey_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (IsTextInputFocused()) return;
            if (CycleSelectedNoteLength(1))
            {
                args.Handled = true;
            }
        }

        private bool IsTextInputFocused()
        {
            var focused = FocusManager.GetFocusedElement(XamlRoot);
            return focused is TextBox
                || focused is RichEditBox
                || focused is AutoSuggestBox
                || focused is PasswordBox;
        }

        private bool CycleSelectedNoteLength(int direction)
        {
            if (_viewModel == null || direction == 0) return false;

            var selected = _viewModel.Project.Notes.Where(n => n.IsSelected).ToList();
            if (selected.Count == 0) return false;

            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            int step = direction > 0 ? 1 : -1;
            NoteLength firstAppliedLength = NoteLength.Quarter;
            bool capturedFirst = false;

            foreach (var note in selected)
            {
                int baseDuration = note.BaseDurationTicks;
                if (baseDuration <= 0)
                {
                    baseDuration = note.AugmentationDots > 0
                        ? InferBaseDurationFromDot(note.DurationTicks, note.AugmentationDots)
                        : Math.Max(1, note.DurationTicks);
                }

                int currentIndex = GetClosestNoteLengthCycleIndex(baseDuration, ppq);
                int nextIndex = (currentIndex + step + NoteLengthCycleOrder.Length) % NoteLengthCycleOrder.Length;
                NoteLength nextLength = NoteLengthCycleOrder[nextIndex];
                int newBaseDuration = Math.Max(1, NoteLengthUtils.ToTicks(nextLength, ppq));

                note.BaseDurationTicks = newBaseDuration;
                note.DurationTicks = ApplyAugmentationDotsToDuration(newBaseDuration, note.AugmentationDots);

                if (!capturedFirst)
                {
                    firstAppliedLength = nextLength;
                    capturedFirst = true;
                }
            }

            _viewModel.SelectedNoteLength = firstAppliedLength;
            MarkProjectChanged();
            StaffCanvas.Invalidate();
            SyncNoteTypeControlsFromSelectedNotes();
            UpdateNoteStepPanel();
            return true;
        }

        private static int GetClosestNoteLengthCycleIndex(int baseDurationTicks, int ppq)
        {
            int safeBase = Math.Max(1, baseDurationTicks);
            int bestIndex = 0;
            int bestDelta = int.MaxValue;

            for (int i = 0; i < NoteLengthCycleOrder.Length; i++)
            {
                int candidateTicks = Math.Max(1, NoteLengthUtils.ToTicks(NoteLengthCycleOrder[i], ppq));
                int delta = Math.Abs(candidateTicks - safeBase);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int ApplyAugmentationDotsToDuration(int baseDurationTicks, int dotCount)
        {
            int duration = Math.Max(1, baseDurationTicks);
            int remainder = duration;
            int safeDotCount = Math.Max(0, dotCount);
            for (int i = 0; i < safeDotCount; i++)
            {
                remainder = Math.Max(0, remainder / 2);
                if (remainder <= 0)
                {
                    break;
                }

                duration += remainder;
            }

            return Math.Max(1, duration);
        }

        private static Rect NormalizeRect(Point a, Point b)
        {
            double x = Math.Min(a.X, b.X);
            double y = Math.Min(a.Y, b.Y);
            double width = Math.Abs(a.X - b.X);
            double height = Math.Abs(a.Y - b.Y);
            return new Rect(x, y, width, height);
        }

        private void SelectByRectangle(Rect rect)
        {
            if (_viewModel == null) return;
            if (rect.Width < 2 || rect.Height < 2) return;

            foreach (var note in _viewModel.Project.Notes)
            {
                note.IsSelected = false;
            }

            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                mark.IsSelected = false;
            }

            foreach (var note in _viewModel.Project.Notes)
            {
                int systemIndex = GetSystemIndexForTick(note.StartTick);
                float x = GetNoteX(note.StartTick);
                float y = GetNoteVisualY(note, systemIndex);
                Rect bounds = new Rect(x - HitRadius, y - HitRadius, HitRadius * 2, HitRadius * 2);
                if (RectIntersects(rect, bounds))
                {
                    note.IsSelected = true;
                }
            }

            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                string code = NormalizeExpressionCode(mark.Code);
                if (code == "ottava")
                {
                    for (int systemIndex = 0; systemIndex < Math.Max(1, _systemCount); systemIndex++)
                    {
                        int measuresInSystem = GetMeasuresInSystem(systemIndex);
                        if (TryGetOttavaSegmentBounds(mark, systemIndex, measuresInSystem, out Rect segmentBounds)
                            && RectIntersects(rect, segmentBounds))
                        {
                            mark.IsSelected = true;
                            break;
                        }
                    }
                }
                else
                {
                    int systemIndex = GetSystemIndexForTick(mark.StartTick);
                    float bottomLineY = GetSystemBottomLineY(systemIndex);
                    float x = GetNoteX(mark.StartTick);
                    float y = StaffStepOffsetToY(mark.StaffStepOffset, bottomLineY);
                    Rect bounds = GetExpressionMarkBounds(mark, x, y);
                    if (RectIntersects(rect, bounds))
                    {
                        mark.IsSelected = true;
                    }
                }
            }

            SyncNoteTypeControlsFromSelectedNotes();
            SyncSlurSlopeControlFromSelection();
            UpdateNoteStepPanel();
        }

        private bool ShouldPlaceSlurAbove(IReadOnlyList<NoteEvent> notes, int systemIndex)
        {
            if (notes.Count == 0)
            {
                return true;
            }

            int trebleVotes = 0;
            int bassVotes = 0;
            foreach (var note in notes)
            {
                int naturalMidi = GetNaturalMidiForDisplay(note.Midi, note.Accidental);
                if (ResolveNoteStaffPreference(note, systemIndex, naturalMidi))
                {
                    trebleVotes++;
                }
                else
                {
                    bassVotes++;
                }
            }

            if (trebleVotes == bassVotes)
            {
                float splitY = (GetSystemTrebleBottom(systemIndex) + GetSystemBassTop(systemIndex)) * 0.5f;
                float avgY = notes.Average(n => GetNoteVisualY(n, systemIndex));
                return avgY <= splitY;
            }

            return trebleVotes >= bassVotes;
        }

        private bool TryInsertSlurForSelectedNotes()
        {
            if (_viewModel == null)
            {
                return false;
            }

            var selectedNotes = _viewModel.Project.Notes
                .Where(n => n.IsSelected && !n.IsRest)
                .OrderBy(n => n.StartTick)
                .ThenBy(n => n.Midi)
                .ToList();
            if (selectedNotes.Count < 2)
            {
                return false;
            }

            int firstTick = selectedNotes.Min(n => Math.Max(0, n.StartTick));
            int lastTick = selectedNotes.Max(n => Math.Max(0, n.StartTick));
            if (lastTick <= firstTick && selectedNotes.All(n => Math.Max(1, n.DurationTicks) <= 1))
            {
                return false;
            }

            int startSystem = GetSystemIndexForTick(firstTick);
            float bottomLineY = GetSystemBottomLineY(startSystem);
            var sameSystemSelected = selectedNotes
                .Where(n => GetSystemIndexForTick(n.StartTick) == startSystem)
                .ToList();
            var ySource = sameSystemSelected.Count > 0
                ? sameSystemSelected
                : selectedNotes.Where(n => n.StartTick == firstTick).ToList();
            if (ySource.Count == 0)
            {
                ySource = new List<NoteEvent> { selectedNotes[0] };
            }

            bool placeAbove = ShouldPlaceSlurAbove(ySource, startSystem);
            var contourNotes = ySource
                .OrderBy(n => n.StartTick)
                .ThenBy(n => n.Midi)
                .ToList();
            float contourTopY = contourNotes.Min(n => GetNoteVisualY(n, startSystem));
            float contourBottomY = contourNotes.Max(n => GetNoteVisualY(n, startSystem));
            float edgeY = placeAbove ? contourTopY : contourBottomY;
            float contourSpanSteps = Math.Abs((contourBottomY - contourTopY) / Math.Max(0.001f, _staffGap / 2f));
            float baseClearance = _staffGap * 3.20f;
            float contourClearance = Math.Min(_staffGap * 1.05f, contourSpanSteps * _staffGap * 0.05f);
            float slurY = placeAbove
                ? edgeY - (baseClearance + contourClearance)
                : edgeY + (baseClearance + contourClearance);
            float staffStepOffset = ClampExpressionStaffStepOffset(YToStaffStepOffset(slurY, bottomLineY));
            float shapeMagnitude = Math.Clamp(
                Math.Max(1f, Math.Abs(GetDefaultExpressionShapeHeightSteps("slur"))) + contourSpanSteps * 0.12f,
                1f,
                18f);
            float shapeHeightSteps = placeAbove ? shapeMagnitude : -shapeMagnitude;

            int contourFirstTick = contourNotes.Min(n => n.StartTick);
            int contourLastTick = contourNotes.Max(n => n.StartTick);
            var firstContourCluster = contourNotes.Where(n => n.StartTick == contourFirstTick).ToList();
            var lastContourCluster = contourNotes.Where(n => n.StartTick == contourLastTick).ToList();
            float startContourY = placeAbove
                ? firstContourCluster.Min(n => GetNoteVisualY(n, startSystem))
                : firstContourCluster.Max(n => GetNoteVisualY(n, startSystem));
            float endContourY = placeAbove
                ? lastContourCluster.Min(n => GetNoteVisualY(n, startSystem))
                : lastContourCluster.Max(n => GetNoteVisualY(n, startSystem));
            float autoSlopeSteps = Math.Clamp(
                (endContourY - startContourY) / Math.Max(0.001f, _staffGap / 2f),
                -SlurAutoSlopeClampSteps,
                SlurAutoSlopeClampSteps);

            int ticksPerBeat = Math.Max(1, _ticksPerBeat > 0 ? _ticksPerBeat : GetTicksPerBeat());
            var lastTickNotes = selectedNotes.Where(n => n.StartTick == lastTick).ToList();
            int lastDurationTicks = lastTickNotes.Count > 0
                ? lastTickNotes.Max(n => Math.Max(1, n.DurationTicks))
                : Math.Max(1, selectedNotes[^1].DurationTicks);
            int minTailTicks = Math.Max(1, (int)Math.Round(ticksPerBeat * 0.35f));
            int visualTailTicks = Math.Max(minTailTicks, (int)Math.Round(lastDurationTicks * 1.35f));
            int endTick = Math.Max(firstTick + 1, lastTick + visualTailTicks);
            int spanTicks = Math.Max(1, endTick - firstTick);
            float spanBeats = Math.Clamp(spanTicks / (float)ticksPerBeat, 0.2f, 512f);

            var mark = new ExpressionMark
            {
                Code = "slur",
                StartTick = firstTick,
                StaffStepOffset = staffStepOffset,
                SpanBeats = spanBeats,
                ShapeHeightSteps = shapeHeightSteps,
                // Auto-range slur follows melodic contour by default.
                SlopeSteps = autoSlopeSteps,
                IsSelected = true
            };

            foreach (var m in _viewModel.Project.ExpressionMarks)
            {
                m.IsSelected = false;
            }

            _viewModel.Project.ExpressionMarks.Add(mark);
            MarkProjectChanged();
            _activeExpressionMark = mark;
            SyncSlurSlopeControlFromSelection();
            return true;
        }

        private bool TryInsertExpressionAtAnchor(string code)
        {
            if (_viewModel == null || !_hasPendingInsertionAnchor)
            {
                return false;
            }

            Point anchorPoint = _pendingInsertionAnchorPoint;
            if (DateTimeOffset.UtcNow - _pendingInsertionAnchorTimestampUtc > InsertionAnchorMaxAge)
            {
                ClearPendingInsertionAnchor();
                _viewModel.SetStatus("\u63d2\u5165\u951a\u70b9\u5df2\u8fc7\u671f\uff0c\u8bf7\u5148\u70b9\u51fb\u76ee\u6807\u4f4d\u7f6e\u3002");
                return false;
            }

            if (StaffCanvas == null
                || anchorPoint.X < 0
                || anchorPoint.Y < 0
                || anchorPoint.X > StaffCanvas.ActualWidth
                || anchorPoint.Y > StaffCanvas.ActualHeight)
            {
                ClearPendingInsertionAnchor();
                return false;
            }

            string normalizedCode = NormalizeExpressionCode(code);
            if (IsBarlineAnchoredScoreMark(normalizedCode))
            {
                if (!TryHitMeasureBarline(anchorPoint, out _, out _, out _, out float barlineX, out _))
                {
                    ClearPendingInsertionAnchor();
                    _viewModel.SetStatus("\u8be5\u8c31\u9762\u8bb0\u53f7\u9700\u8981\u5148\u70b9\u51fb\u76ee\u6807\u5c0f\u8282\u7ebf\u3002");
                    return false;
                }

                anchorPoint = new Point(barlineX, anchorPoint.Y);
            }

            ClearSelection();
            HideMeasureEditPanel();
            HideClefEditPanel();
            var addedMark = AddExpressionMark(anchorPoint, normalizedCode);
            _activeExpressionMark = addedMark;
            _pendingExpressionCode = null;
            ClearPendingInsertionAnchor();
            SyncSlurSlopeControlFromSelection();
            UpdateNoteStepPanel();
            return true;
        }

        private void ClearPendingInsertionAnchor()
        {
            _hasPendingInsertionAnchor = false;
            _pendingInsertionAnchorPoint = new Point(-1, -1);
            _pendingInsertionAnchorTimestampUtc = DateTimeOffset.MinValue;
        }

        private void MoveSelectedNotesByStaffStep(int stepDelta)
        {
            if (_viewModel == null || stepDelta == 0) return;

            var selected = _viewModel.Project.Notes.Where(n => n.IsSelected).ToList();
            if (selected.Count == 0) return;

            foreach (var note in selected)
            {
                if (note.IsRest)
                {
                    continue;
                }
                int naturalMidi = GetNaturalMidiForDisplay(note.Midi, note.Accidental);
                int diatonic = NaturalMidiToDiatonicIndex(naturalMidi) + stepDelta;
                int movedNaturalMidi = DiatonicIndexToNaturalMidi(diatonic);
                int movedMidi = movedNaturalMidi + GetAccidentalSemitoneOffset(note.Accidental);
                note.Midi = Math.Clamp(movedMidi, 24, 108);
            }

            MarkProjectChanged();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private void NoteStepUpButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedNotesByStaffStep(1);
        }

        private void NoteStepDownButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedNotesByStaffStep(-1);
        }

        private void NoteStepStemFlipButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            var selected = _viewModel.Project.Notes.Where(n => n.IsSelected).ToList();
            if (selected.Count == 0) return;

            var firstNotRest = selected.FirstOrDefault(n => !n.IsRest);
            if (firstNotRest == null) return;

            bool target = !(firstNotRest.StemUpOverride ?? (GetEffectiveNoteMidi(firstNotRest) < 71));
            foreach (var note in selected)
            {
                if (note.IsRest) continue;
                note.StemUpOverride = target;
            }

            MarkProjectChanged();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private void NoteStepUnbeamButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            var selectedGrouped = _viewModel.Project.Notes
                .Where(n => n.IsSelected && n.BeamGroupId > 0)
                .ToList();
            if (selectedGrouped.Count == 0) return;

            foreach (var note in selectedGrouped)
            {
                note.BeamGroupId = 0;
            }

            MarkProjectChanged();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private void NoteStepMoreButton_Click(object sender, RoutedEventArgs e)
        {
            SyncNoteTypeControlsFromSelectedNotes();
            UpdateNoteTypeMenuEnabledState();
            if (NoteTypeButton?.Flyout is not FlyoutBase flyout) return;

            if (sender is FrameworkElement element)
            {
                flyout.ShowAt(element);
            }
            else
            {
                flyout.ShowAt(NoteTypeButton);
            }
        }

        private void NoteStepDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void ExpressionDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void RepeatDirectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            var selectedRepeats = _viewModel.Project.ExpressionMarks
                .Where(m => m.IsSelected && NormalizeExpressionCode(m.Code) == ScoreMarkRepeatBarline)
                .ToList();
            if (selectedRepeats.Count == 0) return;

            foreach (var mark in selectedRepeats)
            {
                int boundaryIndex = GetNearestMeasureBoundaryIndexForTick(mark.StartTick);
                int snappedBoundaryTick = GetMeasureBoundaryTick(boundaryIndex);
                mark.StartTick = snappedBoundaryTick;
                float magnitude = Math.Max(1f, Math.Abs(mark.ShapeHeightSteps));
                bool wasStartRepeat = IsStartRepeatBarline(mark);
                int systemIndex = Math.Clamp(GetSystemIndexForMeasureIndex(boundaryIndex), 0, Math.Max(0, _systemCount - 1));
                int localBoundary = boundaryIndex - GetSystemStartMeasureIndex(systemIndex);
                if (wasStartRepeat)
                {
                    // start-repeat -> end-repeat: move to the end boundary of the same measure.
                    int targetBoundary = Math.Min(Math.Max(1, _totalMeasureCount), boundaryIndex + 1);
                    mark.StartTick = GetMeasureBoundaryTick(targetBoundary);
                    mark.ShapeHeightSteps = magnitude;
                }
                else
                {
                    // end-repeat -> start-repeat: move to the front boundary of the same measure.
                    // If this is the first measure in a system, keep it on this system head
                    // (after key/time signature), never jump to previous system.
                    int targetBoundary = localBoundary > 0 ? boundaryIndex - 1 : boundaryIndex;
                    mark.StartTick = GetMeasureBoundaryTick(Math.Max(0, targetBoundary));
                    mark.ShapeHeightSteps = -magnitude;
                }
            }

            MarkProjectChanged();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private void RepeatDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void OttavaDirectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            var selectedOttavas = _viewModel.Project.ExpressionMarks
                .Where(m => m.IsSelected && NormalizeExpressionCode(m.Code) == "ottava")
                .ToList();
            if (selectedOttavas.Count == 0) return;

            foreach (var mark in selectedOttavas)
            {
                float magnitude = Math.Max(1f, Math.Abs(mark.ShapeHeightSteps));
                mark.ShapeHeightSteps = IsOttavaUp(mark) ? -magnitude : magnitude;
            }

            MarkProjectChanged();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private void OttavaDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void MeasureAddButton_Click(object sender, RoutedEventArgs e)
        {
            if (_measurePanelMeasureIndex < 0)
            {
                return;
            }

            InsertMeasureAfter(_measurePanelMeasureIndex);
            HideMeasureEditPanel();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private void MeasureDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_measurePanelMeasureIndex < 0)
            {
                return;
            }

            DeleteMeasure(_measurePanelMeasureIndex);
            HideMeasureEditPanel();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private void MeasureAddSystemButton_Click(object sender, RoutedEventArgs e)
        {
            if (_measurePanelSystemIndex < 0 || _measurePanelBoundaryLocalIndex < 0)
            {
                return;
            }

            int measuresInSystem = GetMeasuresInSystem(_measurePanelSystemIndex);
            if (_measurePanelBoundaryLocalIndex < measuresInSystem)
            {
                return;
            }

            AddSystemAtEnd();
            HideMeasureEditPanel();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private void MeasureDeleteSystemButton_Click(object sender, RoutedEventArgs e)
        {
            if (_measurePanelSystemIndex < 0)
            {
                return;
            }

            DeleteSystemAt(_measurePanelSystemIndex);
            HideMeasureEditPanel();
            StaffCanvas.Invalidate();
            UpdateNoteStepPanel();
        }

        private static bool RectIntersects(Rect a, Rect b)
        {
            return a.X <= b.X + b.Width
                && a.X + a.Width >= b.X
                && a.Y <= b.Y + b.Height
                && a.Y + a.Height >= b.Y;
        }

        private void ExpressionMarkMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item) return;

            string code = NormalizeExpressionCode(item.Tag?.ToString());
            _pendingExpressionCode = code;
            if (!string.IsNullOrWhiteSpace(code))
            {
                TryInsertExpressionAtAnchor(code);
            }
            SwitchToNoNoteLengthIfNeeded();
        }

        private void SlurToolButton_Click(object sender, RoutedEventArgs e)
        {
            _pendingExpressionCode = "slur";
            _pendingSlurGesture = false;
            _pendingSlurGestureRectMode = false;
            SwitchToNoNoteLengthIfNeeded();
        }

        private async void PlayMidiButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            if (_isPlaybackPaused && _playbackTotalTicks > 0)
            {
                ResumePlayback();
                return;
            }

            try
            {
                EnsureDefaultNoteLengthSelection();
                if (_isPlaybackRunning)
                {
                    StopPlaybackInternal(resetPosition: false);
                }
                _midiExporter.Export(_viewModel.Project, _playbackMidiPath);
                await EnsureMidiSynthAsync();
                BuildPlaybackEvents();
                StartPlaybackFromTick(0);
                _viewModel.SelectedNoteLength = NoteLength.None;
            }
            catch (Exception ex)
            {
                _viewModel.SetStatus($"鎾斁澶辫触: {ex.Message}");
            }
        }

        private void PauseMidiButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPlaybackRunning) return;
            _isPlaybackRunning = false;
            _isPlaybackPaused = true;
            _playbackTimer?.Stop();
            StopAllActivePlaybackNotes();
            UpdatePlaybackProgressBar();
            StaffCanvas.Invalidate();
        }

        private void StopMidiButton_Click(object sender, RoutedEventArgs e)
        {
            StopPlaybackInternal(resetPosition: true);
        }

        private async System.Threading.Tasks.Task EnsureMidiSynthAsync()
        {
            _midiSynth ??= await MidiSynthesizer.CreateAsync();
        }

        private void BuildPlaybackEvents()
        {
            if (_viewModel == null) return;

            _playbackEvents.Clear();
            _playbackNoteSpans.Clear();
            _playbackCursorPoints.Clear();
            _playbackEventIndex = 0;
            _playbackCurrentTick = 0;
            _playbackTotalTicks = 0;
            _playbackTicksPerSecond = 1d;

            int ticksPerBeat = Math.Max(1, _viewModel.Project.TimeSignature.TicksPerBeat(_viewModel.Project.Ppq));
            int bpm = Math.Max(20, _viewModel.Bpm);
            _playbackTicksPerSecond = bpm * ticksPerBeat / 60d;
            var repeatSegment = TryGetPlaybackRepeatSegment();
            int repeatDuration = repeatSegment?.DurationTicks ?? 0;
            var ending1Ranges = GetPlaybackEndingRanges(ScoreMarkEnding1);
            var ending2Ranges = GetPlaybackEndingRanges(ScoreMarkEnding2);

            int sourceScoreEndTick = GetMeasureBoundaryTick(Math.Max(1, _totalMeasureCount));
            int playbackScoreEndTick = sourceScoreEndTick;
            if (repeatSegment.HasValue && sourceScoreEndTick >= repeatSegment.Value.EndTick)
            {
                playbackScoreEndTick += repeatDuration;
            }

            static void AddCursorPoint(List<PlaybackCursorPoint> points, int playbackTick, int sourceTick)
            {
                points.Add(new PlaybackCursorPoint(Math.Max(0, playbackTick), Math.Max(0, sourceTick)));
            }

            int MapFirstPassTick(int sourceTick)
            {
                int safe = Math.Max(0, sourceTick);
                if (!repeatSegment.HasValue) return safe;
                var segment = repeatSegment.Value;
                return safe >= segment.EndTick ? safe + repeatDuration : safe;
            }

            var pedalRanges = GetPlaybackPedalRanges(repeatSegment, ending1Ranges, ending2Ranges, playbackScoreEndTick);

            foreach (var note in _viewModel.Project.Notes
                .Where(n => n.DurationTicks > 0)
                .OrderBy(n => n.StartTick)
                .ThenBy(n => n.IsRest ? 1 : 0)
                .ThenBy(n => n.Midi))
            {
                int sourceStart = Math.Max(0, note.StartTick);
                int sourceDuration = Math.Max(1, note.DurationTicks);
                bool inEnding1 = IsTickInRanges(sourceStart, ending1Ranges);
                bool inEnding2 = IsTickInRanges(sourceStart, ending2Ranges);

                int firstStart = MapFirstPassTick(sourceStart);
                bool includeFirstPass = !inEnding2;
                int ottavaTranspose = GetPlaybackOttavaSemitoneOffsetAtTick(sourceStart);
                if (includeFirstPass)
                {
                    AddPlaybackNoteWithOrnament(note, firstStart, sourceDuration, ticksPerBeat, pedalRanges, ottavaTranspose);
                    AddCursorPoint(_playbackCursorPoints, firstStart, sourceStart);
                }

                if (repeatSegment.HasValue)
                {
                    var segment = repeatSegment.Value;
                    bool inRepeatBody = sourceStart >= segment.StartTick && sourceStart < segment.EndTick;
                    if (inRepeatBody && !inEnding1)
                    {
                        int secondStart = segment.EndTick + (sourceStart - segment.StartTick);
                        AddPlaybackNoteWithOrnament(note, secondStart, sourceDuration, ticksPerBeat, pedalRanges, ottavaTranspose);
                        AddCursorPoint(_playbackCursorPoints, secondStart, sourceStart);
                    }
                }
            }

            // Keep playback cursor moving during silent bars too.
            for (int i = 0; i <= Math.Max(1, _totalMeasureCount); i++)
            {
                int sourceBoundary = GetMeasureBoundaryTick(i);
                AddCursorPoint(_playbackCursorPoints, MapFirstPassTick(sourceBoundary), sourceBoundary);

                if (repeatSegment.HasValue
                    && sourceBoundary >= repeatSegment.Value.StartTick
                    && sourceBoundary < repeatSegment.Value.EndTick)
                {
                    int secondPassTick = repeatSegment.Value.EndTick + (sourceBoundary - repeatSegment.Value.StartTick);
                    AddCursorPoint(_playbackCursorPoints, secondPassTick, sourceBoundary);
                }
            }

            _playbackTotalTicks = Math.Max(_playbackTotalTicks, playbackScoreEndTick);

            _playbackEvents.Sort((a, b) =>
            {
                int tickCompare = a.Tick.CompareTo(b.Tick);
                return tickCompare != 0 ? tickCompare : a.Order.CompareTo(b.Order);
            });

            if (_playbackCursorPoints.Count > 0)
            {
                _playbackCursorPoints.Sort((a, b) =>
                {
                    int tickCompare = a.PlaybackTick.CompareTo(b.PlaybackTick);
                    return tickCompare != 0 ? tickCompare : a.SourceTick.CompareTo(b.SourceTick);
                });

                int index = 1;
                while (index < _playbackCursorPoints.Count)
                {
                    if (_playbackCursorPoints[index].PlaybackTick == _playbackCursorPoints[index - 1].PlaybackTick)
                    {
                        _playbackCursorPoints.RemoveAt(index);
                    }
                    else
                    {
                        index++;
                    }
                }
            }

            UpdatePlaybackProgressBar();
        }

        private int GetPlaybackOttavaSemitoneOffsetAtTick(int sourceTick)
        {
            if (_viewModel == null)
            {
                return 0;
            }

            int safeTick = Math.Max(0, sourceTick);
            int offset = 0;
            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                if (NormalizeExpressionCode(mark.Code) != "ottava")
                {
                    continue;
                }

                int start = Math.Max(0, mark.StartTick);
                int end = GetOttavaEndTick(mark);
                if (safeTick >= start && safeTick < end)
                {
                    offset += IsOttavaUp(mark) ? 12 : -12;
                }
            }

            return offset;
        }

        private void AddPlaybackNoteWithOrnament(
            NoteEvent note,
            int startTick,
            int durationTicks,
            int ticksPerBeat,
            IReadOnlyList<PlaybackPedalRange> pedalRanges,
            int semitoneTranspose)
        {
            if (note.IsRest)
            {
                _playbackTotalTicks = Math.Max(_playbackTotalTicks, Math.Max(0, startTick) + Math.Max(1, durationTicks));
                return;
            }

            int baseMidi = Math.Clamp(GetEffectiveNoteMidi(note) + semitoneTranspose, 0, 127);
            int baseVelocity = GetPlaybackVelocityForNote(note, note.StartTick);
            int safeStart = Math.Max(0, startTick);
            int safeDuration = Math.Max(1, durationTicks);

            switch (note.Ornament)
            {
                case NoteOrnament.Trill:
                {
                    int aux = Math.Clamp(baseMidi + 2, 0, 127);
                    float durationBeats = safeDuration / (float)Math.Max(1, ticksPerBeat);
                    int slicesPerBeat = durationBeats >= 4f ? 10 : (durationBeats >= 2f ? 8 : (durationBeats >= 1f ? 6 : 4));
                    int unit = Math.Max(1, ticksPerBeat / slicesPerBeat);
                    int cursor = safeStart;
                    bool useAux = false;
                    while (cursor < safeStart + safeDuration)
                    {
                        int span = Math.Min(unit, safeStart + safeDuration - cursor);
                        AddSinglePlaybackNote(note, cursor, span, useAux ? aux : baseMidi, 92, pedalRanges, applyArticulation: false);
                        cursor += span;
                        useAux = !useAux;
                    }
                    return;
                }
                case NoteOrnament.UpperMordent:
                case NoteOrnament.LowerMordent:
                {
                    int aux = note.Ornament == NoteOrnament.UpperMordent
                        ? Math.Clamp(baseMidi + 2, 0, 127)
                        : Math.Clamp(baseMidi - 2, 0, 127);
                    int unit = Math.Max(1, ticksPerBeat / 12);
                    int lead = Math.Min(unit, safeDuration);
                    int middle = Math.Min(unit, Math.Max(0, safeDuration - lead));
                    int tail = Math.Max(1, safeDuration - lead - middle);
                    AddSinglePlaybackNote(note, safeStart, lead, baseMidi, 96, pedalRanges);
                    AddSinglePlaybackNote(note, safeStart + lead, middle, aux, 92, pedalRanges);
                    AddSinglePlaybackNote(note, safeStart + lead + middle, tail, baseMidi, baseVelocity, pedalRanges);
                    return;
                }
                case NoteOrnament.Turn:
                case NoteOrnament.InvertedTurn:
                {
                    int up = Math.Clamp(baseMidi + 2, 0, 127);
                    int down = Math.Clamp(baseMidi - 2, 0, 127);
                    int unit = Math.Max(1, ticksPerBeat / 12);
                    int[] pattern = note.Ornament == NoteOrnament.Turn
                        ? new[] { up, baseMidi, down, baseMidi }
                        : new[] { down, baseMidi, up, baseMidi };
                    int cursor = safeStart;
                    for (int i = 0; i < pattern.Length; i++)
                    {
                        int remain = safeStart + safeDuration - cursor;
                        if (remain <= 0) break;
                        int span = (i == pattern.Length - 1) ? remain : Math.Min(unit, remain);
                        AddSinglePlaybackNote(note, cursor, span, pattern[i], i == pattern.Length - 1 ? baseVelocity : 92, pedalRanges);
                        cursor += span;
                    }
                    return;
                }
                case NoteOrnament.Appoggiatura:
                {
                    int grace = Math.Clamp(Math.Max(1, ticksPerBeat / 6), 1, Math.Max(1, safeDuration - 1));
                    int aux = Math.Clamp(baseMidi + 2, 0, 127);
                    AddSinglePlaybackNote(note, safeStart, grace, aux, 88, pedalRanges);
                    AddSinglePlaybackNote(note, safeStart + grace, Math.Max(1, safeDuration - grace), baseMidi, baseVelocity, pedalRanges);
                    return;
                }
                case NoteOrnament.Acciaccatura:
                {
                    int grace = Math.Clamp(Math.Max(1, ticksPerBeat / 10), 1, Math.Max(1, safeDuration / 3));
                    int aux = Math.Clamp(baseMidi + 1, 0, 127);
                    int graceStart = Math.Max(0, safeStart - grace);
                    AddSinglePlaybackNote(note, graceStart, grace, aux, 86, pedalRanges);
                    AddSinglePlaybackNote(note, safeStart, safeDuration, baseMidi, baseVelocity, pedalRanges);
                    return;
                }
                case NoteOrnament.TremoloSingle:
                case NoteOrnament.TremoloDouble:
                {
                    int divisor = note.Ornament == NoteOrnament.TremoloDouble ? 16 : 8;
                    int unit = Math.Max(1, ticksPerBeat / divisor);
                    int cursor = safeStart;
                    while (cursor < safeStart + safeDuration)
                    {
                        int span = Math.Min(unit, safeStart + safeDuration - cursor);
                        AddSinglePlaybackNote(note, cursor, span, baseMidi, 98, pedalRanges);
                        cursor += span;
                    }
                    return;
                }
                default:
                    AddSinglePlaybackNote(note, safeStart, safeDuration, baseMidi, baseVelocity, pedalRanges);
                    return;
            }
        }

        private void AddSinglePlaybackNote(
            NoteEvent source,
            int startTick,
            int durationTicks,
            int midi,
            int velocity,
            IReadOnlyList<PlaybackPedalRange> pedalRanges,
            bool applyArticulation = true)
        {
            int start = Math.Max(0, startTick);
            int duration = Math.Max(1, durationTicks);
            int adjustedVelocity = velocity;
            bool suppressPedalSustain = false;
            if (applyArticulation)
            {
                if (source.IsStaccatissimo)
                {
                    duration = Math.Max(1, (int)Math.Round(duration * 0.08));
                    adjustedVelocity += 16;
                    suppressPedalSustain = true;
                }
                else if (source.IsStaccato)
                {
                    duration = Math.Max(1, (int)Math.Round(duration * 0.18));
                    adjustedVelocity += 10;
                    suppressPedalSustain = true;
                }
            }

            int end = Math.Max(start + 1, start + duration);
            if (!suppressPedalSustain)
            {
                end = GetSustainedEndTick(start, end, pedalRanges);
            }

            int safeMidi = Math.Clamp(midi, 0, 127);
            int safeVelocity = Math.Clamp(adjustedVelocity, 1, 127);
            _playbackEvents.Add(new PlaybackEvent(start, 1, safeMidi, true, safeVelocity));
            _playbackEvents.Add(new PlaybackEvent(end, 0, safeMidi, false, 0));
            _playbackNoteSpans.Add(new PlaybackNoteSpan(start, end, safeMidi, safeVelocity));
            _playbackTotalTicks = Math.Max(_playbackTotalTicks, end);
        }

        private int GetPlaybackVelocityForNote(NoteEvent note, int sourceTick)
        {
            int velocity = GetPlaybackDynamicVelocityAtTick(sourceTick);
            if (note.IsAccent)
            {
                velocity += 42;
            }

            if (note.IsStaccatissimo)
            {
                velocity += 14;
            }
            else if (note.IsStaccato)
            {
                velocity += 8;
            }

            return Math.Clamp(velocity, 28, 127);
        }

        private int GetPlaybackDynamicVelocityAtTick(int sourceTick)
        {
            if (_viewModel == null)
            {
                return 92;
            }

            int safeTick = Math.Max(0, sourceTick);
            int velocity = 96;
            foreach (var mark in _viewModel.Project.ExpressionMarks.OrderBy(m => m.StartTick))
            {
                int markTick = Math.Max(0, mark.StartTick);
                if (markTick > safeTick)
                {
                    break;
                }

                string code = NormalizeExpressionCode(mark.Code);
                switch (code)
                {
                    case "ppp": velocity = 46; break;
                    case "pp": velocity = 58; break;
                    case "p": velocity = 70; break;
                    case "mp": velocity = 82; break;
                    case "mf": velocity = 96; break;
                    case "f": velocity = 110; break;
                    case "ff": velocity = 122; break;
                    case "fff": velocity = 127; break;
                    case "sf": velocity = 126; break;
                }
            }

            velocity += GetPlaybackHairpinVelocityDeltaAtTick(safeTick);
            return Math.Clamp(velocity, 24, 127);
        }

        private int GetPlaybackHairpinVelocityDeltaAtTick(int sourceTick)
        {
            if (_viewModel == null)
            {
                return 0;
            }

            int safeTick = Math.Max(0, sourceTick);
            double delta = 0d;
            foreach (var mark in _viewModel.Project.ExpressionMarks)
            {
                string code = NormalizeExpressionCode(mark.Code);
                if (code is not ("cresc" or "dim" or "cresc_text" or "dim_text"))
                {
                    continue;
                }

                int start = Math.Max(0, mark.StartTick);
                int end = Math.Max(start + 1, GetExpressionSpanEndTick(mark));
                if (safeTick < start || safeTick > end)
                {
                    continue;
                }

                double progress = (safeTick - start) / (double)Math.Max(1, end - start);
                double amount = code is "cresc" or "cresc_text" ? 18d : -18d;
                if (code is "cresc_text" or "dim_text")
                {
                    amount *= 0.72d;
                }

                delta += amount * progress;
            }

            return (int)Math.Round(delta);
        }

        private PlaybackRepeatSegment? TryGetPlaybackRepeatSegment()
        {
            if (_viewModel == null)
            {
                return null;
            }

            int pendingStart = 0;
            bool hasPendingStart = false;
            foreach (var mark in _viewModel.Project.ExpressionMarks
                .Where(m => NormalizeExpressionCode(m.Code) == ScoreMarkRepeatBarline)
                .OrderBy(m => m.StartTick))
            {
                if (IsStartRepeatBarline(mark))
                {
                    pendingStart = Math.Max(0, mark.StartTick);
                    hasPendingStart = true;
                }
                else
                {
                    int end = Math.Max(0, mark.StartTick);
                    int start = hasPendingStart ? Math.Min(pendingStart, end) : 0;
                    if (end > start)
                    {
                        return new PlaybackRepeatSegment(start, end);
                    }
                }
            }

            return null;
        }

        private List<(int StartTick, int EndTick)> GetPlaybackEndingRanges(string endingCode)
        {
            var ranges = new List<(int StartTick, int EndTick)>();
            if (_viewModel == null)
            {
                return ranges;
            }

            foreach (var mark in _viewModel.Project.ExpressionMarks
                .Where(m => NormalizeExpressionCode(m.Code) == endingCode)
                .OrderBy(m => m.StartTick))
            {
                int start = Math.Max(0, mark.StartTick);
                int end = Math.Max(start + 1, GetExpressionSpanEndTick(mark));
                ranges.Add((start, end));
            }

            return ranges;
        }

        private static bool IsTickInRanges(int tick, IReadOnlyList<(int StartTick, int EndTick)> ranges)
        {
            int safeTick = Math.Max(0, tick);
            for (int i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (safeTick >= range.StartTick && safeTick < range.EndTick)
                {
                    return true;
                }
            }

            return false;
        }

        private List<PlaybackPedalRange> GetPlaybackPedalRanges(
            PlaybackRepeatSegment? repeatSegment,
            IReadOnlyList<(int StartTick, int EndTick)> ending1Ranges,
            IReadOnlyList<(int StartTick, int EndTick)> ending2Ranges,
            int playbackEndTick)
        {
            var ranges = new List<PlaybackPedalRange>();
            if (_viewModel == null)
            {
                return ranges;
            }

            int repeatDuration = repeatSegment?.DurationTicks ?? 0;
            int MapFirstPassTick(int sourceTick)
            {
                int safe = Math.Max(0, sourceTick);
                if (!repeatSegment.HasValue) return safe;
                return safe >= repeatSegment.Value.EndTick ? safe + repeatDuration : safe;
            }

            var pedalEvents = new List<(int Tick, string Code, int EndTick)>();
            foreach (var mark in _viewModel.Project.ExpressionMarks
                .OrderBy(m => m.StartTick))
            {
                string code = NormalizeExpressionCode(mark.Code);
                if (code is not ("ped" or "ped_release" or "ped_line"))
                {
                    continue;
                }

                int sourceStart = Math.Max(0, mark.StartTick);
                int sourceEnd = code == "ped_line"
                    ? Math.Max(sourceStart + 1, GetExpressionSpanEndTick(mark))
                    : sourceStart;
                bool inEnding1 = IsTickInRanges(sourceStart, ending1Ranges);
                bool inEnding2 = IsTickInRanges(sourceStart, ending2Ranges);
                bool includeFirstPass = !inEnding2;

                if (includeFirstPass)
                {
                    int tick = MapFirstPassTick(sourceStart);
                    int endTick = code == "ped_line" ? MapFirstPassTick(sourceEnd) : tick;
                    pedalEvents.Add((tick, code, Math.Max(tick + 1, endTick)));
                }

                if (repeatSegment.HasValue
                    && sourceStart >= repeatSegment.Value.StartTick
                    && sourceStart < repeatSegment.Value.EndTick
                    && !inEnding1)
                {
                    int tick = repeatSegment.Value.EndTick + (sourceStart - repeatSegment.Value.StartTick);
                    int endTick = code == "ped_line"
                        ? tick + Math.Max(1, sourceEnd - sourceStart)
                        : tick;
                    pedalEvents.Add((tick, code, Math.Max(tick + 1, endTick)));
                }
            }

            pedalEvents.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            bool pedalDown = false;
            int pedalStart = 0;
            foreach (var evt in pedalEvents)
            {
                if (evt.Code == "ped")
                {
                    if (!pedalDown)
                    {
                        pedalDown = true;
                        pedalStart = evt.Tick;
                    }
                    continue;
                }

                if (evt.Code == "ped_release")
                {
                    if (pedalDown)
                    {
                        ranges.Add(new PlaybackPedalRange(pedalStart, Math.Max(pedalStart + 1, evt.Tick)));
                        pedalDown = false;
                    }
                    continue;
                }

                if (evt.Code == "ped_line")
                {
                    ranges.Add(new PlaybackPedalRange(evt.Tick, evt.EndTick));
                }
            }

            if (pedalDown)
            {
                ranges.Add(new PlaybackPedalRange(pedalStart, Math.Max(pedalStart + 1, playbackEndTick)));
            }

            if (ranges.Count <= 1)
            {
                return ranges;
            }

            var merged = ranges
                .OrderBy(r => r.StartTick)
                .ThenBy(r => r.EndTick)
                .ToList();
            int write = 0;
            for (int i = 1; i < merged.Count; i++)
            {
                var current = merged[write];
                var next = merged[i];
                if (next.StartTick <= current.EndTick)
                {
                    merged[write] = new PlaybackPedalRange(current.StartTick, Math.Max(current.EndTick, next.EndTick));
                }
                else
                {
                    write++;
                    merged[write] = next;
                }
            }

            if (write + 1 < merged.Count)
            {
                merged.RemoveRange(write + 1, merged.Count - (write + 1));
            }

            return merged;
        }

        private static int GetSustainedEndTick(int startTick, int noteEndTick, IReadOnlyList<PlaybackPedalRange> pedalRanges)
        {
            int sustainedEnd = Math.Max(startTick + 1, noteEndTick);
            for (int i = 0; i < pedalRanges.Count; i++)
            {
                var range = pedalRanges[i];
                if (sustainedEnd > range.StartTick && sustainedEnd <= range.EndTick && startTick < range.EndTick)
                {
                    sustainedEnd = Math.Max(sustainedEnd, range.EndTick);
                }
            }

            return sustainedEnd;
        }

        private void StartPlaybackFromTick(int tick, bool resumeActiveNotes = true)
        {
            if (_playbackTotalTicks <= 0 || _midiSynth == null) return;

            tick = Math.Clamp(tick, 0, _playbackTotalTicks);
            StopAllActivePlaybackNotes();
            _playbackCurrentTick = tick;

            _playbackEventIndex = 0;
            while (_playbackEventIndex < _playbackEvents.Count && _playbackEvents[_playbackEventIndex].Tick <= tick)
            {
                _playbackEventIndex++;
            }

            if (resumeActiveNotes)
            {
                foreach (var span in _playbackNoteSpans)
                {
                    if (span.StartTick <= tick && tick < span.EndTick)
                    {
                        SendMidiNoteOn(span.Midi, span.Velocity);
                    }
                }
            }

            _playbackStartTimeUtc = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(tick / Math.Max(1e-6, _playbackTicksPerSecond));
            _isPlaybackRunning = true;
            _isPlaybackPaused = false;
            CancelPlaybackOverlayCollapseDelay();
            _isPlaybackOverlayExpanded = true;
            UpdatePlaybackOverlayLayout();
            bool shouldAnimateExpand = PlaybackOverlayScaleTransform?.ScaleX < 0.995d;
            UpdatePlaybackOverlayScale(expanded: true, animate: shouldAnimateExpand);
            if (!_isPlaybackOverlayPointerOver && !_isDraggingPlaybackSlider)
            {
                StartPlaybackOverlayCollapseDelay();
            }
            _playbackTimer?.Start();
            UpdatePlaybackProgressBar();
            CenterPlaybackInViewport(force: true);
            StaffCanvas.Invalidate();
        }

        private int FindPlaybackEventIndex(int tick)
        {
            int clampedTick = Math.Max(0, tick);
            int low = 0;
            int high = _playbackEvents.Count;
            while (low < high)
            {
                int mid = low + ((high - low) / 2);
                if (_playbackEvents[mid].Tick <= clampedTick)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return Math.Clamp(low, 0, _playbackEvents.Count);
        }

        private int GetPlaybackTickForSourceTick(int sourceTick)
        {
            if (_playbackCursorPoints.Count == 0)
            {
                return Math.Clamp(Math.Max(0, sourceTick), 0, Math.Max(0, _playbackTotalTicks));
            }

            int safeSourceTick = Math.Max(0, sourceTick);
            int bestPlaybackTick = _playbackCursorPoints[0].PlaybackTick;
            int bestSourceDistance = Math.Abs(_playbackCursorPoints[0].SourceTick - safeSourceTick);
            int bestPlaybackDistance = Math.Abs(bestPlaybackTick - _playbackCurrentTick);
            for (int i = 1; i < _playbackCursorPoints.Count; i++)
            {
                var point = _playbackCursorPoints[i];
                int sourceDistance = Math.Abs(point.SourceTick - safeSourceTick);
                int playbackDistance = Math.Abs(point.PlaybackTick - _playbackCurrentTick);
                if (sourceDistance < bestSourceDistance
                    || (sourceDistance == bestSourceDistance && playbackDistance < bestPlaybackDistance))
                {
                    bestPlaybackTick = point.PlaybackTick;
                    bestSourceDistance = sourceDistance;
                    bestPlaybackDistance = playbackDistance;
                }
            }

            return Math.Clamp(bestPlaybackTick, 0, Math.Max(0, _playbackTotalTicks));
        }

        private bool TryGetPlaybackViewportAnchor(int playbackTick, out Point anchor)
        {
            anchor = default;
            if (ScoreScrollViewer?.Content is not UIElement scrollContent || StaffCanvas == null)
            {
                return false;
            }

            if (StaffCanvas.ActualWidth <= 1d || StaffCanvas.ActualHeight <= 1d)
            {
                return false;
            }

            double sourceTick = GetSourceTickForPlaybackCursor(playbackTick);
            int cursorSourceTick = Math.Max(0, (int)Math.Round(sourceTick));
            int systemIndex = GetSystemIndexForTick(cursorSourceTick);
            float anchorX = GetNoteX(sourceTick);
            float anchorY = (GetSystemTrebleTop(systemIndex) + GetSystemBassBottom(systemIndex)) * 0.5f;

            GeneralTransform transform = StaffCanvas.TransformToVisual(scrollContent);
            Point canvasOrigin = transform.TransformPoint(new Point(0d, 0d));
            anchor = new Point(canvasOrigin.X + anchorX, canvasOrigin.Y + anchorY);
            return true;
        }

        private void CenterPlaybackInViewport(bool force)
        {
            if (ScoreScrollViewer == null || !TryGetPlaybackViewportAnchor(_playbackCurrentTick, out Point anchor))
            {
                return;
            }

            double viewportWidth = ScoreScrollViewer.ViewportWidth > 1d ? ScoreScrollViewer.ViewportWidth : ScoreScrollViewer.ActualWidth;
            double viewportHeight = ScoreScrollViewer.ViewportHeight > 1d ? ScoreScrollViewer.ViewportHeight : ScoreScrollViewer.ActualHeight;
            if (viewportWidth <= 1d || viewportHeight <= 1d)
            {
                return;
            }

            double targetHorizontal = Math.Clamp(anchor.X - viewportWidth * 0.5d, 0d, Math.Max(0d, ScoreScrollViewer.ScrollableWidth));
            double targetVertical = Math.Clamp(anchor.Y - viewportHeight * 0.5d, 0d, Math.Max(0d, ScoreScrollViewer.ScrollableHeight));

            if (!force
                && Math.Abs(ScoreScrollViewer.HorizontalOffset - targetHorizontal) < 8d
                && Math.Abs(ScoreScrollViewer.VerticalOffset - targetVertical) < 8d)
            {
                return;
            }

            ScoreScrollViewer.ChangeView(targetHorizontal, targetVertical, null, true);
        }

        private void SeekPlaybackToTick(int seekTick, bool keepRunningIfWasRunning)
        {
            int clamped = Math.Clamp(seekTick, 0, Math.Max(0, _playbackTotalTicks));
            bool wasRunning = _isPlaybackRunning;
            bool wasPaused = _isPlaybackPaused;

            if (wasRunning || wasPaused)
            {
                StartPlaybackFromTick(clamped, resumeActiveNotes: false);
                if (!wasRunning && wasPaused)
                {
                    _isPlaybackRunning = false;
                    _isPlaybackPaused = true;
                    _playbackTimer?.Stop();
                    StopAllActivePlaybackNotes();
                }
                else if (!keepRunningIfWasRunning && wasRunning)
                {
                    _isPlaybackRunning = false;
                    _isPlaybackPaused = true;
                    _playbackTimer?.Stop();
                    StopAllActivePlaybackNotes();
                }

                UpdatePlaybackProgressBar();
                CenterPlaybackInViewport(force: true);
                StaffCanvas.Invalidate();
                return;
            }

            StopAllActivePlaybackNotes();
            _playbackCurrentTick = clamped;
            _playbackEventIndex = FindPlaybackEventIndex(clamped);
            UpdatePlaybackProgressBar();
            CenterPlaybackInViewport(force: true);
            StaffCanvas.Invalidate();
        }

        private void SeekPlaybackToSourceTick(int sourceTick, bool keepRunningIfWasRunning)
        {
            int playbackTick = GetPlaybackTickForSourceTick(sourceTick);
            SeekPlaybackToTick(playbackTick, keepRunningIfWasRunning);
        }

        private void ResumePlayback()
        {
            if (_playbackTotalTicks <= 0 || _midiSynth == null) return;
            StartPlaybackFromTick(_playbackCurrentTick);
        }

        private void StopPlaybackInternal(bool resetPosition)
        {
            _isPlaybackRunning = false;
            _isPlaybackPaused = false;
            _playbackTimer?.Stop();
            StopAllActivePlaybackNotes();
            _playbackEventIndex = 0;
            if (resetPosition)
            {
                _playbackCurrentTick = 0;
            }
            if (!_isPlaybackOverlayPointerOver)
            {
                StartPlaybackOverlayCollapseDelay();
            }
            UpdatePlaybackProgressBar();
            StaffCanvas.Invalidate();
        }

        private void PlaybackTimer_Tick(object? sender, object e)
        {
            if (!_isPlaybackRunning) return;

            int tick = (int)Math.Floor((DateTimeOffset.UtcNow - _playbackStartTimeUtc).TotalSeconds * _playbackTicksPerSecond);
            tick = Math.Clamp(tick, 0, Math.Max(0, _playbackTotalTicks));
            _playbackCurrentTick = tick;

            while (_playbackEventIndex < _playbackEvents.Count && _playbackEvents[_playbackEventIndex].Tick <= tick)
            {
                var evt = _playbackEvents[_playbackEventIndex];
                if (evt.IsOn) SendMidiNoteOn(evt.Midi, evt.Velocity);
                else SendMidiNoteOff(evt.Midi);
                _playbackEventIndex++;
            }

            if (tick >= _playbackTotalTicks && _playbackEventIndex >= _playbackEvents.Count)
            {
                _playbackCurrentTick = Math.Max(0, _playbackTotalTicks);
                StopPlaybackInternal(resetPosition: false);
                return;
            }

            UpdatePlaybackProgressBar();
            CenterPlaybackInViewport(force: false);
            StaffCanvas.Invalidate();
        }

        private void UpdatePlaybackProgressBar()
        {
            Slider? playbackSlider = PlaybackProgressSlider;
            Border? playbackOverlay = PlaybackOverlay;
            if (playbackSlider == null || playbackOverlay == null) return;

            playbackOverlay.Visibility = Visibility.Visible;
            UpdatePlaybackOverlayLayout();
            if (!_playbackOverlayScaleInitialized)
            {
                UpdatePlaybackOverlayScale(_isPlaybackOverlayExpanded, animate: false);
            }

            double ticksPerSecond = GetPlaybackTicksPerSecondForDisplay();
            double totalSeconds = _playbackTotalTicks / ticksPerSecond;
            double currentSeconds = _playbackCurrentTick / ticksPerSecond;
            if (_isDraggingPlaybackSlider)
            {
                currentSeconds = Math.Clamp(playbackSlider.Value, 0d, Math.Max(1d, totalSeconds));
            }

            if (!_isDraggingPlaybackSlider)
            {
                _syncingPlaybackSlider = true;
                try
                {
                    playbackSlider.Minimum = 0;
                    playbackSlider.Maximum = Math.Max(1, totalSeconds);
                    playbackSlider.Value = Math.Clamp(currentSeconds, 0, playbackSlider.Maximum);
                }
                finally
                {
                    _syncingPlaybackSlider = false;
                }
            }

            if (PlaybackProgressText != null)
            {
                PlaybackProgressText.Text = $"{FormatPlaybackTime(currentSeconds)} / {FormatPlaybackTime(totalSeconds)}";
            }

            UpdatePlaybackControlToolTips();
            UpdatePlaybackVolumeToolTip();
        }

        private void UpdatePlaybackControlToolTips()
        {
            bool isEnglish = AppSettingsService.Instance.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
            if (PlaybackOverlayPlayPauseButton != null
                && PlaybackOverlayPlayPauseIcon != null
                && PlaybackOverlayStopButton != null)
            {
                if (_isPlaybackRunning)
                {
                    PlaybackOverlayPlayPauseIcon.Glyph = "\uF8AE";
                    ToolTipService.SetToolTip(PlaybackOverlayPlayPauseButton, isEnglish ? "Pause" : "鏆傚仠");
                }
                else if (_isPlaybackPaused)
                {
                    PlaybackOverlayPlayPauseIcon.Glyph = "\uF5B0";
                    ToolTipService.SetToolTip(PlaybackOverlayPlayPauseButton, isEnglish ? "Resume" : "缁х画");
                }
                else
                {
                    PlaybackOverlayPlayPauseIcon.Glyph = "\uF5B0";
                    ToolTipService.SetToolTip(PlaybackOverlayPlayPauseButton, isEnglish ? "Play" : "鎾斁");
                }

                ToolTipService.SetToolTip(PlaybackOverlayStopButton, isEnglish ? "Stop" : "鍋滄");
                PlaybackOverlayStopButton.IsEnabled = _isPlaybackRunning || _isPlaybackPaused;
            }
        }

        private int GetPlaybackVolumePercentage()
        {
            return (int)Math.Round(Math.Clamp(_playbackVolume, 0d, 1d) * 100d);
        }

        private void UpdatePlaybackVolumeToolTip()
        {
            if (PlaybackOverlayVolumeButton == null)
            {
                return;
            }

            bool isEnglish = AppSettingsService.Instance.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
            int percent = GetPlaybackVolumePercentage();
            ToolTipService.SetToolTip(PlaybackOverlayVolumeButton, isEnglish ? $"Volume {percent}%" : $"\u97f3\u91cf {percent}%");
            if (PlaybackVolumeValueText != null) PlaybackVolumeValueText.Text = percent.ToString();
        }
        private void PlaybackOverlayPlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaybackRunning)
            {
                PauseMidiButton_Click(sender, e);
                return;
            }

            if (_isPlaybackPaused)
            {
                ResumePlayback();
                return;
            }

            PlayMidiButton_Click(sender, e);
        }

        private void PlaybackOverlayStopButton_Click(object sender, RoutedEventArgs e)
        {
            StopMidiButton_Click(sender, e);
        }

        private void UpdatePlaybackOverlayTheme()
        {
            if (PlaybackOverlay == null || PlaybackOverlaySheen == null || PlaybackOverlayRefraction == null)
            {
                return;
            }

            // Use theme acrylic brush from XAML resources.
            PlaybackOverlay.Opacity = 1.00d;
            PlaybackOverlay.BorderBrush = new SolidColorBrush(Colors.Transparent);
            PlaybackOverlay.BorderThickness = new Thickness(0d);

            PlaybackOverlayRefraction.Background = new SolidColorBrush(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
            PlaybackOverlayRefraction.BorderBrush = new SolidColorBrush(Colors.Transparent);
            PlaybackOverlayRefraction.BorderThickness = new Thickness(0d);

            if (PlaybackProgressSlider != null)
            {
                PlaybackProgressSlider.ClearValue(Control.BackgroundProperty);
                PlaybackProgressSlider.ClearValue(Control.BorderBrushProperty);
                PlaybackProgressSlider.ClearValue(Control.BorderThicknessProperty);
                PlaybackProgressSlider.ClearValue(Control.ForegroundProperty);
            }

            PlaybackOverlaySheen.Visibility = Visibility.Collapsed;
            PlaybackOverlaySheen.Opacity = 0.00d;
            PlaybackOverlaySheen.Margin = new Thickness(0);
            PlaybackOverlayRefraction.Visibility = Visibility.Collapsed;
            PlaybackOverlayRefraction.Opacity = 0.00d;
            PlaybackOverlayRefraction.Margin = new Thickness(0);
            DisablePlaybackRefractionEffect();
        }

        private static LinearGradientBrush CreateLinearGradientBrush(
            Point start,
            Point end,
            params (Color color, double offset)[] stops)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = start,
                EndPoint = end
            };

            foreach (var (color, offset) in stops)
            {
                brush.GradientStops.Add(new GradientStop
                {
                    Color = color,
                    Offset = offset
                });
            }

            return brush;
        }

        private void DisablePlaybackRefractionEffect()
        {
            if (PlaybackOverlayRefraction != null)
            {
                ElementCompositionPreview.SetElementChildVisual(PlaybackOverlayRefraction, null);
            }
        }

        private void StartPlaybackOverlayCollapseDelay()
        {
            _playbackOverlayCollapseTimer.Stop();
            _playbackOverlayCollapseTimer.Start();
        }

        private void CancelPlaybackOverlayCollapseDelay()
        {
            _playbackOverlayCollapseTimer.Stop();
        }

        private void PlaybackOverlayCollapseTimer_Tick(object? sender, object e)
        {
            _playbackOverlayCollapseTimer.Stop();
            if (_isPlaybackOverlayPointerOver || _isDraggingPlaybackSlider)
            {
                return;
            }

            _isPlaybackOverlayExpanded = false;
            UpdatePlaybackOverlayLayout();
            UpdatePlaybackOverlayScale(expanded: false, animate: true);
        }

        private void UpdatePlaybackOverlayScale(bool expanded, bool animate)
        {
            if (PlaybackOverlayScaleTransform == null)
            {
                return;
            }

            double targetScale = expanded ? 1.0d : 0.78d;
            // Preserve the currently displayed scale before stopping any existing storyboard.
            // Storyboard.Stop() resets animated properties to base values, which would kill expand animation.
            double currentScaleX = PlaybackOverlayScaleTransform.ScaleX;
            double currentScaleY = PlaybackOverlayScaleTransform.ScaleY;
            if (_playbackOverlayScaleStoryboard != null)
            {
                _playbackOverlayScaleStoryboard.Stop();
                _playbackOverlayScaleStoryboard = null;
                PlaybackOverlayScaleTransform.ScaleX = currentScaleX;
                PlaybackOverlayScaleTransform.ScaleY = currentScaleY;
            }

            if (PlaybackOverlayStopButton != null)
            {
                PlaybackOverlayStopButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PlaybackOverlayVolumeButton != null)
            {
                PlaybackOverlayVolumeButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PlaybackProgressText != null)
            {
                PlaybackProgressText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PlaybackOverlayPlayPauseButton != null)
            {
                PlaybackOverlayPlayPauseButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PlaybackProgressSlider != null)
            {
                if (expanded)
                {
                    Grid.SetColumn(PlaybackProgressSlider, 1);
                    Grid.SetColumnSpan(PlaybackProgressSlider, 1);
                    PlaybackProgressSlider.HorizontalAlignment = HorizontalAlignment.Stretch;
                    PlaybackProgressSlider.Width = double.NaN;
                    PlaybackProgressSlider.Margin = new Thickness(4, 2, 4, 2);
                }
                else
                {
                    Grid.SetColumn(PlaybackProgressSlider, 0);
                    Grid.SetColumnSpan(PlaybackProgressSlider, 5);
                    PlaybackProgressSlider.HorizontalAlignment = HorizontalAlignment.Stretch;
                    PlaybackProgressSlider.Width = double.NaN;
                    PlaybackProgressSlider.Margin = new Thickness(3, 1, 3, 1);
                }
            }

            bool needsScaleChange =
                Math.Abs(PlaybackOverlayScaleTransform.ScaleX - targetScale) > 0.0001d
                || Math.Abs(PlaybackOverlayScaleTransform.ScaleY - targetScale) > 0.0001d;
            if (!animate || !needsScaleChange)
            {
                PlaybackOverlayScaleTransform.ScaleX = targetScale;
                PlaybackOverlayScaleTransform.ScaleY = targetScale;
                if (PlaybackProgressTextInverseScaleTransform != null)
                {
                    double inverse = targetScale > 1e-4d ? 1d / targetScale : 1d;
                    PlaybackProgressTextInverseScaleTransform.ScaleX = inverse;
                    PlaybackProgressTextInverseScaleTransform.ScaleY = inverse;
                }
                _playbackOverlayScaleInitialized = true;
                return;
            }

            var easing = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            };
            var duration = new Duration(TimeSpan.FromMilliseconds(300));
            var storyboard = new Storyboard();

            var scaleXAnimation = new DoubleAnimation
            {
                From = PlaybackOverlayScaleTransform.ScaleX,
                To = targetScale,
                Duration = duration,
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(scaleXAnimation, PlaybackOverlayScaleTransform);
            Storyboard.SetTargetProperty(scaleXAnimation, "ScaleX");
            storyboard.Children.Add(scaleXAnimation);

            var scaleYAnimation = new DoubleAnimation
            {
                From = PlaybackOverlayScaleTransform.ScaleY,
                To = targetScale,
                Duration = duration,
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(scaleYAnimation, PlaybackOverlayScaleTransform);
            Storyboard.SetTargetProperty(scaleYAnimation, "ScaleY");
            storyboard.Children.Add(scaleYAnimation);

            if (PlaybackProgressTextInverseScaleTransform != null)
            {
                double targetInverseScale = targetScale > 1e-4d ? 1d / targetScale : 1d;

                var textInverseScaleXAnimation = new DoubleAnimation
                {
                    From = PlaybackProgressTextInverseScaleTransform.ScaleX,
                    To = targetInverseScale,
                    Duration = duration,
                    EasingFunction = easing,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(textInverseScaleXAnimation, PlaybackProgressTextInverseScaleTransform);
                Storyboard.SetTargetProperty(textInverseScaleXAnimation, "ScaleX");
                storyboard.Children.Add(textInverseScaleXAnimation);

                var textInverseScaleYAnimation = new DoubleAnimation
                {
                    From = PlaybackProgressTextInverseScaleTransform.ScaleY,
                    To = targetInverseScale,
                    Duration = duration,
                    EasingFunction = easing,
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(textInverseScaleYAnimation, PlaybackProgressTextInverseScaleTransform);
                Storyboard.SetTargetProperty(textInverseScaleYAnimation, "ScaleY");
                storyboard.Children.Add(textInverseScaleYAnimation);
            }

            storyboard.Completed += (_, _) =>
            {
                PlaybackOverlayScaleTransform.ScaleX = targetScale;
                PlaybackOverlayScaleTransform.ScaleY = targetScale;
                if (PlaybackProgressTextInverseScaleTransform != null)
                {
                    double inverse = targetScale > 1e-4d ? 1d / targetScale : 1d;
                    PlaybackProgressTextInverseScaleTransform.ScaleX = inverse;
                    PlaybackProgressTextInverseScaleTransform.ScaleY = inverse;
                }
                if (ReferenceEquals(_playbackOverlayScaleStoryboard, storyboard))
                {
                    _playbackOverlayScaleStoryboard = null;
                }
            };

            _playbackOverlayScaleStoryboard = storyboard;
            _playbackOverlayScaleInitialized = true;
            storyboard.Begin();
        }

        private void PlaybackOverlay_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            CancelPlaybackOverlayCollapseDelay();
            _isPlaybackOverlayPointerOver = true;
            bool shouldAnimateExpand = !_isPlaybackOverlayExpanded || (PlaybackOverlayScaleTransform?.ScaleX < 0.995d);
            if (shouldAnimateExpand)
            {
                _isPlaybackOverlayExpanded = true;
                UpdatePlaybackOverlayLayout();
                UpdatePlaybackOverlayScale(expanded: true, animate: true);
            }

            if (PlaybackOverlay != null)
            {
                PlaybackOverlay.Translation = new System.Numerics.Vector3(0f, 0f, 14f);
            }

            if (PlaybackOverlaySheen != null)
            {
                PlaybackOverlaySheen.Opacity = 0.00d;
                PlaybackOverlaySheen.Margin = new Thickness(0);
            }

            if (PlaybackOverlayRefraction != null)
            {
                PlaybackOverlayRefraction.Opacity = 0.00d;
                PlaybackOverlayRefraction.Margin = new Thickness(0);
            }
        }

        private void PlaybackOverlay_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isPlaybackOverlayPointerOver = false;
            StartPlaybackOverlayCollapseDelay();

            if (PlaybackOverlay != null)
            {
                PlaybackOverlay.Translation = new System.Numerics.Vector3(0f, 0f, 14f);
            }

            if (PlaybackOverlaySheen != null)
            {
                PlaybackOverlaySheen.Opacity = 0.00d;
                PlaybackOverlaySheen.Margin = new Thickness(0);
            }

            if (PlaybackOverlayRefraction != null)
            {
                PlaybackOverlayRefraction.Opacity = 0.00d;
                PlaybackOverlayRefraction.Margin = new Thickness(0);
            }
        }

        private void UpdatePlaybackOverlayLayout()
        {
            if (PlaybackOverlay == null || PlaybackProgressSlider == null)
            {
                return;
            }

            double viewportWidth = ScoreScrollViewer?.ActualWidth > 1d
                ? ScoreScrollViewer.ActualWidth
                : (RootGrid?.ActualWidth ?? 0d);
            double viewportHeight = ScoreScrollViewer?.ActualHeight > 1d
                ? ScoreScrollViewer.ActualHeight
                : (RootGrid?.ActualHeight ?? 0d);
            if (viewportWidth <= 1d)
            {
                return;
            }

            double expandedWidth = Math.Clamp(viewportWidth * 0.44d, 320d, 720d);
            expandedWidth = Math.Min(expandedWidth, Math.Max(360d, viewportWidth - 24d));
            double collapsedWidth = Math.Clamp(viewportWidth * 0.32d, 230d, 380d);
            collapsedWidth = Math.Min(collapsedWidth, Math.Max(240d, viewportWidth - 60d));
            double targetWidth = _isPlaybackOverlayExpanded ? expandedWidth : collapsedWidth;
            PlaybackOverlay.Width = targetWidth;
            double sliderMinWidth = _isPlaybackOverlayExpanded
                ? Math.Clamp(targetWidth * 0.58d, 190d, 470d)
                : Math.Clamp(targetWidth * 0.80d, 180d, 300d);
            PlaybackProgressSlider.MinWidth = sliderMinWidth;

            double bottomOffset = viewportHeight > 1d
                ? Math.Clamp(viewportHeight * 0.013d, 14d, 24d)
                : 16d;
            PlaybackOverlay.Margin = new Thickness(0d, 0d, 0d, bottomOffset);

            double scale = viewportHeight > 1d
                ? Math.Clamp(viewportHeight / 900d, 0.95d, 1.24d)
                : 1d;
            if (_isPlaybackOverlayExpanded)
            {
                PlaybackOverlay.Padding = new Thickness(
                    Math.Clamp(12d * scale, 10d, 14d),
                    Math.Clamp(10d * scale, 9d, 12d),
                    Math.Clamp(12d * scale, 10d, 14d),
                    Math.Clamp(10d * scale, 9d, 12d));
                PlaybackOverlay.CornerRadius = new CornerRadius(Math.Clamp(28d * scale, 24d, 34d));
                PlaybackOverlay.MinHeight = Math.Clamp(56d * scale, 52d, 64d);
            }
            else
            {
                PlaybackOverlay.Padding = new Thickness(
                    Math.Clamp(8d * scale, 7d, 10d),
                    Math.Clamp(7d * scale, 6d, 9d),
                    Math.Clamp(8d * scale, 7d, 10d),
                    Math.Clamp(7d * scale, 6d, 9d));
                PlaybackOverlay.CornerRadius = new CornerRadius(Math.Clamp(24d * scale, 20d, 30d));
                PlaybackOverlay.MinHeight = Math.Clamp(42d * scale, 40d, 48d);
            }

            if (PlaybackProgressText != null)
            {
                PlaybackProgressText.FontSize = Math.Clamp(11.2d * scale, 10.5d, 13d);
            }

            PlaybackProgressSlider.Height = _isPlaybackOverlayExpanded
                ? Math.Clamp(32d * scale, 30d, 36d)
                : Math.Clamp(30d * scale, 28d, 34d);

            if (PlaybackOverlayPlayPauseButton != null)
            {
                double buttonSize = 32d;
                PlaybackOverlayPlayPauseButton.Width = buttonSize;
                PlaybackOverlayPlayPauseButton.Height = buttonSize;
                PlaybackOverlayPlayPauseButton.CornerRadius = new CornerRadius(buttonSize / 2d);
                if (PlaybackOverlayStopButton != null)
                {
                    PlaybackOverlayStopButton.Width = buttonSize;
                    PlaybackOverlayStopButton.Height = buttonSize;
                    PlaybackOverlayStopButton.CornerRadius = new CornerRadius(buttonSize / 2d);
                }
                if (PlaybackOverlayVolumeButton != null)
                {
                    PlaybackOverlayVolumeButton.Width = buttonSize;
                    PlaybackOverlayVolumeButton.Height = buttonSize;
                    PlaybackOverlayVolumeButton.CornerRadius = new CornerRadius(buttonSize / 2d);
                }
            }

            if (PlaybackOverlayPlayPauseIcon != null)
            {
                PlaybackOverlayPlayPauseIcon.FontSize = 16d;
            }

            if (PlaybackOverlaySheen != null)
            {
                PlaybackOverlaySheen.Height = Math.Clamp(6d * scale, 5d, 9d);
            }

            if (PlaybackOverlayRefraction != null)
            {
                PlaybackOverlayRefraction.CornerRadius = PlaybackOverlay.CornerRadius;
            }
        }

        private void PlaybackProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            CancelPlaybackOverlayCollapseDelay();
            _isDraggingPlaybackSlider = true;
            _resumePlaybackAfterSliderDrag = _isPlaybackRunning;
            if (_resumePlaybackAfterSliderDrag)
            {
                _playbackTimer?.Stop();
                StopAllActivePlaybackNotes();
            }
            _isPlaybackOverlayExpanded = true;
            UpdatePlaybackOverlayLayout();
            UpdatePlaybackOverlayScale(expanded: true, animate: true);
            if (PlaybackProgressSlider != null)
            {
                PlaybackProgressSlider.CapturePointer(e.Pointer);
            }
        }

        private void PlaybackProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingPlaybackSlider || PlaybackProgressSlider == null) return;

            double ticksPerSecond = GetPlaybackTicksPerSecondForDisplay();
            int seekTick = Math.Clamp((int)Math.Round(PlaybackProgressSlider.Value * ticksPerSecond), 0, Math.Max(0, _playbackTotalTicks));
            bool keepRunning = _resumePlaybackAfterSliderDrag;
            _isDraggingPlaybackSlider = false;
            _resumePlaybackAfterSliderDrag = false;
            SeekPlaybackToTick(seekTick, keepRunningIfWasRunning: keepRunning);
            if (!_isPlaybackOverlayPointerOver)
            {
                StartPlaybackOverlayCollapseDelay();
            }
            UpdatePlaybackProgressBar();
        }

        private void PlaybackProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingPlaybackSlider) return;
            if (PlaybackProgressSlider == null) return;
            double ticksPerSecond = GetPlaybackTicksPerSecondForDisplay();
            int seekTick = Math.Clamp((int)Math.Round(PlaybackProgressSlider.Value * ticksPerSecond), 0, Math.Max(0, _playbackTotalTicks));
            bool keepRunning = _resumePlaybackAfterSliderDrag;
            _isDraggingPlaybackSlider = false;
            _resumePlaybackAfterSliderDrag = false;
            PlaybackProgressSlider.ReleasePointerCapture(e.Pointer);
            SeekPlaybackToTick(seekTick, keepRunningIfWasRunning: keepRunning);
            if (!_isPlaybackOverlayPointerOver)
            {
                StartPlaybackOverlayCollapseDelay();
            }
            UpdatePlaybackProgressBar();
        }

        private void PlaybackProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_syncingPlaybackSlider) return;
            double ticksPerSecond = GetPlaybackTicksPerSecondForDisplay();
            int newTick = Math.Clamp((int)Math.Round(e.NewValue * ticksPerSecond), 0, Math.Max(0, _playbackTotalTicks));

            if (!_isDraggingPlaybackSlider)
            {
                return;
            }

            _playbackCurrentTick = newTick;
            if (PlaybackProgressText != null)
            {
                double totalSeconds = _playbackTotalTicks / ticksPerSecond;
                PlaybackProgressText.Text = $"{FormatPlaybackTime(e.NewValue)} / {FormatPlaybackTime(totalSeconds)}";
            }
            StaffCanvas.Invalidate();
        }

        private double GetPlaybackTicksPerSecondForDisplay()
        {
            if (_playbackTicksPerSecond > 1e-6)
            {
                return _playbackTicksPerSecond;
            }

            if (_viewModel == null)
            {
                return 1d;
            }

            int ppq = Math.Max(1, _viewModel.Project.Ppq);
            int denominator = _viewModel.Project.TimeSignature.Denominator;
            int ticksPerBeat = Math.Max(1, (int)Math.Round(ppq * 4.0 / denominator));
            int bpm = Math.Clamp(_viewModel.Project.Bpm, 20, 300);
            return Math.Max(1e-6, bpm * ticksPerBeat / 60d);
        }

        private void PlaybackVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            _playbackVolume = Math.Clamp(e.NewValue / 100d, 0d, 1d);
            UpdatePlaybackVolumeToolTip();
        }

        private static string FormatPlaybackTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            {
                return "00:00";
            }

            int total = (int)Math.Round(seconds);
            int minutes = total / 60;
            int secs = total % 60;
            return $"{minutes:00}:{secs:00}";
        }

        private void SendMidiNoteOn(int midi, int velocity = 100)
        {
            if (_midiSynth == null) return;
            if (_playbackVolume <= 0.001d) return;
            byte pitch = (byte)Math.Clamp(midi, 0, 127);
            int scaledVelocity = (int)Math.Round(Math.Clamp(velocity, 1, 127) * _playbackVolume);
            byte vel = (byte)Math.Clamp(scaledVelocity, 1, 127);
            _midiSynth.SendMessage(new MidiNoteOnMessage(0, pitch, vel));
            if (_activePlaybackNotes.TryGetValue(pitch, out int count))
            {
                _activePlaybackNotes[pitch] = count + 1;
            }
            else
            {
                _activePlaybackNotes[pitch] = 1;
            }
        }

        private void SendMidiNoteOff(int midi)
        {
            if (_midiSynth == null) return;
            byte pitch = (byte)Math.Clamp(midi, 0, 127);
            if (_activePlaybackNotes.TryGetValue(pitch, out int count))
            {
                if (count <= 1)
                {
                    _activePlaybackNotes.Remove(pitch);
                    _midiSynth.SendMessage(new MidiNoteOffMessage(0, pitch, 0));
                }
                else
                {
                    _activePlaybackNotes[pitch] = count - 1;
                }
            }
        }

        private void StopAllActivePlaybackNotes()
        {
            if (_midiSynth == null || _activePlaybackNotes.Count == 0) return;
            foreach (int pitch in _activePlaybackNotes.Keys.ToArray())
            {
                _midiSynth.SendMessage(new MidiNoteOffMessage(0, (byte)pitch, 0));
            }
            _activePlaybackNotes.Clear();
        }

        private void AccidentalMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_syncingNoteTypeControls) return;
            if (sender is not ToggleMenuFlyoutItem selected) return;

            _syncingNoteTypeControls = true;
            try
            {
                if (selected.IsChecked)
                {
                    foreach (var item in new[] { AccidentalSharpItem, AccidentalFlatItem, AccidentalNaturalItem })
                    {
                        if (item != null && !ReferenceEquals(item, selected))
                        {
                            item.IsChecked = false;
                        }
                    }
                }
            }
            finally
            {
                _syncingNoteTypeControls = false;
            }

            SyncPendingNoteTypeFromControls();
            ApplyPendingNoteTypeToSelectedNotes();
            SwitchToNoNoteLengthIfNeeded();
        }

        private void NoteTypeToggleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_syncingNoteTypeControls) return;

            _syncingNoteTypeControls = true;
            try
            {
                bool staccatoOn = StaccatoMenuItem?.IsChecked == true;
                bool staccatissimoOn = StaccatissimoMenuItem?.IsChecked == true;
                bool accentOn = AccentMenuItem?.IsChecked == true;
                if (ReferenceEquals(sender, StaccatoMenuItem) && staccatoOn)
                {
                    if (StaccatissimoMenuItem != null) StaccatissimoMenuItem.IsChecked = false;
                    if (AccentMenuItem != null) AccentMenuItem.IsChecked = false;
                }
                else if (ReferenceEquals(sender, StaccatissimoMenuItem) && staccatissimoOn)
                {
                    if (StaccatoMenuItem != null) StaccatoMenuItem.IsChecked = false;
                    if (AccentMenuItem != null) AccentMenuItem.IsChecked = false;
                }
                else if (ReferenceEquals(sender, AccentMenuItem) && accentOn)
                {
                    if (StaccatoMenuItem != null) StaccatoMenuItem.IsChecked = false;
                    if (StaccatissimoMenuItem != null) StaccatissimoMenuItem.IsChecked = false;
                }
            }
            finally
            {
                _syncingNoteTypeControls = false;
            }

            SyncPendingNoteTypeFromControls();
            ApplyPendingNoteTypeToSelectedNotes();
            SwitchToNoNoteLengthIfNeeded();
        }

        private void NoteOrnamentMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_syncingNoteTypeControls) return;
            if (_isRestInputMode)
            {
                _syncingNoteTypeControls = true;
                try
                {
                    SetOrnamentSelection(NoteOrnament.None);
                }
                finally
                {
                    _syncingNoteTypeControls = false;
                }
                return;
            }

            if (sender is RadioMenuFlyoutItem item
                && TryParseNoteOrnamentTag(item.Tag?.ToString(), out var ornament))
            {
                _syncingNoteTypeControls = true;
                try
                {
                    SetOrnamentSelection(ornament);
                }
                finally
                {
                    _syncingNoteTypeControls = false;
                }
            }

            SyncPendingNoteTypeFromControls();
            ApplyPendingNoteTypeToSelectedNotes();
            SwitchToNoNoteLengthIfNeeded();
        }

        private void AreaSelectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _isSelectingRect = false;
            if (sender is ToggleMenuFlyoutItem item)
            {
                item.IsChecked = false;
            }
            _viewModel?.SetStatus("\u533a\u57df\u9009\u62e9\u5df2\u6539\u4e3a\u9ed8\u8ba4\u62d6\u62fd\u89e6\u53d1\uff08\u6309\u4f4f\u9f20\u6807\u62d6\u52a8\u5373\u53ef\uff09\u3002");
            StaffCanvas.Invalidate();
            SwitchToNoNoteLengthIfNeeded();
        }

        private void ClearAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            var defaults = ProjectFactory.CreateDefault();
            _viewModel.Project.Notes.Clear();
            _viewModel.Project.ExpressionMarks.Clear();
            _viewModel.Project.TimeSignatureChanges.Clear();
            _viewModel.Project.KeySignatureChanges.Clear();
            _viewModel.Project.StaffClefs.Clear();
            _viewModel.Project.TimeSignature = new TimeSignature(defaults.TimeSignature.Numerator, defaults.TimeSignature.Denominator);
            _viewModel.Project.KeySignature = new KeySignature(defaults.KeySignature.Fifths, defaults.KeySignature.Mode);
            _viewModel.Bpm = defaults.Bpm;
            _manualMeasureCount = 0;
            _autoMeasuresPerSystem = GetDefaultMeasuresPerSystemForTimeSignature(_viewModel?.TimeSigNumerator ?? 4, _viewModel?.TimeSigDenominator ?? 4);
            _barlineOffsets.Clear();
            _systemMeasureCounts.Clear();
            SetTimeSignatureSelection(defaults.TimeSignature.Numerator, defaults.TimeSignature.Denominator);
            SetKeySignatureSelection(defaults.KeySignature.Fifths);
            SetTempoSelection(defaults.Bpm);
            ClearSelection();
            StopPlaybackInternal(resetPosition: true);
            MarkProjectChanged();
            StaffCanvas.Invalidate();
            SwitchToNoNoteLengthIfNeeded();
        }

        public void HandleTitleBarFileCommand(string command)
        {
            string normalized = command?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (normalized)
            {
                case "new":
                    NewMenuItem_Click(this, new RoutedEventArgs());
                    break;
                case "open":
                    OpenMenuItem_Click(this, new RoutedEventArgs());
                    break;
                case "save":
                    SaveMenuItem_Click(this, new RoutedEventArgs());
                    break;
                case "saveas":
                    SaveAsMenuItem_Click(this, new RoutedEventArgs());
                    break;
                case "import_musicxml":
                    ImportMusicXmlMenuItem_Click(this, new RoutedEventArgs());
                    break;
                case "export_musicxml":
                    ExportMusicXmlMenuItem_Click(this, new RoutedEventArgs());
                    break;
                case "export_pdf":
                    ExportPdfMenuItem_Click(this, new RoutedEventArgs());
                    break;
                case "print":
                    PrintMenuItem_Click(this, new RoutedEventArgs());
                    break;
            }
        }

        public void HandleTitleBarTimeSignatureCommand(string signature)
        {
            string tag = string.IsNullOrWhiteSpace(signature) ? "4/4" : signature.Trim();
            var proxy = new RadioMenuFlyoutItem
            {
                Tag = tag,
                Text = tag
            };
            TimeSignatureMenuItem_Click(proxy, new RoutedEventArgs());
        }

        public void HandleTitleBarKeySignatureCommand(string fifthsTag)
        {
            var proxy = new RadioMenuFlyoutItem
            {
                Tag = fifthsTag
            };
            KeySignatureMenuItem_Click(proxy, new RoutedEventArgs());
        }

        public void HandleTitleBarTempoCommand(string bpmTag)
        {
            var proxy = new RadioMenuFlyoutItem
            {
                Tag = bpmTag
            };
            TempoMenuItem_Click(proxy, new RoutedEventArgs());
        }

        public void HandleTitleBarSnapCommand(string divisionTag)
        {
            var proxy = new RadioMenuFlyoutItem
            {
                Tag = divisionTag
            };
            SnapMenuItem_Click(proxy, new RoutedEventArgs());
        }

        public void HandleTitleBarDisplayToggleCommand(string command, bool isChecked)
        {
            string normalized = command?.Trim().ToLowerInvariant() ?? string.Empty;
            switch (normalized)
            {
                case "show_grid":
                    if (_viewModel != null)
                    {
                        _viewModel.ShowBeatGrid = isChecked;
                        StaffCanvas.Invalidate();
                    }
                    break;
                case "area_select":
                    _isSelectingRect = false;
                    _viewModel?.SetStatus("\u533a\u57df\u9009\u62e9\u5df2\u6539\u4e3a\u9ed8\u8ba4\u62d6\u62fd\u89e6\u53d1\u3002");
                    StaffCanvas.Invalidate();
                    break;
            }
        }

        public void HandleTitleBarAutoAdjustMeasureRatioCommand(bool isChecked)
        {
            var proxy = new ToggleMenuFlyoutItem
            {
                IsChecked = isChecked
            };
            AutoAdjustMeasureRatioMenuItem_Click(proxy, new RoutedEventArgs());
        }

        public void HandleTitleBarMeasuresPerSystemCommand(string tag)
        {
            var proxy = new RadioMenuFlyoutItem
            {
                Tag = tag
            };
            MeasuresPerSystemMenuItem_Click(proxy, new RoutedEventArgs());
        }

        public void HandleTitleBarMusicFontCommand(string tag)
        {
            var proxy = new RadioMenuFlyoutItem
            {
                Tag = tag
            };
            MusicFontMenuItem_Click(proxy, new RoutedEventArgs());
        }

        public void HandleTitleBarClearCommand()
        {
            ClearAllMenuItem_Click(this, new RoutedEventArgs());
        }

        private void NewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _manualAdditionalSystems = 0;
            _manualMeasureCount = 0;
            _autoMeasuresPerSystem = GetDefaultMeasuresPerSystemForTimeSignature(_viewModel?.TimeSigNumerator ?? 4, _viewModel?.TimeSigDenominator ?? 4);
            _barlineOffsets.Clear();
            _systemMeasureCounts.Clear();
            _viewModel?.CreateNewProject();
            RestoreLayoutFromProject();
            ResetHistoryState();
            StaffCanvas.Invalidate();
        }

        private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel? viewModel = _viewModel;
            if (viewModel == null) return;

            string? path = await PickOpenPathAsync(".json");
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                _manualAdditionalSystems = 0;
                _manualMeasureCount = 0;
                _autoMeasuresPerSystem = GetDefaultMeasuresPerSystemForTimeSignature(viewModel.TimeSigNumerator, viewModel.TimeSigDenominator);
                _barlineOffsets.Clear();
                _systemMeasureCounts.Clear();
                viewModel.LoadProjectFromPath(path);
                RestoreLayoutFromProject();
                ResetHistoryState();
            }
            catch (Exception ex)
            {
                viewModel.SetStatus($"鎵撳紑澶辫触: {ex.Message}");
            }
        }

        private async void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            string path = _viewModel.CurrentProjectPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = await PickSavePathAsync(".json", "MusicBox Project", "Untitled.json") ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(path)) return;
            SyncLayoutToProject();
            _viewModel.SaveProjectToPath(path, updateCurrentPath: true);
        }

        private async void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            string suggested = GetSuggestedProjectName(".json");
            string? path = await PickSavePathAsync(".json", "MusicBox Project", suggested);
            if (string.IsNullOrWhiteSpace(path)) return;

            SyncLayoutToProject();
            _viewModel.SaveProjectToPath(path, updateCurrentPath: true);
        }

        private async void ImportMusicXmlMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel? viewModel = _viewModel;
            if (viewModel == null) return;

            string? path = await PickOpenPathAsync(".musicxml", ".xml");
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                _manualAdditionalSystems = 0;
                _manualMeasureCount = 0;
                _autoMeasuresPerSystem = GetDefaultMeasuresPerSystemForTimeSignature(viewModel.TimeSigNumerator, viewModel.TimeSigDenominator);
                _barlineOffsets.Clear();
                _systemMeasureCounts.Clear();
                viewModel.ImportMusicXmlFromPath(path);
                RestoreLayoutFromProject();
                ResetHistoryState();
                StaffCanvas.Invalidate();
            }
            catch (Exception ex)
            {
                viewModel.SetStatus($"瀵煎叆澶辫触: {ex.Message}");
            }
        }

        private async void ExportMusicXmlMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            string suggested = GetSuggestedProjectName(".musicxml");
            string? path = await PickSavePathAsync(".musicxml", "MusicXML Score", suggested);
            if (string.IsNullOrWhiteSpace(path)) return;

            SyncLayoutToProject();
            _viewModel.ExportMusicXmlToPath(path);
        }

        private void AddSystemAtEnd()
        {
            _manualAdditionalSystems = Math.Clamp(_manualAdditionalSystems + 1, 0, 64);
            int perSystem = Math.Max(1, _measuresPerSystem);
            int minimumRows = Math.Max(1, 1 + _manualAdditionalSystems);
            while (_systemMeasureCounts.Count < minimumRows)
            {
                _systemMeasureCounts.Add(perSystem);
            }

            int contentMeasureCount = GetContentMeasureCount(GetMeasureTicks());
            int measureCountByRows = Math.Max(1, _systemMeasureCounts.Sum());
            _manualMeasureCount = Math.Max(contentMeasureCount, measureCountByRows);
            _viewModel?.SetStatus($"宸插鍔犻澶栬氨琛ㄨ: {_manualAdditionalSystems}");
            PushHistorySnapshot();
            SwitchToNoNoteLengthIfNeeded();
        }

        private void AddSystemButton_Click(object sender, RoutedEventArgs e)
        {
            AddSystemAtEnd();
            StaffCanvas.Invalidate();
        }

        private async void ExportPdfMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            if (_isPreparingPrintPreview)
            {
                _viewModel.SetStatus("正在准备打印页面，请稍后...");
                return;
            }

            try
            {
                _isPreparingPrintPreview = true;
                ShowPrintBusyOverlay();
                await System.Threading.Tasks.Task.Delay(50);
                await PreparePrintPageAsync();
                if (_pendingPdfPages.Count == 0)
                {
                    throw new InvalidOperationException("No PDF page was generated.");
                }

                string suggested = GetSuggestedProjectName(".pdf");
                string? path = await PickSavePathAsync(".pdf", "PDF Document", suggested);
                if (string.IsNullOrWhiteSpace(path))
                {
                    _viewModel.SetStatus("已取消导出。");
                    return;
                }

                _pdfExporter.ExportJpegPages(path, _pendingPdfPages.ToList());
                _viewModel.SetStatus($"已导出 PDF: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                _viewModel.SetStatus($"导出 PDF 失败: {ex.Message}");
            }
            finally
            {
                _isPreparingPrintPreview = false;
                HidePrintBusyOverlay();
            }
        }

        private async void PrintMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            if (_isPreparingPrintPreview)
            {
                _viewModel.SetStatus("正在准备打印，请稍候...");
                return;
            }

            try
            {
                if (App.MainWindow == null) return;
                _isPreparingPrintPreview = true;
                ShowPrintBusyOverlay();
                await System.Threading.Tasks.Task.Delay(50);
                EnsurePrintManager();
                await PreparePrintPageAsync();
                HidePrintBusyOverlay();
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                await PrintManagerInterop.ShowPrintUIForWindowAsync(hwnd);
                _viewModel.SetStatus("已打开系统打印选项。");
            }
            catch (Exception ex)
            {
                _viewModel.SetStatus($"打开打印选项失败: {ex.Message}");
            }
            finally
            {
                _isPreparingPrintPreview = false;
                HidePrintBusyOverlay();
            }
        }

        private void ShowPrintBusyOverlay()
        {
            if (PrintBusyProgressBar != null)
            {
                PrintBusyProgressBar.IsIndeterminate = true;
            }

            if (PrintBusyOverlay != null)
            {
                PrintBusyOverlay.Visibility = Visibility.Visible;
            }
        }

        private void HidePrintBusyOverlay()
        {
            if (PrintBusyProgressBar != null)
            {
                PrintBusyProgressBar.IsIndeterminate = false;
            }

            if (PrintBusyOverlay != null)
            {
                PrintBusyOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async System.Threading.Tasks.Task<string?> PickOpenPathAsync(params string[] extensions)
        {
            if (App.MainWindow == null) return null;

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };

            foreach (string ext in extensions.Where(ext => !string.IsNullOrWhiteSpace(ext)))
            {
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

        private async System.Threading.Tasks.Task<string?> PickSavePathAsync(string extension, string fileTypeDescription, string suggestedFileName)
        {
            if (App.MainWindow == null) return null;

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

        private string GetSuggestedProjectName(string extension)
        {
            string name = _viewModel?.Title ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Untitled";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            string normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
            return $"{name}{normalizedExtension}";
        }

        private void EnsurePrintManager()
        {
            if (_printDocument != null && _printManager != null) return;
            if (App.MainWindow == null) return;

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

            _printDocument = new PrintDocument();
            _printDocument.Paginate += PrintDocument_Paginate;
            _printDocument.GetPreviewPage += PrintDocument_GetPreviewPage;
            _printDocument.AddPages += PrintDocument_AddPages;
            _printDocumentSource = _printDocument.DocumentSource;

            _printManager = PrintManagerInterop.GetForWindow(hwnd);
            _printManager.PrintTaskRequested += PrintManager_PrintTaskRequested;
        }

        private void UnregisterPrintManager()
        {
            if (_printManager != null)
            {
                _printManager.PrintTaskRequested -= PrintManager_PrintTaskRequested;
                _printManager = null;
            }

            if (_printDocument != null)
            {
                _printDocument.Paginate -= PrintDocument_Paginate;
                _printDocument.GetPreviewPage -= PrintDocument_GetPreviewPage;
                _printDocument.AddPages -= PrintDocument_AddPages;
                _printDocument = null;
            }

            _printDocumentSource = null;
            _pendingPrintPages.Clear();
            _pendingPdfPages.Clear();
            _printPages.Clear();
        }

        private async System.Threading.Tasks.Task PreparePrintPageAsync()
        {
            _pendingPrintPages.Clear();
            _pendingPdfPages.Clear();

            try
            {
                const float baseRenderScale = 5.0f;
                // Visual zoom for all notation primitives (staff gap, noteheads, symbols, title, expressions).
                // This controls physical size on page; render scale controls bitmap density.
                const float printContentZoom = 1.175f;
                float adjustedPrintContentZoom = Math.Clamp(1.02f * printContentZoom, 0.5f, 2.6f);
                const float printDpi = 300f;
                const float drawSidePadding = 18f;
                float printLeftGuardLogical = 16.0f * PrintSideMarginScale;
                float printRightGuardLogical = 22.0f * PrintSideMarginScale;
                float printTopGuardLogical = 12.4f * PrintSideMarginScale;
                float printBottomGuardLogical = 8.0f * PrintSideMarginScale;
                float leftReserved = Math.Max(0f, _musicStartX - _staffLeft);
                int maxMeasuresInAnySystem = Math.Max(
                    Math.Max(1, _measuresPerSystem),
                    _systemMeasureCounts.Count == 0 ? 1 : _systemMeasureCounts.Max());
                float dynamicContentWidth = Math.Max(_staffContentWidth, _measureWidth * maxMeasuresInAnySystem);
                float contentWidthLogical = leftReserved + dynamicContentWidth;
                double fallbackWidth = Math.Max(1d, StaffCanvas.ActualWidth);
                double targetLogicalWidth = contentWidthLogical > 0f
                    ? contentWidthLogical + drawSidePadding * 2f + _staffGap * 0.32f
                    : fallbackWidth;
                // Keep total print width stable while increasing left/right safety guards.
                const int preferredPrintTotalLogicalWidth = 2048;
                const int minPrintTotalLogicalWidth = 1420;
                int maxContentLogicalWidth = Math.Max(1, (int)Math.Floor(preferredPrintTotalLogicalWidth - (printLeftGuardLogical + printRightGuardLogical)));
                int minContentLogicalWidth = Math.Max(1, (int)Math.Floor(minPrintTotalLogicalWidth - (printLeftGuardLogical + printRightGuardLogical)));
                int logicalWidth = Math.Clamp((int)Math.Ceiling(Math.Min(targetLogicalWidth, maxContentLogicalWidth)), minContentLogicalWidth, maxContentLogicalWidth);
                float layoutWidthForPrint = Math.Max(360f, logicalWidth / Math.Max(1f, adjustedPrintContentZoom));
                float contentHeightLogical = _systemTopMargin + _systemCount * _systemStride + _staffGap * 3f;
                int logicalHeight = Math.Clamp((int)Math.Ceiling(contentHeightLogical), 1200, 24000);
                float printScale = baseRenderScale;
                const float maxRenderDimension = 20000f;
                const double maxRenderPixels = 48_000_000d;
                if (logicalWidth > 0)
                {
                    printScale = Math.Min(printScale, maxRenderDimension / logicalWidth);
                }
                printScale = Math.Clamp(printScale, 0.2f, baseRenderScale);

                var scaleAttempts = new List<float>();
                float currentAttemptScale = printScale;
                for (int i = 0; i < 6; i++)
                {
                    if (scaleAttempts.Count == 0 || Math.Abs(scaleAttempts[^1] - currentAttemptScale) > 0.001f)
                    {
                        scaleAttempts.Add(currentAttemptScale);
                    }

                    currentAttemptScale = Math.Max(0.2f, currentAttemptScale * 0.72f);
                }

                if (scaleAttempts.All(s => Math.Abs(s - 0.2f) > 0.001f))
                {
                    scaleAttempts.Add(0.2f);
                }

                Exception? lastRenderException = null;
                bool built = false;
                foreach (float attemptScale in scaleAttempts)
                {
                    _pendingPrintPages.Clear();
                    try
                    {
                        int pixelWidth = Math.Max(1, (int)Math.Ceiling(logicalWidth * attemptScale));
                        if (pixelWidth > maxRenderDimension)
                        {
                            throw new InvalidOperationException("Print width exceeds render capability.");
                        }

                        // Probe layout with print width so staff/system metrics match final print.
                        using (var probeTarget = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), 8, 8, printDpi))
                        using (var probeSession = probeTarget.CreateDrawingSession())
                        {
                            probeSession.Clear(Colors.White);
                            bool previousPrintInkOverride = _forcePrintInkOnWhite;
                            _forcePrintInkOnWhite = true;
                            try
                            {
                                DrawScoreToSession(
                                    probeSession,
                                    layoutWidthForPrint,
                                    logicalHeight,
                                    drawSelectionOverlay: false,
                                    layoutWidthOverride: layoutWidthForPrint,
                                    suppressSelectionVisualsOverride: true,
                                    suppressBeatGridOverride: true,
                                    compactForPrintLayout: true);
                            }
                            finally
                            {
                                _forcePrintInkOnWhite = previousPrintInkOverride;
                            }
                        }

                        float effectiveScale = Math.Max(0.01f, attemptScale * adjustedPrintContentZoom);
                        float actualLayoutWidthLogical = Math.Max(
                            layoutWidthForPrint,
                            _staffLeft + _staffWidth + drawSidePadding + _staffGap * 6.4f);
                        int pageContentWidthPixels = Math.Max(1, (int)Math.Ceiling(actualLayoutWidthLogical * effectiveScale));
                        int leftGuardPixels = Math.Max(0, (int)Math.Ceiling(printLeftGuardLogical * effectiveScale));
                        int rightGuardPixels = Math.Max(0, (int)Math.Ceiling(printRightGuardLogical * effectiveScale));
                        int topGuardPixels = Math.Max(0, (int)Math.Ceiling(printTopGuardLogical * effectiveScale));
                        int bottomGuardPixels = Math.Max(0, (int)Math.Ceiling(printBottomGuardLogical * effectiveScale));
                        int pagePixelWidth = Math.Max(1, pageContentWidthPixels + leftGuardPixels + rightGuardPixels);
                        if (pagePixelWidth > maxRenderDimension)
                        {
                            throw new InvalidOperationException("Print width exceeds render capability.");
                        }
                        int maxPageHeightPixels = Math.Max(1024, (int)Math.Floor(maxRenderPixels / Math.Max(1, pagePixelWidth)));
                        int maxContentPageHeightPixels = Math.Max(1, maxPageHeightPixels - topGuardPixels - bottomGuardPixels);
                        float maxPageHeightLogical = maxContentPageHeightPixels / effectiveScale;
                        if (maxPageHeightLogical < _systemStride * 1.15f)
                        {
                            throw new InvalidOperationException("Print scale too large for a full system.");
                        }

                        // Use page 1 as the print scale/height baseline for all pages.
                        // This keeps preview and final output visually consistent across pages.
                        float pageContentMaxHeightLogical = Math.Min(maxPageHeightLogical, logicalHeight);
                        float fixedPageLogicalHeight = -1f;
                        int systemIndex = 0;

                        while (systemIndex < _systemCount)
                        {
                            float activePageHeightLogical = fixedPageLogicalHeight > 0f
                                ? fixedPageLogicalHeight
                                : pageContentMaxHeightLogical;
                            float pageStartYLogical = systemIndex == 0
                                ? Math.Max(0f, (float)_titleHitRect.Y - _staffGap * 2.2f)
                                : Math.Max(0f, GetSystemTrebleTop(systemIndex) - _staffGap * 12.4f);
                            int lastSystem = systemIndex;
                            float pageEndYLogical = GetSystemBassBottom(lastSystem) + _staffGap * 4.2f;
                            if (pageEndYLogical - pageStartYLogical > activePageHeightLogical)
                            {
                                throw new InvalidOperationException("Print scale too large to fit a full system on one page.");
                            }
                            int maxSystemsThisPage = _pendingPrintPages.Count == 0 ? 4 : 5;

                            while (lastSystem + 1 < _systemCount)
                            {
                                int systemsOnPage = lastSystem - systemIndex + 1;
                                if (systemsOnPage >= maxSystemsThisPage)
                                {
                                    break;
                                }

                                float candidateEnd = GetSystemBassBottom(lastSystem + 1) + _staffGap * 4.2f;
                                if (candidateEnd - pageStartYLogical > activePageHeightLogical)
                                {
                                    break;
                                }

                                lastSystem++;
                                pageEndYLogical = candidateEnd;
                            }

                            float actualPageLogicalHeight = Math.Max(1f, pageEndYLogical - pageStartYLogical);
                            if (fixedPageLogicalHeight <= 0f)
                            {
                                fixedPageLogicalHeight = Math.Clamp(actualPageLogicalHeight, _systemStride * 1.1f, pageContentMaxHeightLogical);
                            }

                            int pageContentHeightPixels = Math.Max(1, (int)Math.Ceiling(fixedPageLogicalHeight * effectiveScale));
                            pageContentHeightPixels = Math.Min(pageContentHeightPixels, maxContentPageHeightPixels);
                            int pageHeightPixels = Math.Max(1, pageContentHeightPixels + topGuardPixels + bottomGuardPixels);
                            using var pageRenderTarget = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), pagePixelWidth, pageHeightPixels, printDpi);
                            using (var pageSession = pageRenderTarget.CreateDrawingSession())
                            {
                                pageSession.Clear(Colors.White);

                                var scale = System.Numerics.Matrix3x2.CreateScale(effectiveScale);
                                var translate = System.Numerics.Matrix3x2.CreateTranslation(
                                    printLeftGuardLogical * effectiveScale,
                                    topGuardPixels - pageStartYLogical * effectiveScale);
                                pageSession.Transform = scale * translate;
                                bool previousPrintInkOverride = _forcePrintInkOnWhite;
                                _forcePrintInkOnWhite = true;
                                try
                                {
                                    DrawScoreToSession(
                                        pageSession,
                                        layoutWidthForPrint,
                                        logicalHeight,
                                        drawSelectionOverlay: false,
                                        layoutWidthOverride: layoutWidthForPrint,
                                        suppressSelectionVisualsOverride: true,
                                        suppressBeatGridOverride: true,
                                        compactForPrintLayout: true);
                                }
                                finally
                                {
                                    _forcePrintInkOnWhite = previousPrintInkOverride;
                                }
                                pageSession.Transform = System.Numerics.Matrix3x2.Identity;
                            }

                            _pendingPdfPages.Add(await CreateRasterPdfPageAsync(pageRenderTarget));
                            _pendingPrintPages.Add(await CreatePrintImagePageAsync(pageRenderTarget, 10));
                            systemIndex = lastSystem + 1;
                        }

                        if (_pendingPrintPages.Count == 0)
                        {
                            throw new InvalidOperationException("No printable page generated.");
                        }

                        built = _pendingPrintPages.Count > 0;
                        if (built)
                        {
                            AppendPrintPageNumbers();
                            break;
                        }
                    }
                    catch (Exception attemptEx)
                    {
                        lastRenderException = attemptEx;
                    }
                }

                if (!built)
                {
                    throw lastRenderException ?? new InvalidOperationException("Unknown print rendering failure.");
                }
            }
            catch (Exception ex)
            {
                _viewModel?.SetStatus($"打印预览生成失败: {ex.Message}");
                _pendingPrintPages.Clear();
                _pendingPdfPages.Clear();
                _pendingPrintPages.Add(new Grid
                {
                    Background = new SolidColorBrush(Colors.White),
                    Padding = new Thickness(24),
                    Children =
                    {
                        new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = _viewModel?.Title ?? "Untitled",
                                    FontSize = 20
                                },
                                new TextBlock
                                {
                                    Text = $"Print preview failed: {ex.Message}",
                                    FontSize = 14
                                }
                            }
                        }
                    }
                });
            }
        }

        private static async System.Threading.Tasks.Task<UIElement> CreatePrintImagePageAsync(CanvasRenderTarget renderTarget, double padding)
        {
            using var stream = new InMemoryRandomAccessStream();
            await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            double sidePadding = Math.Max(8d, padding * PrintPageSidePaddingScale);
            const double topPadding = 28d;
            const double bottomPadding = 12d;
            return new Grid
            {
                Background = new SolidColorBrush(Colors.White),
                Padding = new Thickness(sidePadding, topPadding, sidePadding, bottomPadding),
                Children =
                {
                    new Border
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top,
                        Padding = new Thickness(0),
                        Child = new Image
                        {
                            Source = bitmap,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Top
                        }
                    }
                }
            };
        }

        private static async System.Threading.Tasks.Task<RasterPdfPage> CreateRasterPdfPageAsync(CanvasRenderTarget renderTarget)
        {
            using var stream = new InMemoryRandomAccessStream();
            await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Jpeg);
            stream.Seek(0);

            byte[] bytes = new byte[stream.Size];
            using Stream managed = stream.AsStreamForRead();
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = await managed.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                offset += read;
            }

            return new RasterPdfPage(bytes, (int)renderTarget.SizeInPixels.Width, (int)renderTarget.SizeInPixels.Height);
        }

        private void AppendPrintPageNumbers()
        {
            if (_pendingPrintPages.Count == 0)
            {
                return;
            }

            int total = _pendingPrintPages.Count;
            var decorated = new List<UIElement>(total);
            for (int i = 0; i < total; i++)
            {
                UIElement pageContent = _pendingPrintPages[i];
                var root = new Grid
                {
                    Background = new SolidColorBrush(Colors.White),
                    RowDefinitions =
                    {
                        new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                        new RowDefinition { Height = GridLength.Auto }
                    }
                };

                double extraTopMargin = i == 0 ? 0d : 42d;
                var contentHost = new Border
                {
                    Margin = new Thickness(0, extraTopMargin, 0, 0),
                    Child = pageContent
                };
                root.Children.Add(contentHost);

                var footer = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    FontSize = 11,
                    FontFamily = new FontFamily("Times New Roman"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 28),
                    Foreground = new SolidColorBrush(Colors.Black)
                };
                Grid.SetRow(footer, 1);
                root.Children.Add(footer);
                decorated.Add(root);
            }

            _pendingPrintPages.Clear();
            _pendingPrintPages.AddRange(decorated);
        }

        private void PrintManager_PrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
        {
            args.Request.CreatePrintTask(" ", sourceArgs =>
            {
                if (_printDocumentSource != null)
                {
                    sourceArgs.SetSource(_printDocumentSource);
                }
            });
        }

        private void PrintDocument_Paginate(object sender, PaginateEventArgs e)
        {
            if (_printDocument == null) return;

            _printPages.Clear();
            if (_pendingPrintPages.Count > 0)
            {
                _printPages.AddRange(_pendingPrintPages);
            }
            else
            {
                _printPages.Add(CreateFallbackPrintPage());
            }
            _printDocument.SetPreviewPageCount(_printPages.Count, PreviewPageCountType.Final);
        }

        private void PrintDocument_GetPreviewPage(object sender, GetPreviewPageEventArgs e)
        {
            if (_printDocument == null) return;
            int index = Math.Clamp((int)e.PageNumber - 1, 0, Math.Max(0, _printPages.Count - 1));
            _printDocument.SetPreviewPage((int)e.PageNumber, _printPages[index]);
        }

        private void PrintDocument_AddPages(object sender, AddPagesEventArgs e)
        {
            if (_printDocument == null) return;

            foreach (UIElement page in _printPages)
            {
                _printDocument.AddPage(page);
            }

            _printDocument.AddPagesComplete();
        }

        private UIElement CreateFallbackPrintPage()
        {
            return new Grid
            {
                Background = new SolidColorBrush(Colors.White),
                Children =
                {
                    new TextBlock
                    {
                        Text = _viewModel?.Title ?? "Untitled",
                        Margin = new Thickness(24),
                        FontSize = 16
                    }
                }
            };
        }

        private int ResolveMidScoreSignatureInsertTick()
        {
            if (_viewModel == null)
            {
                return 0;
            }

            if (_measurePanelSystemIndex >= 0 && _measurePanelBoundaryLocalIndex >= 0)
            {
                int panelSystem = Math.Clamp(_measurePanelSystemIndex, 0, Math.Max(0, _systemCount - 1));
                int measuresInSystem = GetMeasuresInSystem(panelSystem);
                int localBoundary = Math.Clamp(_measurePanelBoundaryLocalIndex, 0, measuresInSystem);
                int globalBoundary = GetSystemStartMeasureIndex(panelSystem) + localBoundary;
                return GetMeasureBoundaryTick(globalBoundary);
            }

            if (_highlightBarlineSystemIndex >= 0 && _highlightBarlineLocalIndex >= 0)
            {
                int highlightSystem = Math.Clamp(_highlightBarlineSystemIndex, 0, Math.Max(0, _systemCount - 1));
                int measuresInSystem = GetMeasuresInSystem(highlightSystem);
                int localBoundary = Math.Clamp(_highlightBarlineLocalIndex, 0, measuresInSystem);
                int globalBoundary = GetSystemStartMeasureIndex(highlightSystem) + localBoundary;
                return GetMeasureBoundaryTick(globalBoundary);
            }

            var selectedNote = _viewModel.Project.Notes
                .Where(n => n.IsSelected)
                .OrderBy(n => n.StartTick)
                .FirstOrDefault();
            if (selectedNote != null)
            {
                int boundary = GetMeasureIndex(selectedNote.StartTick);
                return GetMeasureBoundaryTick(boundary);
            }

            var selectedMark = _viewModel.Project.ExpressionMarks
                .Where(m => m.IsSelected)
                .OrderBy(m => m.StartTick)
                .FirstOrDefault();
            if (selectedMark != null)
            {
                int boundary = GetMeasureIndex(selectedMark.StartTick);
                return GetMeasureBoundaryTick(boundary);
            }

            if (_lastPointerCanvasPoint.X >= 0d && _lastPointerCanvasPoint.Y >= 0d)
            {
                int systemIndex = GetSystemIndexFromY((float)_lastPointerCanvasPoint.Y);
                int rawTick = SnapBarlineTickForSystem(_lastPointerCanvasPoint.X, systemIndex);
                int boundaryIndex = GetNearestMeasureBoundaryIndexForTick(rawTick);
                return GetMeasureBoundaryTick(boundaryIndex);
            }

            return 0;
        }

        private void UpsertTimeSignatureChange(int tick, int numerator, int denominator)
        {
            if (_viewModel == null) return;

            int safeTick = Math.Max(0, tick);
            int safeNumerator = Math.Clamp(numerator, 1, 12);
            int safeDenominator = denominator is 1 or 2 or 4 or 8 or 16 ? denominator : 4;
            _viewModel.Project.TimeSignatureChanges.RemoveAll(c => c.Tick == safeTick);
            if (safeTick == 0)
            {
                _viewModel.Project.TimeSignature.Numerator = safeNumerator;
                _viewModel.Project.TimeSignature.Denominator = safeDenominator;
            }
            else
            {
                _viewModel.Project.TimeSignatureChanges.Add(new TimeSignatureChange
                {
                    Tick = safeTick,
                    Numerator = safeNumerator,
                    Denominator = safeDenominator
                });
            }

            _viewModel.Project.TimeSignatureChanges = _viewModel.Project.TimeSignatureChanges
                .OrderBy(c => c.Tick)
                .ToList();
        }

        private void UpsertKeySignatureChange(int tick, int fifths)
        {
            if (_viewModel == null) return;

            int safeTick = Math.Max(0, tick);
            int safeFifths = Math.Clamp(fifths, -7, 7);
            _viewModel.Project.KeySignatureChanges.RemoveAll(c => c.Tick == safeTick);
            if (safeTick == 0)
            {
                _viewModel.Project.KeySignature.Fifths = safeFifths;
            }
            else
            {
                _viewModel.Project.KeySignatureChanges.Add(new KeySignatureChange
                {
                    Tick = safeTick,
                    Fifths = safeFifths,
                    Mode = _viewModel.Project.KeySignature.Mode
                });
            }

            _viewModel.Project.KeySignatureChanges = _viewModel.Project.KeySignatureChanges
                .OrderBy(c => c.Tick)
                .ToList();
        }

        private void TimeSignatureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || sender is not RadioMenuFlyoutItem item) return;

            if (!TryParseTimeSignature(item.Tag?.ToString() ?? item.Text, out int numerator, out int denominator))
            {
                return;
            }

            int insertTick = ResolveMidScoreSignatureInsertTick();
            UpsertTimeSignatureChange(insertTick, numerator, denominator);
            if (insertTick <= 0)
            {
                SetTimeSignatureSelection(numerator, denominator);
            }
            else
            {
                int boundary = GetNearestMeasureBoundaryIndexForTick(insertTick);
                _viewModel.SetStatus($"\u5df2\u5728\u7b2c {Math.Max(1, boundary + 1)} \u5c0f\u8282\u8d77\u59cb\u5904\u63d2\u5165\u62cd\u53f7 {numerator}/{denominator}");
            }
            MarkProjectChanged();
            StaffCanvas.Invalidate();
            SwitchToNoNoteLengthIfNeeded();
        }

        private void KeySignatureMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || sender is not RadioMenuFlyoutItem item) return;

            if (int.TryParse(item.Tag?.ToString(), out int fifths))
            {
                int insertTick = ResolveMidScoreSignatureInsertTick();
                UpsertKeySignatureChange(insertTick, fifths);
                if (insertTick <= 0)
                {
                    SetKeySignatureSelection(fifths);
                }
                else
                {
                    int boundary = GetNearestMeasureBoundaryIndexForTick(insertTick);
                    _viewModel.SetStatus($"\u5df2\u5728\u7b2c {Math.Max(1, boundary + 1)} \u5c0f\u8282\u8d77\u59cb\u5904\u63d2\u5165\u8c03\u53f7");
                }
                MarkProjectChanged();
                StaffCanvas.Invalidate();
            }
            SwitchToNoNoteLengthIfNeeded();
        }

        private void TempoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || sender is not RadioMenuFlyoutItem item) return;

            if (int.TryParse(item.Tag?.ToString(), out int bpm))
            {
                _viewModel.Bpm = bpm;
            }
            SwitchToNoNoteLengthIfNeeded();
        }

        private void NoteLengthButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || sender is not RadioButton item) return;

            if (item.Tag is string tag && Enum.TryParse(tag, out NoteLength length))
            {
                _viewModel.SelectedNoteLength = length;
            }
        }

        private void SnapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || sender is not RadioMenuFlyoutItem item) return;
            if (int.TryParse(item.Tag?.ToString(), out int division))
            {
                _viewModel.SnapDivision = Math.Clamp(division, 1, 32);
            }
            SwitchToNoNoteLengthIfNeeded();
        }

        private void MeasuresPerSystemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioMenuFlyoutItem item)
            {
                return;
            }

            string tag = item.Tag?.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
            if (tag == "auto")
            {
                _displayMeasuresPerSystemOverride = 0;
                _autoMeasuresPerSystem = GetDefaultMeasuresPerSystemForTimeSignature(_viewModel?.TimeSigNumerator ?? 4, _viewModel?.TimeSigDenominator ?? 4);
                _systemMeasureCounts.Clear();
                _barlineOffsets.Clear();
                _viewModel?.SetStatus("姣忚灏忚妭鏁板凡璁句负鑷姩");
            }
            else if (int.TryParse(tag, out int perSystem))
            {
                _displayMeasuresPerSystemOverride = Math.Clamp(perSystem, 1, 32);
                int measureTicks = GetMeasureTicks();
                int desiredTotal = GetFixedModeTotalMeasureCount(measureTicks, _displayMeasuresPerSystemOverride);
                EnsureFixedSystemMeasureCounts(_displayMeasuresPerSystemOverride, desiredTotal);
                _viewModel?.SetStatus($"姣忚灏忚妭鏁板凡璁句负 {_displayMeasuresPerSystemOverride}");
            }
            else
            {
                return;
            }

            _barlineOffsets.Clear();
            HideMeasureEditPanel();
            StaffCanvas.Invalidate();
        }

        private void AutoAdjustMeasureRatioMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleMenuFlyoutItem item)
            {
                return;
            }

            _allowAutoMeasureRatioAdjust = item.IsChecked;
            _barlineOffsets.Clear();
            HideMeasureEditPanel();
            _viewModel?.SetStatus(_allowAutoMeasureRatioAdjust
                ? "\u5df2\u5f00\u542f\u81ea\u52a8\u8c03\u6574\u6bd4\u4f8b"
                : "\u5df2\u5173\u95ed\u81ea\u52a8\u8c03\u6574\u6bd4\u4f8b");
            StaffCanvas.Invalidate();
        }

        private void SetKeySignatureSelection(int fifths)
        {
            foreach (var item in KeySignatureMenu.Items.OfType<RadioMenuFlyoutItem>())
            {
                if (int.TryParse(item.Tag?.ToString(), out int itemFifths) && itemFifths == fifths)
                {
                    item.IsChecked = true;
                    break;
                }
            }
        }

        private void SetTimeSignatureSelection(int numerator, int denominator)
        {
            foreach (var item in TimeSignatureMenu.Items.OfType<RadioMenuFlyoutItem>())
            {
                if (TryParseTimeSignature(item.Tag?.ToString() ?? item.Text, out int itemNumerator, out int itemDenominator)
                    && itemNumerator == numerator
                    && itemDenominator == denominator)
                {
                    item.IsChecked = true;
                    break;
                }
            }
        }

        private void SetTempoSelection(double bpm)
        {
            int target = (int)Math.Round(bpm);
            bool matched = false;

            foreach (var item in TempoMenu.Items.OfType<RadioMenuFlyoutItem>())
            {
                if (int.TryParse(item.Tag?.ToString(), out int itemBpm))
                {
                    bool isMatch = itemBpm == target;
                    item.IsChecked = isMatch;
                    matched |= isMatch;
                }
            }

            if (!matched)
            {
                foreach (var item in TempoMenu.Items.OfType<RadioMenuFlyoutItem>())
                {
                    item.IsChecked = false;
                }
            }
        }

        private void SetSnapSelection(int division)
        {
            bool matched = false;
            foreach (var item in SnapMenu.Items.OfType<RadioMenuFlyoutItem>())
            {
                if (int.TryParse(item.Tag?.ToString(), out int value))
                {
                    bool isMatch = value == division;
                    item.IsChecked = isMatch;
                    matched |= isMatch;
                }
            }

            if (!matched)
            {
                foreach (var item in SnapMenu.Items.OfType<RadioMenuFlyoutItem>())
                {
                    item.IsChecked = false;
                }
            }
        }

        private void SetNoteLengthSelection(NoteLength length)
        {
            if (NoteLengthButtons == null) return;

            foreach (var item in NoteLengthButtons.Children.OfType<RadioButton>())
            {
                if (item.Tag is string tag && Enum.TryParse(tag, out NoteLength candidate))
                {
                    item.IsChecked = candidate == length;
                }
                else
                {
                    item.IsChecked = false;
                }
            }
        }

        private void SetDurationInputModeSelection(bool restMode)
        {
            if (DurationModeToggleSwitch == null) return;

            _syncingDurationModeControls = true;
            try
            {
                DurationModeToggleSwitch.IsOn = restMode;
            }
            finally
            {
                _syncingDurationModeControls = false;
            }

            UpdateDurationModeStateText();
        }

        private void UpdateDurationModeStateText()
        {
            if (DurationModeStateText == null)
            {
                return;
            }

            bool restMode = _isRestInputMode;
            bool isEnglish = AppSettingsService.Instance.ResolveLanguageTag().StartsWith("en", StringComparison.OrdinalIgnoreCase);
            DurationModeStateText.Text = restMode
                ? (isEnglish ? "Rest" : LocalizationService.Translate("editor.duration_mode.rest"))
                : (isEnglish ? "Note" : LocalizationService.Translate("editor.duration_mode.note"));
            DurationModeStateText.Opacity = restMode ? 0.95 : 0.88;
        }

        private void UpdateNoteTypeMenuEnabledState()
        {
            bool noteMode = !_isRestInputMode;
            if (AccidentalSharpItem != null) AccidentalSharpItem.IsEnabled = noteMode;
            if (AccidentalFlatItem != null) AccidentalFlatItem.IsEnabled = noteMode;
            if (AccidentalNaturalItem != null) AccidentalNaturalItem.IsEnabled = noteMode;
            if (StaccatoMenuItem != null) StaccatoMenuItem.IsEnabled = noteMode;
            if (StaccatissimoMenuItem != null) StaccatissimoMenuItem.IsEnabled = noteMode;
            if (AccentMenuItem != null) AccentMenuItem.IsEnabled = noteMode;
            if (OrnamentSubMenuItem != null) OrnamentSubMenuItem.IsEnabled = noteMode;
            if (_isRestInputMode)
            {
                _syncingNoteTypeControls = true;
                try
                {
                    if (AccidentalSharpItem != null) AccidentalSharpItem.IsChecked = false;
                    if (AccidentalFlatItem != null) AccidentalFlatItem.IsChecked = false;
                    if (AccidentalNaturalItem != null) AccidentalNaturalItem.IsChecked = false;
                    if (StaccatoMenuItem != null) StaccatoMenuItem.IsChecked = false;
                    if (StaccatissimoMenuItem != null) StaccatissimoMenuItem.IsChecked = false;
                    if (AccentMenuItem != null) AccentMenuItem.IsChecked = false;
                    SetOrnamentSelection(NoteOrnament.None);
                }
                finally
                {
                    _syncingNoteTypeControls = false;
                }
                SyncPendingNoteTypeFromControls();
            }
        }

        private void DurationModeToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_syncingDurationModeControls) return;
            if (DurationModeToggleSwitch == null) return;

            _isRestInputMode = DurationModeToggleSwitch.IsOn;
            UpdateDurationModeStateText();
            UpdateNoteTypeMenuEnabledState();
            SwitchToNoNoteLengthIfNeeded();
        }

        private static bool TryParseTimeSignature(string? value, out int numerator, out int denominator)
        {
            numerator = 0;
            denominator = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var parts = value.Split('/');
            if (parts.Length != 2) return false;

            return int.TryParse(parts[0].Trim(), out numerator)
                && int.TryParse(parts[1].Trim(), out denominator);
        }
    }
}





















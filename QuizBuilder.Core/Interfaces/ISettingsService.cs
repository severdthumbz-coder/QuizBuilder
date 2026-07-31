using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// What the pass mark is a percentage OF.
///
/// These genuinely disagree on a weighted paper. Three 1-point MC questions
/// plus one 10-point essay: a student who aces the MC and skips the essay has
/// 75% of the questions right but only 23% of the marks. Neither answer is
/// wrong -- they are different assessments -- so the author picks.
/// </summary>
public enum PassMarkBasis
{
    // Order matters twice over: it is the default (first == ordinal 0) and,
    // although JsonStringEnumConverter persists these by NAME so reordering
    // will not corrupt saved files, inserting a value before QuestionCount
    // would change default(PassMarkBasis) for any settings.json lacking the
    // key. Append new values; do not insert.

    /// <summary>
    /// A percentage of the questions answered correctly. Weight-blind: a
    /// 10-point essay counts the same as a 1-point true/false.
    /// </summary>
    QuestionCount,

    /// <summary>
    /// A percentage of the paper's total points. Respects question weighting.
    /// </summary>
    TotalPoints
}

/// <summary>
/// Where the Flash Cards tab draws its cards from.
/// </summary>
public enum FlashCardSource
{
    // Persisted by name (JsonStringEnumConverter), so reordering is safe -- but
    // the first value is default() for any settings.json lacking the key, so
    // Quiz staying first keeps existing files behaving as they did before study
    // cards existed: quiz questions only.

    /// <summary>The quiz's questions and their answers. The original behaviour.</summary>
    Quiz,

    /// <summary>Only the hand-authored study cards.</summary>
    StudyCards,

    /// <summary>Both, quiz questions followed by study cards.</summary>
    Both
}

public enum GradingScope
{
    /// <summary>Every section is graded.</summary>
    AllSections,
    /// <summary>The user picks sections at quiz time.</summary>
    SelectAtQuizTime
}

public enum QuestionSelectionMode
{
    AllQuestions,
    ExactCountPerSection,

    /// <summary>
    /// A single total across the whole quiz, distributed across sections in
    /// proportion to how many questions each holds. A section too small for its
    /// proportional share simply contributes all it has; the arithmetic never
    /// asks for more than a section contains.
    /// </summary>
    TotalCount
}

public enum ResultsDisplayMode
{
    AfterEachQuestion,
    AtEnd
}

/// <summary>
/// Everything persisted to settings.json, next to the .exe.
///
/// Per-tab settings are namespaced by the owning tab so future additions
/// don't collide. Adding a property is backward-compatible (older files
/// simply deserialize it as the default); renaming one silently resets that
/// value for existing users.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Schema version, for future migrations.</summary>
    public int Version { get; set; } = 1;

    public QuizSettings Quiz { get; set; } = new();
    public ThemeSettings Theme { get; set; } = new();
    public PublishSettings Publish { get; set; } = new();
    public GitHubSettings GitHub { get; set; } = new();
    public ShellSettings Shell { get; set; } = new();
    public AutoSaveSettings AutoSave { get; set; } = new();
    public UndoSettings Undo { get; set; } = new();

    /// <summary>
    /// Escape hatch for per-tab settings added later without touching this
    /// class. Keys should be namespaced, e.g. "preview.zoomLevel".
    /// </summary>
    public Dictionary<string, string> Extra { get; set; } = new();
}

/// <summary>
/// Autosave for the current .qbx session.
///
/// Interval is in MINUTES rather than a change count. "Every N changes" sounds
/// more precise but fires unpredictably -- one drag-reorder can be several
/// model changes, so the user cannot anticipate when a write happens. A clock
/// interval is what people mean by autosave and what they can reason about.
/// </summary>
public sealed class UndoSettings
{
    /// <summary>
    /// Zero disables undo entirely. Offered because snapshots cost memory and
    /// someone on a very large quiz may prefer not to pay it.
    /// </summary>
    public const int MinDepth = 0;

    /// <summary>
    /// Each step is a full serialised copy of the document, so depth is
    /// linear in memory. A hundred steps of a large quiz is already tens of
    /// megabytes; beyond that the cost outweighs any plausible use.
    /// </summary>
    public const int MaxDepth = 100;

    /// <summary>
    /// Deep enough to cover a wrong turn and back out of it, shallow enough
    /// that the memory cost stays invisible on an ordinary quiz.
    /// </summary>
    public const int DefaultDepth = 15;

    public int Depth { get; set; } = DefaultDepth;
}

public sealed class AutoSaveSettings
{
    /// <summary>Below one minute the timer would thrash the disk.</summary>
    public const int MinIntervalMinutes = 1;

    /// <summary>An hour between saves is already barely autosave.</summary>
    public const int MaxIntervalMinutes = 60;

    public const int DefaultIntervalMinutes = 5;

    /// <summary>
    /// Off by default. Autosave silently overwrites the user's file, and doing
    /// that without them having asked is a surprise -- particularly for someone
    /// who opened a .qbx to look at it rather than edit it.
    /// </summary>
    public bool Enabled { get; set; }

    public int IntervalMinutes { get; set; } = DefaultIntervalMinutes;
}

public sealed class QuizSettings
{
    public const int MinPassPercentage = 0;
    public const int MaxPassPercentage = 100;
    public const int DefaultPassPercentage = 50;

    /// <summary>
    /// Fraction of its own points a question must score to count as "correct"
    /// under QuestionCount. Half: an essay marked 6/10 counts, 4/10 does not.
    /// Only relevant to partially-credited types -- multiple choice and
    /// true/false are all-or-nothing anyway.
    /// </summary>
    public const double CorrectAtFraction = 0.5;

    public GradingScope GradingScope { get; set; } = GradingScope.AllSections;

    /// <summary>
    /// Whether the pass mark counts questions or points. Defaults to counting
    /// questions: "get 75% of them right" is what most people mean by a pass
    /// mark, and it only diverges from points on a weighted paper.
    /// </summary>
    public PassMarkBasis PassMarkBasis { get; set; } = PassMarkBasis.QuestionCount;

    /// <summary>
    /// Percentage needed to pass -- of questions or of points, per
    /// <see cref="PassMarkBasis"/>.
    ///
    /// Stored as an int: a pass mark of 62.75% is false precision, and whole
    /// percentages are what anyone actually sets. Clamped on read as well as
    /// on write, because settings.json is hand-editable and a value of 500
    /// there should not make every paper unpassable.
    /// </summary>
    public int PassPercentage
    {
        get => Math.Clamp(_passPercentage, MinPassPercentage, MaxPassPercentage);
        set => _passPercentage = Math.Clamp(value, MinPassPercentage, MaxPassPercentage);
    }

    private int _passPercentage = DefaultPassPercentage;

    public QuestionSelectionMode SelectionMode { get; set; } = QuestionSelectionMode.AllQuestions;

    /// <summary>
    /// Which source the Flash Cards tab uses. Defaults to Quiz, so anyone who
    /// never touches study cards sees exactly the behaviour they had before the
    /// feature existed.
    /// </summary>
    public FlashCardSource FlashCardSource { get; set; } = FlashCardSource.Quiz;

    /// <summary>
    /// Multiplier applied to the flash card's text, on top of whatever the
    /// theme's type ramp already gives. A multiplier rather than a fixed point
    /// size so the setting survives a theme change: pick a bigger theme base
    /// size and the cards scale with it instead of staying pinned.
    /// </summary>
    public double FlashCardTextScale { get; set; } = FlashCardTextScaleDefault;

    public const double FlashCardTextScaleMin = 0.75;
    public const double FlashCardTextScaleMax = 2.5;
    public const double FlashCardTextScaleDefault = 1.0;
    public const double FlashCardTextScaleStep = 0.25;

    /// <summary>Section id -> question count, when SelectionMode is ExactCountPerSection.</summary>
    public Dictionary<string, int> QuestionCountPerSection { get; set; } = new();

    /// <summary>
    /// The total questions to include when SelectionMode is TotalCount, spread
    /// proportionally across sections. Zero or negative is treated as "no paper".
    /// A value at or above the quiz's total simply includes everything.
    /// </summary>
    public int TotalQuestionCount { get; set; }

    public bool RandomizeQuestionOrder { get; set; }
    public bool RandomizeAnswerOrder { get; set; }

    /// <summary>Null means no time limit.</summary>
    public int? TimeLimitMinutes { get; set; }

    /// <summary>Default point value per question type, keyed by QuestionKind name.</summary>
    public Dictionary<string, double> DefaultPoints { get; set; } = new()
    {
        [nameof(QuestionKind.MultipleChoiceSingle)] = 1,
        [nameof(QuestionKind.MultipleChoiceMultiple)] = 2,
        [nameof(QuestionKind.TrueFalse)] = 1,
        [nameof(QuestionKind.ShortAnswer)] = 2,
        [nameof(QuestionKind.FillInTheBlank)] = 2,
        [nameof(QuestionKind.Matching)] = 3,

        // Same as matching: both ask the taker to arrange several items rather
        // than pick one, and both award partial credit.
        [nameof(QuestionKind.Sequence)] = 3,

        [nameof(QuestionKind.Essay)] = 10,
    };

    public double PointsFor(QuestionKind kind) =>
        DefaultPoints.TryGetValue(kind.ToString(), out var p) ? p : 1;
}

public sealed class ThemeSettings
{
    public string ActiveThemeId { get; set; } = Theming.BuiltInThemes.AcademicId;

    /// <summary>The user's edited theme, if any. Survives app restarts.</summary>
    public Theming.ThemeTokens? CustomTheme { get; set; }
}

public sealed class PublishSettings
{
    public bool AppendAnswerKey { get; set; } = true;
    public string? LastExportFolder { get; set; }
    public string? LastWebPublishFolder { get; set; }
    public ResultsDisplayMode ResultsDisplay { get; set; } = ResultsDisplayMode.AtEnd;

    /// <summary>Null means "use all selected questions".</summary>
    public int? WebQuizQuestionCount { get; set; }

    /// <summary>Most-recent-first, capped at 10 by SettingsService.</summary>
    public List<string> RecentFiles { get; set; } = new();
}

public sealed class GitHubSettings
{
    public string? RepositoryUrl { get; set; }
    public string? DefaultBranch { get; set; } = "main";

    /// <summary>
    /// How <see cref="EncryptedToken"/> is protected at rest. User-selectable,
    /// because the right answer depends on how the app is actually used:
    /// MachineBound is strongest but does not travel between machines;
    /// Passphrase travels but costs one prompt per session; None never
    /// persists the token at all.
    /// </summary>
    public TokenProtectionMode TokenProtection { get; set; } = TokenProtectionMode.MachineBound;

    /// <summary>
    /// The protected PAT, prefixed with the scheme that produced it
    /// ("dpapi$..." or "pbkdf2$..."). Null when no token is stored, or when
    /// TokenProtection is None (that mode keeps the token in memory only).
    ///
    /// The prefix matters: it lets Unprotect detect a blob written under a
    /// different mode and fail cleanly instead of throwing.
    /// </summary>
    public string? EncryptedToken { get; set; }

    public string? LastCommitHash { get; set; }
    public string? PublishedPagesUrl { get; set; }

    /// <summary>
    /// A link to the mobile player's downloadable APK (typically a GitHub
    /// release asset). Purely a convenience the GitHub tab turns into a
    /// scannable QR so a phone can fetch the app; it is never used to publish
    /// anything. Optional and free-text, so old settings files without it load
    /// unchanged (plain get/set, no required, matching the rest of this model).
    /// </summary>
    public string? ApkDownloadUrl { get; set; }
}

public sealed class ShellSettings
{
    public NavDestination LastActiveTab { get; set; } = NavDestination.QuizBuilder;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> to settings.json beside the
/// executable. Never touches %AppData% or the registry: the app is portable.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Full path to settings.json, for display in the UI.</summary>
    string SettingsFilePath { get; }

    event EventHandler? SettingsChanged;

    void Load();
    void Save();

    /// <summary>Adds a path to the recent list, de-duplicating and capping at 10.</summary>
    void AddRecentFile(string path);

    /// <summary>
    /// Encrypts and stores the GitHub PAT. Pass null to clear it.
    /// In Passphrase mode, throws InvalidOperationException when locked --
    /// call <see cref="UnlockTokens"/> first.
    /// </summary>
    void SetGitHubToken(string? plainTextToken);

    /// <summary>
    /// Decrypts the stored PAT, or null when absent, locked, or undecryptable
    /// (e.g. a machine-bound blob opened on a different machine). Callers must
    /// handle null by prompting for re-entry rather than treating it as fatal.
    /// </summary>
    string? GetGitHubToken();

    /// <summary>
    /// True when a stored token exists but a passphrase is needed to read it.
    /// The GitHub tab uses this to decide whether to prompt on first use.
    /// </summary>
    bool RequiresPassphrase { get; }

    /// <summary>
    /// Supplies the passphrase for Passphrase mode. Returns false on a wrong
    /// passphrase, leaving the locked state unchanged.
    /// </summary>
    bool UnlockTokens(string passphrase);

    /// <summary>
    /// Changes how the token is protected. The existing token is cleared --
    /// ciphertext is not transcoded between modes -- so the user must re-enter
    /// it afterwards. The UI should say so before calling this.
    /// </summary>
    void SetTokenProtectionMode(TokenProtectionMode mode);
}

using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using QuizBuilder.Player.Models;

namespace QuizBuilder.Player.Services;

/// <summary>
/// The single source of truth for a session: who is taking the quiz, which quiz
/// is loaded, the paper compiled from it, and the graded result. Injected as a
/// singleton so the identity screen, take screen and results screen all read
/// the same state.
///
/// <para>
/// The compile/grade calls go through Core's QuizCompiler and QuizGrader,
/// constructed directly -- no DI graph, no TokenProtector, no DPAPI. This is
/// exactly the composition pinned by MobileReadPathContractTests in the Core
/// test suite, which runs on desktop CI with storage sandboxed and DPAPI
/// disabled. If that test is green, this path is proven platform-neutral.
/// </para>
/// </summary>
public sealed class QuizSessionService
{
    private readonly IQuizCompiler _compiler = new QuizCompiler();
    private readonly IQuizGrader _grader = new QuizGrader();

    // The one Core storage service the player uses. Injected (not new'd like the
    // compiler/grader) because it must be pointed at the app sandbox, which only
    // the MAUI layer knows -- see its registration in MauiProgram. A singleton,
    // so its in-memory list is the same one the history screen reads.
    private readonly IAttemptHistoryService _history;

    // Paused sittings, same sandbox-path story as history. Kept so the Take flow
    // can save a sitting partway and the Home screen can list and resume them.
    private readonly IPausedAttemptService _paused;

    public QuizSessionService(IAttemptHistoryService history, IPausedAttemptService paused)
    {
        _history = history;
        _paused = paused;
    }

    // The paused-attempt id the current sitting was resumed from, if any. Set by
    // ResumeFrom; used so re-pausing updates the same entry and finishing removes
    // it. Null for a fresh sitting until it is first paused.
    private Guid? _resumedFromId;

    // Seconds already spent before a resume, snapshotted from the paused attempt.
    // Added to the fresh sitting's elapsed so a resumed sitting's total time
    // reflects time actually spent across both parts.
    private int _resumeOffsetSeconds;

    // A read-only player has no settings UI, so it uses sensible defaults: grade
    // every section, include every question, present in authored order. The one
    // knob that matters for presentation is answer randomisation; leaving it off
    // keeps matching/sequence in a stable, non-surprising order for a solo taker.
    private readonly QuizSettings _settings = new()
    {
        GradingScope = GradingScope.AllSections,
        SelectionMode = QuestionSelectionMode.AllQuestions,
        RandomizeQuestionOrder = false,
        RandomizeAnswerOrder = true,
    };

    public TakerIdentity? Identity { get; private set; }

    public IQuizPackageService? Package { get; private set; }
    public QuizPackageReadResult? Loaded { get; private set; }

    public TakeSession? Take { get; private set; }
    public AttemptResult? LastResult { get; private set; }

    /// <summary>
    /// The attempt whose detail is being opened, handed from the history list to
    /// the detail screen. A plain property rather than a navigation parameter:
    /// the record is a reference already in memory, and Shell route parameters
    /// only carry strings cleanly. Set immediately before navigating; read once
    /// on the detail screen.
    /// </summary>
    public AttemptRecord? SelectedAttempt { get; set; }

    public QuizSettings Settings => _settings;

    public void SetIdentity(TakerIdentity identity) => Identity = identity;

    /// <summary>The signed-in taker's normalized email key (or null), for scoping
    /// history and paused sittings to this person on a shared device.</summary>
    public string? CurrentTakerEmailKey => TakerKey.Normalize(Identity?.Email);

    /// <summary>Records the freshly imported quiz. Clears any prior take/result.</summary>
    public void SetLoadedQuiz(IQuizPackageService package, QuizPackageReadResult loaded)
    {
        Package = package;
        Loaded = loaded;
        Take = null;
        LastResult = null;
    }

    public string QuizTitle => Loaded?.Document.Title ?? "Quiz";

    /// <summary>
    /// Compiles the loaded document into a fresh paper and starts a new take.
    /// A time-derived seed gives a different shuffle each sitting; the seed is
    /// captured on the CompiledQuiz so grading stays internally consistent.
    /// </summary>
    public TakeSession StartTake()
    {
        if (Loaded is null)
            throw new InvalidOperationException("No quiz has been loaded.");

        var seed = Environment.TickCount;
        var quiz = _compiler.Compile(Loaded.Document, _settings, seed);

        var questions = quiz.Sections.SelectMany(s => s.Questions).ToList();
        var take = new TakeSession { Quiz = quiz, Questions = questions };

        // Seed each sequence question's working answer with its presentation
        // order, so a taker who leaves it untouched submits what they were shown
        // (a partial, not a free full mark). Other types start genuinely empty.
        foreach (var cq in questions)
        {
            if (cq.Question is Core.Models.SequenceQuestion && cq.SequencePresentation is { } pres)
            {
                take.AnswerFor(cq).SequenceAnswer.AddRange(pres);
            }
        }

        Take = take;
        _resumedFromId = null;
        _resumeOffsetSeconds = 0;
        return take;
    }

    /// <summary>Grades the current take and stores the result.</summary>
    public AttemptResult Submit(bool timedOut = false)
    {
        if (Take is null)
            throw new InvalidOperationException("No take is in progress.");

        var elapsed = DateTimeOffset.UtcNow - Take.StartedUtc;
        var result = _grader.Grade(Take.Quiz, Take.Answers, _settings, elapsed, timedOut);
        LastResult = result;

        // Record the sitting so it shows in this quiz's history. Keyed on the
        // document Id (survives a .qbx round trip), exactly as the desktop does.
        // Add() persists and trims; a write failure there is swallowed by the
        // service and must not stop the taker seeing their score.
        if (Loaded is { } loaded)
            _history.Add(AttemptRecordBuilder.Build(
                loaded.Document.Id,
                loaded.Document.Title,
                result,
                Identity?.Email,
                Identity?.FullName));

        // A resumed sitting that reaches submission is finished, so its paused
        // snapshot is stale and should not linger in the resume list.
        if (_resumedFromId is { } pausedId)
        {
            _paused.Remove(pausedId);
            _resumedFromId = null;
            _resumeOffsetSeconds = 0;
        }

        return result;
    }

    /// <summary>Forget a paused sitting (the taker chose to discard it).</summary>
    public void DeletePaused(Guid pausedId)
    {
        _paused.Remove(pausedId);

        // If they discarded the very sitting the current take was resumed from,
        // detach so finishing it later does not try to remove it again.
        if (_resumedFromId == pausedId)
        {
            _resumedFromId = null;
            _resumeOffsetSeconds = 0;
        }
    }

    /// <summary>Paused sittings for the loaded quiz AND the signed-in taker
    /// (plus legacy entries without an identity). Empty when nothing is loaded.</summary>
    public IReadOnlyList<PausedAttempt> PausedForCurrentQuiz() =>
        Loaded is { } loaded
            ? _paused.ForQuizAndTaker(loaded.Document.Id, TakerKey.Normalize(Identity?.Email))
            : Array.Empty<PausedAttempt>();

    /// <summary>
    /// Saves the current sitting as a paused snapshot: the paper exactly as
    /// shown (section order, shuffled matching/sequence presentation) and every
    /// answer entered so far. Mirrors the desktop's CreatePausedSnapshot. The id
    /// is stable across re-pauses of the same sitting, so re-saving updates one
    /// entry rather than piling up snapshots. Returns null if there is nothing
    /// to save.
    /// </summary>
    public PausedAttempt? PauseAndSave(int? elapsedSeconds = null)
    {
        if (Take is null || Loaded is null) return null;

        // No timer in the player, so elapsed defaults to wall-clock since the
        // sitting began. It is recorded for display and carried across resumes;
        // nothing enforces it (the .qbx format carries no time limit).
        var thisSession = elapsedSeconds
            ?? (int)Math.Max(0, (DateTimeOffset.UtcNow - Take.StartedUtc).TotalSeconds);

        // Reuse the id when this sitting itself came from a paused attempt, so
        // pausing a resumed sitting updates the same entry; otherwise mint one.
        var id = _resumedFromId ?? Guid.NewGuid();
        _resumedFromId = id;

        var sections = Take.Quiz.Sections.Select(section => new PausedSection
        {
            SourceSectionId = section.SourceSectionId,
            Title = section.Title,
            Questions = section.Questions.Select(cq => new PausedQuestion
            {
                Number = cq.Number,
                Question = cq.Question,
                MatchingOptions = cq.MatchingOptions?.ToList(),
                SequencePresentation = cq.SequencePresentation?.ToList(),
                Answer = Take.AnswerFor(cq),
            }).ToList(),
        }).ToList();

        var snapshot = new PausedAttempt
        {
            Id = id,
            QuizId = Loaded.Document.Id,
            QuizTitle = Loaded.Document.Title,
            TakerEmailKey = TakerKey.Normalize(Identity?.Email),
            TakerName = string.IsNullOrWhiteSpace(Identity?.FullName) ? null : Identity!.FullName,
            SavedAt = DateTimeOffset.Now,
            // Total time spent = this session's elapsed + any carried from a
            // prior resume, so pausing repeatedly never loses or double-counts.
            ElapsedSeconds = thisSession + _resumeOffsetSeconds,
            TimeLimitMinutes = Take.Quiz.TimeLimitMinutes,
            PassPercentage = Take.Quiz.PassPercentage,
            PassOnQuestionCount = Take.Quiz.PassMarkBasis == PassMarkBasis.QuestionCount,
            Sections = sections,
        };

        _paused.Save(snapshot);
        return snapshot;
    }

    /// <summary>
    /// Rebuilds a take from a paused snapshot and makes it the current sitting.
    /// The snapshot is authoritative -- the paper is not recompiled and the live
    /// document is not consulted -- so a resumed sitting shows exactly what was
    /// paused even if the quiz was edited since. The saved answers are dropped
    /// back into the rebuilt paper's answer set by matching paper order.
    /// </summary>
    public TakeSession ResumeFrom(PausedAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var quiz = PausedAttemptPaper.ToCompiledQuiz(attempt);
        var questions = quiz.Sections.SelectMany(s => s.Questions).ToList();
        var answers = PausedAttemptPaper.Answers(attempt);

        var take = new TakeSession { Quiz = quiz, Questions = questions };

        // Answers() returns one entry per question in the same flattened order
        // as the rebuilt paper, so a positional zip restores each answer to its
        // question. The stored QuestionAnswer is reused as-is: it already holds
        // the shuffled matching/sequence state the taker was working against.
        for (var i = 0; i < questions.Count && i < answers.Count; i++)
            take.Answers[questions[i]] = answers[i];

        Take = take;
        _resumedFromId = attempt.Id;
        _resumeOffsetSeconds = Math.Max(0, attempt.ElapsedSeconds);
        return take;
    }

    /// <summary>Resets everything except the identity, for taking another quiz.</summary>
    public void ClearQuiz()
    {
        Package = null;
        Loaded = null;
        Take = null;
        LastResult = null;
    }
}

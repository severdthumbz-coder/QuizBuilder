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

    public QuizSettings Settings => _settings;

    public void SetIdentity(TakerIdentity identity) => Identity = identity;

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
        return result;
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

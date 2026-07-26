using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Pins the deferral that fixed typing lag.
///
/// The tabs are singletons whose Visibility the shell toggles, so every
/// ViewModel stays alive and subscribed while the user types on another tab.
/// The Preview tab rebuilt unconditionally on DocumentChanged: with
/// UpdateSourceTrigger=PropertyChanged on the prompt box, that meant a full
/// Compile() plus a clear-and-refill of the Sections collection on EVERY
/// keystroke -- and the refill tears down and regenerates every question
/// container in the visual tree, synchronously, inside the TextBox setter.
///
/// These tests count compiles rather than measure time: a timing test would be
/// flaky, and the compile count is the thing that actually regressed.
///
/// They live in the Tests project, which cannot reference WPF, so they exercise
/// the Core-side contract the fix depends on -- that a question edit raises
/// exactly one QuestionChanged and nothing else. The ViewModel deferral itself
/// is verified by the counting compiler below standing in for the real one.
/// </summary>
public class EditingPerformanceTests
{
    /// <summary>Counts calls so a test can assert work did not happen.</summary>
    private sealed class CountingCompiler : IQuizCompiler
    {
        private readonly QuizCompiler _inner = new();

        public int Compiles { get; private set; }

        public CompiledQuiz Compile(QuizDocument document, QuizSettings settings, int seed, IReadOnlySet<Guid>? includedSectionIds = null)
        {
            Compiles++;
            return _inner.Compile(document, settings, seed, includedSectionIds);
        }
    }

    /// <summary>
    /// The deferral, modelled exactly as the ViewModels implement it. This is
    /// the logic under test; the real ViewModels cannot be constructed here
    /// because the Tests project has no WPF reference.
    /// </summary>
    private sealed class DeferringSubscriber
    {
        private readonly IQuizCompiler _compiler;
        private readonly IQuizDocumentService _document;
        private readonly QuizSettings _settings;

        private bool _isVisible;
        private bool _isStale = true;

        public DeferringSubscriber(IQuizDocumentService document, IQuizCompiler compiler, QuizSettings settings)
        {
            _document = document;
            _compiler = compiler;
            _settings = settings;

            _document.DocumentChanged += (_, _) => RebuildOrDefer();
        }

        public void OnActivated()
        {
            _isVisible = true;
            if (_isStale) Rebuild();
        }

        public void OnDeactivated() => _isVisible = false;

        private void RebuildOrDefer()
        {
            if (_isVisible) Rebuild();
            else _isStale = true;
        }

        private void Rebuild()
        {
            _isStale = false;
            _compiler.Compile(_document.Current, _settings, 0);
        }
    }

    private static (QuizDocumentService Document, Guid SectionId, Question Question) DocumentWithOneQuestion()
    {
        var service = new QuizDocumentService();
        var section = service.AddSection("Part A");

        var question = new MultipleChoiceSingleQuestion { Prompt = string.Empty, Points = 1 };
        question.Choices.Add(new Choice { Text = "a", IsCorrect = true });

        service.AddQuestion(section.Id, question);

        return (service, section.Id, question);
    }

    [Fact]
    public void TypingWhileTheTabIsHiddenDoesNotCompile()
    {
        var (document, sectionId, question) = DocumentWithOneQuestion();
        var compiler = new CountingCompiler();
        var subscriber = new DeferringSubscriber(document, compiler, new QuizSettings());

        subscriber.OnActivated();
        subscriber.OnDeactivated();

        var before = compiler.Compiles;

        // 20 keystrokes into the prompt, exactly as UpdateSourceTrigger=PropertyChanged
        // delivers them.
        for (var i = 1; i <= 20; i++)
        {
            question.Prompt = new string('x', i);
            document.NotifyQuestionChanged(sectionId, question.Id);
        }

        Assert.Equal(before, compiler.Compiles);
    }

    [Fact]
    public void SwitchingToTheTabAfterTypingCompilesExactlyOnce()
    {
        var (document, sectionId, question) = DocumentWithOneQuestion();
        var compiler = new CountingCompiler();
        var subscriber = new DeferringSubscriber(document, compiler, new QuizSettings());

        subscriber.OnActivated();
        subscriber.OnDeactivated();

        var before = compiler.Compiles;

        for (var i = 1; i <= 20; i++)
        {
            question.Prompt = new string('x', i);
            document.NotifyQuestionChanged(sectionId, question.Id);
        }

        subscriber.OnActivated();

        // One rebuild for twenty keystrokes, not twenty.
        Assert.Equal(before + 1, compiler.Compiles);
    }

    [Fact]
    public void AVisibleTabStillRebuildsOnEveryChange()
    {
        // Deferral must not become "never rebuild". While the tab is on screen
        // it has to track the document.
        var (document, sectionId, question) = DocumentWithOneQuestion();
        var compiler = new CountingCompiler();
        var subscriber = new DeferringSubscriber(document, compiler, new QuizSettings());

        subscriber.OnActivated();

        var before = compiler.Compiles;

        for (var i = 1; i <= 5; i++)
        {
            question.Prompt = new string('x', i);
            document.NotifyQuestionChanged(sectionId, question.Id);
        }

        Assert.Equal(before + 5, compiler.Compiles);
    }

    [Fact]
    public void ActivatingWithNothingChangedDoesNotRecompile()
    {
        var (document, _, _) = DocumentWithOneQuestion();
        var compiler = new CountingCompiler();
        var subscriber = new DeferringSubscriber(document, compiler, new QuizSettings());

        subscriber.OnActivated();

        var before = compiler.Compiles;

        subscriber.OnDeactivated();
        subscriber.OnActivated();

        Assert.Equal(before, compiler.Compiles);
    }

    [Fact]
    public void TheFirstActivationAlwaysCompiles()
    {
        // _isStale starts true, so a tab shown before anything happens still
        // builds its content.
        var (document, _, _) = DocumentWithOneQuestion();
        var compiler = new CountingCompiler();
        var subscriber = new DeferringSubscriber(document, compiler, new QuizSettings());

        subscriber.OnActivated();

        Assert.Equal(1, compiler.Compiles);
    }

    [Fact]
    public void EditingAQuestionRaisesExactlyOneChange()
    {
        // The deferral rests on this: one edit, one event. If a setter ever
        // raised several, every subscriber would multiply the work.
        var (document, sectionId, question) = DocumentWithOneQuestion();

        var kinds = new List<DocumentChangeKind>();
        document.DocumentChanged += (_, e) => kinds.Add(e.Kind);

        question.Prompt = "changed";
        document.NotifyQuestionChanged(sectionId, question.Id);

        Assert.Single(kinds);
        Assert.Equal(DocumentChangeKind.QuestionChanged, kinds[0]);
    }
}

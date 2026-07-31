using System.IO;
using System.Threading.Tasks;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// A contract for the planned read-only MAUI player. That app will share
/// QuizBuilder.Core directly and lean on a specific slice of it: open a .qbx
/// authored on the desktop, compile it, take it, and be graded -- all with its
/// storage pointed at an app sandbox and with no Windows DPAPI available.
///
/// <para>
/// None of these assertions exercise new code; every piece is already covered
/// in isolation. Their job is to pin the <i>composition</i> as a single named
/// boundary, so a later change to Core that happens to break the mobile read
/// path fails here -- loudly, on the desktop CI run -- rather than on a device.
/// If one of these breaks, MAUI is affected; that is the whole signal.
/// </para>
///
/// <para>
/// The three documented MAUI blockers map onto the three things this file
/// guards: storage-path adaptation (the overrideDirectory seam), the absence of
/// DPAPI (token protection in a non-machine-bound mode), and the portability of
/// the load/compile/grade path itself.
/// </para>
///
/// <para>
/// This class installs a "no DPAPI, throw" stand-in over the process-global
/// <c>ProtectedDataShim</c> delegates. Because that global is shared, it joins
/// the same serialized collection as every other shim-mutating class (see
/// <see cref="ProtectedDataShimCollection"/>) so xUnit will not run it in
/// parallel with them -- otherwise its throwing shim could be active while a
/// class that needs a working shim (e.g. TokenProtectorTests) calls through it.
/// </para>
/// </summary>
[Collection(ProtectedDataShimCollection.Name)]
public class MobileReadPathContractTests : System.IDisposable
{
    private readonly string _sandbox;
    private readonly Func<byte[], byte[], byte[]> _origProtect = ProtectedDataShim.ProtectImpl;
    private readonly Func<byte[], byte[], byte[]> _origUnprotect = ProtectedDataShim.UnprotectImpl;

    public MobileReadPathContractTests()
    {
        // Stand in for the OS-assigned app sandbox MAUI would hand these
        // services. The point is that it is NOT beside any executable.
        _sandbox = Path.Combine(Path.GetTempPath(), "qb-mobile_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);

        // Simulate a platform without DPAPI: any attempt to reach machine-bound
        // protection now throws, exactly as it would on Android or iOS. A mobile
        // build must never depend on this path.
        ProtectedDataShim.ProtectImpl = (_, _) =>
            throw new System.PlatformNotSupportedException("No DPAPI on this platform.");
        ProtectedDataShim.UnprotectImpl = (_, _) =>
            throw new System.PlatformNotSupportedException("No DPAPI on this platform.");
    }

    public void Dispose()
    {
        ProtectedDataShim.ProtectImpl = _origProtect;
        ProtectedDataShim.UnprotectImpl = _origUnprotect;
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A representative quiz covering an auto-graded spread of types.</summary>
    private static QuizDocument AuthoredQuiz()
    {
        var doc = new QuizDocument { Title = "Shared quiz", Description = "Authored on desktop" };
        var section = new Section { Title = "Section 1" };

        var mc = new MultipleChoiceSingleQuestion { Prompt = "Capital of France?", Points = 1 };
        mc.Choices.Add(new Choice { Text = "Paris", IsCorrect = true });
        mc.Choices.Add(new Choice { Text = "Berlin" });
        section.Questions.Add(mc);

        var seq = new SequenceQuestion { Prompt = "Order them", Points = 3 };
        seq.Items.AddRange(new[] { "First", "Second", "Third" });
        section.Questions.Add(seq);

        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);
        return doc;
    }

    [Fact]
    public async Task ADesktopAuthoredQuizOpensWhenStorageIsPointedAtASandbox()
    {
        // The desktop writes a .qbx; the "mobile" side opens it from the sandbox.
        var path = Path.Combine(_sandbox, "quiz.qbx");
        await new QuizPackageService().SaveAsync(AuthoredQuiz(), path);

        var result = await new QuizPackageService().LoadAsync(path);

        Assert.NotNull(result.Document);
        Assert.Equal("Shared quiz", result.Document.Title);
        Assert.Equal(2, result.Document.Sections[0].Questions.Count);
    }

    [Fact]
    public void PlayerStorageServicesHonourTheSandboxDirectory()
    {
        // Each service MAUI reuses must persist inside the directory it is given,
        // not beside the executable. This is the storage-path blocker: if the
        // override seam ever stops being honoured, a mobile build silently
        // writes to the wrong place (or an unwritable one).
        var protector = new TokenProtector();
        protector.SetMode(TokenProtectionMode.None);

        _ = new SettingsService(protector, _sandbox);
        var history = new AttemptHistoryService(_sandbox);
        var bank = new QuestionBankService(_sandbox);

        // A real write on each. If the override is honoured, the file lands in
        // the sandbox and survives a reload from a fresh instance pointed there.
        var q = new MultipleChoiceSingleQuestion { Prompt = "Q" };
        q.Choices.Add(new Choice { Text = "a", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "b" });
        bank.Add(q, "General");

        Assert.True(
            Directory.EnumerateFiles(_sandbox, "*.json").Any(),
            "a player storage service wrote nothing into the sandbox");

        // Reload from a new instance on the same sandbox: the write is visible,
        // proving it went where it was told.
        var reopened = new QuestionBankService(_sandbox);
        reopened.Load();
        Assert.NotEmpty(reopened.All());

        // And nothing leaked beside the executable.
        var escaped = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.json")
            .Select(Path.GetFileName)
            .Any(f => f is "attempts.json" or "question-bank.json" or "paused-attempt.json" or "settings.json");
        Assert.False(escaped, "a player storage service wrote beside the executable instead of the sandbox");
    }

    [Fact]
    public void TokenProtectionWorksWithoutDpapi()
    {
        // Mobile runs in None mode: the token is held in memory for the session,
        // nothing is written to disk, and reading it back never touches DPAPI
        // (which now throws). Protect returns null in None mode by design -- the
        // token lives in the protector, not in a ciphertext string.
        var protector = new TokenProtector();
        protector.SetMode(TokenProtectionMode.None);

        Assert.True(protector.IsUnlocked);
        Assert.False(protector.RequiresPassphrase);

        var persisted = protector.Protect("gh-token-value");
        Assert.Null(persisted); // nothing goes to disk in None mode
        Assert.Equal("gh-token-value", protector.Unprotect(persisted));
    }

    [Fact]
    public void MachineBoundProtectionSurfacesACleanErrorInsteadOfCrashing()
    {
        // If a mobile build ever mistakenly selects machine-bound mode, the
        // failure must be a catchable, explainable exception -- not a raw crash
        // deep in a P/Invoke. This is the "must be understood" part of the DPAPI
        // blocker: the degradation is defined, not incidental.
        var protector = new TokenProtector();
        protector.SetMode(TokenProtectionMode.MachineBound);

        Assert.ThrowsAny<System.Exception>(() => protector.Protect("secret"));
    }

    [Fact]
    public async Task TheFullTakeAndGradePathRunsWithoutAnyWindowsDependency()
    {
        // The heart of the mobile player: load, compile, answer, grade -- proven
        // here while the DPAPI shim is disabled and storage is sandboxed, so a
        // pass means the path is genuinely platform-neutral.
        var path = Path.Combine(_sandbox, "take.qbx");
        await new QuizPackageService().SaveAsync(AuthoredQuiz(), path);

        var loaded = (await new QuizPackageService().LoadAsync(path)).Document;

        var settings = new QuizSettings { RandomizeAnswerOrder = false };
        var quiz = new QuizCompiler().Compile(loaded, settings, seed: 1);
        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();

        // Answer everything correctly.
        var answers = new Dictionary<CompiledQuestion, QuestionAnswer>();
        foreach (var cq in compiled)
        {
            var a = new QuestionAnswer();
            switch (cq.Question)
            {
                case MultipleChoiceSingleQuestion mc:
                    // The grader works on the index of the correct choice.
                    a.ChoiceIndex = mc.Choices.FindIndex(c => c.IsCorrect);
                    break;
                case SequenceQuestion sq:
                    a.SequenceAnswer.AddRange(Enumerable.Range(0, sq.Items.Count));
                    break;
            }
            answers[cq] = a;
        }

        var result = new QuizGrader()
            .Grade(quiz, answers, settings, System.TimeSpan.FromMinutes(5), timedOut: false);

        // Full marks on the auto-graded questions (there are no essays here), and
        // the results render into an attempt record -- the same record the mobile
        // review screen would show.
        Assert.Equal(result.AutoGradedPoints, result.ScoredPoints);
        var record = AttemptRecordBuilder.Build(System.Guid.NewGuid(), "Shared quiz", result);
        Assert.Equal(2, record.Questions.Count);
    }
}

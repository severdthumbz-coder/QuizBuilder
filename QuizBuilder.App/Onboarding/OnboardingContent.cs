using OnboardingKit;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.App.Onboarding;

/// <summary>
/// The QuizBuilder-specific tour script and legend. This is the one file the
/// OnboardingKit handoff calls genuinely app-specific: the kit's logic, styles
/// and controls are generic, but what the tour *says* and which destinations it
/// visits belong here.
///
/// The tour is 7 steps (the handoff recommends 5–8), grouping the app's 12
/// navigation destinations in workflow order rather than one step per tab:
/// build → configure → check → study → reuse/share → done. Each step's
/// <see cref="TourStep.TabIndex"/> is the integer value of a
/// <see cref="NavDestination"/>; the selector wired in App startup casts it back
/// and navigates there. Step 1 stays where it is (TabIndex -1). The Settings
/// step says the helpers can be turned off; the last step says where Replay is —
/// the two lines the handoff calls out as what makes the tour feel optional.
/// </summary>
public static class OnboardingContent
{
    private static int Tab(NavDestination d) => (int)d;

    public static readonly TourStep[] TourSteps =
    {
        new("Welcome to Quiz Builder 👋",
            "A one-minute tour of the app. Press → or Enter to go forward, ← to go back, Esc to skip."),

        new("Build your quiz",
            "Start here: add sections and questions — multiple choice, true/false, numeric, dropdown and more. This is where a quiz takes shape.",
            TabIndex: Tab(NavDestination.QuizBuilder)),

        new("Set it up and style it",
            "Settings controls grading, question selection and timing; Theme controls colours and type. You can turn these onboarding helpers off in Settings whenever you like.",
            TabIndex: Tab(NavDestination.Settings)),

        new("Check it, then take it",
            "Preview shows the quiz as students will see it, and Take lets you sit it yourself and review past results before you share it.",
            TabIndex: Tab(NavDestination.Preview)),

        new("Study tools",
            "Author front/back Study Cards, flip through them as Flash Cards, and use Review for spaced-repetition practice that brings cards back just as you're about to forget them.",
            TabIndex: Tab(NavDestination.StudyCards)),

        new("Reuse and share",
            "The Question Bank keeps questions you can reuse across quizzes; Publish exports to Word, Excel, HTML or an interactive web page; GitHub publishes a shareable link.",
            TabIndex: Tab(NavDestination.QuestionBank)),

        new("You're all set ✓",
            "That's the tour. The ❔ legend explains every icon, and you can replay this tour any time from the Help tab.",
            TabIndex: Tab(NavDestination.Help)),
    };

    /// <summary>
    /// The status glyphs and colours the app actually uses, so the ❔ legend and
    /// the Help-tab list explain what a user sees elsewhere. Kept short and
    /// honest — only symbols that genuinely appear in the UI.
    /// </summary>
    public static readonly LegendItem[] LegendItems =
    {
        new("ⓘ", "Hover for a short explanation of that section"),
        new("❔", "Opens this legend of icons and colours"),
        new("✓", "Correct answer, or a completed step"),
    };
}

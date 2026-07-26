using QuizBuilder.Core;

namespace QuizBuilder.App.ViewModels;

public sealed record FeatureEntry(string Name, string Description, bool Implemented);

public sealed record WorkflowStep(int Number, string Title, string Detail);

public sealed record VersionEntry(
    string Version,
    int Build,
    string ReleaseDate,
    IReadOnlyList<string> Changes);

/// <summary>
/// Help / About.
///
/// Content lives here rather than in the XAML so it stays testable and so the
/// feature list cannot silently disagree with the version it claims to
/// describe. Version values come from VersionInfo, which reads the attributes
/// build.bat stamps from version.json.
/// </summary>
public sealed class HelpViewModel : ViewModelBase
{
    public string Version => VersionInfo.Semantic;
    public int Build => VersionInfo.Build;
    public string VersionDisplay => VersionInfo.Display;
    public string RuntimeVersion => Environment.Version.ToString();

    /// <summary>
    /// Honest status per feature. Marking unbuilt things as done would make
    /// this screen a liability rather than documentation, so Implemented is
    /// tracked explicitly and the view renders the difference.
    /// </summary>
    public IReadOnlyList<FeatureEntry> Features { get; } = new[]
    {
        new FeatureEntry("Portable settings",
            "All preferences save to settings.json beside the executable. No registry, no AppData.", true),
        new FeatureEntry("Theme system",
            "Five built-in themes with contrast-checked palettes, plus a live custom theme editor.", true),
        new FeatureEntry("Navigation rail",
            "Icon and label navigation with active-state highlight and keyboard focus rings.", true),
        new FeatureEntry("Eight question types",
            "Multiple choice (single and multiple), true/false, short answer, fill-in-the-blank, matching, sequence, essay.", true),
        new FeatureEntry("Sequence questions",
            "Give a list of items in the right order; the taker drags them into place. The quiz shuffles them first so they never start correct, and marks each correctly-ordered neighbouring pair, so one item out of place does not fail the whole question.", true),
        new FeatureEntry("Quiz document model",
            "Sections, questions, point values, hints and image attachments.", true),
        new FeatureEntry(".qbx session files",
            "Save and reopen a full quiz session, images included, in a single portable file.", true),
        new FeatureEntry("Token protection",
            "GitHub tokens stored machine-bound, passphrase-encrypted, or not at all. Your choice.", true),
        new FeatureEntry("Quiz builder tab",
            "Add, rename, reorder and delete sections and questions. Drag to reorder, or use Alt+Up and Alt+Down.", true),
        new FeatureEntry("Settings tab",
            "Grading scope, question selection, randomisation, timing and default point values.", true),
        new FeatureEntry("Autosave",
            "Saves the open session back to its .qbx file on a timer you choose, once the quiz has been saved at least once.", true),
        new FeatureEntry("Preview tab",
            "Student and answer-key views of the compiled paper, honouring question selection, randomisation and timing.", true),
        new FeatureEntry("Web page export",
            "A single self-contained HTML file, student copy or answer key, printable to PDF from the browser.", true),
        new FeatureEntry("Word export",
            "A .docx you can edit, styled from the current theme, student copy or answer key.", true),
        new FeatureEntry("Question bank",
            "A reusable pool of questions saved beside the app. Use \"Save to bank\" on any question in the Quiz Builder, organise entries with a category on the Question Bank tab, then add copies into any quiz's section.", true),
        new FeatureEntry("Save & continue later",
            "While taking a quiz, click \"Save & continue later\" to stop and keep your place. The paused quiz appears on the Take tab, and resuming brings back your answers and, for a timed quiz, the time you had left.", true),
        new FeatureEntry("Question selection",
            "In Settings, choose to use every question, a set number per section, or a single total spread proportionally across sections by size. The proportional split never asks a section for more than it holds.", true),
        new FeatureEntry("Choose sections at quiz time",
            "Set the grading scope to \"choose when taken\" in Settings, and the Take tab shows a checklist of sections before you start — tick the ones to include.", true),
        new FeatureEntry("Images",
            "Add an image to any question, and to either side of a study card. Images show in the editor, Preview, when taking the quiz, on the flash cards, and in every export — HTML, the self-grading web quiz, and Word.", true),
        new FeatureEntry("Study cards",
            "Author your own front/back cards in the Study Cards tab — terms, definitions, facts that aren't quiz questions. Choose in Settings whether the Flash Cards tab draws from the quiz, your study cards, or both.", true),
        new FeatureEntry("Self-grading web quiz",
            "Export a single HTML file that marks itself in the browser — the taker answers, submits, and sees their score and mistakes, graded by the same rules the app uses. For practice and self-assessment.", true),
        new FeatureEntry("Flash cards",
            "Flip through the quiz's questions as cards — question on the front, answer on the back. Click to flip, step through the deck, or shuffle. For review; nothing is marked.", true),
        new FeatureEntry("Formatted descriptions",
            "The quiz description accepts bold, italic, bullet lists and line breaks — using a small set of safe tags. Everything else is shown as typed, so a description can never inject anything into a published page.", true),
        new FeatureEntry("Take the quiz",
            "Sit the quiz in its own window, timed and marked, with a result screen listing what you got wrong. Every attempt is kept with the quiz and is there when you reopen it.", true),
        new FeatureEntry("Spreadsheet export and import",
            "One row per question, for bulk editing. Export to .xlsx, change it in Excel, then import it back. Includes a Guide sheet explaining every column.", true),
        new FeatureEntry("PDF export",
            "Via the web page export: open it and print to PDF from the browser. No separate PDF engine, so pagination is the browser's, which handles page breaks better than a hand-rolled one would.", true),
        new FeatureEntry("GitHub publishing",
            "Connect a personal access token, publish the quiz as a web page, and turn on GitHub Pages for a link you can hand out. Publishing again updates the same page.", true),
    };

    public IReadOnlyList<WorkflowStep> Workflow { get; } = new[]
    {
        new WorkflowStep(1, "Build the quiz",
            "Start on the Quiz Builder tab. Give the exam a title, add a section, then add questions to it. Each question carries its own point value, and can take an optional hint or image. For a sequence question, list its items in the correct order — the quiz shuffles them for the taker."),
        new WorkflowStep(2, "Configure the rules",
            "On Settings, decide whether every section is graded or chosen at quiz time, how many questions to draw from each section, and whether to randomise question or answer order. Set a time limit if you want one."),
        new WorkflowStep(3, "Choose a look",
            "On Theme, pick one of the five built-in themes or edit your own colours, fonts and spacing. Reorder how sections appear in the published output. The theme applies to the app, the printed documents and the web quiz alike."),
        new WorkflowStep(4, "Check it",
            "On Preview, switch between the student view and the answer key. This reflects your Settings, so it is where randomisation and question counts get sanity-checked before anyone sits the exam."),
        new WorkflowStep(5, "Publish it",
            "On Publish, export to PDF, Word or Excel, or generate a self-contained web quiz. Save the session as a .qbx file to pick up exactly where you left off."),
        new WorkflowStep(6, "Share it",
            "On GitHub, connect an account and publish the web quiz to GitHub Pages for a live link you can hand out."),
    };

    /// <summary>
    /// Newest first. Kept in code rather than a data file so it ships inside
    /// the single-file executable with nothing else to deploy.
    /// </summary>
    public IReadOnlyList<VersionEntry> History { get; } = new[]
    {
        new VersionEntry("0.25.0", 1, "2026-07-24", new[]
        {
            "New Sequence question type: the taker drags items into the correct order",
            "Add and arrange items in the editor, with move up and down; the quiz shuffles them so they never start in the right order",
            "Partial credit for each correctly-ordered neighbouring pair, so one misplaced item does not cost the whole question",
            "Works everywhere: preview, the printed and Word papers, the self-marking web quiz, and Excel import and export",
            "Session files are now format version 2; a sequence-bearing quiz will not open in an older build",
        }),
        new VersionEntry("0.24.1", 1, "2026-07-24", new[]
        {
            "Adding or removing an image on a question or study card can now be undone, like every other structural change",
            "Undoing an image does not discard it: redo brings it straight back",
        }),
        new VersionEntry("0.24.0", 1, "2026-07-24", new[]
        {
            "Flash cards now fill the space available and grow when the window is maximised, instead of staying a fixed size",
            "Card text size can be adjusted from the buttons below the card, from 75% up to 250%",
            "The chosen size is remembered between sessions and scales with your theme rather than overriding it",
        }),
        new VersionEntry("0.23.1", 1, "2026-07-24", new[]
        {
            "Fixed: deleting a section left the question editor showing on the right, still displaying the deleted question",
            "The editor panel now clears whenever the selected question goes away, however it went",
        }),
        new VersionEntry("0.23.0", 1, "2026-07-24", new[]
        {
            "Groundwork for a new Sequence question type, where the taker arranges items into the correct order",
            "Scoring gives partial credit for each correctly-ordered neighbouring pair, so one misplaced item does not cost the whole question",
            "Not yet available to add to a quiz: the editor and quiz-taking screens for it are still to come",
        }),
        new VersionEntry("0.22.1", 1, "2026-07-23", new[]
        {
            "Fixed: redoing the deletion of a section left its questions listed under no section at all",
            "The question list is now rebuilt whenever the document is replaced, including when the last section goes",
        }),
        new VersionEntry("0.22.0", 1, "2026-07-23", new[]
        {
            "Undo and redo for structural changes: adding, deleting, reordering and renaming sections, questions and study cards (Ctrl+Z / Ctrl+Y)",
            "Deleting a section that still holds questions now asks first, and names what would be lost",
            "Empty sections still delete without a prompt",
            "Settings: choose how many undo steps to keep (default 15, 0 turns undo off)",
            "Typing inside a question is left to the text box's own undo, so Ctrl+Z there behaves as it always did",
        }),
        new VersionEntry("0.21.0", 1, "2026-07-22", new[]
        {
            "Dragging a question or a section now shows a line marking exactly where it will land",
            "Fixed an off-by-one when dragging a row downwards: it previously landed one place short of where it was dropped",
            "The indicator clears correctly when a drag is cancelled with Escape or released outside the list",
        }),
        new VersionEntry("0.20.2", 1, "2026-07-22", new[]
        {
            "Fixed: dragging a section to reorder it did nothing. Section rows now have a grip handle on the left -- drag that to reorder",
            "Renaming a section by clicking its title is unaffected; the grip keeps the two gestures separate",
            "The up/down arrows still work as before",
        }),
        new VersionEntry("0.20.1", 1, "2026-07-20", new[]
        {
            "Sections could be reordered with the up/down arrows (drag-to-reorder shipped but did not work -- fixed in 0.20.2)",
        }),
        new VersionEntry("0.20.0", 1, "2026-07-20", new[]
        {
            "Question bank: save questions to a reusable pool and add copies into any quiz",
            "Organise bank questions by category and filter the list",
        }),
        new VersionEntry("0.19.1", 1, "2026-07-19", new[]
        {
            "Internal tidy-up: removed an unused navigation interface (no functional change)",
        }),
        new VersionEntry("0.19.0", 2, "2026-07-19", new[]
        {
            "Fixed: a build error from the resumed paper missing its pass-mark fields",
        }),
        new VersionEntry("0.19.0", 1, "2026-07-19", new[]
        {
            "Save a quiz partway through and resume it later, with answers and remaining time intact",
            "Paused quizzes are listed on the Take tab, where they can be resumed or discarded",
        }),
        new VersionEntry("0.18.0", 1, "2026-07-19", new[]
        {
            "Question selection: set a number per section, or a total spread proportionally across sections",
            "The per-section count editor is now built out; both modes take a random draw each sitting",
        }),
        new VersionEntry("0.17.0", 3, "2026-07-19", new[]
        {
            "Cleaned up a test-only analyzer warning (no behaviour change)",
        }),
        new VersionEntry("0.17.0", 2, "2026-07-19", new[]
        {
            "Fixed: a test build error from the compiler's new optional section-filter parameter",
        }),
        new VersionEntry("0.17.0", 1, "2026-07-19", new[]
        {
            "\"Choose sections when the quiz is taken\" now works: pick which sections to include before starting",
        }),
        new VersionEntry("0.16.0", 1, "2026-07-19", new[]
        {
            "The self-grading web quiz now honours the time limit: it counts down and auto-submits, matching the app",
        }),
        new VersionEntry("0.15.0", 3, "2026-07-18", new[]
        {
            "Fixed: image dimension reader rejected small JPEGs due to an over-strict length check",
        }),
        new VersionEntry("0.15.0", 2, "2026-07-18", new[]
        {
            "Fixed: a build error from a helper method accidentally removed while adding image support",
        }),
        new VersionEntry("0.15.0", 1, "2026-07-18", new[]
        {
            "Word (.docx) export now embeds question images",
            "Images are sized to fit the page and stored once even if reused across questions",
        }),
        new VersionEntry("0.14.0", 2, "2026-07-18", new[]
        {
            "Fixed: two nullability warnings from the image resolver's method signature",
        }),
        new VersionEntry("0.14.0", 1, "2026-07-18", new[]
        {
            "Add images to questions and to study cards (front and back)",
            "Images appear in the editor, Preview, the Take window, flash cards, and the HTML and web exports",
            "Images are stored once each and travel inside the .qbx; Word export support is coming next",
        }),
        new VersionEntry("0.13.0", 1, "2026-07-18", new[]
        {
            "New Study Cards tab: write your own front/back cards for the flash cards",
            "Settings: choose the flash card source — quiz questions, study cards, or both",
            "Study cards are saved in the .qbx alongside the quiz",
        }),
        new VersionEntry("0.12.0", 1, "2026-07-17", new[]
        {
            "New Publish option: export a self-grading quiz as a web page",
            "The page marks itself in the browser using the same rules as the in-app quiz — verified to agree on every question type",
            "Shows the score, the questions you got wrong, and any essays to review",
        }),
        new VersionEntry("0.11.0", 1, "2026-07-17", new[]
        {
            "New Flash Cards tab: flip through questions and answers, step through the deck, shuffle",
            "Essay cards show the rubric if there is one, or note that the answer is open",
        }),
        new VersionEntry("0.10.0", 2, "2026-07-17", new[]
        {
            "Fixed: plain-text description conversion used a platform-specific line ending",
            "Quiz descriptions now support bold, italic, bullet lists and line breaks",
            "Fixed: the HTML export collapsed line breaks in the description into one paragraph",
            "Formatting uses a safe tag list; anything else is shown as typed and cannot inject into a published page",
        }),
        new VersionEntry("0.9.0", 2, "2026-07-17", new[]
        {
            "Fixed: the results panel was drawing on top of the questions before the quiz was submitted",
            "New Take tab: sit the quiz in a separate window, timed and marked",
            "Results screen shows your score, what you got wrong, and the right answers",
            "Attempt history is kept per quiz and is there when you reopen it",
            "Essay questions are listed for your review and left out of the automatic score, rather than counted as zero",
            "The timer uses a monotonic clock, so a daylight-saving change cannot end an exam early",
        }),
        new VersionEntry("0.8.1", 1, "2026-07-17", new[]
        {
            "Fixed: building a new version no longer wipes your settings, saved quizzes, GitHub token or custom theme",
            "The custom theme editor now has Save and Discard, so you can experiment without committing",
            "Choosing a theme still saves straight away -- only edits to the custom theme wait for Save",
        }),
        new VersionEntry("0.8.0", 1, "2026-07-17", new[]
        {
            "GitHub tab: publish the quiz to GitHub Pages and get a shareable link",
            "Tokens are checked before they are stored, and encrypted using the mode set on Settings",
            "No git client needed -- it uses GitHub's web API, so nothing extra to install",
            "Publishing twice updates the same page instead of failing",
        }),
        new VersionEntry("0.7.0", 1, "2026-07-17", new[]
        {
            "Fixed typing lag in the question editors -- the Preview and Publish tabs were rebuilding on every keystroke while hidden",
            "Publish tab: export the quiz as a spreadsheet, one row per question",
            "Import questions back from a spreadsheet, so a quiz can be bulk-edited in Excel",
            "Every repeated field gets its own column, so answers containing \"/\" or \",\" survive the round trip",
            "Import reads files Excel itself writes, and reports any row it could not use rather than dropping it quietly",
            "No spreadsheet library needed -- the .xlsx is written and read directly",
        }),
        new VersionEntry("0.6.0", 2, "2026-07-16", new[]
        {
            "Fixed a test that could never pass, in the Word export's control-character handling",
            "Publish tab: export the quiz as an editable Word document",
            "Word styling comes from the current theme, so it matches the preview",
            "No document library needed -- the .docx is written directly, so it could be verified",
        }),
        new VersionEntry("0.5.0", 2, "2026-07-16", new[]
        {
            "Fixed: a custom theme font could put junk in the exported page's stylesheet",
            "Publish tab: export the quiz as a single self-contained web page",
            "Student copy or answer key, with print rules that keep each question on one page",
            "PDF via the browser's own print engine, so no bundled PDF library",
        }),
        new VersionEntry("0.4.0", 3, "2026-07-16", new[]
        {
            "The pass mark can now count questions or points -- they differ on a weighted paper",
            "Question-based passing counts a question correct at half its points or better",
            "Settings: a pass mark, as a percentage of the paper's points",
            "The preview shows the pass mark against the paper's actual total",
            "Fixed: Reshuffle now says why it is unavailable instead of doing nothing",
            "Preview tab: see the paper as a student would, or with the answer key",
            "Question selection and randomisation from Settings are applied to the preview",
            "Reshuffle draws a new random paper; the same paper is kept when switching views",
            "Matching questions shuffle their right-hand column and include distractors",
        }),
        new VersionEntry("0.3.0", 5, "2026-07-16", new[]
        {
            "Fixed: opening a saved quiz showed the section but none of its questions",
            "Editable fields (title, description, section name) now show a pencil",
            "Quizzes now have an optional description, shown under the title",
            "Fixed: the quiz title, section names and question editors could not be typed into",
            "Section names are now editable in place",
            "Fixed: adding an option no longer clears the selected question",
        }),
        new VersionEntry("0.3.0", 1, "2026-07-16", new[]
        {
            "Quiz Builder tab: sections, questions and all eight question types",
            "Drag to reorder questions, or Alt+Up and Alt+Down for the same without a mouse",
            "Save, Save As and Open for .qbx session files",
            "Inline validation showing what each question still needs, without blocking",
            "Fill-in-the-blank keeps answers bound to their token when the text is edited",
            "Autosave now activates once a quiz has been saved",
        }),
        new VersionEntry("0.2.0", 2, "2026-07-16", new[]
        {
            "Autosave setting: save the session to its .qbx file every 1 to 60 minutes",
            "Theme tab: pick any of the five built-in themes, applied live",
            "Custom theme editor: colours, font, corner radius and spacing",
            "Settings tab: grading scope, question selection, randomisation, timing",
            "Default point values per question type, with a reset",
            "GitHub token storage mode is now user-selectable, with a warning before it clears a saved token",
        }),
        new VersionEntry("0.1.0", 1, "2026-07-15", new[]
        {
            "Application shell: navigation rail, theming, Help/About",
            "Five built-in themes, every palette verified against WCAG AA contrast",
            "Portable settings.json written atomically beside the executable",
            ".qbx session format: ZIP container with content-hashed images and orphan collection",
            "GitHub token protection with a user-selectable mode (machine-bound, passphrase or none)",
            "Eight question types with polymorphic serialisation",
            "Version-stamped build output and single-file portable publish",
        }),
    };
}

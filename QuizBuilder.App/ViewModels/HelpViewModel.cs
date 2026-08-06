using System.Linq;
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
/// One version and all the builds shipped under it, newest build first. The
/// version history groups by this so a version's header shows once and its
/// builds stack beneath, rather than repeating the version on every build.
/// </summary>
public sealed record VersionGroup(
    string Version,
    IReadOnlyList<VersionEntry> Builds);

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
        new VersionEntry("0.26.0", 42, "2026-08-04", new[]
        {
            "Spaced repetition now works on the Android player too, from a new \"Spaced repetition\" button on the home screen. It shows the cards that are due, one at a time — tap to reveal the answer, then grade it (Again/Hard/Good/Easy). It uses exactly the same scheduling as the desktop, so a card graded on your phone comes back at the same time it would on the computer, and your progress is stored privately in the app. This completes spaced repetition across desktop and mobile.",
        }),
        new VersionEntry("0.26.0", 41, "2026-08-04", new[]
        {
            "Spaced repetition comes to the desktop: a new Review tab. It shows the cards the schedule says are due, one at a time — click to reveal the answer, then grade how well you knew it (Again, Hard, Good, Easy). Your grade sets when the card returns: miss it and it's back tomorrow, find it easy and it won't reappear for a good while. When you've cleared the due cards, it says you're done for today. Progress is saved privately on your device, so it never travels with a shared quiz.",
        }),
        new VersionEntry("0.26.0", 40, "2026-08-04", new[]
        {
            "Version history now groups builds under one version heading instead of repeating the version on every entry. Each version shows once as a card, with its builds stacked inside (newest first), each build showing its number and date above its notes. Presentation only — the underlying changelog is unchanged.",
        }),
        new VersionEntry("0.26.0", 39, "2026-08-04", new[]
        {
            "Groundwork for spaced repetition — the study feature that shows you cards just as you're about to forget them. This build adds the engine (the proven SM-2 algorithm, the same approach Anki uses): cards you find easy come back after longer and longer gaps, while ones you miss return the next day. Your review progress is saved privately on your device, separate from the quiz file, so sharing a quiz never carries your personal history and never changes the shared file. No visible change yet — the study screens that use this arrive next. All of it is covered by tests.",
        }),
        new VersionEntry("0.26.0", 38, "2026-08-04", new[]
        {
            "The Android player now shows Numeric and Dropdown questions when taking a quiz — a numeric question gets a number pad and its unit, a dropdown gets a picker. This was the last place these two types weren't yet handled. With it, numeric and dropdown questions work everywhere: authoring and taking on the desktop, all four export formats, and now the mobile player. (Reviewing past attempts already showed them correctly.)",
        }),
        new VersionEntry("0.26.0", 37, "2026-08-04", new[]
        {
            "Test fix for b36: one of the new web-export tests looked for a numeric unit's raw character (m/s²) in the page, but the exporter correctly HTML-encodes units for safety, so the raw character isn't there. The web export itself was fine — this was the test making a wrong assumption. Switched that test to a plain unit and added a dedicated test proving units with special characters are safely encoded. No product code changed.",
        }),
        new VersionEntry("0.26.0", 36, "2026-08-04", new[]
        {
            "The interactive web export now handles Numeric and Dropdown questions — the last export surface. A dropdown becomes a real dropdown in the exported page; a numeric question gets a number box with its unit. Both are graded live in the browser exactly as they are on the desktop: the numeric grader uses a strict number check (so a stray '3.14abc' is marked wrong, not silently accepted as 3.14) and the same tolerance rule, verified against the desktop grader case-for-case. With this, numeric and dropdown questions work everywhere except the Android player — authoring, taking, grading, and all four exports (Word, HTML, Excel, and interactive web).",
        }),
        new VersionEntry("0.26.0", 35, "2026-08-04", new[]
        {
            "Fix for the b34 Excel round-trip: numeric and dropdown questions were being written to the spreadsheet's Type column as 'Essay', so they came back as essays on import. The exporter had a separate type-label list that hadn't been updated for the two new types. Fixed — they now export with the correct Type and round-trip properly. Word and HTML exports were unaffected.",
        }),
        new VersionEntry("0.26.0", 34, "2026-08-04", new[]
        {
            "Exporters now handle the Numeric and Dropdown question types. Word, HTML, and Excel exports render both types and show their answers in the answer key. Excel is a full round-trip: a numeric question stores its correct value, tolerance, and unit; a dropdown stores its options and correct choice — and both import back correctly, with the Guide sheet explaining the format. The interactive web export handles them in a follow-on update. Combined with the desktop authoring from before, numeric and dropdown questions now work across authoring, taking, grading, and document export.",
        }),
        new VersionEntry("0.26.0", 33, "2026-08-04", new[]
        {
            "Test fix: b32 added the Numeric and Dropdown question types, which correctly tripped a guard test that pinned Sequence as the last item in the question-kind list. All the new feature code and its own tests passed (700 of 701) — this was just the guard doing its job. Updated it to a sturdier form that pins the saved numeric value of every question kind (so existing files can never be silently renumbered) and confirms new kinds are appended. No product code changed.",
        }),
        new VersionEntry("0.26.0", 32, "2026-08-04", new[]
        {
            "Two new question types — Numeric and Dropdown (desktop authoring + taking; mobile and exporters to follow). Numeric accepts a typed number, correct when it's within an optional tolerance of the target (0 tolerance means exact); an optional unit label can be shown. Dropdown is a single-answer question presented as a dropdown instead of radio buttons — handy for long option lists — and is graded exactly like single-choice. Both are available in the 'add question' menu, the editor, preview, and when taking a quiz on the desktop. The .qbx format is now version 3; version-2 files still open. Note: exporting these two types to Word/Excel/HTML and taking them on the Android player arrive in the next update — a quiz using them opens and grades correctly everywhere, but those two surfaces don't render them yet.",
        }),
        new VersionEntry("0.26.0", 31, "2026-08-03", new[]
        {
            "AI grammar review — Claude support (completes the feature): you can now choose Claude as the AI grammar provider, not just a local endpoint. Select it in Settings, save your API key (stored encrypted on this machine), and the 'AI grammar check' button uses Claude. Whichever provider you pick in Settings is the one that runs — switch any time. Everything else works the same: pick a scope, review before/after suggestions, accept or reject, undo with Ctrl+Z. This completes the opt-in AI grammar review; the offline spell-checker remains the default and is unaffected.",
        }),
        new VersionEntry("0.26.0", 30, "2026-08-03", new[]
        {
            "AI grammar review — now runnable (phase 3 of 3): an 'AI grammar check' button sits next to 'Check spelling' on both the Quiz Builder and Study Cards tabs. It opens a dialog where you pick a scope — a section, the study cards, or the whole quiz — and run the check. Each suggestion is shown as a clear before → after with a short reason; Accept applies it (and Ctrl+Z undoes it), Reject dismisses it, and 'Accept all' applies every remaining one at once. The check runs against the provider you set up in Settings (a local endpoint works fully offline); it's cancellable, and any problem (nothing configured, unreachable, a bad reply) shows a plain message instead of failing silently. Description text is checked with its formatting tags removed, as elsewhere. This completes the local-endpoint AI grammar feature; Claude support is a small follow-on.",
        }),
        new VersionEntry("0.26.0", 29, "2026-08-03", new[]
        {
            "AI grammar review — engine + local provider (phase 2 of 3): built the machinery that turns quiz text into an AI grammar check and the reply back into concrete suggestions. Starts with a local/self-hosted endpoint (OpenAI-compatible, e.g. Ollama) so it works fully offline with no cloud account. The shared engine builds the prompt from HTML-stripped field text and parses the model's reply robustly — it copes with JSON wrapped in prose or code fences, ignores malformed output gracefully, and drops any suggestion whose text can't be found in your quiz (so a model can't rewrite something that isn't there). There's still no button to run it — that arrives in the final phase. Claude support comes right after the local endpoint. The offline spell-checker is unchanged.",
        }),
        new VersionEntry("0.26.0", 28, "2026-08-03", new[]
        {
            "Test-build fix: two xUnit issues in the b27 test code broke the core-tests build — a wrong assertion name (Assert.NotContains instead of Assert.DoesNotContain) and a Where-clause before Assert.Single (the analyzer wants the filtering overload). Both corrected; no product code changed. The b27 AI-review settings feature is otherwise intact.",
        }),
        new VersionEntry("0.26.0", 27, "2026-08-02", new[]
        {
            "AI grammar review — setup (phase 1 of 3): Settings gains an 'AI grammar review (optional)' section to configure an opt-in AI pass that will check grammar and phrasing. It is Off by default — nothing runs and no quiz text leaves your device unless you turn it on. You can choose a provider (Off; a local/self-hosted endpoint that keeps content on your machine; or Claude), set an endpoint URL or model, and store an API key that is encrypted on this machine (Windows DPAPI, machine-bound). This build only sets it up — actually running a check (scoped to a section, the study cards, or the whole quiz) arrives in a later update. The offline spell-checker is unchanged and always available.",
        }),
        new VersionEntry("0.26.0", 26, "2026-08-02", new[]
        {
            "Spelling dictionary management: the spell-check dialog's 'Ignore' button is now labelled 'Add to dictionary' — it already removed every occurrence of the word and remembered it for future checks, and the new label makes that clear. Settings gains a 'Spelling dictionary' section where you can see every word you've added, add new domain terms by hand (e.g. licensure, subagent), and remove any word to start flagging it again. This makes it easy to teach the checker your field's vocabulary instead of clicking past the same correct terms repeatedly.",
        }),
        new VersionEntry("0.26.0", 25, "2026-08-02", new[]
        {
            "Spell-check improvements: (1) a 'Check spelling' button now also lives on the Study Cards tab (it runs the same whole-quiz review, which already includes card text). (2) Fixed the quiz description being spell-checked with its formatting tags intact — the checker was flagging tag names like 'strong', 'br', 'ul', and 'li' as misspellings. The description is now checked as the reader-visible text (tags stripped via the same parser the app uses to render it), so only real words are flagged. Description findings are shown for review and can be ignored, but are not auto-replaceable (their positions refer to the stripped text, so an in-place fix could damage the markup).",
        }),
        new VersionEntry("0.26.0", 24, "2026-08-02", new[]
        {
            "Cleanup: removed a nullable-dictionary-key warning (CS8714) in the spell-check grouping by using a non-nullable sentinel key for the quiz-level group instead of null. No behaviour change; the build is now warning-clean. (b23 was the first fully green build of the spell-check feature: 634 tests pass and the single-file exe publishes.)",
        }),
        new VersionEntry("0.26.0", 23, "2026-08-02", new[]
        {
            "Build fix: the spell-check dialog's empty-state text set its Style both as an attribute and as a child element, which WPF rejects (MC3024). Folded into a single style. Caught by the app-build CI job; no behaviour change",
        }),
        new VersionEntry("0.26.0", 22, "2026-08-02", new[]
        {
            "Spell-check UI: a 'Check spelling' button on the Quiz Builder toolbar opens a review dialog that scans every field on the quiz and lists possible misspellings grouped by section (plus a quiz-level group for the title, description, and study cards). Each finding shows the word in context with a suggestion picker; Replace applies the fix through undo and dirty-tracking (Ctrl+Z reverts it), and Ignore adds the word to your custom dictionary so it is never flagged again. The dictionary is offline and portable — no cloud, no account. The review re-runs after every fix so offsets and grouping stay correct. (An opt-in AI grammar pass is the planned next step.)",
        }),
        new VersionEntry("0.26.0", 21, "2026-08-02", new[]
        {
            "Build fix: restored the b19 changelog entry's VersionEntry(...) constructor in HelpViewModel, which a prior edit had dropped, leaving a bare object-initializer block that broke the App compile (CS1003). Caught by the app-build CI job (core-tests and android-build were unaffected). No behaviour change; the b20 spell-check engine is otherwise intact",
        }),
        new VersionEntry("0.26.0", 20, "2026-08-01", new[]
        {
            "Spell-check engine (offline): added the provider layer for the desktop spell-checker. Core gains ISpellDictionary (the seam), ITextReviewProvider, and SpellReviewEngine — the pure, tested pipeline that tokenizes each authored field, skips things that must never be flagged ({{n}} blank tokens, numbers, alphanumerics like mp3, URLs, emails, short ALL-CAPS acronyms), honours the user's ignore-list (case/space-insensitive, matching the taker-email normalization), and de-dupes repeats into one issue carrying every occurrence. App gains HunspellDictionary (WeCantSpell.Hunspell, pure-managed, en_US SCOWL dictionary embedded as a resource — MIT/BSD licensed), a SpellIgnoreListStore that persists the custom dictionary via settings.json/Extra (no .qbx change), and OfflineSpellProvider tying them together. Logic proved in tools/port/spell_review_port.py first (it caught a real 'mp3'->'mp' tokenization bug); pinned by SpellReviewEngineTests. Core stays at 2 package refs; the review UI (a by-section 'Check spelling' panel) and the opt-in AI grammar pass come next",
        }),
        new VersionEntry("0.26.0", 19, "2026-08-01", new[]
        {
            "Spell/grammar review (groundwork): added DocumentTextInventory to Core — a pure, WPF-free walk that yields every authored, user-facing text field on a quiz (titles, prompts, hints, choices, accepted answers, blanks, match pairs, distractors, sequence items, rubric notes, study cards) as an addressable read/write TextField, grouped by section. This is the shared source of truth for the coming offline spell-checker and an opt-in AI grammar pass. Design was proved in tools/port/text_inventory_port.py before the C# was written; pinned by DocumentTextInventoryTests (coverage, no-machinery-leak, round-trip). Core stays at 2 package references; no .qbx change; the Android player is untouched",
        }),
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

    /// <summary>
    /// <see cref="History"/> grouped by version for display: each version appears
    /// once with its builds nested (newest build first, matching History's order).
    /// Grouping here keeps the flat History as the single source of truth — build
    /// entries are still added there — while the UI shows them grouped.
    /// </summary>
    public IReadOnlyList<VersionGroup> GroupedHistory =>
        History
            .GroupBy(entry => entry.Version)
            .Select(group => new VersionGroup(group.Key, group.ToList()))
            .ToList();
}

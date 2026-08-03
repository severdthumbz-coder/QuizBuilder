# Quiz Builder — Project Handoff

**Last shipped:** v0.26.0 build 28 (stage `maui-android-player`)
**Deliverable:** `QuizBuilder_v0.26.0.28.zip` (kept in a sibling folder, outside the repo tree)
**Status:** b27 (AI-review phase 1: settings + DPAPI key) had two xUnit mistakes in
the NEW test code that broke the `core-tests` build — `Assert.NotContains` (should be
`Assert.DoesNotContain`) and a `.Where()` before `Assert.Single` (analyzer wants the
predicate overload; a latent one in `SpellReviewEngineTests` surfaced too). **b28
fixes both; no product code changed.** The phase-1 feature (AiProvider/AiReviewSettings,
AiKeyProtector, Settings UI) is intact. Local check note: the test project builds with
xUnit analyzers as errors, which validate.py doesn't run — added test-anti-pattern
greps (NotContains, Where-before-Single) to the local pre-flight.
Decided: one active provider at a time (not simultaneous); privacy-first ordering
(Local before Claude); AI scope will be section / study-cards / whole-quiz; apply
flow is one-by-one accept/reject plus accept-all, all routed through undo; AI input
reuses the description HTML-strip so tags never reach the model.

This document exists so a new chat can resume without re-reading the whole
history. Read the BUILD STATUS block immediately below, then §0 for what shipped,
then the rest as reference.

---

## BUILD STATUS & COMMANDS (quick reference)

### Last successful build of each target
| Target | Last confirmed-green build | How it was confirmed |
|---|---|---|
| **Desktop (WPF, `QuizBuilder.App`)** | **v0.26.0 build 17** | CI `app-build` job (Windows, self-contained single-file publish) green on every push through b17. Locally confirmed via `.\build.bat` at b14 (612/612 tests, exe produced); b15–17 are desktop-neutral or Core-safe and stay green in CI. |
| **Android player (MAUI, `QuizBuilder.Player`)** | **v0.26.0 build 17** | CI `android-build` job (Windows, JDK 21, `dotnet workload install android maui`, Debug build) green on every push since it was added in b13. Maintainer has also run it **on-device (emulator)** through the tier-1 features + library. |
| **Core (`QuizBuilder.Core`)** | **b19 green (621/621, `DocumentTextInventory` tests passed on CI); b20 adds `SpellReviewEngineTests`, expected green** | CI `core-tests` (Ubuntu, xUnit). b19 confirmed green. b20 adds `SpellReviewEngine` + contracts + tests (~15 more facts); the App-side Hunspell wrapper is NOT tested here (App-only). Core multi-targeted `net8.0;net10.0`; player consumes net10. |

CI = `.github/workflows/build.yml`, **three jobs, all green, no annotations**:
`core-tests` (Ubuntu) → `app-build` (Windows) and `android-build` (Windows), the
latter two both `needs: core-tests`. All actions pinned to `@v5`.

### Building the DESKTOP app — `build.bat`
Run from the repo root on Windows (PowerShell or cmd):
```
.\build.bat                 Build + test + publish a self-contained single-file exe,
                            then prompt to launch. Output: publish\QuizBuilder v<ver>.exe
                            and a QuizBuilder v<ver>.zip (the exe only).
.\build.bat --no-test       Build + publish, skip the xUnit run.
.\build.bat --no-publish    Build + test only (no exe, no zip).
.\build.bat --quiet         Don't prompt to launch at the end.
```
Five stages: (1) clean — preserves the user's `settings.json` and any `.qbx`
saved beside the exe; (2) build; (3) test; (4) publish single-file; (5) package —
**zips only the current build's exe** (b16: immune to a locked leftover exe and
never ships the user's settings). If the exe is locked (the app is running),
close it first. Needs a .NET 10 SDK (it warns but proceeds; the projects still
target net8.0 for the desktop).

### Building the ANDROID app — `build-android.bat` (wraps `build-android.ps1`)
Run from the repo root on Windows. Flags combine in any order:
```
build-android.bat                         Debug APK; prints the .apk path, no install.
build-android.bat launch                  Debug APK → install → launch on the device.
build-android.bat launch device=emulator-5554   ...target a specific adb device.
build-android.bat install                 Debug APK → adb-install it.
build-android.bat release                 Release APK + AAB (needs QB_KEYSTORE* env vars).
build-android.bat release install         Release, then install.
build-android.bat clean                   Clean first, then Debug build.
```
`launch` implies `install`; `device=<serial>` targets one device when several are
connected. The script probes installed JDKs and picks one in the Android-SDK-
supported range **17–21** (b9: a too-new JDK like 25 is rejected with a clear
"install JDK 21" message — that was a real blocker; the maintainer installed
Microsoft OpenJDK 21). The APK lands under
`QuizBuilder.Player/bin/{Debug|Release}/net10.0-android/` (NOT committed; `bin/`
is gitignored).

### Publishing the APK for download (+ desktop QR)
`build-android.bat release` → go to the repo's **Releases** on github.com → draft
a release, tag it (e.g. `v0.26.0`), upload the `.apk` as an asset → copy the asset
URL (`…/releases/download/<tag>/<file>.apk`) → paste it into the desktop app's
**GitHub tab → APK download link**, which renders a scannable QR live (b8). The
link persists in desktop settings; it is **not** written into any `.qbx`.

### Validation gate — `tools/validate.py`
```
py tools/validate.py        12 static checks; must pass before any delivery.
```

### Git workflow (per build)
Push-first: commit → push → wait for green CI (`gh run watch`) → then build
locally. Zips are gitignored (`*.zip`) so `git add -A` stages source only.
```
git add -A
git commit -m "v0.26.0 build <N>: <summary>"
git push origin main
```

---

## 0. CURRENT STATE & RECENT WINS (read this first)

### What has shipped (builds 3–14) — all confirmed building; mobile confirmed on-device
Everything below is built, validated (validate.py 12/12), and — because the
Android CI job now compiles the player on every push — confirmed to compile in a
clean environment, not just locally. The maintainer has also run the player
on-device. Verification level is no longer a caveat.

- **b3 — CI test-isolation fix (the real one).** The DPAPI flake is fixed
  deterministically by putting every test that mutates the global
  `ProtectedDataShim` delegates into one xUnit collection
  (`ProtectedDataShimCollection`), so `TokenProtectorTests` and
  `MobileReadPathContractTests` never run in parallel. Root cause was parallelism
  (collection = unit of parallelization), NOT ordering. Supersedes the two
  candidate fixes the old §13 note suggested; do not "skip off-Windows".
- **b3 — Android script gained `-Launch` and `-Device`.** `build-android.bat
  launch` installs+launches; `device=<serial>` targets one adb device.
- **b4 — Study Cards review (mobile).** Flip through the quiz via Core's
  FlashDeck; source toggle (Questions/Study cards/Both); shuffle. Home button.
- **b5 — Attempt history (mobile).** Finished attempts saved to history.json in
  the sandbox (storage-path blocker resolved via the overrideDirectory seam →
  FileSystem.AppDataDirectory). List + per-question detail; swipe-delete; clear-all.
- **b6 — Pause/resume (mobile).** "Pause & save" snapshots the exact paper +
  answers to paused-attempts.json; Home lists paused sittings to resume; back
  button offers Pause/Leave/Keep (three-way).
- **b9 — Android build script JDK fix.** Probes each JDK's version and picks one
  in the Android-SDK-supported range (17–21), skipping too-new JDKs (the XA0030
  error on JDK 25). Prints a clear "install JDK 21" message if none compatible.
  (Resolved a real blocker: the maintainer's Android Studio bundled JDK 25.)
- **b8 — APK download QR (desktop GitHub tab).** Paste an APK link → a QR renders
  live (QRCoder, PngByteQRCode renderer, no System.Drawing dep); copy-link and
  save-image. The link persists in desktop settings (NOT in the .qbx).
- **b10 — Three mobile fixes.** (1) Quiz description renders as readable text, not
  raw HTML (HtmlToText converter). (2) Paused attempts individually deletable
  (trash button). (3) History + paused scoped per taker by normalized email
  (case/space-insensitive); legacy records with no identity show to everyone so
  nothing vanishes. Core: AttemptRecord/PausedAttempt gained TakerEmailKey +
  TakerName; ForQuizAndTaker queries; TakerKey helper.
- **b11 — Import cleanup** (later superseded by b12's library).
- **b12 — Quiz library (option C).** Player is no longer single-load: after
  identity, a Library screen lists kept quizzes (QuizLibraryService + library.json
  index, files stored as quiz_<id>.qbx). Tap to open, Choose file to import, trash
  to delete with a keep-results / wipe-results prompt. Re-import updates the same
  entry (keyed by QuizId, so history/paused stay attached). Nav is now Identity →
  Library → Home(detail) → Take → Results. Core: PausedAttemptService gained
  ClearForQuiz.
- **b13 — Android CI job.** New android-build job (windows-latest, needs
  core-tests): SDK from global.json, JDK 21, `dotnet workload install android
  maui`, Debug build of the player. Gates mobile compiles automatically. GREEN.
- **b14 — Actions bumped to @v5.** Cleared the Node-20 deprecation warning; all
  GitHub Actions now @v5.
- **b15 — Handoff refresh.** Brought this document current (it had been stale at
  b6/b7).
- **b16 — Packaging fix (build.bat).** The package step now zips ONLY the current
  build's version-stamped exe, not `publish\*`. Fixes two things: a leftover exe
  from a previous build (e.g. one still running, which defeats `rmdir /s /q`) can
  no longer make packaging fail; and the user's `settings.json` (encrypted GitHub
  token) / saved `.qbx` can never end up in a distributable zip.
- **b17 — Nullable-warning fix, caught by the Android CI job.** `AttemptRow` in
  HistoryViewModel left `PassFail` (a non-nullable string) unassigned on the
  no-auto-score branch (all-essay attempt) — a latent NRE, flagged as a compiler
  warning only because `android-build` compiles the player. Now every field is
  assigned on every path. (Textbook example of the CI job earning its keep and of
  the "all-fields-assigned" bug family in §9.)
- **b19 — Spell/grammar review groundwork (Core only).** First slice of the
  desktop spell/grammar-check feature. Added `DocumentTextInventory` (Core,
  `Services/`): a pure, WPF-free static walk that yields every authored
  user-facing text field on a `QuizDocument` as a `TextField` — a
  `(TextFieldKind, Label, SectionId?, QuestionId?, Func<string> Get, Action<string> Set)`
  record. Get/Set close over the live model so an accepted correction
  round-trips to the exact source (scalar property, optional property, or a
  `List<string>` element). Grouping ids let the review panel group findings by
  section (the primary UX ask) and jump to the owning question. Deterministic
  reading order. `QuestionHint` is emitted even when empty so an author-written
  hint is checked; callers filter on `.Text`. Design was ported to
  `tools/port/text_inventory_port.py` and run exhaustively first (coverage,
  no-machinery-leak, round-trip), then the C# was written; both are pinned by
  `DocumentTextInventoryTests` (9 facts). **Invariants held:** Core stays at 2
  package refs, stays WPF-free, tests reference only Core; no `.qbx`/format
  change (an ignore-list and any AI settings are desktop-local, like the APK
  link); the Android player is untouched by construction.
  **NOT yet built (next):** the App-side provider layer — `ITextReviewProvider`
  with an `OfflineSpellProvider` (WeCantSpell.Hunspell, pure-managed, one package
  on App only) and the custom-dictionary/ignore-list persisted via
  `SettingsService`/`AppSettings.Extra`; then the by-section review UI on the
  QuizBuilder tab; then the opt-in tier-3 AI grammar pass (provider-selectable
  in settings — `Off`/`Claude`/custom-OpenAI-compatible-endpoint — default off,
  section-scoped, suggestions advisory-only, key stored via the encrypted-token
  machinery, never plaintext). Accepted corrections must route through
  `IQuizDocumentService` (undo/autosave), NOT `TextField.Set` directly.
- **b20 — Offline spell-check engine + provider layer (the "B" increment).**
  Built the engine and its offline provider; the provider-layer item above is
  now DONE except the UI. Split per the agreed (b) design — testable logic in
  Core, the unverifiable engine in App:
  - **Core (tested):** `ISpellDictionary` (IsKnown/Suggest — the seam that keeps
    the pipeline testable without Hunspell), `ITextReviewProvider` (DisplayName +
    `Review(fields)`; one-shot, button-triggered, not live), and
    `SpellReviewEngine` — tokenization with spans, exclusions ({{n}} tokens,
    numbers, alphanumerics like "mp3", URLs, emails, short ALL-CAPS acronyms,
    single letters), ignore-list suppression (trim + lower-invariant, same
    normalization as `TakerKey`), and de-dup into one `TextIssue` per word
    carrying every `TextOccurrence`. Pinned by `SpellReviewEngineTests`.
  - **App (maintainer must confirm at runtime):** `HunspellDictionary` wrapping
    WeCantSpell.Hunspell (API verified against upstream source:
    `WordList.CreateFromStreams`/`Check`/`Suggest`), loading an **embedded**
    en_US SCOWL dictionary (`Resources/Dictionaries/en_US.dic/.aff`, sourced
    from the npm `dictionary-en` package, **MIT/BSD** — clean to bundle;
    `en_US.LICENSE.txt` kept alongside for attribution). `SpellIgnoreListStore`
    persists the custom dictionary in `AppSettings.Extra` under
    `spellcheck.ignoreWords` (JSON array; no `.qbx`/format change).
    `OfflineSpellProvider` composes engine + dictionary + ignore-list. All three
    registered in `App.xaml.cs` DI.
  - **Package count:** App gains ONE ref (WeCantSpell.Hunspell); **Core stays at
    2**. Logic was ported to `tools/port/spell_review_port.py` and run first — it
    caught a real tokenization bug ("mp3" leaving a stray "mp" token) before any
    C# was written.
  - **NOT done (next):** the review UI — a "Check spelling" button on the
    QuizBuilder tab opening a by-section results panel (Replace / Ignore-once /
    Add-to-dictionary), with Replace routing through `IQuizDocumentService` so
    corrections join undo/autosave. Then the opt-in AI grammar provider.
  - **Verification gap to close on your machine:** confirm the embedded
    dictionary loads under the single-file publish (`build.bat`), and that
    Hunspell actually flags a planted misspelling and suggests a fix. The engine
    logic is proved; the engine *dependency* is not.
- **b21 — App compile fix.** b20's App build broke on a dropped
  `new VersionEntry(...)` constructor in `HelpViewModel` (the b19 changelog entry
  lost its opening line during a `str_replace`), leaving a bare `{ "..." }`
  block — `CS1003`. Caught by `app-build` CI; `core-tests`/`android-build` were
  green throughout, confirming the engine + tests were fine. One-line fix.
  Lesson recorded: `validate.py` does not parse C#, so a brace-balanced but
  malformed initializer passes it — after any changelog/initializer edit,
  re-read the whole entry block (now done as a routine check).
- **b22 — Spell-check UI (the "B" increment's UI).** Wired the engine to a real
  interface:
  - **Entry point:** a "Check spelling" button on the Quiz Builder toolbar
    (before New, after a divider). Opens a modal `SpellCheckWindow` built with a
    fresh `SpellCheckViewModel` each time, so it reviews the document as it
    stands (post-edit / post-undo / post-Open), never a stale snapshot.
  - **Grouping:** issues grouped by section, in document order, with a synthetic
    "Quiz (title, description, study cards)" group first for non-section text.
    Each row shows the word, its field label, the word in context, a suggestion
    picker, and Replace / Ignore.
  - **Replace** routes through a new App `SpellFixApplier` (routing proved in
    `tools/port/spell_fix_apply_port.py`): captures an undo snapshot BEFORE the
    edit, then dispatches by `TextFieldKind` — quiz title/description/section
    title via the dedicated service setters; question-internal fields via raw
    `TextField.Set` + `NotifyQuestionChanged`; study cards via `UpdateStudyCard`.
    Splices by offset so a field with the same misspelling twice fixes only the
    targeted instance. After every fix the review RE-RUNS (undo swaps the whole
    document; stale `TextField` closures must not be reused — the guard/staleness
    family again).
  - **Ignore** adds the word to the custom dictionary (`SpellIgnoreListStore`,
    `settings.json`/`Extra`) and re-runs so all occurrences vanish.
  - **Core change:** `TextField` gained an optional `OwnerId` (Guid?, default
    null) so study-card text can be addressed for `UpdateStudyCard` without the
    review layer re-scanning. Backward-compatible (optional trailing param);
    existing call sites and tests compile unchanged. Pinned by a new
    `DocumentTextInventoryTests` fact. **Core still at 2 package refs.**
  - **Study-card Replace subtlety (RUNTIME-UNCONFIRMED):** `ApplyStudyCard`
    computes both sides and lets `UpdateStudyCard` do the write rather than
    pre-writing via the raw setter — otherwise `UpdateStudyCard`'s no-op guard
    (front/back unchanged → skip) would swallow the notification. Logic proved in
    the port; visual deck refresh after a study-card fix is a maintainer check.
  - **Verified here:** validate.py 12/12 (new window's x:Class + 15
    DynamicResource keys resolve), every XAML binding resolves to a VM member,
    every `Click` handler exists in code-behind, brace-balance clean, all three
    ports green. **NOT verifiable here:** MAUI/WPF compile and the Hunspell
    dictionary actually loading/flagging — the "Check spelling" button is now the
    way to confirm that on Windows.
  - **NOT done (next):** the opt-in AI grammar provider (provider-selectable in
    settings — `Off`/`Claude`/custom-OpenAI-compatible-endpoint — default off,
    section-scoped, suggestions advisory, key via encrypted-token store). Also
    optional: a "Check spelling" entry point on the Study Cards tab itself
    (currently the whole-quiz scan on the Builder tab already covers cards).
- **b23 — XAML compile fix.** `SpellCheckWindow.xaml`'s empty-state TextBlock set
  `Style` twice: once as a `Style="{StaticResource Context}"` attribute and again
  via a `<TextBlock.Style>` element (needed for the HasIssues DataTrigger) — WPF
  forbids both (`MC3024`). Folded into the single element style, which already
  used `BasedOn="{StaticResource Context}"` so the styling is unchanged. Caught
  by `app-build`; `core-tests`/`android-build` green throughout. Added a repo-wide
  pre-flight (attribute-`Style=` plus child-`.Style` on the same element) to the
  session's local checks so this class is caught before push next time — the same
  category of gap as b21 (validate.py doesn't compile XAML/C#).
- **b24 — Warning cleanup (CS8714).** b23 built green but warned: the
  spell-check grouping keyed a `Dictionary<Guid?, …>` on a nullable Guid, using
  `null` for the quiz-level group — `Guid?` violates the `notnull` key
  constraint. Switched to a non-nullable `Guid` key with `Guid.Empty` as the
  quiz-level sentinel (safe: sections always get `Guid.NewGuid()`, so no
  collision). Build is now warning-clean. Same nullable-warning family as b17.
  **b23 milestone:** first fully green spell-check build — `build.bat` succeeded,
  634/634 tests, single-file exe published with the embedded Hunspell dictionary.
- **b25 — Study Cards spell-check button + HTML-description fix.**
  - **Study Cards button:** added a "Check spelling" button to the Study Cards
    tab header (beside "+ Add card"). It opens the same whole-quiz
    `SpellCheckWindow` as the Builder tab — study-card text is already in that
    scan (quiz-level group), so this is a convenience entry point, not a
    cards-only checker. Same DI-injected services (`IQuizDocumentService`,
    `ITextReviewProvider`, `SpellIgnoreListStore`, `SpellFixApplier`).
  - **HTML-in-description fix (the real bug):** the quiz description is the one
    authored field carrying markup (safelist b/strong/i/em/br/ul/li). The engine
    was tokenizing the raw string, so tag NAMES ("strong", "br", "ul", "li") were
    flagged as misspellings. Fix: for `TextFieldKind.QuizDescription` only, the
    engine checks `DescriptionParser.ToPlainText(field.Text)` — the same parser
    the renderer/exporters use, so what's checked is exactly what a reader sees.
    Only the description is HTML-bearing (confirmed: all four `DescriptionParser`
    call sites parse the description; nothing parses prompts/choices), so every
    other field is unchanged and a literal `<` there is still checked as typed.
  - **Non-replaceable description issues:** offsets from stripped text don't map
    back to the raw markup, so a splice-Replace could corrupt formatting. Rather
    than build a fragile offset-mapper, description occurrences are flagged
    `Replaceable = false` (new bool on `TextOccurrence`, default true) — they're
    surfaced with context and can be Ignored/added-to-dictionary, but Replace is
    disabled (`SpellIssueRowViewModel.CanReplace = HasSuggestions && Replaceable`;
    the button binds to `CanReplace`). Full description Replace is a possible
    future add via offset-mapping; deliberately deferred as higher-risk, low-value
    (description is a short field).
  - Design proved in `tools/port/description_plaintext_port.py` first (tag names
    don't leak, literal `<` survives, attributed tags stay literal per the
    parser's no-attributes rule, words don't fuse across `<li>`). New
    `SpellReviewEngineTests` facts pin: tag names not flagged, real description
    misspelling flagged-but-non-replaceable, non-description issues replaceable.
  - **Verified here:** validate 12/12, MC3024 pre-flight clean, all four ports
    green, StudyCards Click handler + `CanReplace` binding resolve. **Core still
    at 2 package refs.** Not verifiable here: WPF/MAUI compile, runtime behaviour.
- **b26 — Spelling dictionary management (the "make Ignore powerful" work).**
  Runtime confirmed the checker flags correct domain terms a general dictionary
  doesn't know (licensure×7, subagent), with bad suggestions ("censurer"). The
  engine is right; the gap was no easy way to teach it vocabulary. Fix, App-only:
  - **"Ignore" → "Add to dictionary".** The button already added the word to the
    custom dictionary and re-ran the review (so all occurrences vanished in one
    click — it was already "Ignore All"). Relabelled to say what it does; the
    Replace/Ignore semantics are unchanged.
  - **Settings "Spelling dictionary" card.** New section on the Settings tab
    listing every custom word (sorted), with a text box + "Add word" to add a
    term by hand and a "Remove" per word. Backed by the existing
    `SpellIgnoreListStore` (already had Add/Remove/GetWords), surfaced via new
    `SettingsViewModel` members (`SpellWords`, `NewSpellWord`,
    `AddSpellWordCommand`, `SpellWordRow` with a remove command). The list
    refreshes when the Settings tab is shown (`IsVisibleChanged`), so words added
    via the dialog appear without a restart. `SettingsViewModel` gained a
    `SpellIgnoreListStore` ctor param — DI supplies it (both are singletons;
    `SettingsViewModel` is DI-only).
  - No `.qbx`/format change (dictionary is in `settings.json`/`Extra`). Core
    untouched — 634 tests unchanged. Verified: validate 12/12, MC3024 clean,
    balance clean, all new bindings resolve, DI wiring confirmed.
- **b27 — AI grammar review, phase 1: settings + secure key (no network).**
  First of three phases for the opt-in AI pass. This phase adds only
  configuration and secret storage — nothing calls the network yet.
  - **Core model:** `AiProvider` enum (`Off` default / `LocalEndpoint` /
    `Claude`) and `AiReviewSettings` (provider, `LocalEndpointUrl`, `Model`) as a
    typed field on `AppSettings`. Off by default, so upgrading adds nothing that
    reaches the network. Serialises as a string (existing `JsonStringEnumConverter`).
  - **Key storage (option C):** `AiKeyProtector` — a minimal, isolated DPAPI
    protector (CurrentUser, machine-bound) with its OWN entropy, wrapping the
    existing `ProtectedDataShim`. Deliberately NOT reusing/refactoring the
    stateful, passphrase-capable `TokenProtector` (leaves that tested,
    slightly-flaky code alone). Key ciphertext lives in `Extra` under
    `ai.reviewKey`; `SettingsService` gained `SetAiReviewKey`/`GetAiReviewKey`/
    `HasAiReviewKey`. Machine-bound is correct for an API key (re-enter if the
    settings file is copied).
  - **Settings UI:** provider dropdown (privacy-first order: Off, Local, Claude),
    conditional endpoint/model fields for Local, a cloud-notice banner for Claude
    stating plainly that content is sent to Anthropic only when a check is run,
    and an encrypted API-key `PasswordBox`. The key follows the GitHub-token
    discipline exactly: read from the password box at the point of use, passed
    straight to the VM, never bound to a property or held in a field, box cleared
    after save.
  - **Tests:** `AiKeyProtectorTests` + `SettingsServiceAiKeyTests` — round-trip,
    blank/null, wrong-scheme, corrupted-base64, unicode, persist-across-reload,
    key-stored-protected-not-plaintext, default-Off. BOTH join
    `[Collection(ProtectedDataShimCollection.Name)]` — mandatory, since they swap
    the global shim delegates (the documented DPAPI-flake race).
  - **Verified here:** validate 12/12, MC3024 clean, balance clean, all 10 AI
    Settings bindings resolve, `OnSaveAiKeyClick` handler present, no other
    `ISettingsService` implementer to break. **Core package count unchanged.**
    Not verifiable here: WPF compile, DPAPI on real Windows, runtime.

### Tier-1 mobile backlog: COMPLETE. Quiz library: COMPLETE.
Study Cards, history, pause/resume (the decided tier-1 set) all shipped, plus the
library (the biggest post-tier-1 feature). The old §13 "not yet built" wording is
historical.

### On timed quizzes (still deferred — "option B")
The player does NOT enforce a time limit, and that is correct: the .qbx /
QuizDocument has NO time-limit field. TimeLimitMinutes lives only on QuizSettings
(a desktop-local setting) and is never serialized into the .qbx. Making timed
quizzes portable = add optional TimeLimitMinutes to QuizDocument (author into the
.qbx, desktop writes it, player reads+enforces; desktop TakeQuizViewModel has the
full timer/auto-submit reference). Product owner deferred this. Same story for the
APK link and GitHub-tab info: desktop-local settings, not .qbx content.

### Suggested next steps (none started)
- Library search/sort (natural once there are many quizzes).
- One-time migration adopting pre-existing sandbox files into the library.
- Timed quizzes ("option B" above) — the notable format-level gap.
- App icon/monogram (still placeholder); iOS target (needs a Mac).
- To publish the APK: `build-android.bat release` (needs QB_KEYSTORE* env vars) →
  GitHub Releases → upload the .apk → copy the asset URL → paste into the GitHub
  tab. The APK lives under QuizBuilder.Player/bin/Release/net10.0-android/ (not in
  the repo; bin/ is gitignored).

### Where the project is right now (v0.26.0)
- **Two apps now share one Core.** The original **WPF desktop app** (`net8.0`)
  and a new **.NET MAUI Android player** (`net10.0-android`) both reference
  `QuizBuilder.Core`, which is now **multi-targeted `net8.0;net10.0`**. Scoring,
  the `.qbx` format, and the domain model are shared by construction — the mobile
  player never re-implements grading.
- **The Android player works end to end on a device.** Built, deployed to an
  Android emulator, and run: identity capture (first/last name + email) → `.qbx`
  import → take (all **8** question types, touch-first) → grade via Core →
  results → **email results via the native mail composer**. The score matches the
  desktop app because it is the *same Core grader*.
- **Desktop app is unchanged and still green.** `dotnet build` on the solution
  builds `QuizBuilder.Core` (both `net8.0` and `net10.0` slices),
  `QuizBuilder.Tests` (`net8.0`), and `QuizBuilder.App` (`net8.0-windows`). The
  MAUI project is in the `.sln` but has **no `Build.0`**, so a plain solution
  build skips it (it needs the MAUI workload, which desktop/CI runners lack).
- **CI is green on all three jobs** (`.github/workflows/build.yml`): `core-tests`
  (ubuntu, full xUnit suite, **612/612** — the old DPAPI flake was fixed in b3),
  `app-build` (windows, publishes the self-contained WPF exe), and `android-build`
  (windows, compiles the MAUI player — added in b13). All actions on `@v5`.
- **Repo:** `severdthumbz-coder/QuizBuilder`, branch `main`, working tree clean
  and pushed as of this handoff. The push-first workflow (add/commit/push → wait
  for the green checkmark → build locally) remains the gate.

### What this session did (v0.25 → v0.26)
Built the entire MAUI Android player from nothing, got it compiling green, and
ran it on a device. New project `QuizBuilder.Player` (`net10.0-android`, MAUI 10)
with: touch-first pages (identity → home/import → take → results); eight
per-type answer widgets behind a `DataTemplateSelector`; `.qbx` import via file
picker + Android VIEW/SEND intents; a native-mail results composer; and a
`build-android.ps1`/`.bat` pair that auto-detects the whole Android toolchain.
Design follows the UI/UX Pro Max rules (semantic light/dark tokens, 48dp touch
targets, one CTA per screen, inline validate-on-blur). See §13 for the full
build story, the toolchain lessons, every fix applied, and the next-session plan.

### The .NET 10 / MAUI build — how the Android app is built
- **The MAUI player targets `net10.0-android`.** MAUI 10 ships in lockstep with
  .NET 10, which is why Core gained a `net10.0` target. The player consumes
  Core's `net10.0` slice; the desktop app consumes Core's `net8.0` slice. One
  source, two hosts.
- **`global.json` is pinned to a stable .NET 10 SDK** (`version: 10.0.100`,
  `rollForward: latestFeature`, `allowPrerelease: false`). This is load-bearing:
  the machine also has a **.NET 11 preview SDK**, and without the pin, roll-
  forward jumps to 11 and `dotnet workload install maui-android` then pulls a
  **mismatched net11 preview workload band** against a `net10.0-android` project.
  The pin keeps everything on the stable net10 band. `dotnet --version` run from
  the repo folder MUST print `10.0.x`, not 11 — that is the canary.
- **Build the Android app with `build-android.bat`** (Debug APK, self-contained,
  installs directly) or `build-android.bat release` (signed APK + AAB when the
  `QB_KEYSTORE*` env vars are set). The `.bat` wraps `build-android.ps1`, which
  is the single source of truth. The script checks the toolchain, sanity-builds
  Core's net10 slice first (fast fail), then builds the app, and reports the APK
  path. **Full command reference and the fast dev-loop (`dotnet build -t:Run`)
  are in §13.**
- **Required toolchain (all detected/handled by the script):** stable .NET 10
  SDK; the **maui-android workload** (`dotnet workload install maui-android` —
  separate from the SDK); the **Android SDK** (not bundled by the workload —
  Android Studio's wizard installs it to `%LOCALAPPDATA%\Android\Sdk`); a **JDK**
  (Android Studio bundles one at `…\Android Studio\jbr`); and the **API-36
  platform** (`android.jar`, auto-installed by the script via
  `InstallAndroidDependencies` on first run).

### Format version (unchanged this arc)
`.qbx` `FormatVersion` is **2** (bumped in v0.25 for the sequence type). A v1
file still opens in v2 (reading ≠ upgrading). The mobile player reads v2 `.qbx`
files through the same `QuizPackageService` the desktop uses.

### Immediate next-step options
Nothing is blocking; the tree is all-green (CI 3/3, on-device confirmed). The CI
test-isolation bug, tier-1 mobile, the quiz library, the Android CI job, and the
Node-20 actions bump are all DONE (see §0). Open candidates, none started:
- **Library search/sort** — natural once there are many quizzes. Small.
- **One-time migration** adopting pre-existing sandbox files into the library.
- **Timed quizzes ("option B", see §0)** — the notable format-level gap; needs a
  `.qbx`/QuizDocument change plus desktop authoring plus a mobile timer.
- **Desktop feature work from §14** (note the drag-and-drop *sequence* type
  already shipped in 0.25.0, so §14's #1 is partly done — hotspot/numeric types
  are the fresh candidates).
- **Polish/housekeeping:** replace the placeholder app icon; test the email
  composer on a real phone (emulators usually have no mail app); iOS target
  (needs a Mac).

---

## 0b. HISTORICAL: the Sequence question arc (v0.25, shipped)

The prior arc added the **Sequence question type** (the 8th type). Kept here for
reference since it is the most recent domain feature.

A taker drags items into the correct order; scoring is **adjacent-pair partial
credit** (each correctly-ordered neighbouring pair earns a share, so one
misplaced item does not fail the whole question). Model: `SequenceQuestion` with
`Items` in correct order (the answer key). Presentation: the shuffle the taker
sees is a projection on the compiled question —
`CompiledQuestion.SequencePresentation` (a permutation of item indices); the
model's `Items` are never reordered (mirrors `MatchingOptions`). Guard: never
the identity permutation for n≥2. Wired end-to-end across all App integration
points, every exporter, Excel import, and **pause/resume persistence**
(`SequencePresentation` is saved in the paused snapshot, or a resumed sequence
would show the answer). An untouched sequence counts as unanswered → scores 0.

Test suites from that arc (all in CI): `SequenceQuestionTests`,
`SequencePresentationTests`, `PackageBackwardCompatTests` (a v1 `.qbx` still
opens), and `MobileReadPathContractTests` (pins the Core slice the mobile player
depends on — this is the suite involved in the current CI note; see §13).

---

## 1. What this is

Originally a portable Windows desktop **Quiz Builder** (WPF / .NET 8 / C# /
MVVM); **as of v0.26 also a .NET MAUI Android player** that shares the same
`QuizBuilder.Core`. An author builds quizzes, takes them, and exports them on
desktop; a taker imports a `.qbx` and takes quizzes on Android. Everything is
local — no cloud, no account, no LMS, on either platform.

**Portability is a hard requirement.** On desktop, settings and stored data are
written *beside the .exe* (never AppData, never the registry); the app publishes
as a single self-contained file. On mobile, the same Core services write into
the **app sandbox** (`FileSystem.AppDataDirectory`) via their `overrideDirectory`
constructor seam — the desktop's "beside the exe" rule has no meaning on a phone.

### How to build and run it

**Desktop (WPF):**
```
build.bat                 clean, build, test, publish, package
build.bat --no-test       skip tests
build.bat --no-publish    build + test only
build.bat --quiet         don't prompt to launch at the end
```
Requires a **.NET 10 SDK** (the repo's `global.json` now pins `10.0.100`; a
.NET 8 SDK also satisfies the desktop's `net8.0` targets, but the MAUI player
REQUIRES .NET 10, so a net10 SDK is the one to have installed). The script
checks the SDK up front and gives a clear message instead of NETSDK1045.
Output: `publish\QuizBuilder vX.Y.Z.B.exe` — self-contained, single file.

**Android (MAUI):**
```
build-android.bat                 Debug APK (self-contained, installs directly)
build-android.bat release         Release APK + AAB (needs QB_KEYSTORE* env vars)
build-android.bat install         Debug APK, then adb-install to a connected device
build-android.bat clean           clean first
```
Requires the **.NET 10 SDK**, the **maui-android workload**, the **Android SDK**,
a **JDK**, and the **API-36 platform** — the script detects/installs all of these
and prints what it found. See §0 above for the toolchain summary and §13 for the
exact fast dev-loop command, the emulator setup, and every gotcha.

**GitHub / CI is the primary gate.** Workflow: `git add/commit/push` to `main` →
GitHub Actions runs `build.yml` (all three jobs must go green) → then build locally.
Check with `gh run list` / `gh run watch` / `gh run view <id> --log-failed`.

---

## 2. THE CRITICAL CONSTRAINT (read this too, before touching code)

**The AI assistant cannot compile or run this app.**

- No .NET SDK in the environment.
- WPF cannot run on Linux.
- NuGet is not allowlisted.

**The workflow that has worked for ~26 rounds:**

1. AI writes C#.
2. AI verifies logic by **porting the tricky parts to Python or JS and actually
   running them** before writing the C#.
3. AI runs `tools/validate.py` (12 static checks) and a set of hand-rolled greps.
4. AI zips to `/mnt/user-data/outputs/QuizBuilder.zip`.
5. **The user builds on Windows** (`build.bat`) and pastes the build log back.
6. AI fixes whatever the real compiler found.

Do not promise a build is green. Only the user's Windows build proves that.

---

## 3. Solution structure

```
QuizBuilder.sln
├── QuizBuilder.Core/     net8.0;net10.0   ← portable, WPF-free, MULTI-TARGETED
├── QuizBuilder.App/      net8.0-windows    ← WPF desktop host (uses Core net8)
├── QuizBuilder.Player/   net10.0-android   ← MAUI Android player (uses Core net10) [NEW v0.26]
├── QuizBuilder.Tests/    net8.0            xUnit, 612 test methods
├── build.bat             desktop: 5-stage build, REFUSES to publish if tests fail
├── build-android.ps1     MAUI: toolchain-detecting Android build (SDK/JDK/platform) [NEW]
├── build-android.bat     thin wrapper over build-android.ps1 [NEW]
├── version.json          single source of version truth (now 0.26.0)
├── global.json           pins SDK to STABLE .NET 10 (10.0.100) — see §0 [CHANGED]
├── Directory.Build.props
├── tools/validate.py     12 static checks; XML check now covers .xml/.manifest [CHANGED]
├── assets/               icon.svg / icon.ico / make-icon.py
└── .github/workflows/build.yml   (3 jobs: core-tests, app-build, android-build)
```

**The `.sln` lists `QuizBuilder.Player` with `ActiveCfg` but NO `Build.0`.** This
is deliberate: a plain `dotnet build QuizBuilder.sln` (desktop, CI) skips the
Android project, which needs the MAUI workload the desktop/CI runners lack. The
IDE can still build the player on demand; `build-android.bat` builds it directly.

### Package references — HELD DELIBERATELY LOW

| Project | TFM | Packages |
|---|---|---|
| Core | `net8.0;net10.0` | **exactly 2, per-TFM-conditional**: `System.Security.Cryptography.ProtectedData` (8.0.0 / 10.0.0), `Microsoft.Extensions.DependencyInjection.Abstractions` (8.0.2 / 10.0.0) |
| App | `net8.0-windows` | `Microsoft.Extensions.DependencyInjection` |
| Player | `net10.0-android` | `Microsoft.Maui.Controls`, `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Debug` |
| Tests | `net8.0` | xUnit 2.9.2, Test.Sdk 17.11.1, runner.visualstudio 2.8.2, coverlet |

**Core is still at 2 package refs and must stay there.** The multi-target made
them TFM-conditional (net8 majors for net8, net10 majors for net10) so neither
build resolves a mismatched-major assembly. No PDF/Word/Excel/image/git library.
Instead: PDF = browser print; .docx/.xlsx = written by hand via
`System.IO.Compression`; GitHub = REST over `HttpClient`; image dimensions =
parsed by hand. **The Player's MAUI packages do NOT count against Core's limit —
Core stays clean; the MAUI dependencies live only in the Player project.**

### The MAUI player (`QuizBuilder.Player`) at a glance
```
QuizBuilder.Player/
├── App.xaml(.cs)              merges Colors/Styles dicts; registers 3 converters
├── AppShell.xaml(.cs)         single-stack flow; routes: home/take/results
├── MauiProgram.cs             DI: session singleton, importer, email svc, VMs, pages
├── Models/                    TakerIdentity, TakeSession
├── Services/
│   ├── QuizSessionService     THE SPINE: holds identity + loaded quiz + take state;
│   │                          constructs QuizPackageService/QuizCompiler/QuizGrader
│   │                          DIRECTLY (no DI graph, no DPAPI) — the exact sequence
│   │                          pinned by MobileReadPathContractTests
│   ├── QbxImporter            copies .qbx into FileSystem.AppDataDirectory, loads via Core
│   ├── IncomingFileHandler    static bridge for Android VIEW/SEND "open with" intents
│   ├── PlatformUri(.Android)  partial: content:// URI → stream via ContentResolver
│   ├── ResultsEmailService    native mail composer, pre-filled results report
│   ├── InputValidation        email/name validators (GeneratedRegex)
│   ├── BytesToImageSourceConverter   the MAUI per-host image converter (Core stays imaging-free)
│   └── CommonConverters       NotEmpty, InverseBool
├── ViewModels/
│   ├── IdentityViewModel      name+email, inline validate-on-blur
│   ├── HomeViewModel          import + start (Start command UNGATED — see §10)
│   ├── TakeViewModel          one-question-at-a-time nav + submit
│   ├── ResultsViewModel       score + email + restart
│   └── QuestionPresenters.cs  8 per-type presenter classes + SelfList host trick
├── Views/                     Identity/Home/Take/Results pages + QuestionTemplateSelector
├── Platforms/Android/         MainActivity (intent filters), MainApplication, manifest
└── Resources/                 Colors.xaml, Styles.xaml, OpenSans font, icon/splash SVGs
```

---

## 4. ARCHITECTURAL INVARIANTS (do not break these)

These have been re-verified in every audit. Breaking one is a regression.

1. **Core has exactly 2 package references.**
2. **Core is WPF-free.** No `System.Windows`, no `BitmapImage`, no
   `System.Drawing`. This is what makes a MAUI companion app possible (§13).
3. **Tests reference ONLY Core — never App.** All testable logic must therefore
   live in Core.
4. **TFMs:** Core `net8.0` (portable), App `net8.0-windows`, Tests `net8.0`.
5. **Exporters take resolver delegates (`Func<...>`), not services.** They are
   pure functions of their inputs.
6. **Enums persist BY NAME** (`JsonStringEnumConverter`). Store `int` seconds,
   not `TimeSpan`.
7. **`SettingsService.JsonOptions` is the one shared serializer** (camelCase +
   string enums). The polymorphic `Question` (`$kind` discriminator) rides on it
   automatically. Used by .qbx, history, paused attempts, and question bank.
8. **ViewModels never touch WPF imaging types.** `BytesToImageConverter` does
   bytes→`BitmapImage` in the View layer.
9. `System.Text` is **NOT** in Core's ImplicitUsings — add
   `using System.Text;` explicitly when needed.

---

## 5. Domain reference (what the app actually models)

### The 8 question types
All derive from `Question` (abstract, polymorphic `$kind` JSON, `Clone()` mints a
fresh Id via `CopyBaseTo`):

| Class | Notes |
|---|---|
| `MultipleChoiceSingleQuestion` | `Choices` (Text/IsCorrect), one correct |
| `MultipleChoiceMultipleQuestion` | `Choices`, several correct |
| `TrueFalseQuestion` | `CorrectAnswer` bool |
| `ShortAnswerQuestion` | text match |
| `FillInTheBlankQuestion` | blanks kept in sync by `BlankSynchroniser` |
| `MatchingQuestion` | `Pairs` (Left/Right); right column shuffles at compile |
| `SequenceQuestion` | `Items` in correct order; presentation shuffles at compile via `CompiledQuestion.SequencePresentation`; adjacent-pair partial credit. Added 0.25.0 |
| `EssayQuestion` | `RubricNotes`; manual review, not auto-scored |

Base members: `Id`, `Prompt`, `Points`, `Hint`, `ImageRelativePath?`,
`KindDisplayName`.

### The 11 tabs (`NavDestination`)
`QuizBuilder`, `Settings`, `Theme`, `Preview`, `Take`, `StudyCards`,
`FlashCards`, `QuestionBank`, `Publish`, `GitHub`, `Help`

### The 5 built-in themes
Academic, Modern Minimal, Dark Exam, Playful, Corporate
(defined in `Theming/BuiltInThemes.cs`; contrast is unit-tested)

### Core service index (27 services in `QuizBuilder.Core/Services/`)

**Document & storage**
- `QuizDocumentService` — the live `QuizDocument`; all mutations (Add/Move/Remove
  question & section) go through it and raise `DocumentChanged`
- `QuizPackageService` — reads/writes `.qbx`; content-hashed image dedup
- `SettingsService` — `settings.json`; owns the shared `JsonOptions`
- `AttemptHistoryService` — `history.json` (completed attempts)
- `PausedAttemptService` — `paused-attempts.json` (in-progress, 50 cap)
- `QuestionBankService` — `question-bank.json` (reusable pool)
- `AutoSaveService` — periodic save timer

**Quiz mechanics**
- `QuizCompiler` — document + settings → `CompiledQuiz` (selection modes,
  section filter, shuffling)
- `QuizGrader` — scores answers → result
- `AttemptRecordBuilder` — flattens a graded attempt for storage (answers become
  text here)
- `AnswerDescriber` — renders the correct answer as text, per question type
- `PausedAttemptPaper` — rebuilds a `CompiledQuiz` + answers from a snapshot
- `FlashDeck` — builds flash cards from quiz questions or study cards
- `BlankSynchroniser` — keeps fill-in-the-blank `Blank` list in step with
  `{{n}}` tokens in the prompt

**Export / import**
- `HtmlExporter` — printable HTML (PDF via browser print)
- `QuizWebExporter` — self-grading standalone HTML (JS grader + timer)
- `WordExporter` — hand-written OOXML `.docx`
- `ExcelExporter` / `ExcelImporter` — `.xlsx` round-trip for bulk editing
- `QuizSheetSchema` — the shared column layout for the two above
- `ImageDimensions` — parses pixel dims from PNG/GIF/JPEG byte headers
- `DescriptionParser` — inline formatting (bold/italic) in descriptions

**Infrastructure**
- `NavigationService` — tab switching
- `ThemeService` — applies `ThemeTokens` as WPF resources
- `GitHubService` — REST over `HttpClient` (verify token, PUT file, Pages)
- `TokenProtector` / `ProtectedDataShim` — DPAPI or passphrase token storage
  (**Windows-only path — see §13 blocker 3**)

### `AppSettings` shape
Top level: `Version`, `Quiz`, `Theme`, `Publish`, `GitHub`, `Shell`, `AutoSave`,
`Extra`.

`QuizSettings` (the one that drives compilation/grading):
- `GradingScope` — `AllSections` | `SelectAtQuizTime`
- `PassMarkBasis` — `QuestionCount` | `TotalPoints`
- `PassPercentage`
- `SelectionMode` — `AllQuestions` | `ExactCountPerSection` | `TotalCount`
- `QuestionCountPerSection` (Dictionary), `TotalQuestionCount`
- `RandomizeQuestionOrder`, `RandomizeAnswerOrder`
- `TimeLimitMinutes` (`int?`, null = untimed)
- `FlashCardSource` — `Quiz` | `StudyCards` | `Both` (first value is the
  default for older settings files — do not reorder)
- `DefaultPoints` (per question kind)
- `ResultsDisplayMode` — `AfterEachQuestion` | `AtEnd`

---

## 6. The .qbx file format (IMPORTANT for the MAUI app)

A `.qbx` is a plain **ZIP** containing:

```
manifest.json     { FormatVersion: 1, ... }
quiz.json         the QuizDocument (polymorphic questions via $kind)
images/           content-hashed image files (dedup by hash)
```

- `CurrentFormatVersion = 1`.
- Reading a file with a **higher** FormatVersion throws a clear
  "please update" error rather than crashing — forward-compat is already handled.
- Written/read by `QuizPackageService` (async, stream-based).
- No proprietary encoding. Any platform that can unzip + parse JSON can read it.

---

## 7. Feature inventory (all shipped and building)

| Feature | Version | Notes |
|---|---|---|
| Core spine, 8 question types, MVVM shell, themes | 0.1–0.15 | |
| Export PDF / Word / Excel / HTML, xlsx import | 0.1–0.15 | hand-written OOXML |
| GitHub PAT + Pages publish | 0.1–0.15 | REST over HttpClient |
| Images in questions + study cards | ≤0.15 | content-hashed in package |
| Self-grading web export | ≤0.15 | single HTML file, grades offline |
| **#4 Web quiz timer** | 0.16.0 | countdown auto-submits, mirrors app |
| **#1 Choose sections at quiz time** | 0.17.0 | `GradingScope.SelectAtQuizTime` |
| **#2 Question selection modes** | 0.18.0 | AllQuestions / ExactCountPerSection / **TotalCount** (proportional) |
| **#3 Save & continue later** | 0.19.0 | paused-attempt snapshots |
| Removed dead `INavigationAware` | 0.19.1 | cleanup |
| **Question bank** | 0.20.0 | reusable pool, `question-bank.json` |
| **Drag-to-reorder sections** | 0.20.1 | questions already had it |
| **Sequence question type** | 0.25.0 | drag-to-order; bumped `.qbx` FormatVersion → 2 |
| **APK download QR (desktop GitHub tab)** | 0.26.0 b8 | QRCoder; link in desktop settings, not the .qbx |
| **MAUI Android player** | 0.26.0 | read-only take + flash-card review; shares Core |
| ├ Study Cards review (mobile) | 0.26.0 b4 | Core FlashDeck |
| ├ Attempt history (mobile) | 0.26.0 b5 | `history.json` in the app sandbox |
| ├ Pause / resume (mobile) | 0.26.0 b6 | `paused-attempts.json`; back-button offers Pause |
| ├ Formatted description | 0.26.0 b10 | HtmlToText (was showing raw tags) |
| ├ Per-identity history & paused | 0.26.0 b10 | scoped by normalized email; legacy records shown to all |
| └ **Quiz library** | 0.26.0 b12 | kept quizzes; open/import/delete with keep-or-wipe-data |
| **Android CI job** | 0.26.0 b13 | gates the MAUI compile on every push |

### History / paused data model — how deletes and identity interact
Records (attempts and paused sittings) are keyed by **QuizId first, identity
second** (`TakerEmailKey`, a normalized email). The two are independent filters,
which makes the delete behavior predictable:
- **History screen → "Clear all history":** clears only the *currently shown*
  list = this quiz + this signed-in taker (plus legacy no-identity records).
  Other quizzes and other takers are untouched.
- **History screen → delete one attempt:** removes that single record by id.
- **Library screen → "Delete quiz and results":** device-level; wipes that ONE
  quiz's history + paused for **everyone** (all identities), because the quiz
  itself is being removed. Other quizzes untouched.
So: deleting quiz A's history never affects quiz B's; changing the signed-in
email afterwards does not erase anything; loading quiz B still shows quiz B's
history for that email. The only way to lose quiz B's data is to act on quiz B
specifically. (Verified by tracing all three delete paths + modelling.)

### Local data files (all beside the .exe)
- `settings.json` — app + quiz settings
- `history.json` — completed attempts
- `paused-attempts.json` — in-progress sittings (50 cap)
- `question-bank.json` — reusable question pool

### Key design decisions worth preserving

- **Paused attempts snapshot the compiled paper, NOT a seed.** A resumed sitting
  shows exactly what was paused even if the quiz was edited since. The clock
  **stops** while paused, so saving never costs the taker time.
- **Proportional distribution (TotalCount mode)** uses largest-remainder
  (Hamilton) apportionment. **Key proof:** if target ≤ total pool, a section's
  ideal share can never exceed its own pool (that would need target > total), so
  **no capping/redistribution is needed**. Verified over 150,000 random cases:
  exact sum, never exceeds a pool, bigger pool never gets fewer.
- **Section filter applies ONLY under `SelectAtQuizTime`** — a stale selection
  set passed under another scope is ignored, so sections can never be silently
  dropped from a normally-graded quiz.
- **Question bank stores independent clones**, and bank questions are
  **text-only** (images live in a quiz's package; the bank has no package).
  `Clone()` mints a fresh Id via `CopyBaseTo`.

---

## 8. Tooling: build.bat and validate.py

### build.bat — 5 stages
```
[1/5] Cleaning
[2/5] Building
[3/5] Running tests     ← REFUSES to continue if any test fails
[4/5] Publishing portable single-file exe
[5/5] Packaging QuizBuilder vX.Y.Z.B.zip
```
Flags: `--no-test`, `--no-publish`, `--quiet`.
Checks the SDK is on PATH and that the .NET 8 targeting pack exists (else
NETSDK1045) before doing anything.

**The test gate has caught real bugs 6×.** Do not weaken it.

> **Note on docs drift:** `build.bat`'s header comment (previously "as of the
> current slice there is no QuizBuilder.App project") was corrected in b17, and
> README.md was corrected earlier ("slice 1 of 4, no UI yet", "39/39 tests",
> `ThemeService`/`INavigationService` "have no implementations" were all stale).
> Docs have drifted before while the code moved; if you resume after a long gap,
> distrust prose and check the code.

### tools/validate.py — 12 static checks
Each was added after a distinct silent-failure class bit us:

1. `check_xml` — XML/JSON well-formedness
2. `check_batch` — batch `goto` targets exist
3. `check_xaml_classes` — XAML `x:Class` ↔ code-behind
4. `check_pack_uris` — Pack URI ↔ AssemblyName
5. `check_resource_keys` — every `DynamicResource` key is defined ← **caught a real bug in 0.20.0**
6. `check_collating_assertions` — zero-weight-needle assertions
7. `check_itemssource_types` — `ItemsSource` ↔ collection type
8. `check_call_signatures` — Core interface signatures
9. `check_markup_extensions` — XAML markup-extension escaping
10. `check_attached_properties` — attached-property Get/Set pairs
11. `check_datacontext_shadowing` — DataContext shadowing
12. `check_deterministic_newlines` — bans `AppendLine`/`Environment.NewLine` in
    Core (allowlists HtmlExporter/WordExporter)

### Every-delivery checklist
1. Bump `version.json`
2. Update `HelpViewModel` version history honestly
3. Normalise build.bat CRLF:
   `open('build.bat','rb').read().replace(b'\r\n',b'\n').replace(b'\n',b'\r\n')`
4. `python3 tools/validate.py` → must be ALL CHECKS PASSED
5. Zip excluding `bin/ obj/ .vs/ *.user .build-preserve/ node_modules/`
6. **Re-unzip to /tmp and re-run the validator on the SHIPPED copy**
7. `present_files`

---

## 9. ISSUES ENCOUNTERED — the recurring failure pattern

**The domain logic has been consistently clean. The AI's own scaffolding is what
breaks.** Every build failure in this project came from the assistant's helper
code, checkers, or object initializers — never from a wrong algorithm. This
creates *false confidence*: the checks pass, then the real compiler disagrees.

### Actual build failures (all caught by the user's Windows build)

| Failure | Cause | Lesson |
|---|---|---|
| `CS8622` nullability | `GetImage` param needed widening to `string?` | — |
| Deleted `Letter()` method | bad `str_replace` anchor (used a method body as the anchor) | anchor on unique signatures |
| Small JPEGs rejected | `ImageDimensions` had a flat 24-byte floor | per-format bounds (PNG 24, JPEG 4) |
| `CS0535` CountingCompiler | changed `IQuizCompiler` signature; checked **callers** but not **implementers** | **when changing an interface, check IMPLEMENTERS too** |
| `CS9035` missing required members | built `CompiledQuiz` outside the compiler, missed `PassPercentage` + `PassMarkBasis` | **when constructing a type with `required` members outside its usual home, enumerate ALL required members** |
| `xUnit2029` warning | `Assert.Empty(x.Where(...))` | use `Assert.DoesNotContain(x, pred)` |
| Pause/resume showed the answer | `PausedAttemptPaper` carried `MatchingOptions` but not `SequencePresentation`; a resumed sequence fell back to correct order | **when adding a presentation projection, persist it through the paused snapshot too** |
| Word answer-key test wrong | test expected `->` but Word XML-escapes `>` to `-&gt;` | assert on the escaped form; escaping is correct behaviour |
| Excel 1-item-sequence test wrong | assumed `Success==true` for an all-skipped file | an all-skipped import reports failure; pair the bad row with a valid one |
| CI red on first run | Restore step named **two** projects on one `dotnet restore` (MSB1008) | restore the Tests project only (Core comes via project ref); never build the Windows App on the Linux runner |

**NEW THIS ARC — bugs caught by RUNNING tests in-sandbox, not just compiling.**
The pause/resume answer-leak and both test-logic errors were caught by actually
executing the suite in the sandbox (see §11) before the user pushed. This is a
meaningfully stronger net than the old "compiles clean" bar. Prefer it.

### Self-checks now in the pre-package routine
1. **Unresolved-local-call scan** per edited file.
2. **Interface-implementer check** when changing any interface (greps every file
   referencing it — this is what would have caught `CountingCompiler`).
3. **Required-member check** when constructing a type with `required` fields
   outside its usual home (would have caught `CompiledQuiz`).

**Note on false positives:** these greps routinely flag BCL types, constructors,
and hand-built nested VMs (`QuestionRowViewModel`, `TakeChoiceViewModel`). Verify
each flag before "fixing" it — several audits produced flags that were correct
code.

### Deliberately NOT added
Validator checks for compiler-caught classes (CS8622, symbol resolution,
required-members) were considered and **rejected as fragile over-reach**. The
compiler is the right tool for those; the validator covers what the compiler
cannot see (XAML keys, batch labels, binding names).

---

## 10. Code-level traps (these cost real debugging time)

Things that are not obvious from reading the code and have bitten before.

### Raw-string interpolation braces
Both exporters emit CSS/JS, which is full of `{` and `}`.

- `HtmlExporter.Css()` and `QuizWebExporter.Css()` use **`$$"""`** (interpolated).
  In these, literal braces must be **doubled**: `{{` and `}}`. Interpolation
  holes are `{{Num()}}`.
- `QuizWebExporter.GraderScript()` uses a **plain `"""`** raw string. Braces and
  `${...}` are **literal** — do NOT double them. This is JS template-literal
  territory and doubling would corrupt it.

Getting this backwards produces output that looks fine in C# and is broken in the
browser. Check which form a method uses before editing it.

### `RelayCommand.RaiseCanExecuteChanged()` is STATIC
It calls `CommandManager.InvalidateRequerySuggested()`, which re-queries **every**
command. There is no per-command refresh. Call it once after a batch of state
changes, not in a loop.

### (MAUI) CommunityToolkit `[RelayCommand(CanExecute=…)]` caches — a mobile trap
Different toolkit from the WPF one above. In `QuizBuilder.Player`, a generated
`RelayCommand` caches its `CanExecute` result and only re-evaluates when
`NotifyCanExecuteChanged` fires. If the `CanExecute` predicate depends on state
that is set **during a one-time refresh** (e.g. `QuestionCount` set once at
import), the initial evaluation can run while that state is still default, and
the command latches disabled forever — this was the real "Start Quiz does
nothing" bug. **Rule for the player: do not gate a command with a computed
`CanExecute` over refresh-set state; drop the gate and validate inside the
command body instead.** (Commands that gate on a property which changes *after*
load, like the take-screen `Index`, are fine because the change fires the notify.)

### Deterministic newlines in Core
`validate.py` check #12 **bans** `StringBuilder.AppendLine()` and
`Environment.NewLine` in Core, because they emit `\r\n` on Windows and `\n`
elsewhere — which broke tests that asserted on exact output.
**Allowlisted exceptions:** `HtmlExporter` and `WordExporter` (their output is
markup where the platform newline is harmless).

### `System.Text` is not implicit in Core
Core has ImplicitUsings on, but `System.Text` is **not** in the default set.
`StringBuilder` needs an explicit `using System.Text;`.

### JSON `<` escaping is a feature, not a bug
The default `System.Text.Json` encoder escapes `<`, `>`, `&`. This is why
embedding `quiz.json` inside a `<script>` tag in the web export is safe. Do not
"fix" it with `UnsafeRelaxedJsonEscaping`.

### Enum ordering matters twice
Enums persist **by name** (`JsonStringEnumConverter`), so reordering will not
corrupt saved files. **But** the first value is `default()` for any settings file
missing that key. `FlashCardSource.Quiz` and `PassMarkBasis.QuestionCount` must
stay first to preserve existing behaviour.

### `MoveSection` / `MoveQuestion` clamp their index
Both use `Math.Clamp`, so drag-and-drop dropping past the last row is safe and
dropping onto self is a no-op. Callers do not need to guard.

---

## 11. The AI working environment (for the next session)

This is what the assistant has available — useful for planning verification.

- Working dir: `/home/claude/QuizBuilder/`
- Deliverable path: `/mnt/user-data/outputs/QuizBuilder.zip`
- **Node v22 with jsdom** in `/home/claude/node_modules` — this is how the web
  export's JS grader and countdown timer were verified (build a fake DOM, run
  the exported HTML's script, assert on results). Reuse this for any further
  web-export work.
- Python 3 — used for logic models before writing C#, and for `tools/validate.py`.
- **A .NET 8 SDK CAN be bootstrapped in-sandbox** (discovered this arc): apt has
  `dotnet-sdk-8.0`; `apt-get download` the debs + deps and `dpkg-deb -x` them
  into a prefix. NuGet is still blocked, so the two Core package DLLs are
  referenced directly from the SDK folder, and Core is compiled with `csc`
  against the net8 ref pack. Tests were run via a hand-written xUnit stub +
  reflection runner (`Fact`/`Theory`/`Assert`). **This let 59 tests actually
  RUN and pass in-sandbox before the user pushed** — a big step up from
  "compiles". The WPF App still cannot be built here (net8.0-windows). Reuse
  this approach for Core/Tests verification.
- `api.github.com` and the Ubuntu apt feed are reachable; most other domains
  (incl. nuget.org) are not.

---

## 12. Last full audit result (v0.20.1)

All 7 checks clean:
1. DI graph — 55 registered types, all constructor deps resolve
2. Every interface has exactly one implementation and is registered
3. All XAML bindings resolve (row-scoped and `DataContext.X` flagged items were
   false positives)
4. Core invariants hold (2 refs, Tests≠App, no WPF in Core, correct TFMs)
5. All features wired end-to-end: setting → compiler/exporter → UI
6. Startup `Load()` called for settings, history, paused attempts, question bank
7. Validator 12/12 green; build.bat gates on tests

---

## 13. MAUI companion app (Android — SHIPPED; iOS deferred)

> **STATUS UPDATE (v0.26.0 — Android player IMPLEMENTED).**
> The Android read-only player now exists as `QuizBuilder.Player`
> (`net10.0-android`, MAUI 10). It imports a `.qbx`, captures the taker's first
> name / last name / email, presents all eight question types touch-first,
> grades via Core, and emails a formatted result report to the entered address
> through the device's native mail composer. The three planned blockers below
> are resolved (see the "How the blockers were resolved" note at the end of this
> section). iOS remains deferred (needs a Mac). What follows is the original
> plan, kept for reference and for the iOS work still to come.
>
> **Build it:** `build-android.bat` (Debug APK, installs directly) or
> `build-android.bat release` (signed APK + AAB when `QB_KEYSTORE*` env vars are
> set). PowerShell entry point is `build-android.ps1`. Requires the
> `maui-android` workload: `dotnet workload install maui-android`.
>
> **Not yet verified by a real build** — written without a local .NET 10 /
> Android toolchain, so the first Windows build is the real gate, exactly as the
> critical constraint in §2 demands. The Core→net10 multi-target and the
> compose path are the two things to watch first; the compose path mirrors
> `MobileReadPathContractTests`, which is already green on desktop CI.

**Goal:** a read-only "player" app that imports a `.qbx` and lets the user take
quizzes and flip flash cards on a phone.

### Why this is genuinely feasible
`QuizBuilder.Core` is portable `net8.0` and **WPF-free**. A MAUI project can
reference it directly, so the phone reuses the *same* grading and compilation
logic as the desktop — the two agree by construction rather than by careful
re-implementation.

### Confirmed platform facts (verified against Microsoft docs, 2026)
- MAUI targets **Android, iOS, Mac Catalyst, and Windows** from one C# codebase.
- Minimums: **Android 5.0 (API 21)+**, **iOS 12.2+**.
- MAUI ships in lockstep with .NET (MAUI 10 ↔ .NET 10).
- **A Mac is required to build for iOS** (Apple's rule, any framework).
  Android builds fine on Windows alone.
- Apple's "26" generation (iOS 26 / Xcode 26) is **fully supported on .NET 10**;
  .NET 9 only has partial/servicing support. **Target .NET 10 / MAUI 10.**
- iOS distribution needs an Apple Developer account (~$99/yr).

### Reuse vs. build-new

**Reuse essentially as-is from Core:**
- `QuizPackageService` — reads .qbx (already async/stream-based)
- `QuizCompiler` — selection modes, section filtering
- `QuizGrader` — scoring (**the important one:** identical results across devices)
- `FlashDeck` — flash-card building
- `Question` model + polymorphic JSON

**Must be written new for mobile:**
- Touch-first pages: import → quiz list → take quiz → flash cards
- Image loading (desktop's `BytesToImageConverter` is WPF-specific; MAUI needs
  its own — small)
- File-import UX (share sheet, cloud drive, email attachment)
- Timer *display* (the countdown logic is portable; only the UI is new)

### Known blockers to resolve early

1. **TFM alignment.** Core is `net8.0`; MAUI 10 wants `net10.0`. Either bump Core
   or multi-target it. Mechanical, but verify — do not assume.

   **DECISION (2026, deferred deliberately):** stay on `net8.0` for now; make Core
   `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` (plural) *when MAUI work
   begins*, not before. Rationale: the desktop app keeps building on `net8.0`
   while MAUI gets `net10.0` from the **same source** — a side-by-side, verifiable
   move rather than a big-bang cutover. Once MAUI is stable, drop `net8.0` and put
   the WPF app on `net10.0` too (one line). **net8 EOL is Nov 2026**, so this must
   happen before then regardless — but there is runway, and no capability in net10
   is needed by a local WPF tool, so there is no reason to rush it ahead of MAUI.

   **Audit already done:** Core was scanned for anything blocking a newer TFM —
   no WPF/Drawing/Win32/Registry, no removed APIs (BinaryFormatter/WebClient/etc.),
   reflection is version-safe and avoids `Assembly.Location` (single-file safe).
   The one Windows dependency (DPAPI, blocker 3) is isolated behind a guarded shim,
   and its package publishes for 9.x/10.x. **The multi-target is expected to be a
   one-line change; the audit found no code migration needed.** See also
   `MobileReadPathContractTests` — pins the load/compile/grade + sandbox-storage +
   no-DPAPI path so a Core change that breaks mobile fails on desktop CI.

2. **Storage paths — the biggest one.** Four Core services write beside
   `AppContext.BaseDirectory`, which has no equivalent on a phone:
   - `SettingsService` → `settings.json`
   - `AttemptHistoryService` → `history.json`
   - `PausedAttemptService` → `paused-attempts.json`
   - `QuestionBankService` → `question-bank.json`

   Each already takes an **optional `overrideDirectory` constructor parameter**
   (used by the tests), so the seam exists — a MAUI host can pass
   `FileSystem.AppDataDirectory`. **Verified present on all four.** Two details:
   - `SettingsService` also takes a `TokenProtector` (see blocker 3).
   - `AttemptHistoryService` calls `GetExecutableDirectory()`, but that is a
     one-line wrapper returning `AppContext.BaseDirectory` — so all four resolve
     identically. No inconsistency; do not "fix" it.

3. **⚠️ Core is WPF-free but NOT fully platform-neutral.**
   `TokenProtector` / `ProtectedDataShim` use **Windows DPAPI**
   (`System.Security.Cryptography.ProtectedData` — one of Core's 2 packages).
   It is already guarded: the DPAPI call sits inside `if (OperatingSystem.IsWindows())`
   and throws a clear `CryptographicException` ("Machine-bound protection
   requires Windows. Choose passphrase mode instead.") elsewhere, so it
   **degrades gracefully rather than crashing** — there is a passphrase mode.

   For a player app this is likely a non-issue: token protection exists only for
   the GitHub publish feature, which a read-only mobile app does not need. But
   the type is still *referenced* from Core, so either accept the dependency,
   trim it, or keep GitHub publishing out of the mobile build.

4. **Authoring is out of scope.** Read-only player is the right split:
   build on desktop, take on mobile.

### Recommendation
**.NET MAUI, read-only player, sharing `QuizBuilder.Core`, targeting .NET 10.**

**Start this in a NEW CHAT.** Bring: this handoff, the .qbx format (§6), and the
"Core is portable/WPF-free" invariant. The new chat will also need the same
constraint acknowledged: the assistant cannot build or run a MAUI app either, so
the same write → verify-by-model → user-builds loop applies, with the added
wrinkle that mobile UI is much harder to verify statically than pure logic.

### How the three blockers were resolved (v0.26.0)

1. **TFM alignment.** Core is now `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`.
   The two package refs (`ProtectedData`, `DI.Abstractions`) are TFM-conditional
   so net8 stays byte-for-byte identical and net10 pulls matching-major
   assemblies. Desktop App (`net8.0-windows`) and Tests (`net8.0`) reference Core
   by project path and auto-resolve its net8 output — no change to them. The
   player's `net10.0-android` resolves Core's net10 output. The `.sln` lists the
   player with `ActiveCfg` but **no `Build.0`**, so `dotnet build` on the desktop
   CI runner (no MAUI workload) skips it and stays green; an Android CI job with
   the workload builds it explicitly.

2. **Storage-path adaptation.** The player never lets Core write "beside the exe".
   `QbxImporter` copies the imported `.qbx` into `FileSystem.AppDataDirectory`
   (the app sandbox) and hands Core that path. Core's `QuizPackageService`
   already reads from a supplied `filePath` and holds images in-memory
   (`GetImage`), so nothing in Core needed the `overrideDirectory` seam for the
   read-only player.

3. **DPAPI / `TokenProtector`.** Sidestepped entirely. The player constructs only
   `QuizPackageService`, `QuizCompiler`, and `QuizGrader` directly — the exact
   trio in `MobileReadPathContractTests` — none of which touch `TokenProtector`,
   `SettingsService`, or the DI graph that would pull in DPAPI. `ProtectedData`
   is still referenced by Core (so it compiles) but its Windows path is never
   taken on Android.

**Mobile-new pieces actually written:** touch-first pages (identity → home/import
→ take → results); a MAUI `BytesToImageSourceConverter` (the promised small
per-host converter); file-import UX via `FilePicker` + Android VIEW/SEND intent
filters (`IncomingFileHandler` + `PlatformUri` ContentResolver opener); eight
per-type answer presenters behind a `DataTemplateSelector`; and a native-mail
results composer (`ResultsEmailService`). Design tokens follow the UI/UX Pro Max
rules (semantic light/dark brushes, 48dp targets, one CTA per screen, inline
validate-on-blur). Flash-card review was **not** built this pass — the player
covers quiz-taking only; study-card review is the obvious next mobile slice.

### Mobile status after first device run (v0.26.0)

The player builds green, deploys to an Android emulator, and runs end to end:
identity capture → `.qbx` import → take (all eight question types) → grade via
Core → results. Scoring is Core's, so it matches desktop by construction. This
proves the whole shared-Core premise on-device.

Toolchain notes learned the hard way (all now handled by `build-android.ps1`):
the machine needs a **stable .NET 10 SDK** with `global.json` pinned to it (a
.NET 11 preview SDK pulls a mismatched net11 workload band); the **maui-android
workload**, the **Android SDK** and a **JDK** are separate installs the workload
does not bundle (the script detects all three and the API-36 platform, and
accepts licences); and a hand-installed **debug APK crashes** with "No
assemblies found … Fast Deployment" unless built with
`-p:EmbedAssembliesIntoApk=true` (the script now does this) or deployed via
`dotnet build -t:Run`.

**Bug fixed on-device:** the Android back button used to pop the take page and
destroy the in-progress `TakeSession`, restarting the quiz. `TakePage` now
overrides `OnBackButtonPressed` to confirm before leaving, and hides the Shell
nav bar so there is no stray back arrow. (This is a stopgap; real pause/resume
below supersedes it.)

### Every fix applied this session (chronological — the build-up to a running app)

Environment/toolchain (all now automated in `build-android.ps1`, so they are
"solved" but recorded here because they WILL recur on a fresh machine):
1. **SDK band mismatch.** Machine had only a .NET 11 preview SDK; `global.json`
   rolled forward to it, so `maui-android` pulled a net11 preview workload band
   against a `net10.0-android` project. **Fix:** installed the stable .NET 10 SDK
   and pinned `global.json` to `10.0.100` (`rollForward: latestFeature`,
   `allowPrerelease: false`). Canary: `dotnet --version` in the repo folder must
   print `10.0.x`.
2. **Concurrent-installer lock (`0x00000652`).** Running the SDK installer and
   the workload install at once wedged Windows Installer. **Fix:** reboot, then
   run installers one at a time.
3. **Missing MAUI workload / Android SDK / JDK / API-36 platform** — four
   separate "XA5300 / XA5207" failures, each a piece the workload does not
   bundle. **Fix:** the script now detects the Android SDK
   (`%LOCALAPPDATA%\Android\Sdk`), the JDK (Android Studio's `jbr`), and the
   API-36 platform (auto-installs via `InstallAndroidDependencies`), passing
   `AndroidSdkDirectory`/`JavaSdkDirectory` explicitly and accepting licences.
4. **Brittle workload detection** in the script gave a false "not installed"
   right after a successful install. **Fix:** read
   `dotnet workload list --machine-readable` (JSON) with a text-scan fallback;
   added a `-SkipWorkloadCheck` escape hatch.
5. **Hand-installed debug APK crashed** with "No assemblies found … Fast
   Deployment. Exiting." **Fix:** the debug build now sets
   `-p:EmbedAssembliesIntoApk=true` so the APK is self-contained; alternatively
   deploy with `dotnet build -t:Run` (see commands below).

Code fixes (real compiler/runtime issues in the new player):
6. **`--` inside XML comments** in `QuizBuilder.Core.csproj`, `Colors.xaml`, and
   (missed on the first sweep) `AndroidManifest.xml` — illegal XML, breaks the
   build. **Fix:** replaced with em-dashes; **widened `validate.py`'s XML check
   to cover `.xml`/`.manifest`** so this class can never reach a build again.
7. **CS8796** — value-returning partial method `PlatformUri.OpenReadPlatformAsync`
   lacked an accessibility modifier. **Fix:** `private static partial` on both
   halves.
8. **CS0103** — `MainActivity` referenced `IncomingFileHandler` without
   `using QuizBuilder.Player.Services;`. **Fix:** added the using.
9. **CS0618** — `Page.DisplayAlert` is obsolete in MAUI 10. **Fix:** switched to
   `DisplayAlertAsync`.
10. **CS8604** — possible null `uri.ToString()` passed to `OfferAndroidUri`.
    **Fix:** null-guard the string before the call.
11. **Start Quiz button dead** (the on-device bug). The `[RelayCommand(CanExecute=
    nameof(CanStart))]` gate cached its result from when `QuestionCount` was still
    0 (before import completed), latching the button disabled. **Fix:** removed
    the `CanExecute` gate entirely and validate *inside* `StartAsync` (immune to
    notification timing, and it tells the user why if a quiz has no questions).
    **General lesson for the next session:** avoid computed `CanExecute` that
    depends on state set during a one-time refresh; validate in the command body.
12. **Android back button destroyed the attempt** (see paragraph above).

All code fixes are in the tree and the shipped `QuizBuilder_v26.zip`. The desktop
build stayed green throughout (Core multi-target verified:
`Core net8.0`, `Core net10.0`, `Tests net8.0`, `App net8.0-windows` all build).

**✅ RESOLVED in build 3 — kept for history.** The fix landed and is NOT
either candidate below; see §0 "Build 3 — CI test-isolation fix." Short version:
the real cause was xUnit **parallelism** (the collection is the unit of
parallelization; classes default to their own collection and run concurrently),
not ordering. The fix puts every `ProtectedDataShim`-mutating class into one
shared collection (`ProtectedDataShimCollection`) so they serialize and the
throwing shim can never be active during `MachineBound_RoundTripsToken`. The
MachineBound test keeps Linux coverage via its XOR shim (candidate (b), skipping
off-Windows, would have thrown that coverage away — rejected). Original note
follows for context.

**⚠️ LATENT CI FLAKE — fix early in the next session.** CI (`core-tests`,
ubuntu-latest) is **green as of the latest push (611–612/612 depending on test
ordering)**, but there is a real **test-isolation bug** that makes one test
non-deterministic: `TokenProtectorTests.MachineBound_RoundTripsToken` can fail
with `PlatformNotSupportedException: No DPAPI on this platform`. It flaked RED on
one push this session, then passed on the next with no code change — that is the
tell. **Not a product bug, not caused by the MAUI work.**
`MobileReadPathContractTests` installs a **process-global DPAPI-shim test
double** (one that throws "No DPAPI on this platform" to prove the mobile path
never calls DPAPI). Depending on xUnit's test ordering, that global can leak into
`MachineBound_RoundTripsToken`, which genuinely needs DPAPI, and it throws. DPAPI
is Windows-only, so this test never belonged on a Linux runner at all. Two valid
fixes (read both test files first): (a) scope the shim override in
`MobileReadPathContractTests` so it is reset in a finally/Dispose and cannot
leak; or (b) guard `MachineBound_RoundTripsToken` to skip off-Windows
(`OperatingSystem.IsWindows()` or `[SkippableFact]`) — **(b) is likely the
correct fix** since the assertion is Windows-specific. Green today ≠ fixed;
make it deterministic before it flakes on a push that matters. Existed latent
before v0.26.0; the multi-target changed test ordering enough to expose it.

**Exact commands used to build and deploy to an Android emulator** (so the next
session doesn't re-derive them):

```powershell
# One-time PATH setup so 'adb' resolves (platform-tools ships with the SDK):
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$env:LOCALAPPDATA\Android\Sdk\platform-tools", "User")
# (reopen the shell afterwards)

# Confirm the emulator is up and visible before install/push:
adb devices          # want: emulator-5554   device

# FAST DEV LOOP — build, deploy (both APK + assemblies), and launch in one step.
# This is the reliable path; it handles Fast Deployment so the app actually runs:
dotnet build .\QuizBuilder.Player\QuizBuilder.Player.csproj -t:Run -f net10.0-android -c Debug `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" `
  "-p:JavaSdkDirectory=C:\Program Files\Android\Android Studio\jbr"

# STANDALONE APK — build a self-contained APK, then sideload it by hand.
# build-android.ps1 already sets EmbedAssembliesIntoApk=true, so this APK runs
# when installed directly (without it, a hand-installed debug APK aborts with
# "No assemblies found … Fast Deployment"):
.\build-android.bat
adb install -r ".\QuizBuilder.Player\bin\Debug\net10.0-android\com.severdthumbz.quizplayer-Signed.apk"

# Push a .qbx onto the emulator so the in-app file picker (Downloads) can see it:
adb push "C:\path\to\Some Quiz.qbx" /sdcard/Download/

# When the app crashes on launch, capture the managed exception:
adb logcat -c        # clear, then reproduce the crash, then:
adb logcat -d > crash.txt
Select-String -Path crash.txt -Pattern "AndroidRuntime|Unhandled managed|at QuizBuilder" | Select-Object -First 40
```

The emulator itself needs the **Windows Hypervisor Platform** enabled (Device
Manager shows a red "Enable" banner otherwise) and a **reboot**; prefer a
**stable API 34/35 image** over a preview (the machine had an API 37 "CinnamonBun"
preview image, which mixes app bugs with preview-OS bugs). The emulator usually
has **no mail app**, so the email-results composer will report "no mail app" —
test that feature on a real phone.

### NEXT MOBILE SESSION: tier-1 offline features (✅ ALL BUILT & CONFIRMED — builds 4–6)

**Status: complete and confirmed on-device.** All three (Study Cards b4, history
b5, pause/resume b6) build in CI (the android-build job) and the maintainer has
run them on the emulator. The back-button "pause instead of leaving" follow-on is
also done (b6). The original spec is kept below as the record of what was asked
and how Core supported it.

Four things were requested after the first run. The back-button bug is done. The
other three are real features for a **fresh chat** (bring HANDOFF.md). The
data-location question was decided: **tier 1 — local on the device only.** No
backend, no accounts; matches the app's offline/no-cloud invariant, and the
existing email-results feature already covers getting a result to an instructor.
(Tier 2 = add file export/import to bridge devices; tier 3 = cloud sync, a
separate product with a backend/auth/privacy surface — explicitly out of scope.)

The happy news: **Core already models all of this.** The work is wiring, not
design. Build order:

1. **Study Cards review screen.** Terminology: "Study Cards" (NOT "Flip Cards").
   The `.qbx` already carries them; the desktop `StudyCardsView` is the
   reference. Add a home-screen choice ("Take Quiz" vs "Review Study Cards") and
   a card-flip UI. Self-contained; no persistence needed for a first cut.

2. **Attempt history (local).** Core has it ready:
   - `IAttemptHistoryService` / `AttemptHistoryService(string? overrideDirectory)`
     — construct it with `FileSystem.AppDataDirectory` and it writes
     `history.json` to the sandbox. Same `overrideDirectory` seam SettingsService
     uses; no Core change needed.
   - `AttemptRecordBuilder.Build(quizId, quizTitle, result)` converts the
     `AttemptResult` the player already produces into a storable `AttemptRecord`.
     So after grading: `history.Add(AttemptRecordBuilder.Build(doc.Id, title, result)); history.Save();`
   - `AttemptRecord`/`AttemptQuestionRecord` are deliberately plain get/set (not
     `required`) for forward-compatible reading — do not "tidy" that.
   - New work: register the service (with the sandbox dir) in `MauiProgram`, call
     it on the results screen, and add a history list screen (per quiz via
     `ForQuiz(quizId)`, newest first) with a per-attempt detail view.

3. **Pause / resume.** Core has `IPausedAttemptService` /
   `PausedAttemptService(string? overrideDirectory)` writing
   `paused-attempts.json`, plus the `PausedAttempt`/`PausedSection`/
   `PausedQuestion` snapshot model — self-contained (stores the presented paper,
   shuffled matching options, sequence presentation, and answers so far), so a
   resumed sitting shows exactly what was paused even if the quiz is later
   edited. New work: a "Pause" action on the take screen that snapshots the
   `TakeSession` into a `PausedAttempt` and saves it; a "Resume" entry on the
   home/quiz screen that rebuilds a `TakeSession` from the snapshot; and make the
   back-button confirm dialog offer "Pause instead of leaving" once this exists.
   The clock is time-spent (seconds), not wall-clock — pausing must not cost the
   taker time.

Mapping mobile ↔ Core snapshot needs care: the player's `TakeSession`/
`QuestionPresenter` layer must translate to/from `PausedQuestion.Answer`
(`QuestionAnswer`) and carry `MatchingOptions`/`SequencePresentation` so a
resumed matching/sequence question keeps its shuffle (otherwise resume hands the
taker the correct order). Model this in Python first, like the grading port.

**Still to do beyond tier 1:** iOS target (needs a Mac); an Android CI job that
installs `maui-android` and runs `build-android.ps1`; replace the placeholder
app icon/monogram; test the email-results composer on a real phone (the emulator
usually has no mail app).

---

## 14. Other candidate features (not started)

Ranked by value, from the competitor analysis:

1. **Drag-and-drop ordering/sequence question type** — highest value. Closes a
   real gap vs. iSpring (14 types vs. our 7), reuses the grading model, and
   works on desktop + web export + future mobile.
2. **More question types** — hotspot (click an image region), numeric (answer
   within a tolerance).
3. **Question bank import/export** — share or back up the bank; builds directly
   on the existing JSON store.
4. **Question bank images** — the deliberate limitation noted in §7 (Feature inventory).
5. **Multiple tags / difficulty** on bank entries (currently one free-text
   category).
6. **Printable answer key** — a graded key alongside the quiz export.
7. **Audio/video in questions** — bigger lift; competitors have it.

### Spec'd but never built (old backlog)
- `push-to-github.bat` + `.ps1`
- A Windows job in the CI workflow

---

## 15. Working style that has worked

- User is terse, approves fast, and pastes build logs back.
- AI ports tricky logic to Python/JS and **runs it** before writing C#.
- AI is candid that its own checkers are a false-confidence source.
- AI owns mistakes plainly, without over-apologising, and states what process
  change prevents a repeat.
- Every delivery ends with `present_files` on the zip and a short, honest summary
  of what was verified vs. what only the Windows build can prove.

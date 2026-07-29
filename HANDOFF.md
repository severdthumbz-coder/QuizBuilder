# Quiz Builder — Project Handoff

**Last shipped:** v0.25.0 build 1 (stage `sequence-question`)
**Status:** Builds green on Windows. Live on GitHub with green CI (both jobs).
**Deliverable:** `/mnt/user-data/outputs/QuizBuilder_v25.zip`

This document exists so a new chat can resume without re-reading the whole
history. Read §0 first for current state, then the rest as reference.

---

## 0. CURRENT STATE & RECENT WINS (read this first)

### Where the project is right now
- **Version 0.25.0** — the **Sequence question type** is complete, shipped, and
  verified in real CI. The app now has **8 question types** (see §5).
- **On GitHub for the first time.** Repo: `severdthumbz-coder/QuizBuilder`,
  branch `main`. The push-first workflow (add/commit/push → wait for the green
  checkmark → build locally) is now live for this project, matching the user's
  other repos (Video Metadata Editor, TimerSuite, FileOrganizer).
- **CI is green on both jobs** (`.github/workflows/build.yml`):
  - `core-tests` (ubuntu) — restores/builds/tests the Tests project (Core comes
    via project reference); runs the full xUnit suite with the *real* runner.
  - `app-build` (windows) — publishes the WPF app as a self-contained
    single-file exe. Previously never run; **now confirmed working.**

### The Sequence question type (the headline feature of this arc)
A taker drags items into the correct order; scoring is **adjacent-pair partial
credit** (each correctly-ordered neighbouring pair earns a share of the points,
so one misplaced item does not fail the whole question).
- **Model:** `SequenceQuestion` with `Items` in correct order (the answer key).
- **Presentation:** the shuffle the taker sees is a *projection* on the compiled
  question — `CompiledQuestion.SequencePresentation` (a permutation of item
  indices). The model's `Items` are never reordered. Mirrors how
  `MatchingOptions` works. Guard: **never the identity permutation for n≥2** when
  randomising (Fisher-Yates with a rotate-by-one fallback). With randomisation
  off, presents in correct order (author's choice, like fixed-order matching).
- **Wired end-to-end (all 8 App integration points + Core):** grader, compiler,
  HTML/Word/Excel/self-grading-web exporters, Excel import, preview, editor VM +
  DataTemplate (with move up/down), take-view drag UI (reuses the existing
  `ListReorderDropTarget`, so the drag-down off-by-one fix is inherited),
  picker, `CreateQuestion`, editor factory, settings default-points row, and
  **pause/resume persistence** (`SequencePresentation` is saved in the paused
  snapshot and restored — without it a resumed sequence would show the answer).
- **Design decisions locked in:** min 2 items (editor seeds 3 empty rows);
  RandomizeAnswerOrder off → present correct order; an untouched sequence counts
  as **unanswered → scores 0** (consistent with every other type).

### Format version bump — IMPORTANT
`.qbx` `FormatVersion` is now **2** (was 1). The load gate rejects files
*newer* than the build understands, so a v0.25 file will not open in v0.24 or
earlier — even a quiz with no sequence question. This is the deliberate
unconditional-bump tradeoff. A v1 file still opens in v0.25 (proven, see below).

### Test suite additions this arc (all run in CI with real xUnit)
- `SequenceQuestionTests` — grader: all permutations, malformed input, results-
  screen rendering via `AttemptRecordBuilder` (that builder had had **zero**
  tests before; now covered).
- `SequencePresentationTests` — compiler shuffle/guard, every exporter, Excel
  round-trip, pause/resume presentation survival.
- `PackageBackwardCompatTests` — a hand-built **v1 `.qbx` still opens** in the
  v2 build, keeps its reported version (reading ≠ upgrading), preserves content
  and images; a current save is stamped v2.
- `MobileReadPathContractTests` — pins the exact Core slice the future MAUI
  player depends on (load → compile → grade with **storage sandboxed** and
  **DPAPI disabled**), so a Core change that breaks mobile fails on desktop CI.

### GitHub hygiene note (learned the hard way)
The first push accidentally committed ~40 build-snapshot zips (~814 MB). Fixed by
un-tracking (`git rm --cached "*.zip"`), adding `*.zip` to `.gitignore`, and
`git commit --amend` + `git push --force` on the single fresh commit (safe: solo
repo, no other clones). Repo dropped from 814 MB to ~420 KB. **Build zips live in
a sibling folder outside the repo now, never in the tree.** If build artifacts
should be kept *with* the repo, the right home is GitHub **Releases**, not commits.

### MAUI readiness (the planned next chapter — see §13 for detail)
All three documented blockers are de-risked:
1. **TFM** — Core is `net8.0`; MAUI wants `net10.0`. **DECISION: stay on net8
   now, multi-target `net8.0;net10.0` when MAUI begins, drop net8 after MAUI is
   stable (before net8 EOL, Nov 2026).** A full audit found Core is
   multi-target-ready with **no code migration** — no WPF/Drawing/Win32, no
   removed APIs, reflection is single-file-safe.
2. **Storage** — all four data services already take an `overrideDirectory`
   seam; MAUI passes a sandbox path. Guarded by `MobileReadPathContractTests`.
3. **DPAPI** — isolated behind `ProtectedDataShim`/`TokenProtector`; degrades to
   a clean, catchable error off-Windows; None/Passphrase modes are cross-platform.

### Immediate next-step options
- **MAUI/Android player** — the big one. Start in a **fresh chat with this doc.**
  First step there is literally `dotnet new maui` and reading the template's
  `Platforms/Android` structure — do not pre-create folders.
- **Android CI job** — a third workflow job producing an `.apk`/`.aab` as a
  build **artifact** (not committed); needs the keystore as an encrypted secret.
  Belongs *after* the MAUI project exists.
- **CI housekeeping** — bump `actions/checkout` and `actions/setup-dotnet` off
  the deprecated Node 20 (yellow warning only, not failing).
- **More question types** — considered (iSpring comparison). Verdict: numeric and
  dropdown-lists are low-cost/high-value if wanted; the drag family (hotspot,
  drag-drop) is expensive and hits the least-verifiable surface; Likert = survey
  mode, a different concept. **Reach (MAUI) was judged higher value than breadth.**

---

## 1. What this is

A portable Windows desktop **Quiz Builder**: WPF / .NET 8 / C# / MVVM. An author
builds quizzes, takes them, and exports them. Everything is local — no cloud, no
account, no LMS.

**Portability is a hard requirement, not a nice-to-have.** Settings and all
stored data are written *beside the .exe* (never AppData, never the registry).
The app publishes as a single self-contained file.

### How to build and run it

```
build.bat                 clean, build, test, publish, package
build.bat --no-test       skip tests
build.bat --no-publish    build + test only
build.bat --quiet         don't prompt to launch at the end
```

Requires the **.NET 8 SDK** (or a newer SDK *plus* the .NET 8 targeting pack —
`global.json` rolls forward). The script checks both up front and gives a clear
message instead of letting MSBuild emit NETSDK1045.

Output: `publish\QuizBuilder vX.Y.Z.B.exe` — self-contained, single file.
The user has been building on **.NET SDK 11.0.100-preview** successfully.

**GitHub / CI is now the primary gate.** The user's workflow is: `git add/commit/
push` to `main` → GitHub Actions runs `build.yml` (both jobs must go green) →
*then* build locally. The green checkmark is the authoritative "it survives on a
clean machine" signal; `build.bat` is the local confirmation. Check runs with
`gh run list` / `gh run watch` / `gh run view <id> --log-failed`.

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
├── QuizBuilder.Core/     net8.0        50 .cs   ← portable, WPF-free
├── QuizBuilder.App/      net8.0-windows 37 .cs + 16 .xaml  ← WPF host
├── QuizBuilder.Tests/    net8.0        35 .cs, 465 test methods (xUnit)
├── build.bat             5-stage build, REFUSES to publish if tests fail
├── version.json          single source of version truth
├── global.json           pins SDK to .NET 8, rolls forward
├── Directory.Build.props
├── tools/validate.py     12 static checks (see §8)
├── assets/               icon.svg / icon.ico / make-icon.py
└── .github/workflows/build.yml
```

### Package references — HELD DELIBERATELY LOW

| Project | TFM | Packages |
|---|---|---|
| Core | `net8.0` | **exactly 2**: `System.Security.Cryptography.ProtectedData`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| App | `net8.0-windows` | `Microsoft.Extensions.DependencyInjection` |
| Tests | `net8.0` | xUnit 2.9.2, Test.Sdk 17.11.1, runner.visualstudio 2.8.2, coverlet |

**Core is at 2 package refs and must stay there.** No PDF/Word/Excel/image/git
library. Instead:
- PDF = browser print
- .docx / .xlsx = written by hand via `System.IO.Compression`
- GitHub = REST over `HttpClient`
- Image dimensions = parsed by hand from PNG/GIF/JPEG byte headers

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

> ⚠️ **Known stale comment:** the header of `build.bat` still says
> "as of the current slice there is no QuizBuilder.App project". That is long
> outdated — the App exists and publishes fine. Harmless, but worth fixing.
>
> **README.md was also badly stale** ("slice 1 of 4, no UI yet", "39/39 tests",
> `ThemeService`/`INavigationService` "have no implementations") and has been
> corrected as part of this handoff. Docs drifted while the code moved; if you
> resume after a long gap, distrust prose and check the code.

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

## 13. NEXT STEP: MAUI companion app (Android + iPhone)

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

**⚠️ OPEN CI FAILURE — fix first in the next session.** After the v0.26.0 push,
`core-tests` on ubuntu-latest is RED: 611/612 pass, and the one failure is
`TokenProtectorTests.MachineBound_RoundTripsToken` →
`PlatformNotSupportedException: No DPAPI on this platform`. This is a
**test-isolation bug, not a product bug, and not caused by the MAUI work.**
`MobileReadPathContractTests` installs a process-global DPAPI-shim test double
(one that throws "No DPAPI on this platform" to prove the mobile path never
calls DPAPI). That global leaks into `MachineBound_RoundTripsToken`, which
genuinely needs DPAPI, and it throws. DPAPI is Windows-only, so this test never
belonged on a Linux runner. Two valid fixes (pick one after reading both test
files): (a) scope the shim override in `MobileReadPathContractTests` so it is
reset in a finally/Dispose and cannot leak; or (b) guard
`MachineBound_RoundTripsToken` to skip off-Windows (`OperatingSystem.IsWindows()`
or `[SkippableFact]`) — (b) is likely the correct fix since the assertion is
Windows-specific. The desktop `build.bat` test gate would also catch this
locally on a non-Windows box, but on Windows the test passes, which is why it
slipped through to CI. This existed latent before v0.26.0; the multi-target
just changed test ordering enough to expose it.

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

### NEXT MOBILE SESSION: tier-1 offline features (decided, not yet built)

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

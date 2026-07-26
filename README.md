# Quiz Builder

Portable WPF quiz authoring tool for Windows. Build quizzes, take them, and
export them — entirely offline, with all data stored beside the .exe.

**Status: feature-complete and building green.** v0.20.1, 465 tests passing.

## At a glance

| Area | State |
|---|---|
| Core domain (models, `.qbx`, settings, theming, grading) | Complete |
| All 11 tabs (Builder, Settings, Theme, Preview, Take, Study Cards, Flash Cards, Question Bank, Publish, GitHub, Help) | Complete |
| 8 question types | Complete |
| Export: PDF (browser print), Word, Excel, HTML, self-grading web quiz | Complete |
| Import: xlsx | Complete |
| GitHub publish (PAT + Pages) | Complete |
| Question bank, paused attempts, attempt history | Complete |

See `HANDOFF.md` for the full architecture, invariants, and development notes.

## Build

Easiest path — from the solution root:

```
build.bat
```

Runs an optional pre-build validation (`tools/validate.py`) when a working
Python is available, checking the things a C# compiler can't see or reports
badly: double-hyphens in XML comments (which surface as `MSB4025` during
*clean*, never naming the real cause), duplicate XAML attributes, `x:Class`
mismatches, pack URI vs `AssemblyName`, and unregistered `DynamicResource` keys
(these fail *silently* at runtime, rendering as system defaults).

Prompts use `choice`, not `set /p`. When the script is launched from a
PowerShell host, `set /p` reads stdin — but PowerShell buffers the keystroke
rather than passing it to the child process, so the prompt returns empty and
PowerShell then tries to run your answer as its own command. `choice` reads the
console directly.

It's advisory: problems are reported, the build continues. Python is not a
build dependency. Note that Windows ships an App Execution Alias at
`python.exe` that isn't Python — it prints a Store advert and exits non-zero —
so detection runs each candidate rather than trusting `where`.

Cleans, builds, runs the tests, and (once `QuizBuilder.App` exists) publishes a
self-contained single-file `.exe` and offers to launch it. Flags: `--no-test`,
`--no-publish`, `--quiet`.

Or by hand:

```
dotnet restore
dotnet build
dotnet test
```

Requires the .NET 8 SDK. Core and Tests target plain `net8.0`, so they build and
test on Windows, Linux or macOS. The WPF host will target `net8.0-windows` when
it lands.

## Architecture

```
QuizBuilder.Core/          Platform-neutral. No WPF reference.
  Models/                  QuizDocument, Section, 8 question types
  Interfaces/              Service contracts
  Services/                Implementations
  Theming/                 Design tokens + 5 built-in themes
QuizBuilder.Tests/         xUnit, runs on any OS
```

### Key decisions

**Core has no WPF dependency.** The domain model, settings and `.qbx` logic
don't need it, and keeping it out means the tests run on a cheap Linux CI
runner instead of a Windows agent. The one Windows-only call (DPAPI) sits
behind `ProtectedDataShim`, guarded by `OperatingSystem.IsWindows()`.

**Theme tokens are plain POCOs, not `ResourceDictionary`.** The same
`ThemeTokens` object feeds the WPF resource layer, the PDF exporter and the
HTML emitter. Had they been WPF types, the other two would need a parallel copy
that drifts.

**`IQuizDocumentService` is a shared singleton.** The Builder, Settings, Theme,
Preview and Publish tabs all need the section list. Message-passing would leave
each tab with a projection that drifts on rename. The service owns the *data*;
tabs own their *behaviour* — which is what "no God object" is actually about.

**`.qbx` is a ZIP, not JSON.** Questions carry image attachments; base64
inlining inflates the file ~33% and produces multi-megabyte single-line JSON.
Images are named by SHA-256 of their content, so duplicates dedup for free, and
orphans are garbage-collected on save by walking live references.

**Token protection is user-selectable.** `MachineBound` (DPAPI, default) is
strongest but won't travel between machines. `Passphrase` (AES-256-GCM,
PBKDF2-SHA256 @ 210k) travels, at one prompt per session. `None` never
persists. There's no universally right answer, so the user picks.

## What was verified

The SDK was unavailable, so anything with real edge cases was ported to a
reference model and executed:

- **Contrast** — all 5 palettes against WCAG AA. Caught two live defects:
  Playful's warning colour at 3.58:1, and control borders below 3:1 in four
  themes. `ThemeContrastTests` pins these so a future tweak can't undo them.
- **`MoveQuestion` index math** — 9 cases (within-section, cross-section,
  drop-past-end, last-item-out). The naive implementation throws when a
  question is dragged below the last row.
- **Passphrase state machine** — 19 cases. Wrong passphrase leaves state
  clean; locked reads return null; salt is stable across re-protect while the
  nonce rotates (nonce reuse under a fixed GCM key is catastrophic).
- **`.qbx` round-trip** — 16 cases. Orphan GC shrinks the file after a delete,
  dedup works, dangling refs warn instead of crashing, future format versions
  are refused, zip-slip is blocked.
- **C# contrast port** — the formula was parsed back out of the committed C#
  and diffed against the verified numbers, since a transcription slip would
  pass the compiler silently.

**Now also verified: it compiles, and the tests pass.**

The CA1416 platform warnings from the first green build are fixed. The DPAPI
call now sits inside an `if (OperatingSystem.IsWindows())` positive guard,
which is the form the platform-compatibility analyzer recognises; the earlier
`if (!IsWindows()) throw;` expressed the same intent but relied on the analyzer
tracking a negative guard across a following statement, which it does not do
dependably.

## Versioning

`version.json` is the single source of truth. `build.bat` reads it and passes
the values to MSBuild via `-p:Version`, so the Help/About tab can read them
back off the assembly at runtime. Bump `build` per CI run; bump the rest by hand.

Note: `build.bat` parses `version.json` with batch string tokens, not a real
JSON parser. It expects one key per line. Reformat the file onto a single line
and the script fails loudly with a clear message rather than building the wrong
version number.

## Icon

`assets/icon.ico` — a genuine 7-size icon (16 through 256) generated from
`assets/icon.svg` via `assets/make-icon.py`. It's wired into
`Directory.Build.props` conditionally, so it attaches to `QuizBuilder.App` when
that project lands and is inert until then.

Honest caveat: at 16x16 the checkmark badge degrades into a blob. The overall
silhouette still reads, but if you want something sharper at taskbar size it
wants a hand-tuned 16px variant rather than a downscale.

## Build output

`build.bat` produces:

- `publish\QuizBuilder v0.1.0.1.exe` — portable, self-contained, single file
- `QuizBuilder v0.1.0.1.zip` — the published output

Both carry the full four-part version (`major.minor.patch.build`), so builds are
distinguishable on disk without opening them.

The *file* is renamed after publish rather than overriding `<AssemblyName>`.
That is deliberate: the assembly's internal name stays `QuizBuilder.App`,
because XAML pack URIs embed it (`/AssemblyName;component/MainWindow.xaml`) and
putting spaces in those is a known source of resource-resolution failures. A
single-file publish produces a self-contained bundle whose filename is just a
label, so renaming it is safe.

The window title also carries the version: `Quiz Builder v0.1.0 (build 1)`.
Both read from `VersionInfo`, which reads the assembly attributes that
`build.bat` stamps from `version.json` — so the title bar, the zip filename and
the Help/About tab cannot drift apart.

## The toolchain probe

`QuizBuilder.App` is currently a deliberately minimal probe, not the real shell.
It exists to prove four things separately, before 20+ XAML files are written
against assumptions that might not hold:

1. **The WPF toolchain builds at all.** `UseWPF` pulls in the Windows Desktop
   targeting pack and XAML build tasks, which are separate from the plain
   `net8.0` pack that Core already builds against. On a preview-only SDK these
   may be absent — the failure is NETSDK1147 and the fix is installing the
   .NET 8 SDK.
2. **DI reaches the window.** The diagnostics panel is constructor-injected. If
   the container is misconfigured it throws at resolve time rather than
   silently rendering an unconfigured window.
3. **Theme tokens survive Core → `ThemeResourceBuilder` → XAML.** Every colour,
   font and radius in the probe is a `DynamicResource` lookup. If the bridge is
   broken, everything renders as system defaults and it's immediately obvious.
4. **8-digit alpha tokens parse correctly.** The tokens are CSS-order
   `#RRGGBBAA`; WPF's `ColorConverter` expects `#AARRGGBB`. The last swatch is
   an overlay: it must be a *faint tint*. Solid or muddy means the alpha
   ordering is wrong.

### Probe results (confirmed)

All four checks passed on .NET SDK 11.0.100-preview:

1. WPF toolchain builds — the Windows Desktop targeting pack is present even
   without a .NET 8 SDK installed
2. DI reaches the window — diagnostics rendered from injected services
3. Theme bridge works — `theme bridge : OK (24 keys resolved)`, serif type,
   warm background
4. Alpha ordering correct — the overlay swatch renders as a faint tint

Single-file publish also verified: 13.3s, working `.exe`.

### Layout fix found via the probe

Resizing the probe window ~60px shorter made the swatch caption vanish with no
scrollbar and no error. That is WPF's default: a `StackPanel` lays out children
in order and simply stops when it runs out of height, so the *last* child gets
nothing. With a fixed window height and one star-sized row absorbing the slack,
any content growth silently ate the bottom of the panel.

Fixed by wrapping the content in a `ScrollViewer` with all rows `Auto`. This
matters well beyond the probe: seven tabs of variable-length content would have
hit it constantly, and the symptom (text just missing) gives no clue about the
cause.

## Shell architecture

**Views come from DI, not DataTemplates.** Each View takes its ViewModel as a
constructor parameter, exactly as the probe did. The alternative — a
`DataTemplate` mapping ViewModel types to Views — is more idiomatic MVVM but
requires parameterless constructors and wires the ViewModel through
`DataContext`, which fails *silently* when mistyped. Constructor injection
throws at resolve time. Given how much of this session has been silent XAML
failures, a loud failure is worth the small loss of elegance.

**Views are singletons, toggled by `Visibility`.** Each tab keeps its state
(scroll position, half-typed input) across navigation. Seven UserControls is a
trivial memory cost against rebuilding a tab on every switch.

**The rail binds to a `NavItem` collection**, not hardcoded buttons, so adding
a destination is one line in `ShellViewModel`. `IsActive` is a real property
with change notification rather than a converter comparison — converters can't
raise `PropertyChanged`, so the highlight wouldn't update.

**Focus rings use `:focus-visible` semantics.** `IsKeyboardFocused` was the
obvious binding and it's wrong: WPF sets it whenever an element holds focus,
including the first tab stop when the window merely opens, and on a plain mouse
click. So the active nav item showed a ring on launch. `Controls/FocusVisible`
is an attached property tracking the last input device, giving WPF the
`:focus-visible` behaviour CSS has and WPF lacks — rings appear on Tab, vanish
on click.

**`ShellViewModel` is not a God object.** It owns the destination list and which
is active. No quiz, settings, theme or export logic — those live in each tab's
own ViewModel, with shared state in `IQuizDocumentService` / `ISettingsService`.

## Question editors: DataTemplate, not DI

The shell resolves views from DI; the question editors use `DataTemplate`
selection instead. That looks inconsistent, and isn't:

- The shell has **seven fixed views**, built once at startup. A silent
  `DataContext` failure there leaves a blank tab forever, so constructor
  injection's loud failure is worth the ceremony.
- The editors are **created per question** as the user clicks around. DI can't
  resolve "an editor for *this* question object" without a factory per type.
  `DataTemplate` maps ViewModel→View, which is exactly the job, and a missing
  template renders the type name — loud enough to spot instantly.

## The quiz compiler

`IQuizCompiler` turns an authored quiz plus its settings into the paper a
student sees: question selection, shuffling, numbering, matching columns.

It lives in **Core, not the UI**, because Publish must produce the identical
paper. If Preview computed its own shuffle and the PDF exporter computed
another, an exported paper could differ from the one that was checked — and
nobody would find out until the papers were printed.

Everything derives from a **seed**. Without one, every repaint would reshuffle
and Preview could never show the same paper twice; switching to the answer key
would show a different paper than the student view it was meant to explain.

## Two pass marks

A pass mark of 75% is ambiguous the moment questions carry different weights.
Three 1-point MC questions plus one 10-point essay: a student who aces the MC
and skips the essay has **75% of the questions** right but **23% of the marks**.

Neither reading is wrong, so `PassMarkBasis` lets the author pick:

- **QuestionCount** (default) — every question counts equally. A question counts
  as correct at **half its own points or better**, so a part-credited essay on
  6/10 counts and 4/10 does not.
- **TotalPoints** — weighted; a 10-point essay carries ten times a 1-point
  true/false.

Zero-point questions are excluded from the question count: counting them as
incorrect would put 100% out of reach through no fault of the student.

## Why there is no PDF library

Every good .NET PDF library carries a cost that follows the app around:
QuestPDF's free tier is revenue-gated, iText is AGPL (viral for a distributed
desktop app), and the MIT options have no HTML→PDF path — meaning hand-built
pagination and text measurement, written blind.

The exported HTML carries `@media print` rules (`break-inside: avoid` on every
question) and a print button. The browser's own print engine paginates better
than anything hand-rolled here, honours those rules, and produces a real PDF
via "Save as PDF". Cost: one extra click. That is a better trade than a licence
obligation or a layout engine nobody can see the output of.

## Why there is no OpenXml dependency either

A `.docx` is a ZIP of XML parts, and `System.IO.Compression` is already in the
BCL — already used by `QuizPackageService` for `.qbx`. So `DocumentFormat.OpenXml`
would buy type safety at the price of being unverifiable in the environment this
was written in.

Writing the parts directly means the output can be **unzipped and parsed** in a
test. `WordExporterTests` does exactly that: it opens the archive, loads each
part with `XDocument`, and asserts on what Word will actually read.

The OOXML traps that silently corrupt a document, all covered by tests:

- **Three different units.** `w:sz` is half-points (24 = 12pt), `w:spacing` and
  `w:pgSz` are twips (240 = 12pt). A font size of 12 in `w:sz` is six-point text.
- **`xml:space="preserve"`** on every run, or Word strips edge whitespace and
  every option loses its indent.
- **`<w:br/>` for newlines** — a newline inside `w:t` is only whitespace, so a
  multi-line prompt would collapse onto one line.
- **Control characters** must be stripped: one `\x01` pasted into a prompt makes
  Word declare the whole file unreadable.
- **Colours have no `#`** — Word wants a bare hex triplet and silently falls back
  to automatic otherwise.

## Export ordering

HTML first because it is the **only format verifiable without a build**: it is
text, so the escaping, the CSS injection defences and the page structure were
all checked by running the logic and parsing the output. HTML also forces the
shared "compiled quiz → document" shape the other formats reuse.

Word and Excel then shipped **without a library each**, which was not the
original plan. The reasoning that changed it: a .docx and an .xlsx are both a
ZIP of XML, and `System.IO.Compression` is already in the framework. Writing the
parts directly means the output can be unzipped and parsed right here — so the
format is verifiable in an environment with no .NET SDK, which a NuGet package
would not have been. It also keeps Core's dependencies at two, and sidesteps the
licence traps in this space (QuestPDF is revenue-gated, iText is AGPL, EPPlus
has been non-commercial-only since v5).

PDF is the exception that proves it: there is no zip-of-XML trick, every option
is a heavyweight dependency or a licence problem, and the browser already has a
better paginator than anything hand-rolled. So PDF goes through the HTML export
and the browser's own print dialog.

## The spreadsheet schema

One row per question, one wide table (34 columns), plus a Guide sheet inside the
workbook explaining each column.

Every repeated field has its **own column** — `Option 1..8`, `Match 1..8`,
`Distractor 1..4` — rather than a delimited list in one cell. The first design
packed matching pairs into `Left = Right` and alternatives into `a / b`, and a
round-trip test caught what that costs: a short answer of `and/or` silently
became two answers, and a distractor of `x, y` became two distractors. Any
delimiter can appear in the content it separates. Columns cannot collide.

Import reads what **Excel** writes, not just what this app writes: shared
strings, rich-text runs, worksheet parts whose number does not match their
position, and — the important one — omitted empty cells. Excel writes no element
at all for a blank cell, so reading cells in order and zipping them against the
headers shifts every field after the first gap. Cells are placed by their `r=`
reference instead. That bug produces *plausible* data, which is why it gets a
test of its own.

Rows that cannot be read are **reported, not skipped**: a partial import that
claims success leaves someone printing a paper that is quietly missing three
questions.

## The GitHub tab

The spec said LibGit2Sharp. It ships native binaries per-architecture that fight
single-file publish, it could not be restored or verified in the environment
this was written in, and — the deciding point — it is a sledgehammer for what
this tab actually does. The user story is "I made a quiz, give me a link to hand
out", which is three REST calls:

1. `GET /user` — check the token
2. `PUT /repos/{owner}/{repo}/contents/{path}` — commit the page
3. `POST /repos/{owner}/{repo}/pages` — turn on Pages

`HttpClient` is already in the framework. No git client, nothing to install, and
the app stays portable.

The sha rule is the part worth knowing about: the Contents API refuses an update
that does not carry the file's current sha, so "just PUT it" **works the first
time and fails every time after**. Every publish reads the sha first. A `409`
means someone changed the file in between, and it is reported rather than forced
— overwriting their work silently would be worse than an error.

### What is NOT verified

Everything above was checked by inspecting the requests: URLs, JSON bodies,
base64 encoding, sha handling, error mapping. GitHub's error contract (a
`message` field) was confirmed against the live API.

**No authenticated call was ever made.** Whether GitHub accepts these requests is
the one thing that could not be tested here, and it needs your token and your
network to confirm. This is a weaker guarantee than the other export slices,
where the output could be unzipped and parsed locally.

## Settings are preserved across builds

`build.bat` cleans `publish/` before each build, and the app is portable -- so
`settings.json` lives *inside* the directory being deleted. Every build silently
wiped the user's settings, GitHub token, recent files and custom theme.

The script now copies `settings.json` and any `.qbx` files to `.build-preserve/`
before the clean and puts them back afterwards. Three details matter:

- **Restore runs after the zip.** `Compress-Archive` packages `publish\*`
  wholesale, and the zip is a distributable — restoring first would ship the
  user's encrypted GitHub token and recent-files list to whoever they hand it to.
- **Restore runs before the launch prompt.** `start` returns immediately, so
  restoring at exit would race the app reading its own settings.
- **Restore runs on the failure path too.** Batch has no `try/finally`, so a
  build that fails after the clean would otherwise leave the settings stranded.

`.build-preserve/` sits beside the repo rather than in `%TEMP%`: if the script
ever dies mid-build, the files are somewhere findable.

## Theme editing has Save and Discard

The custom theme editor used to persist on every keystroke, so an experiment was
permanent the moment it was made and the only way out was Delete — which throws
away the whole theme. `BeginEdit` snapshots, `DiscardChanges` restores, `Save`
commits and re-baselines.

Choosing a theme, switching to the custom one, and deleting it all still save
immediately: those are decisions, not experiments. Only the colour and font
editors defer.

## Taking the quiz

The scoring rules were modelled and run before any of them was written, because
a grader that is subtly generous produces plausible numbers forever and nothing
ever reports it.

**Essays are excluded from the score, not counted as zero.** This is the
difference between honest and defamatory: a 10-point multiple choice answered
perfectly beside a 10-point essay is *100% of what could be marked*. Counting
the essay as zero reads 50% — a fail at the default bar — for someone who got
everything markable right. They are listed for review instead, and the score
line always says what it is a percentage of.

**Multi-select partial credit is `(hits − misses) / correct`, floored at zero.**
The obvious rule, `hits / correct`, awards full marks for ticking every box.

**Pass/fail comes from the same percentage shown on screen.** Reusing
`CompiledQuiz.PassesOnQuestions` was tempting, but it counts every question with
points as gradeable — essays included — because it describes the *printed*
paper. Feeding one's numbers to the other produced a screen reading "100%" above
the word FAIL. Reusing a rule that answers a different question is not reuse.

**The timer uses `Stopwatch`, not `DateTime.Now`.** A wall clock jumps an hour at
a daylight-saving boundary: a 30-minute exam crossing 02:00 in autumn would
suddenly have 90 minutes left, and in spring would end the moment it began.

History lives in `history.json` beside the executable, keyed on the quiz's Guid —
which survives a `.qbx` round trip, so reopening a quiz finds its attempts. Not
inside the `.qbx`, which is the authored document and gets shared: baking one
person's scores into the file they hand out would be wrong.

### A bug that shipped green

The results panel rendered on top of the question paper from the moment the
window opened — an empty white card and two stray headings over the questions.
The cause:

```xml
<ScrollViewer Visibility="{Binding IsSubmitted, ...}"
              DataContext="{Binding Summary}">
```

`DataContext` applies to the element's *own* bindings, so `Visibility` resolved
`IsSubmitted` against `Summary` — null until submit. A binding against a null
context yields `UnsetValue`, the converter never runs, and `Visibility` falls
back to its default of `Visible`.

Every check passed: the XAML is well-formed, and the binding checker only asks
"does some ViewModel have a member called `IsSubmitted`?" — never *which*
DataContext the binding resolves against. That is a weak check that looked like
a strong one.

Validator check 11 now flags any element setting `DataContext` alongside another
binding. Full scope resolution would need a XAML-aware tree walker and type
information there is no compiler here to provide, but this shape is mechanical
and catches the whole class.

## Formatted descriptions

The description accepts a small safelist — `<b>`/`<strong>`, `<i>`/`<em>`,
`<br>`, `<ul>`/`<li>` — plus raw line breaks. Everything else is escaped and
shown as typed. It renders on four surfaces (HTML export, Word export, Preview,
Take window) from one parsed model in Core, so they cannot drift apart.

It is a **safelist parser, not an HTML engine**. Three things make it defensible:

- **The default is text.** Only the safelist earns markup; `if x < 5` shows the
  `<`, and any other tag is escaped.
- **No attribute parsing.** `<b onclick=x>` contains a space, never matches the
  bare-name safelist, and falls through to literal text — so `onclick`,
  `onerror` and `href` are unreachable, not merely filtered.
- **A scanner, not a regex.** `<scr<b></b>ipt>` defeats naive tag-stripping by
  interleaving; a scanner reading one tag at a time cannot be fooled.

The full injection suite is verified by rendering to HTML and asserting on
*structure* — no element outside the safelist, no attributes — rather than
grepping for dangerous substrings, which a substring check gets wrong in both
directions.

Raw newlines map to line breaks, which also fixed a real bug: the HTML export
had no `white-space` rule and collapsed a six-line description into one
paragraph. The parser now emits explicit breaks that every surface honours.

## Flash cards

Flip through the questions as cards. The whole behaviour -- building the cards,
navigation bounds, flip, shuffle -- lives in a `FlashDeck` class in Core, so it
is tested on Linux like everything else. The WPF view model is a thin wrapper
that forwards to the deck and raises change notifications; there is no logic up
there that a Core test does not already cover.

The card's answer comes from `AnswerDescriber`, the same describer the results
report uses -- one describer, so a card and the review screen can never disagree
about the same question's answer. Essays have no single answer, so a card shows
the author's rubric if there is one, or says the response is open.

## Images

Questions and study cards can carry images. The `.qbx` already stored images
content-addressed (identical bytes stored once, orphans pruned on save); this
work reaches that storage from the UI and the renderers.

- **Attach**: a picker on the question editor (shared by all seven types via the
  base editor) and on each side of a study card.
- **Display**: the editor, Preview, the Take window, and the flash cards, via a
  `BytesToImageConverter` so the view models stay free of WPF imaging types.
- **Export**: the HTML and self-grading web exports embed each image as a
  `data:` URI, keeping the output a single self-contained file. The exporters
  take an image-resolver delegate rather than depending on the package service,
  so they remain pure functions of their inputs. PDF (browser-print) rides the
  HTML path.

**Word (`.docx`) images (stage B)** embed each picture as a proper OOXML part.
A single pre-pass builds an image plan -- one relationship id and media file per
distinct image -- and the four parts that must agree (content types, the
document relationships, the media files, and the `<w:drawing>` elements in the
body) are all generated from it. That is the crux: a drawing's `r:embed` can
never point at a relationship that was not written, which is the usual cause of
Word's "unreadable content, repair?". A reused image is stored once but drawn
each place it appears; oversized images are capped to the page's text width;
image dimensions are read straight from the PNG/GIF/JPEG headers (no imaging
library, keeping Core at two package references). The produced `.docx` is
unzipped in tests and checked for exactly this coherence.

Safety: the stored path is a content hash, so a hostile filename never reaches
the markup, and a `data:` URI's base64 payload has no quote or angle bracket to
break out of the `src` attribute.

## Question bank

A reusable pool of questions, saved to `question-bank.json` beside the exe. In
the Quiz Builder, "Save to bank" stores a copy of the selected question. On the
Question Bank tab, entries can be tagged with a category, filtered by it, and
added into the current quiz's chosen section.

Bank questions are stored as an independent copy (editing a quiz's copy never
touches the bank, and adding one bank question to two quizzes gives each its
own), and are text-only: images live inside a quiz's package, so a question
pulled from the bank arrives without one and the author adds it in the quiz.

## Save & continue later

A sitting can be paused and resumed. "Save & continue later" in the quiz window
snapshots the paper exactly as shown, every answer so far, and the time spent,
to `paused-attempts.json` beside the exe. The Take tab lists paused sittings for
the quiz; resuming rebuilds that exact paper, restores the answers, and -- for a
timed quiz -- continues the countdown from the time that was left.

The snapshot is self-contained: it stores the paper, not a seed to recompile
from, so a paused sitting resumes unchanged even if the quiz is edited
afterward. The clock stops while paused, so saving never costs the taker time.
Finishing a resumed sitting removes its paused entry; re-pausing updates the same
one rather than piling up copies.

## Study cards

A second source for the Flash Cards tab: front/back cards the user writes by
hand, separate from the quiz questions. A Settings toggle chooses whether the
flash cards come from the quiz, the study cards, or both (questions first, then
study cards, numbered continuously).

Study cards are deliberately **not** questions -- no type, no points, no grading
-- so they get their own tab and their own small model rather than being bent to
fit `Question`. They live on `QuizDocument.StudyCards`, a plain list, so they
travel in the `.qbx` exactly like the questions with no special serialisation;
a round-trip test pins that, including that an older file with no study-card key
reopens with an empty list rather than null.

The deck assembly lives in `FlashDeck.Build(document, source)` in Core, tested
across all three sources. This is **stage one, text only**; images on study
cards reuse the existing content-hashed image storage
and extend the flash renderer.

## Self-grading web quiz

Publish exports a single HTML file that grades itself in the browser: inputs, a
submit button, and a client-side grader, all self-contained and offline.

The hard part is that a static page cannot call the C# grader, so the rules are
**re-implemented in JavaScript** — a second implementation of logic that must
agree with the first, exactly, or the same quiz scores differently in the app
and the browser. That risk was handled head-on:

- Both graders were ported to a reference model and checked to **agree on a
  battery** of 28 cases covering every question type, the essay exclusion, and
  the pass boundary.
- The embedded JavaScript was then **extracted and run in Node** against that
  same battery — not a model of it, the actual shipped script — and it matches.
- A **full end-to-end run in jsdom** fills a mixed quiz's inputs, submits, and
  confirms the browser reads every input and produces the score the app would.
- The page **self-tests on load**, logging a grader check to the console, so a
  future regression is visible without reading the source.

The answer key is embedded in the page — client-side grading is impossible
without it — so this is honestly for self-assessment and practice, and the page
says so. A `</script>` in a prompt cannot break out: the JSON is encoded with the
default `System.Text.Json` encoder, which escapes `<` to `<`.

## Known open items

- Switching token protection mode clears the stored token — the Settings tab
  **must warn before doing it**, or it's silent data loss
- `SettingsService.Save()` falls back to delete+move on FAT32, where
  `File.Replace` isn't atomic. Non-atomic, but the best available there.

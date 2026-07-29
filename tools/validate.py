#!/usr/bin/env python3
"""
Pre-commit validation for the things a C# compiler cannot catch until too late,
and the things it catches with a message that does not name the real cause.

Run:  python tools/validate.py

Checks:
  1. Every XML-family file parses (catches '--' in comments, duplicate
     attributes, unclosed tags). MSBuild reports these as MSB4025 during
     *clean*, long before it mentions your .csproj.
  2. Batch files have CRLF endings and every goto resolves.
  3. XAML x:Class matches its code-behind namespace and class.
  4. XAML pack URIs match the csproj AssemblyName.
  5. Every DynamicResource key in XAML is registered by ThemeResourceBuilder.

Exit code is non-zero on any failure, so this can gate a commit hook or CI.
"""
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
failures = []


def fail(msg):
    failures.append(msg)
    print(f"  FAIL {msg}")


def ok(msg):
    print(f"  OK   {msg}")


def check_xml():
    print("\n[1] XML/JSON well-formedness")
    # Include every XML-family file the .NET / Android build parses strictly.
    # AndroidManifest.xml (a plain .xml) and .targets/.manifest were previously
    # unchecked, which let a '--'-in-comment slip through to the Android build
    # (illegal XML: a comment may not contain '--'). ET.parse rejects that, so
    # widening the glob is the whole fix.
    patterns = ["**/*.csproj", "**/*.props", "**/*.targets", "**/*.xaml",
                "**/*.svg", "**/*.config", "**/*.xml", "**/*.manifest"]
    files = []
    for p in patterns:
        files.extend(glob.glob(os.path.join(ROOT, p), recursive=True))
    for f in sorted(files):
        if os.sep + "obj" + os.sep in f or os.sep + "bin" + os.sep in f:
            continue
        rel = os.path.relpath(f, ROOT)
        try:
            ET.parse(f)
            ok(rel)
        except ET.ParseError as e:
            fail(f"{rel}: {e}")
    for f in glob.glob(os.path.join(ROOT, "*.json")):
        rel = os.path.relpath(f, ROOT)
        try:
            json.load(open(f))
            ok(rel)
        except Exception as e:
            fail(f"{rel}: {e}")


def check_batch():
    print("\n[2] Batch files")
    for f in glob.glob(os.path.join(ROOT, "*.bat")):
        rel = os.path.relpath(f, ROOT)
        raw = open(f, "rb").read()
        # cmd.exe parses line-by-line; LF-only endings break labels and goto.
        lf_only = raw.count(b"\n") - raw.count(b"\r\n")
        if lf_only > 0:
            fail(f"{rel}: {lf_only} LF-only line endings (needs CRLF)")
        else:
            ok(f"{rel}: CRLF")

        src = raw.decode("utf-8", errors="replace")
        # Strip REM lines first: prose mentioning "goto" is not a jump, and
        # matching it produces false failures on the script's own comments.
        code = "\n".join(
            l for l in src.splitlines()
            if not l.strip().lower().startswith("rem")
        )
        # Both spellings. Batch accepts `goto fail` and `goto :fail`, and this
        # check only ever matched the first: \w does not match a colon, so the
        # match died at it and the whole jump went unseen. It had been passing
        # vacuously on every `goto :label` in the script.
        #
        # CALL targets matter too, and were never checked at all -- a typo in
        # `call :restore_user_files` would silently do nothing at runtime, which
        # for that particular routine means quietly not restoring the user's
        # settings.
        gotos = {g.lower() for g in re.findall(r"goto\s+:?(\w+)", code, re.I)}
        calls = {c.lower() for c in re.findall(r"call\s+:(\w+)", code, re.I)}

        # :eof is a cmd built-in, not a label anyone declares.
        targets = (gotos | calls) - {"eof"}

        labels = {l.lower() for l in re.findall(r"^\s*:(\w+)", code, re.M)}
        missing = targets - labels
        if missing:
            fail(f"{rel}: unresolved goto/call targets {sorted(missing)}")
        else:
            ok(f"{rel}: all {len(targets)} goto/call targets resolve")

        # Labels inside a parenthesised block are unreachable: cmd parses the
        # block as one statement, so jumping out abandons it. This fails at
        # runtime, mid-build, with no error message.
        depth = 0
        inside = []
        for i, line in enumerate(code.splitlines(), 1):
            if re.match(r"^\s*:\w+", line) and depth > 0:
                inside.append(f"line {i}: {line.strip()}")
            depth += line.count("(") - line.count(")")
            depth = max(depth, 0)
        if inside:
            fail(f"{rel}: labels inside a block (unreachable): {inside}")
        else:
            ok(f"{rel}: no labels inside blocks")

        # `choice` errorlevel tests must descend. `if errorlevel 1` matches ANY
        # value >= 1, so testing 1 before 2 silently treats every answer as the
        # first option.
        for m in re.finditer(r"choice\s+/c\s+(\w+)", code, re.I):
            after = code[m.end():m.end() + 600]
            tests = [int(x) for x in re.findall(r"if errorlevel (\d+)", after)]
            if tests and tests != sorted(tests, reverse=True):
                fail(f"{rel}: choice errorlevel tests not descending: {tests}")
            elif tests:
                ok(f"{rel}: choice /c {m.group(1)} tests descend {tests}")

        # set /p is unreliable when the script is launched from PowerShell:
        # the keystroke can be buffered by the host and never reach stdin.
        for i, line in enumerate(code.splitlines(), 1):
            if re.search(r"set\s+/p", line, re.I):
                fail(f"{rel}: line {i} uses `set /p` (unreliable under a "
                     f"PowerShell host; use `choice`)")


def check_xaml_classes():
    print("\n[3] XAML x:Class <-> code-behind")
    for f in sorted(glob.glob(os.path.join(ROOT, "**/*.xaml"), recursive=True)):
        if os.sep + "obj" + os.sep in f:
            continue
        rel = os.path.relpath(f, ROOT)
        src = open(f).read()
        m = re.search(r'x:Class="([\w.]+)"', src)
        if not m:
            continue  # resource dictionary
        cb = f + ".cs"
        if not os.path.exists(cb):
            fail(f"{rel}: declares x:Class but has no code-behind")
            continue
        s2 = open(cb).read()
        ns = re.search(r"namespace ([\w.]+);", s2)
        cls = re.search(r"public partial class (\w+)", s2)
        if not ns or not cls:
            fail(f"{rel}: code-behind missing namespace or partial class")
            continue
        expected = f"{ns.group(1)}.{cls.group(1)}"
        if m.group(1) != expected:
            fail(f"{rel}: x:Class={m.group(1)} but code-behind is {expected}")
        else:
            ok(f"{rel}: {expected}")


def check_pack_uris():
    print("\n[4] Pack URIs <-> AssemblyName")
    csproj = os.path.join(ROOT, "QuizBuilder.App", "QuizBuilder.App.csproj")
    if not os.path.exists(csproj):
        print("  --   App project not present, skipping")
        return
    asm = re.search(r"<AssemblyName>([\w.]+)</AssemblyName>", open(csproj).read())
    if not asm:
        fail("csproj has no explicit AssemblyName")
        return
    name = asm.group(1)
    found = 0
    for f in glob.glob(os.path.join(ROOT, "**/*.xaml"), recursive=True):
        if os.sep + "obj" + os.sep in f:
            continue
        for uri in re.findall(r'Source="/([\w.]+);component', open(f).read()):
            found += 1
            if uri != name:
                fail(f"{os.path.relpath(f, ROOT)}: pack URI '{uri}' != AssemblyName '{name}'")
            else:
                ok(f"{os.path.relpath(f, ROOT)}: /{uri};component")
    if found == 0:
        print("  --   no pack URIs found")


def check_resource_keys():
    print("\n[5] DynamicResource keys registered")
    builder = os.path.join(ROOT, "QuizBuilder.App", "Theming", "ThemeResourceBuilder.cs")
    if not os.path.exists(builder):
        print("  --   ThemeResourceBuilder not present, skipping")
        return
    b = open(builder).read()
    registered = set(re.findall(r'd\[\s*"([\w.]+)"\s*\]', b))
    for n in re.findall(r'AddColorPair\(d,\s*"(\w+)"', b):
        registered.add(f"Color.{n}")
        registered.add(f"Brush.{n}")

    for f in sorted(glob.glob(os.path.join(ROOT, "**/*.xaml"), recursive=True)):
        if os.sep + "obj" + os.sep in f:
            continue
        rel = os.path.relpath(f, ROOT)
        used = set(re.findall(r"\{DynamicResource\s+([\w.]+)\}", open(f).read()))
        if not used:
            continue
        missing = used - registered
        if missing:
            fail(f"{rel}: unregistered keys {sorted(missing)}")
        else:
            ok(f"{rel}: {len(used)} keys resolve")


def check_collating_assertions():
    """
    Assert.Contains/DoesNotContain(string, string) compare CULTURE-SENSITIVELY.
    Under ICU, control and formatting characters carry no collation weight, so
    a needle made only of them collates to the empty string and matches at
    position 0 of ANY haystack.

    Assert.DoesNotContain("\u0001", text) can therefore never pass, whatever the
    code does -- and Assert.Contains("\u0001", text) can never fail. Both are
    silently useless. The fix is a char predicate, which compares ordinally.
    """
    print("\n[10] String assertions with zero-weight needles")

    import unicodedata

    def zero_weight(text):
        if not text:
            return True
        return all(unicodedata.category(c) in ("Cc", "Cf", "Mn") for c in text)

    problems = []
    checked = 0

    for f in sorted(glob.glob(os.path.join(ROOT, "QuizBuilder.Tests", "*.cs"))):
        src = open(f).read()
        rel = os.path.relpath(f, ROOT)

        for m in re.finditer(r'Assert\.(?:Contains|DoesNotContain)\("((?:[^"\\]|\\.)*)"', src):
            literal = m.group(1)
            try:
                resolved = literal.encode().decode("unicode_escape")
            except Exception:
                continue

            checked += 1
            if zero_weight(resolved):
                line = src[:m.start()].count("\n") + 1
                problems.append(
                    f"{rel}:{line}: needle {literal!r} has no collation weight -- "
                    "this assertion always matches. Use a char predicate.")

    if problems:
        for p_ in problems:
            fail(p_)
    else:
        ok(f"{checked} string assertion needle(s) carry collation weight")


def check_itemssource_types():
    """
    ItemsSource expects an IEnumerable. Bound to an int or a bool, WPF renders
    NOTHING and reports nothing -- the control is simply empty forever. Nothing
    else catches this: the binding path resolves, so check 6 is happy, and the
    XAML parses fine.
    """
    print("\n[9] ItemsSource bound to a collection")

    collection_hints = ("IEnumerable", "IReadOnlyList", "IList", "ICollection",
                        "ObservableCollection", "List<", "[]", "Array")
    problems = []
    checked = 0

    for xaml_path in sorted(glob.glob(os.path.join(ROOT, "**/*.xaml"), recursive=True)):
        if os.sep + "obj" + os.sep in xaml_path:
            continue

        xaml = open(xaml_path).read()
        names = set(re.findall(r'ItemsSource="\{Binding ([\w.]+)[,}]', xaml))
        if not names:
            continue

        # Gather every property declaration in the App layer. A binding may
        # resolve against any ViewModel, so this is a name-based lookup rather
        # than a per-view one.
        props = {}
        for cs in glob.glob(os.path.join(ROOT, "QuizBuilder.App", "**", "*.cs"), recursive=True):
            if os.sep + "obj" + os.sep in cs:
                continue
            src = open(cs).read()
            for m in re.finditer(r"public\s+([\w<>?\[\], ]+?)\s+(\w+)\s*(?:=>|\{\s*get)", src):
                props.setdefault(m.group(2), m.group(1).strip())

        rel = os.path.relpath(xaml_path, ROOT)
        for name in sorted(names):
            leaf = name.split(".")[-1]
            declared = props.get(leaf)
            if declared is None:
                continue        # resolved elsewhere (DataContext of a template)
            checked += 1
            if not any(h in declared for h in collection_hints):
                problems.append(f"{rel}: ItemsSource={name} is '{declared}', not a collection")

    if problems:
        for p_ in problems:
            fail(p_)
    else:
        ok(f"{checked} ItemsSource binding(s) resolve to collections")


def check_call_signatures():
    """
    Catches passing the WRONG TYPE to a Core interface method.

    Name-only checks pass happily on AddSection(sectionObject) when the real
    signature is AddSection(string) -- both are "a method called AddSection with
    one argument". This resolves the argument's declared type from nearby local
    variable declarations and compares it to the parameter type. Deliberately
    conservative: it only flags a mismatch it can prove, since a false alarm on
    every build would get the whole check ignored.
    """
    print("\n[8] Core interface call signatures")

    iface_dir = os.path.join(ROOT, "QuizBuilder.Core", "Interfaces")
    if not os.path.isdir(iface_dir):
        print("  --   no interfaces found")
        return

    # name -> list of param type lists (a name may be overloaded)
    sigs = {}
    for f in glob.glob(os.path.join(iface_dir, "*.cs")):
        src = open(f).read()
        # Collapse multi-line signatures onto one line first.
        flat = re.sub(r"\s*\n\s*", " ", src)
        for m in re.finditer(r"(?:^|;|\}|\{)\s*([\w<>?\[\], ]+?)\s+(\w+)\(([^)]*)\)\s*;", flat):
            name, params = m.group(2), m.group(3).strip()
            ptypes = []
            for prm in re.split(r",(?![^<>()]*[>)])", params):
                prm = prm.strip()
                if not prm:
                    continue
                # strip default values, then take the type token
                prm = prm.split("=")[0].strip()
                parts = prm.split()
                if len(parts) >= 2:
                    ptypes.append(parts[-2])
            sigs.setdefault(name, []).append(ptypes)

    problems = []
    checked = 0

    for f in glob.glob(os.path.join(ROOT, "QuizBuilder.App", "**", "*.cs"), recursive=True):
        if os.sep + "obj" + os.sep in f:
            continue
        src = open(f).read()
        rel = os.path.relpath(f, ROOT)

        # Local declarations: "var x = new Foo" / "Foo x = "
        local_types = {}
        for m in re.finditer(r"var\s+(\w+)\s*=\s*new\s+([\w<>]+)", src):
            local_types[m.group(1)] = m.group(2)
        for m in re.finditer(r"^\s*([A-Z][\w<>?]*)\s+(\w+)\s*=", src, re.M):
            local_types.setdefault(m.group(2), m.group(1))

        for m in re.finditer(r"_\w+\.(\w+)\(([^;]*?)\);", src, re.DOTALL):
            name, argstr = m.group(1), m.group(2).strip()
            if name not in sigs:
                continue

            args = [a.strip() for a in re.split(r",(?![^<>()]*[>)])", argstr) if a.strip()]
            checked += 1

            # Only judge a call whose arity matches exactly one overload.
            candidates = [p for p in sigs[name] if len(p) == len(args)]
            if len(candidates) != 1:
                continue

            for arg, ptype in zip(args, candidates[0]):
                if arg.startswith('"') or arg.startswith("$\""):
                    actual = "string"
                elif arg in local_types:
                    actual = local_types[arg]
                else:
                    continue

                base_p = ptype.rstrip("?")
                if actual == base_p:
                    continue

                # Compare only when BOTH types are ones we recognise. An
                # earlier version guarded with "both start uppercase" to avoid
                # false alarms -- which silently excluded every C# keyword
                # type, so passing a Section where a string was expected (the
                # exact bug this check exists for) sailed through. Capitalisation
                # is not a type system; use an explicit set.
                KNOWN = {"string", "int", "double", "bool", "Guid", "Section",
                         "Question", "QuizDocument", "ThemeTokens"}
                if actual in KNOWN and base_p in KNOWN:
                    problems.append(
                        f"{rel}: {name}({arg}) passes {actual}, expects {ptype}")

    if problems:
        for p_ in problems:
            fail(p_)
    else:
        ok(f"{checked} interface call(s) type-checked")


def check_markup_extensions():
    """
    A XAML attribute value whose first character is '{' is parsed as a markup
    extension. So Text="{{1}} here" throws XamlParseException at RUNTIME --
    the XML is perfectly well-formed, so nothing else catches it. The escape is
    a leading empty extension: Text="{}{{1}} here".
    """
    print("\n[7] XAML markup extension escaping")
    known = ("Binding", "StaticResource", "DynamicResource", "x:Type", "x:Null",
             "x:Static", "TemplateBinding", "RelativeSource", "MultiBinding",
             "x:Array", "x:Reference", "ThemeResource",
             # MAUI markup extensions (QuizBuilder.Player). Valid, and like the
             # WPF ones they legitimately begin an attribute value with '{'.
             "AppThemeBinding", "AppThemeResource", "OnPlatform", "OnIdiom",
             "DataTemplate", "FontImage")
    found = 0
    for f in sorted(glob.glob(os.path.join(ROOT, "**/*.xaml"), recursive=True)):
        if os.sep + "obj" + os.sep in f:
            continue
        rel = os.path.relpath(f, ROOT)
        problems = []
        for i, line in enumerate(open(f).read().splitlines(), 1):
            for m in re.finditer(r'(\w+)="(\{[^"]*)"', line):
                val = m.group(2)
                if val.startswith("{}"):
                    continue
                inner = val[1:].lstrip()
                if any(inner.startswith(k) for k in known):
                    continue
                problems.append(f'line {i}: {m.group(1)}="{val[:40]}"')
        found += 1
        if problems:
            fail(f"{rel}: unescaped markup extension: {problems}")
        else:
            ok(f"{rel}")
    if found == 0:
        print("  --   no XAML found")


def check_attached_properties():
    """
    Attached properties resolve by NAME at runtime. If RegisterAttached("Foo")
    does not have matching GetFoo/SetFoo statics, XAML fails with an obscure
    XamlParseException at runtime -- the compiler never sees it.
    """
    print("\n[6] Attached property registration")
    found = 0
    for f in sorted(glob.glob(os.path.join(ROOT, "**/*.cs"), recursive=True)):
        if os.sep + "obj" + os.sep in f or os.sep + "bin" + os.sep in f:
            continue
        src = open(f).read()
        regs = re.findall(r'RegisterAttached\(\s*"(\w+)"', src)
        if not regs:
            continue
        found += 1
        rel = os.path.relpath(f, ROOT)
        getters = set(re.findall(r"public static \w+\??\s+Get(\w+)\(", src))
        setters = set(re.findall(r"public static void Set(\w+)\(", src))
        fields = set(re.findall(r"DependencyProperty (\w+)Property\b", src))
        for name in regs:
            problems = []
            if name not in getters:
                problems.append(f"missing Get{name}")
            if name not in setters:
                problems.append(f"missing Set{name}")
            if name not in fields:
                problems.append(f"missing {name}Property field")
            if problems:
                fail(f"{rel}: RegisterAttached(\"{name}\") {', '.join(problems)}")
            else:
                ok(f"{rel}: {name}")
    if found == 0:
        print("  --   no attached properties found")


def check_datacontext_shadowing():
    """
    [11] An element must not set DataContext AND another {Binding} at once.

    Added after shipping a bug this would have caught. The results panel in
    TakeQuizWindow had:

        <ScrollViewer Visibility="{Binding IsSubmitted, ...}"
                      DataContext="{Binding Summary}">

    DataContext applies to the element's OWN bindings, so Visibility resolved
    IsSubmitted against Summary -- which is null until the quiz is submitted. A
    binding against a null context yields UnsetValue, the converter never runs,
    and Visibility falls back to its default of Visible. The entire results
    panel rendered on top of the question paper from the moment the window
    opened: an empty white card and two stray headings.

    Everything about it was individually valid, which is why every other check
    passed. The binding checker only asks "does some ViewModel have a member
    called IsSubmitted?" -- it does not ask which DataContext the binding
    actually resolves against. Real scope resolution needs a XAML-aware tree
    walker and type knowledge this has no compiler to provide. But THIS shape is
    mechanical and unambiguous: put the DataContext on a child, or source the
    other binding explicitly.
    """
    print("\n[11] DataContext shadowing")

    pattern = re.compile(r"<(\w[\w.]*)\s([^>]*?)/?>", re.DOTALL)
    found = 0

    for f in sorted(glob.glob(os.path.join(ROOT, "**/*.xaml"), recursive=True)):
        rel = os.path.relpath(f, ROOT)
        text = open(f, encoding="utf-8").read()

        for match in pattern.finditer(text):
            attrs = match.group(2)

            if 'DataContext="{Binding' not in attrs:
                continue

            others = [a for a in re.findall(r"(\w+)=\"\{Binding", attrs) if a != "DataContext"]
            if not others:
                continue

            found += 1
            line = text[: match.start()].count("\n") + 1
            fail(f"{rel}:{line}: <{match.group(1)}> sets DataContext and {others} "
                 f"on the same element; {others} will resolve against the new context")

    if not found:
        ok("no element sets DataContext alongside another binding")


def check_deterministic_newlines():
    """
    [12] No platform-dependent newline in a Core data transform.

    Added after a test on Windows caught what Linux could not: ToPlainText used
    StringBuilder.AppendLine(), which appends Environment.NewLine -- "\r\n" on
    Windows, "\n" on Linux. The output is compared, may land in an Excel cell or
    a window title, and the app is portable, so it must be byte-identical on
    every platform. Building only on Linux made the bug invisible here.

    AppendLine()/Environment.NewLine are fine in the HTML and Word exporters:
    whitespace between block tags is ignored by the consuming parser. Those two
    files are allowlisted. Anywhere else in Core, a nondeterministic newline in
    a string that gets compared or stored is a portability bug.
    """
    print("\n[12] Deterministic newlines in Core")

    allow = {"HtmlExporter.cs", "WordExporter.cs"}
    found = 0

    for f in sorted(glob.glob(os.path.join(ROOT, "QuizBuilder.Core/**/*.cs"), recursive=True)):
        name = os.path.basename(f)
        if name in allow:
            continue

        rel = os.path.relpath(f, ROOT)
        code = open(f, encoding="utf-8").read()

        # Strip comments so the explanatory text above ToPlainText does not trip it.
        stripped = re.sub(r"//.*", "", code)
        stripped = re.sub(r"/\*.*?\*/", "", stripped, flags=re.DOTALL)

        for m in re.finditer(r"\.AppendLine\(\s*\)|Environment\.NewLine", stripped):
            found += 1
            line = stripped[: m.start()].count("\n") + 1
            token = m.group(0)
            fail(f"{rel}:{line}: {token} is platform-dependent; use an explicit '\\n' "
                 f"in a Core transform whose output is compared or stored")

    if not found:
        ok("no platform-dependent newlines in Core transforms")


def main():
    print("=" * 60)
    print("  Quiz Builder pre-build validation")
    print("=" * 60)

    check_xml()
    check_batch()
    check_xaml_classes()
    check_pack_uris()
    check_resource_keys()
    check_attached_properties()
    check_markup_extensions()
    check_call_signatures()
    check_itemssource_types()
    check_collating_assertions()
    check_datacontext_shadowing()
    check_deterministic_newlines()

    print("\n" + "=" * 60)
    if failures:
        print(f"  {len(failures)} FAILURE(S)")
        for f in failures:
            print(f"    - {f}")
        print("=" * 60)
        return 1
    print("  ALL CHECKS PASSED")
    print("=" * 60)
    return 0


if __name__ == "__main__":
    sys.exit(main())

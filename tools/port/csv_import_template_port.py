#!/usr/bin/env python3
"""
Python port of the CSV layer and the import-template layout, proven here before
any C#.

Why port this. The feature itself (a "Download import template" button) is low
risk -- it writes a file. The risk lives in ONE place: CSV parsing. The quiz
schema deliberately puts every repeated field in its OWN COLUMN precisely so no
in-cell delimiter can collide with content (see QuizSheetSchema). The moment we
let a template be a .csv that imports back, we take on RFC-4180 quoting: a cell
like  He said "go"  or  a, b  or a cell with an embedded newline must survive a
write -> read round-trip byte-for-byte, or the whole point of the per-column
design is lost at the file boundary. Hand-rolled CSV splitters that just
str.split(',') are the classic silent-corruption bug; this port pins the quoting
rules so the C# CsvReader/CsvWriter can be a faithful translation.

What is proven here:
  1. CSV WRITE (RFC 4180): a field is quoted iff it contains a comma, a quote,
     CR or LF; inner quotes are doubled; the record separator is CRLF.
  2. CSV READ (RFC 4180): quoted fields, doubled quotes, embedded commas and
     newlines, and a trailing newline all parse back to the exact grid.
  3. ROUND-TRIP: write(grid) then read() == grid, for adversarial content.
  4. HEADER-ADDRESSED ROWS: like the xlsx path, a CSV row is matched to the
     schema by HEADER NAME, so reordered or extra columns do not shift fields.
     (The xlsx importer already does this by cell r=; CSV has no r=, so position
     within the row is used, but the HEADER->index map is still what addresses
     fields -- identical downstream contract.)
  5. TEMPLATE SHAPE: the "both" template = one worked example row per common
     type (blank/illustrative) + the current quiz's real rows, under the schema
     header, and that whole thing parses back to well-formed question rows.

The C# will share the row->question builder with the existing Excel importer, so
this port only has to guarantee the CSV grid is faithful and header-addressed;
the question construction is already proven by numeric_grading_port and the live
ExcelImporter tests.
"""

from __future__ import annotations

import io

# Mirror of the subset of QuizSheetSchema we need. Kept in the same order the
# C# BuildHeaders() produces so the template header matches the importer exactly.
MAX_OPTIONS = 8
MAX_DISTRACTORS = 4


def schema_headers() -> list[str]:
    headers = ["Section", "Type", "Prompt", "Points", "Hint"]
    headers += [f"Option {i}" for i in range(1, MAX_OPTIONS + 1)]
    headers += [f"Correct {i}" for i in range(1, MAX_OPTIONS + 1)]
    headers += [f"Match {i}" for i in range(1, MAX_OPTIONS + 1)]
    headers += [f"Distractor {i}" for i in range(1, MAX_DISTRACTORS + 1)]
    headers.append("Extra")
    return headers


# --------------------------------------------------------------------------- #
#  1 + 2: RFC 4180 CSV write / read
# --------------------------------------------------------------------------- #

CRLF = "\r\n"


def _needs_quoting(field: str) -> bool:
    return any(c in field for c in (",", '"', "\r", "\n"))


def write_csv(grid: list[list[str]]) -> str:
    """Grid of rows -> RFC-4180 text. Fields quoted only when they must be."""
    out = io.StringIO()
    for row in grid:
        cells = []
        for field in row:
            field = "" if field is None else str(field)
            if _needs_quoting(field):
                cells.append('"' + field.replace('"', '""') + '"')
            else:
                cells.append(field)
        out.write(",".join(cells))
        out.write(CRLF)
    return out.getvalue()


def read_csv(text: str) -> list[list[str]]:
    """
    RFC-4180 reader. A hand state machine rather than text.split, because the
    whole reason this port exists is that split() cannot see quoting.

    States: at-field-start, in-unquoted, in-quoted, after-closing-quote.
    """
    rows: list[list[str]] = []
    field: list[str] = []
    row: list[str] = []
    i = 0
    n = len(text)
    in_quotes = False
    field_started = False  # distinguishes a genuine empty trailing line

    def end_field():
        nonlocal field, field_started
        row.append("".join(field))
        field = []
        field_started = False

    def end_row():
        nonlocal row
        rows.append(row)
        row = []

    while i < n:
        c = text[i]
        if in_quotes:
            if c == '"':
                # doubled quote inside a quoted field -> literal quote
                if i + 1 < n and text[i + 1] == '"':
                    field.append('"')
                    i += 2
                    continue
                in_quotes = False
                i += 1
                continue
            field.append(c)
            i += 1
            continue

        # not in quotes
        if c == '"' and not field:
            in_quotes = True
            field_started = True
            i += 1
            continue
        if c == ",":
            end_field()
            field_started = True  # a comma implies at least one more field
            i += 1
            continue
        if c == "\r":
            # swallow CRLF or lone CR
            end_field()
            end_row()
            if i + 1 < n and text[i + 1] == "\n":
                i += 2
            else:
                i += 1
            continue
        if c == "\n":
            end_field()
            end_row()
            i += 1
            continue
        field.append(c)
        field_started = True
        i += 1

    # trailing field/row with no terminating newline
    if field or field_started or row:
        end_field()
        end_row()

    return rows


# --------------------------------------------------------------------------- #
#  4: header-addressed row access (the downstream contract)
# --------------------------------------------------------------------------- #

def header_index(headers: list[str]) -> dict[str, int]:
    """Normalise like the importer: letters+digits only, lowercased."""
    def norm(s: str) -> str:
        return "".join(ch.lower() for ch in s if ch.isalnum())
    idx: dict[str, int] = {}
    for pos, h in enumerate(headers):
        key = norm(h)
        if key and key not in idx:
            idx[key] = pos
    return idx


def value(row: list[str], idx: dict[str, int], header: str) -> str:
    def norm(s: str) -> str:
        return "".join(ch.lower() for ch in s if ch.isalnum())
    pos = idx.get(norm(header))
    if pos is None or pos >= len(row):
        return ""
    return row[pos].strip()


# --------------------------------------------------------------------------- #
#  5: the "both" template grid
# --------------------------------------------------------------------------- #

def example_rows() -> list[list[str]]:
    """
    One illustrative row per common type, laid out against schema_headers().
    Returns rows aligned to the full header width.
    """
    headers = schema_headers()
    width = len(headers)

    def blank_row() -> list[str]:
        return [""] * width

    def put(row, header, val):
        row[headers.index(header)] = val

    rows = []

    # MultipleChoiceSingle
    r = blank_row()
    put(r, "Section", "Example Section")
    put(r, "Type", "MultipleChoiceSingle")
    put(r, "Prompt", "What is the capital of France?")
    put(r, "Points", "1")
    put(r, "Option 1", "Paris"); put(r, "Correct 1", "TRUE")
    put(r, "Option 2", "London")
    put(r, "Option 3", "Berlin")
    rows.append(r)

    # TrueFalse
    r = blank_row()
    put(r, "Section", "Example Section")
    put(r, "Type", "TrueFalse")
    put(r, "Prompt", "The sky is blue.")
    put(r, "Points", "1")
    put(r, "Correct 1", "TRUE")
    rows.append(r)

    # Numeric (target, tolerance, unit)
    r = blank_row()
    put(r, "Section", "Example Section")
    put(r, "Type", "Numeric")
    put(r, "Prompt", "What is 10 divided by 4?")
    put(r, "Points", "1")
    put(r, "Option 1", "2.5"); put(r, "Option 2", "0.1")
    put(r, "Extra", "units")
    rows.append(r)

    # Matching with a distractor
    r = blank_row()
    put(r, "Section", "Example Section")
    put(r, "Type", "Matching")
    put(r, "Prompt", "Match the country to its capital.")
    put(r, "Points", "2")
    put(r, "Option 1", "France"); put(r, "Match 1", "Paris")
    put(r, "Option 2", "Japan"); put(r, "Match 2", "Tokyo")
    put(r, "Distractor 1", "Madrid")
    rows.append(r)

    # ShortAnswer with an answer that itself contains a comma -> CSV quoting test
    r = blank_row()
    put(r, "Section", "Example Section")
    put(r, "Type", "ShortAnswer")
    put(r, "Prompt", 'Name a punctuation mark, any one.')
    put(r, "Points", "1")
    put(r, "Option 1", "comma")
    put(r, "Option 2", 'semicolon, maybe')  # embedded comma on purpose
    rows.append(r)

    return rows


def build_template_grid(current_quiz_rows: list[list[str]]) -> list[list[str]]:
    """header row + example rows + the loaded quiz's own rows."""
    grid = [schema_headers()]
    grid += example_rows()
    grid += current_quiz_rows
    return grid


# --------------------------------------------------------------------------- #
#  Tests
# --------------------------------------------------------------------------- #

def _check(name, cond):
    print(("PASS " if cond else "FAIL ") + name)
    if not cond:
        raise SystemExit(1)


def test_write_quotes_only_when_needed():
    out = write_csv([["a", "b,c", 'he said "hi"', "plain"]])
    # a plain, b quoted (comma), quote field quoted+doubled, plain plain
    assert out == 'a,"b,c","he said ""hi""",plain' + CRLF, repr(out)
    _check("write quotes only when needed", True)


def test_write_embedded_newline():
    out = write_csv([["line1\nline2", "x"]])
    assert out == '"line1\nline2",x' + CRLF, repr(out)
    _check("write quotes embedded newline", True)


def test_read_simple():
    grid = read_csv("a,b,c\r\n1,2,3\r\n")
    _check("read simple grid", grid == [["a", "b", "c"], ["1", "2", "3"]])


def test_read_quoted_comma():
    grid = read_csv('x,"a,b",y\r\n')
    _check("read quoted comma", grid == [["x", "a,b", "y"]])


def test_read_doubled_quote():
    grid = read_csv('"he said ""hi""",z\r\n')
    _check("read doubled quote", grid == [['he said "hi"', "z"]])


def test_read_embedded_newline():
    grid = read_csv('"l1\nl2",after\r\n')
    _check("read embedded newline", grid == [["l1\nl2", "after"]])


def test_read_no_trailing_newline():
    grid = read_csv("a,b")
    _check("read last row without newline", grid == [["a", "b"]])


def test_read_empty_fields():
    grid = read_csv("a,,c\r\n")
    _check("read empty middle field", grid == [["a", "", "c"]])


def test_round_trip_adversarial():
    original = [
        ["Section", "Type", "Prompt"],
        ["S1", "ShortAnswer", 'and/or, "quoted", and\na newline'],
        ["", "", ""],  # all-blank row
        ["x,y", 'z"z', "plain"],
    ]
    round_tripped = read_csv(write_csv(original))
    _check("round-trip adversarial grid", round_tripped == original)


def test_header_addressing_survives_reorder():
    # headers in a weird order; fields still resolve by name
    headers = ["Prompt", "Type", "Section", "Option 1", "Correct 1"]
    idx = header_index(headers)
    row = ["2+2?", "MultipleChoiceSingle", "Math", "4", "TRUE"]
    _check(
        "header addressing survives reorder",
        value(row, idx, "Type") == "MultipleChoiceSingle"
        and value(row, idx, "Prompt") == "2+2?"
        and value(row, idx, "Section") == "Math"
        and value(row, idx, "Option 1") == "4"
        and value(row, idx, "Correct 1") == "TRUE",
    )


def test_header_addressing_ignores_extra_columns():
    headers = ["Type", "Prompt", "My Notes", "Option 1"]
    idx = header_index(headers)
    row = ["TrueFalse", "Sky is blue", "ignore me", "unused"]
    _check(
        "extra columns ignored",
        value(row, idx, "Type") == "TrueFalse"
        and value(row, idx, "Prompt") == "Sky is blue",
    )


def test_template_parses_back():
    # Simulate a loaded quiz with a comma- and quote-bearing prompt.
    headers = schema_headers()
    width = len(headers)
    quiz_row = [""] * width
    quiz_row[headers.index("Section")] = "My Quiz"
    quiz_row[headers.index("Type")] = "ShortAnswer"
    quiz_row[headers.index("Prompt")] = 'Define "osmosis", briefly.'
    quiz_row[headers.index("Points")] = "1"
    quiz_row[headers.index("Option 1")] = "water movement"

    grid = build_template_grid([quiz_row])
    text = write_csv(grid)
    parsed = read_csv(text)

    # header round-trips
    assert parsed[0] == headers, parsed[0]

    idx = header_index(parsed[0])
    # the worked-example MCQ row (row 1) resolves
    mcq = parsed[1]
    assert value(mcq, idx, "Type") == "MultipleChoiceSingle"
    assert value(mcq, idx, "Option 1") == "Paris"
    assert value(mcq, idx, "Correct 1") == "TRUE"

    # the quiz's own comma+quote prompt survived intact
    last = parsed[-1]
    assert value(last, idx, "Prompt") == 'Define "osmosis", briefly.', value(last, idx, "Prompt")
    assert value(last, idx, "Type") == "ShortAnswer"

    # the ShortAnswer example's embedded-comma option survived
    sa = parsed[5]
    assert value(sa, idx, "Option 2") == "semicolon, maybe", value(sa, idx, "Option 2")

    _check("template grid parses back intact", True)


def test_every_example_type_has_prompt_and_type():
    headers = schema_headers()
    idx = header_index(headers)
    for r in example_rows():
        assert value(r, idx, "Type"), r
        assert value(r, idx, "Prompt"), r
    _check("every example row has Type and Prompt", True)


if __name__ == "__main__":
    test_write_quotes_only_when_needed()
    test_write_embedded_newline()
    test_read_simple()
    test_read_quoted_comma()
    test_read_doubled_quote()
    test_read_embedded_newline()
    test_read_no_trailing_newline()
    test_read_empty_fields()
    test_round_trip_adversarial()
    test_header_addressing_survives_reorder()
    test_header_addressing_ignores_extra_columns()
    test_template_parses_back()
    test_every_example_type_has_prompt_and_type()
    print("\nAll CSV / template port checks passed.")

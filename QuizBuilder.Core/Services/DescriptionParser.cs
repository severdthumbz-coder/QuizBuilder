namespace QuizBuilder.Core.Services;

/// <summary>
/// A stretch of description text with its formatting.
///
/// Flags rather than a nested tree: bold and italic are the only marks, they
/// compose to exactly four states, and every renderer this feeds -- HTML, Word,
/// WPF -- wants precisely this shape. A nested inline model would be more
/// faithful to HTML and more work to render three times over, for no gain.
/// </summary>
public sealed record DescriptionRun(string Text, bool Bold, bool Italic)
{
    /// <summary>A line break inside a paragraph, from &lt;br&gt;.</summary>
    public bool IsLineBreak => Text == "\n";

    public static DescriptionRun LineBreak { get; } = new("\n", false, false);
}

/// <summary>A paragraph or a bullet list.</summary>
public abstract class DescriptionBlock;

public sealed class DescriptionParagraph : DescriptionBlock
{
    public List<DescriptionRun> Runs { get; } = new();
}

public sealed class DescriptionList : DescriptionBlock
{
    public List<List<DescriptionRun>> Items { get; } = new();
}

/// <summary>
/// Parses a quiz description into blocks, honouring a small safelist of tags and
/// treating everything else as literal text.
///
/// This is a security boundary. The description ends up in a published web page,
/// so what it decides is markup is what a browser will execute. Three rules make
/// that defensible:
///
///   1. The default is TEXT. Only the safelist earns an exception; anything
///      else is escaped and shown as typed. An author writing "if x &lt; 5"
///      gets that on the page, not a swallowed tag.
///   2. Tags carry NO attributes. "&lt;b class=x&gt;" is not a bold tag, it is
///      the literal characters. This is what makes onclick/onerror/href
///      unreachable rather than merely filtered -- there is no attribute
///      handling to get wrong.
///   3. It is a scanner with an explicit stack, not a regex. Tag-stripping by
///      regex is how injection gets in: "&lt;scr&lt;b&gt;&lt;/b&gt;ipt&gt;"
///      defeats naive stripping by interleaving. A scanner reading one tag at a
///      time cannot be tricked that way.
///
/// The safelist is b/strong, i/em, br, ul, li. Deliberately not a: a link in a
/// quiz description is not worth a URL scheme allowlist and the
/// javascript:-in-href class of bug that comes with it.
/// </summary>
public static class DescriptionParser
{
    private static readonly HashSet<string> Safe = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "strong", "i", "em", "br", "ul", "li",
    };

    public static IReadOnlyList<DescriptionBlock> Parse(string? text)
    {
        var blocks = new List<DescriptionBlock>();

        if (string.IsNullOrEmpty(text)) return blocks;

        var runs = new List<DescriptionRun>();
        List<List<DescriptionRun>>? items = null;
        List<DescriptionRun>? itemRuns = null;

        var bold = 0;
        var italic = 0;
        var buffer = new System.Text.StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0) return;

            var target = itemRuns ?? runs;
            target.Add(new DescriptionRun(buffer.ToString(), bold > 0, italic > 0));

            buffer.Clear();
        }

        void EndParagraph()
        {
            Flush();

            // Drop trailing line breaks. A newline just before a list (or the
            // end of the text) is the gap leading into the next block, not
            // content -- keeping it renders a dangling <br> at the end of the
            // paragraph. Breaks BETWEEN text stay; only trailing ones go.
            while (runs.Count > 0 && runs[^1].IsLineBreak)
                runs.RemoveAt(runs.Count - 1);

            if (runs.Count > 0)
            {
                var paragraph = new DescriptionParagraph();
                paragraph.Runs.AddRange(runs);
                blocks.Add(paragraph);
            }

            runs.Clear();
        }

        void EndList()
        {
            if (items is null) return;

            if (itemRuns is { Count: > 0 }) items.Add(itemRuns);
            itemRuns = null;

            if (items.Count > 0)
            {
                var list = new DescriptionList();
                list.Items.AddRange(items);
                blocks.Add(list);
            }

            items = null;
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '\r')
            {
                // Swallow CR so a Windows CRLF becomes a single break, not two.
                i++;
                continue;
            }

            if (c == '\n')
            {
                // A raw newline the author typed with Enter is a line break, the
                // same as <br>. Existing descriptions have no tags at all, just
                // newlines -- WPF and Word already render those, so the safelist
                // must preserve them or it would be a regression the moment it
                // ships. It also fixes the HTML export, which collapsed them.
                Flush();

                if (itemRuns is not null)
                {
                    itemRuns.Add(DescriptionRun.LineBreak);
                }
                else if (items is null)
                {
                    runs.Add(DescriptionRun.LineBreak);
                }

                // else: inside a <ul> but between items -- the newlines that
                // format the source list are layout, not a break in any item.
                i++;
                continue;
            }

            if (c != '<')
            {
                buffer.Append(c);
                i++;
                continue;
            }

            var close = text.IndexOf('>', i);
            if (close == -1)
            {
                // A '<' with no '>' after it. Literal.
                buffer.Append(c);
                i++;
                continue;
            }

            var raw = text[(i + 1)..close].Trim();
            var isClosing = raw.StartsWith('/');
            var name = (isClosing ? raw[1..] : raw).Trim().TrimEnd('/').Trim();

            // No attribute parsing, on purpose. A tag is its bare name or it is
            // not a tag: "<b onclick=x>" contains a space, so it never matches
            // the safelist and falls through to literal text. Every event
            // handler and URL attack lands here.
            if (!Safe.Contains(name))
            {
                // Emit only the '<' and rescan from the next character: the rest
                // may contain a real tag, and swallowing to '>' would eat it.
                buffer.Append(c);
                i++;
                continue;
            }

            Flush();

            switch (name.ToLowerInvariant())
            {
                case "b":
                case "strong":
                    bold = isClosing ? Math.Max(0, bold - 1) : bold + 1;
                    break;

                case "i":
                case "em":
                    italic = isClosing ? Math.Max(0, italic - 1) : italic + 1;
                    break;

                case "br":
                    (itemRuns ?? runs).Add(DescriptionRun.LineBreak);
                    break;

                case "ul":
                    if (isClosing)
                    {
                        EndList();
                    }
                    else
                    {
                        // Text before a list is its own paragraph.
                        EndParagraph();
                        items = new List<List<DescriptionRun>>();
                        itemRuns = null;
                    }

                    break;

                case "li":
                    // <li> outside a <ul> is meaningless: ignore rather than
                    // invent a list the author did not ask for.
                    if (items is null) break;

                    if (isClosing)
                    {
                        if (itemRuns is not null) items.Add(itemRuns);
                        itemRuns = null;
                    }
                    else
                    {
                        // An unclosed previous item ends here.
                        if (itemRuns is not null) items.Add(itemRuns);
                        itemRuns = new List<DescriptionRun>();
                    }

                    break;
            }

            i = close + 1;
        }

        Flush();

        // Be forgiving at the end: an unclosed <b> or <ul> is a typo in a
        // description, not something to throw over.
        EndList();
        EndParagraph();

        return blocks;
    }

    /// <summary>
    /// The description as plain text, for surfaces with no formatting at all
    /// (the Excel sheet, a window title).
    /// </summary>
    public static string ToPlainText(string? text)
    {
        var blocks = Parse(text);
        var sb = new System.Text.StringBuilder();

        // '\n' explicitly, never AppendLine(). AppendLine emits
        // Environment.NewLine -- "\r\n" on Windows, "\n" on Linux -- which would
        // make this transform's output depend on which OS ran it. This string is
        // compared, may land in an Excel cell or a window title, and the app is
        // portable, so it must be byte-identical on every platform. (A test on
        // Windows caught this: I build on Linux, where the bug is invisible.)
        foreach (var block in blocks)
        {
            switch (block)
            {
                case DescriptionParagraph paragraph:
                    foreach (var run in paragraph.Runs)
                        sb.Append(run.IsLineBreak ? "\n" : run.Text);

                    sb.Append('\n');
                    break;

                case DescriptionList list:
                    foreach (var item in list.Items)
                    {
                        sb.Append("• ");

                        foreach (var run in item)
                            sb.Append(run.IsLineBreak ? " " : run.Text);

                        sb.Append('\n');
                    }

                    break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>True when the text uses any of the safelist tags.</summary>
    public static bool HasFormatting(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var blocks = Parse(text);

        return blocks.Any(b => b is DescriptionList)
               || blocks.OfType<DescriptionParagraph>()
                   .SelectMany(p => p.Runs)
                   .Any(r => r.Bold || r.Italic || r.IsLineBreak);
    }
}

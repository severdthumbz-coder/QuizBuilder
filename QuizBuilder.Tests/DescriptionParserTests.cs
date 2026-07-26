using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The description safelist parser.
///
/// This is a security boundary -- what it decides is markup is what a published
/// web page will run -- so alongside the happy path it carries a full injection
/// suite. Those tests assert on STRUCTURE (render to HTML, then check no element
/// outside the safelist and no attribute survives), not on substrings: a
/// substring check would fail on the safely-escaped text "&lt;b onclick=...&gt;"
/// even though it is inert, and pass on genuinely dangerous output that happened
/// not to contain the searched word.
/// </summary>
public class DescriptionParserTests
{
    // --- Helpers ------------------------------------------------------------

    private static string Html(string input)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var block in DescriptionParser.Parse(input))
        {
            switch (block)
            {
                case DescriptionParagraph p:
                    sb.Append("<p>").Append(Runs(p.Runs)).Append("</p>");
                    break;

                case DescriptionList list:
                    sb.Append("<ul>");
                    foreach (var item in list.Items)
                        sb.Append("<li>").Append(Runs(item)).Append("</li>");
                    sb.Append("</ul>");
                    break;
            }
        }

        return sb.ToString();
    }

    private static string Runs(IReadOnlyList<DescriptionRun> runs)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var run in runs)
        {
            if (run.IsLineBreak) { sb.Append("<br>"); continue; }

            var text = System.Net.WebUtility.HtmlEncode(run.Text);
            if (run.Bold) text = $"<strong>{text}</strong>";
            if (run.Italic) text = $"<em>{text}</em>";
            sb.Append(text);
        }

        return sb.ToString();
    }

    // --- Happy path ---------------------------------------------------------

    [Fact]
    public void PlainTextIsOneParagraph()
    {
        Assert.Equal("<p>hello world</p>", Html("hello world"));
    }

    [Fact]
    public void BoldAndItalicRender()
    {
        Assert.Equal("<p><strong>bold</strong></p>", Html("<b>bold</b>"));
        Assert.Equal("<p><em>it</em></p>", Html("<i>it</i>"));
        Assert.Equal("<p><strong>s</strong></p>", Html("<strong>s</strong>"));
        Assert.Equal("<p><em>e</em></p>", Html("<em>e</em>"));
    }

    [Fact]
    public void TagsAreCaseInsensitive()
    {
        Assert.Equal("<p><strong>x</strong></p>", Html("<B>x</B>"));
    }

    [Fact]
    public void NestedBoldItalicComposes()
    {
        // Both wrappers applied; order of the two does not change rendering.
        Assert.Equal("<p><em><strong>both</strong></em></p>", Html("<b><i>both</i></b>"));
    }

    [Fact]
    public void BulletListRenders()
    {
        Assert.Equal("<ul><li>one</li><li>two</li></ul>", Html("<ul><li>one</li><li>two</li></ul>"));
    }

    [Fact]
    public void TextBeforeAListBecomesItsOwnParagraph()
    {
        Assert.Equal("<p>intro</p><ul><li>a</li></ul>", Html("intro<ul><li>a</li></ul>"));
    }

    [Fact]
    public void FormattingWorksInsideListItems()
    {
        Assert.Equal("<ul><li><strong>x</strong></li></ul>", Html("<ul><li><b>x</b></li></ul>"));
    }

    // --- Newlines -----------------------------------------------------------

    [Fact]
    public void RawNewlinesBecomeLineBreaks()
    {
        // The existing behaviour that must not regress: descriptions are typed
        // with Enter, no tags, and WPF/Word already show the breaks. This is
        // also what fixes the HTML export, which used to collapse them.
        Assert.Equal("<p>a<br>b<br>c</p>", Html("a\nb\nc"));
    }

    [Fact]
    public void CarriageReturnsCollapseWithTheNewline()
    {
        Assert.Equal("<p>a<br>b</p>", Html("a\r\nb"));
    }

    [Fact]
    public void ExplicitBrAlsoBreaks()
    {
        Assert.Equal("<p>a<br>b</p>", Html("a<br>b"));
    }

    [Fact]
    public void ATrailingNewlineDoesNotLeaveADanglingBreak()
    {
        // A newline right before a list is the gap into the list, not content.
        Assert.Equal("<p>intro</p><ul><li>a</li></ul>", Html("intro\n<ul><li>a</li></ul>"));
    }

    [Fact]
    public void NewlinesBetweenListItemsAddNoJunk()
    {
        // The newlines that format the source list are layout, not breaks.
        Assert.Equal("<ul><li>a</li><li>b</li></ul>", Html("<ul>\n<li>a</li>\n<li>b</li>\n</ul>"));
    }

    [Fact]
    public void ANewlineInsideAnItemIsKept()
    {
        Assert.Equal("<ul><li>one<br>two</li></ul>", Html("<ul><li>one\ntwo</li></ul>"));
    }

    // --- The author typed a literal angle bracket ---------------------------

    [Fact]
    public void ALiteralLessThanSurvivesAsText()
    {
        Assert.Equal("<p>if x &lt; 5 then y</p>", Html("if x < 5 then y"));
    }

    [Fact]
    public void AmpersandsAreEscaped()
    {
        Assert.Equal("<p>Tom &amp; Jerry</p>", Html("Tom & Jerry"));
    }

    // --- Malformed but harmless ---------------------------------------------

    [Fact]
    public void UnclosedTagsAreForgiven()
    {
        Assert.Equal("<p><strong>unclosed</strong></p>", Html("<b>unclosed"));
    }

    [Fact]
    public void AStrayClosingTagIsIgnored()
    {
        Assert.Equal("<p>text</p>", Html("</b>text"));
    }

    [Fact]
    public void AnUnclosedListItemStillCloses()
    {
        Assert.Equal("<ul><li>a</li></ul>", Html("<ul><li>a"));
    }

    [Fact]
    public void ListItemOutsideAListIsIgnored()
    {
        Assert.Equal("<p>loose</p>", Html("<li>loose</li>"));
    }

    // --- Injection suite: assert on structure -------------------------------

    private static readonly System.Collections.Generic.HashSet<string> Allowed =
        new() { "p", "ul", "li", "strong", "em", "br" };

    /// <summary>
    /// True when the rendered HTML contains only safelist elements and no
    /// attributes. Parses the output as XML (the render helper emits
    /// well-formed markup) and inspects the element tree.
    /// </summary>
    private static bool StructurallySafe(string input)
    {
        var html = Html(input);

        // Wrap so multiple top-level blocks parse under one root.
        // <br> is valid HTML5 but not XML; close it so the audit can parse.
        var xml = html.Replace("<br>", "<br/>");

        var doc = new System.Xml.XmlDocument();
        doc.LoadXml($"<root>{xml}</root>");

        foreach (System.Xml.XmlNode node in doc.SelectNodes("//*")!)
        {
            if (node.Name == "root") continue;

            if (!Allowed.Contains(node.Name)) return false;
            if (node.Attributes is { Count: > 0 }) return false;
        }

        return true;
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<b onclick='evil()'>x</b>")]
    [InlineData("<scr<b></b>ipt>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<b><script>alert(1)</script></b>")]
    [InlineData("</p><script>alert(1)</script><p>")]
    [InlineData("<style>body{display:none}</style>")]
    [InlineData("<a href='javascript:alert(1)'>click</a>")]
    [InlineData("<iframe src=evil></iframe>")]
    [InlineData("<svg onload=alert(1)>")]
    [InlineData("<ul><li onclick=x>a</li></ul>")]
    [InlineData("<b href=x>y</b>")]
    [InlineData("<b\tonclick=x>tab</b>")]
    public void InjectionAttemptsAreNeutralised(string attack)
    {
        Assert.True(StructurallySafe(attack), $"unsafe output for: {attack}");
    }

    [Fact]
    public void ASafelistTagWithAnAttributeIsTreatedAsLiteralText()
    {
        // "<b onclick=x>" must NOT open a bold run. The parser matches bare tag
        // names only, so a tag carrying anything extra falls through to text.
        // The injection suite alone would miss this: the output is structurally
        // safe either way (no attribute is ever emitted), but accepting the tag
        // would still be wrong -- it would open formatting from attacker markup.
        Assert.Equal("<p>&lt;b onclick=x&gt;text</p>", Html("<b onclick=x>text"));
    }

    [Fact]
    public void TheSafelistStillWorksAfterAllThatHardening()
    {
        // Being safe is not enough; it must still format. A parser that escaped
        // everything would pass every injection test and be useless.
        Assert.Equal("<p><strong>bold</strong> and <em>italic</em></p>",
            Html("<b>bold</b> and <i>italic</i>"));
    }

    // --- ToPlainText / HasFormatting ---------------------------------------

    [Fact]
    public void PlainTextStripsTagsAndBulletsList()
    {
        Assert.Equal("\u2022 a\n\u2022 b", DescriptionParser.ToPlainText("<ul><li>a</li><li>b</li></ul>"));
    }

    [Fact]
    public void PlainTextKeepsNewlines()
    {
        Assert.Equal("a\nb", DescriptionParser.ToPlainText("a\nb"));
    }

    [Fact]
    public void HasFormattingIsFalseForPlainText()
    {
        Assert.False(DescriptionParser.HasFormatting("just words"));
    }

    [Fact]
    public void HasFormattingIsTrueForTagsOrBreaks()
    {
        Assert.True(DescriptionParser.HasFormatting("<b>x</b>"));
        Assert.True(DescriptionParser.HasFormatting("a\nb"));
        Assert.True(DescriptionParser.HasFormatting("<ul><li>a</li></ul>"));
    }

    [Fact]
    public void EmptyInputProducesNoBlocks()
    {
        Assert.Empty(DescriptionParser.Parse(""));
        Assert.Empty(DescriptionParser.Parse(null));
    }
}

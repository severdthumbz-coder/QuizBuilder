using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace QuizBuilder.Player.Services;

/// <summary>
/// Turns the quiz description's HTML into readable plain text for a MAUI Label,
/// which cannot render markup. Desktop authors descriptions as HTML (&lt;strong&gt;,
/// &lt;ul&gt;/&lt;li&gt;, &lt;br&gt;, &lt;p&gt;), and showing that raw leaks tags on
/// screen (the reported bug). This is deliberately a lightweight converter, not
/// an HTML engine: it maps the block/line tags to newlines and bullets, drops
/// the rest, and decodes entities. It is not a sanitizer and is only ever fed
/// the quiz's own description.
/// </summary>
public static class HtmlToText
{
    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var s = html;

        // Normalize line-breaking tags to newlines. <br>, </p>, </div>, </li>
        // and end-of-heading all become a break; <li> gains a bullet prefix.
        s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*/\s*p\s*>", "\n\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*/\s*div\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*li\s*>", "\u2022 ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*/\s*li\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*/\s*(ul|ol)\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*/\s*h[1-6]\s*>", "\n\n", RegexOptions.IgnoreCase);

        // Strip every remaining tag (bold/italic/anchors/etc.). We keep their
        // inner text; the emphasis itself is lost, which is acceptable for a
        // plain Label -- the words matter more than the styling.
        s = Regex.Replace(s, @"<[^>]+>", string.Empty);

        // Decode entities (&amp; &lt; &nbsp; ...).
        s = WebUtility.HtmlDecode(s);

        // Tidy whitespace: collapse runs of spaces/tabs, trim trailing spaces on
        // each line, and cap consecutive blank lines at one so the spacing looks
        // intentional rather than echoing the source's <br><br> runs.
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        s = Regex.Replace(s, @"[ \t]+", " ");
        s = Regex.Replace(s, @" *\n *", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");

        return s.Trim();
    }
}

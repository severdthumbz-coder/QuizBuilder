using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Writes a compiled quiz as a .docx.
///
/// No library. A .docx is a ZIP of XML parts, and System.IO.Compression is
/// already in the BCL and already used by QuizPackageService for .qbx. That
/// matters more than convenience: a NuGet dependency cannot be exercised in the
/// environment this was written in, and every slice of this project so far has
/// had clean domain logic and broken scaffolding. Writing the format directly
/// means the output can be unzipped and checked here rather than hoped about.
///
/// DocumentFormat.OpenXml would give type safety and validation. What it would
/// not give is the ability to verify the result before you build it.
/// </summary>
public sealed class WordExporter : IWordExporter
{
    // Word measures in three different units and mixing them silently produces
    // nonsense rather than an error.
    //   w:sz      half-points  -- 24 means 12pt
    //   w:spacing twips        -- 240 means 12pt
    //   w:pgSz    twips        -- 11906 x 16838 is A4
    private static int HalfPoints(double points) => (int)Math.Round(points * 2);
    private static int Twips(double points) => (int)Math.Round(points * 20);

    private const int A4WidthTwips = 11906;
    private const int A4HeightTwips = 16838;
    private const int MarginTwips = 1134;      // 2cm

    private const string WordMlNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public void Write(Stream stream, CompiledQuiz quiz, ThemeTokens theme, WordExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(quiz);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        // One pre-pass over the questions decides every image's relationship id,
        // media filename, and pixel size. The four package parts that must agree
        // -- content types, the document rels, the media files, and the drawings
        // in the body -- are all generated from this single plan, so an id in the
        // body can never point at a relationship that was not written. A mismatch
        // there is the classic cause of Word's "unreadable content, repair?".
        var images = BuildImagePlan(quiz, options.ImageBytesResolver);

        // leaveOpen: the caller owns the stream. Closing a caller's stream is
        // the kind of thing that works until someone wraps this in a using.
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        WriteEntry(archive, "[Content_Types].xml", ContentTypesXml(images));
        WriteEntry(archive, "_rels/.rels", PackageRelsXml());
        WriteEntry(archive, "word/_rels/document.xml.rels", DocumentRelsXml(images));
        WriteEntry(archive, "word/styles.xml", StylesXml(theme));
        WriteEntry(archive, "word/document.xml", DocumentXml(quiz, theme, options, images));

        foreach (var image in images.All)
            WriteBinaryEntry(archive, $"word/media/{image.MediaFileName}", image.Bytes);
    }

    private static void WriteBinaryEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Resolves each referenced image once, in question order, assigning a stable
    /// relationship id and media filename. The same image path reused on several
    /// questions gets ONE media file and ONE relationship, referenced from each
    /// drawing -- matching how the package service already dedupes on disk.
    /// </summary>
    private static ImagePlan BuildImagePlan(CompiledQuiz quiz, Func<string?, byte[]?>? resolver)
    {
        var plan = new ImagePlan();
        if (resolver is null) return plan;

        foreach (var compiled in quiz.Sections.SelectMany(s => s.Questions))
        {
            var path = compiled.Question.ImageRelativePath;
            if (string.IsNullOrEmpty(path)) continue;
            if (plan.Contains(path)) continue;

            var bytes = resolver(path);
            if (bytes is null || bytes.Length == 0) continue;

            plan.Add(path, bytes);
        }

        return plan;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);

        using var entryStream = entry.Open();
        // No BOM: a byte-order mark before the XML declaration makes some
        // readers reject the part outright.
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));

        writer.Write(content);
    }

    // --- Package parts ------------------------------------------------------

    private static string ContentTypesXml(ImagePlan images)
    {
        var imageDefaults = new StringBuilder();

        foreach (var ext in images.DistinctExtensions)
            imageDefaults.Append($"<Default Extension=\"{ext}\" ContentType=\"{ImageContentType(ext)}\"/>");

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              {imageDefaults}
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            </Types>
            """;
    }

    private static string PackageRelsXml() =>
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private static string DocumentRelsXml(ImagePlan images)
    {
        var imageRels = new StringBuilder();

        foreach (var image in images.All)
        {
            imageRels.Append(
                $"<Relationship Id=\"{image.RelationshipId}\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" " +
                $"Target=\"media/{image.MediaFileName}\"/>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              {imageRels}
            </Relationships>
            """;
    }

    /// <summary>
    /// Styles derived from the theme, so a Word export and the on-screen
    /// preview come from the same tokens rather than drifting apart.
    ///
    /// Colours are emitted WITHOUT the leading '#': Word wants a bare hex
    /// triplet, and a '#' makes it fall back to automatic silently.
    /// </summary>
    private static string StylesXml(ThemeTokens theme)
    {
        var t = theme.Typography;
        var font = FirstFontName(t.FontFamily);
        var textColor = HexOnly(theme.Colors.TextPrimary, "1A1A1A");
        var secondary = HexOnly(theme.Colors.TextSecondary, "4A4A4A");

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="{WordMlNamespace}">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr>
                    <w:rFonts w:ascii="{Esc(font)}" w:hAnsi="{Esc(font)}"/>
                    <w:sz w:val="{HalfPoints(t.Body)}"/>
                    <w:color w:val="{textColor}"/>
                  </w:rPr>
                </w:rPrDefault>
                <w:pPrDefault>
                  <w:pPr><w:spacing w:after="{Twips(6)}" w:line="{(int)(t.LineHeightBody * 240)}" w:lineRule="auto"/></w:pPr>
                </w:pPrDefault>
              </w:docDefaults>

              <w:style w:type="paragraph" w:styleId="QuizTitle">
                <w:name w:val="Quiz Title"/>
                <w:pPr><w:spacing w:after="{Twips(4)}"/></w:pPr>
                <w:rPr><w:b/><w:sz w:val="{HalfPoints(t.Title)}"/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="QuizMeta">
                <w:name w:val="Quiz Meta"/>
                <w:pPr><w:spacing w:after="{Twips(2)}"/></w:pPr>
                <w:rPr><w:sz w:val="{HalfPoints(t.Caption)}"/><w:color w:val="{secondary}"/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="SectionHeading">
                <w:name w:val="heading 1"/>
                <w:pPr>
                  <w:outlineLvl w:val="0"/>
                  <w:spacing w:before="{Twips(18)}" w:after="{Twips(8)}"/>
                  <w:keepNext/>
                </w:pPr>
                <w:rPr><w:b/><w:sz w:val="{HalfPoints(t.Subtitle)}"/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="QuestionPrompt">
                <w:name w:val="Question Prompt"/>
                <w:pPr>
                  <w:spacing w:before="{Twips(10)}" w:after="{Twips(4)}"/>
                  <w:keepNext/>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="QuestionOption">
                <w:name w:val="Question Option"/>
                <w:pPr><w:ind w:left="{Twips(24)}"/><w:spacing w:after="{Twips(2)}"/></w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="AnswerLine">
                <w:name w:val="Answer Line"/>
                <w:pPr>
                  <w:ind w:left="{Twips(24)}"/>
                  <w:spacing w:before="{Twips(8)}" w:after="{Twips(8)}"/>
                  <w:pBdr><w:bottom w:val="single" w:sz="4" w:color="{HexOnly(theme.Colors.Border, "D0D0D0")}"/></w:pBdr>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="AnswerKey">
                <w:name w:val="Answer Key"/>
                <w:pPr>
                  <w:ind w:left="{Twips(24)}"/>
                  <w:spacing w:before="{Twips(4)}" w:after="{Twips(6)}"/>
                  <w:pBdr><w:left w:val="single" w:sz="12" w:space="6" w:color="{HexOnly(theme.Colors.Success, "2E6B4F")}"/></w:pBdr>
                </w:pPr>
                <w:rPr><w:color w:val="{HexOnly(theme.Colors.Success, "2E6B4F")}"/></w:rPr>
              </w:style>
            </w:styles>
            """;
    }

    // --- Document -----------------------------------------------------------

    private static string DocumentXml(CompiledQuiz quiz, ThemeTokens theme, WordExportOptions options, ImagePlan images)
    {
        var sb = new StringBuilder();

        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine($"""<w:document xmlns:w="{WordMlNamespace}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">""");
        sb.AppendLine("<w:body>");

        AppendParagraph(sb, "QuizTitle", quiz.Title);

        if (!string.IsNullOrWhiteSpace(quiz.Description))
            AppendDescription(sb, quiz.Description);

        var meta = new List<string>
        {
            quiz.QuestionCount == 1 ? "1 question" : $"{quiz.QuestionCount} questions",
            quiz.TotalPoints == 1 ? "1 point" : $"{Num(quiz.TotalPoints)} points",
        };

        if (quiz.TimeLimitMinutes is { } minutes) meta.Add($"{minutes} minutes");

        AppendParagraph(sb, "QuizMeta", string.Join("   \u00b7   ", meta));

        var passLine = PassMarkLine(quiz);
        if (passLine is not null) AppendParagraph(sb, "QuizMeta", passLine);

        if (options.ShowAnswers) AppendParagraph(sb, "QuizMeta", "Answer key");

        foreach (var section in quiz.Sections)
            AppendSection(sb, section, options, images);

        // sectPr belongs at the end of the BODY. Inside a paragraph it means a
        // section break instead, which is a different thing entirely.
        sb.AppendLine($"""
            <w:sectPr>
              <w:pgSz w:w="{A4WidthTwips}" w:h="{A4HeightTwips}"/>
              <w:pgMar w:top="{MarginTwips}" w:right="{MarginTwips}" w:bottom="{MarginTwips}" w:left="{MarginTwips}"/>
            </w:sectPr>
            """);

        sb.AppendLine("</w:body>");
        sb.AppendLine("</w:document>");

        return sb.ToString();
    }

    private static string? PassMarkLine(CompiledQuiz quiz)
    {
        if (quiz.PassMarkBasis == PassMarkBasis.QuestionCount)
        {
            if (quiz.GradeableQuestionCount <= 0) return null;

            var label = quiz.QuestionsToPass == 1 ? "question" : "questions";
            return $"Pass mark: {quiz.PassPercentage}% of the questions "
                   + $"({quiz.QuestionsToPass} of {quiz.GradeableQuestionCount} {label} correct)";
        }

        if (quiz.TotalPoints <= 0) return null;

        return $"Pass mark: {quiz.PassPercentage}% of the points "
               + $"({Num(quiz.PointsToPass)} of {Num(quiz.TotalPoints)})";
    }

    private static void AppendSection(StringBuilder sb, CompiledSection section, WordExportOptions options, ImagePlan images)
    {
        var points = section.TotalPoints == 1 ? "1 point" : $"{Num(section.TotalPoints)} points";
        AppendParagraph(sb, "SectionHeading", $"{section.Title}   ({points})");

        if (section.Questions.Count == 0)
        {
            AppendParagraph(sb, "QuizMeta", "(no questions in this section)");
            return;
        }

        foreach (var question in section.Questions)
            AppendQuestion(sb, question, options, images);
    }

    private static void AppendQuestion(StringBuilder sb, CompiledQuestion compiled, WordExportOptions options, ImagePlan images)
    {
        var q = compiled.Question;
        var points = q.Points == 1 ? "1 point" : $"{Num(q.Points)} points";

        AppendParagraph(sb, "QuestionPrompt", $"{compiled.Number}.  {q.Prompt}   [{points}]");

        AppendImage(sb, images.Find(q.ImageRelativePath));

        switch (q)
        {
            case MultipleChoiceSingleQuestion mc:
                AppendOptions(sb, mc.Choices.Select(c => (c.Text, c.IsCorrect)), options);
                break;

            case MultipleChoiceMultipleQuestion mc:
                AppendOptions(sb, mc.Choices.Select(c => (c.Text, c.IsCorrect)), options);
                break;

            case TrueFalseQuestion tf:
                AppendOptions(sb, new[] { ("True", tf.CorrectAnswer), ("False", !tf.CorrectAnswer) }, options);
                break;

            case MatchingQuestion m:
                foreach (var pair in m.Pairs.Where(p => !string.IsNullOrWhiteSpace(p.Left)))
                    AppendParagraph(sb, "QuestionOption", $"____   {pair.Left}");

                if (compiled.MatchingOptions is { Count: > 0 } rights)
                {
                    var index = 0;
                    foreach (var right in rights)
                    {
                        AppendParagraph(sb, "QuestionOption", $"{Letter(index)}.  {right}");
                        index++;
                    }
                }

                break;

            case SequenceQuestion sequence:
                // Items in the compiler's presentation order, each with a blank
                // for the taker to write the position it belongs in.
                var seqOrder = compiled.SequencePresentation
                    ?? Enumerable.Range(0, sequence.Items.Count).ToList();

                foreach (var sourceIndex in seqOrder)
                {
                    if (sourceIndex < 0 || sourceIndex >= sequence.Items.Count) continue;
                    AppendParagraph(sb, "QuestionOption", $"____   {sequence.Items[sourceIndex]}");
                }

                break;

            case ShortAnswerQuestion:
                AppendParagraph(sb, "AnswerLine", string.Empty);
                break;

            case DropdownQuestion dd:
                // Same options as single choice — on paper a dropdown reads as a
                // labelled option list.
                AppendOptions(sb, dd.Choices.Select(c => (c.Text, c.IsCorrect)), options);
                break;

            case NumericQuestion numeric:
                // An answer line, with the unit shown after it when present so
                // the taker knows what to write.
                AppendParagraph(sb, "AnswerLine",
                    string.IsNullOrWhiteSpace(numeric.Unit) ? string.Empty : $"________  {numeric.Unit}");
                break;

            case EssayQuestion essay:
                var lines = Math.Clamp(essay.SuggestedWordCount / 10, 3, 40);
                for (var i = 0; i < lines; i++)
                    AppendParagraph(sb, "AnswerLine", string.Empty);
                break;

            case FillInTheBlankQuestion:
                // The {{n}} tokens are already in the prompt, so it is complete.
                break;
        }

        if (!string.IsNullOrWhiteSpace(q.Hint))
            AppendParagraph(sb, "QuizMeta", $"Hint: {q.Hint}");

        if (options.ShowAnswers)
            AppendParagraph(sb, "AnswerKey", $"Answer: {AnswerText(compiled)}");
    }

    private static void AppendOptions(
        StringBuilder sb, IEnumerable<(string Text, bool IsCorrect)> choices, WordExportOptions options)
    {
        var index = 0;
        foreach (var (text, isCorrect) in choices)
        {
            // A tick, not a colour: the export gets printed, often in
            // monochrome, and a colour-only cue vanishes.
            var mark = options.ShowAnswers && isCorrect ? "\u2713  " : string.Empty;

            AppendParagraph(sb, "QuestionOption", $"{mark}{Letter(index)}.  {text}");
            index++;
        }
    }

    /// <summary>
    /// One paragraph. Newlines become &lt;w:br/&gt; because a newline inside a
    /// w:t is only whitespace to Word -- a multi-line prompt would otherwise
    /// collapse onto one line.
    /// </summary>
    /// <summary>
    /// Writes the description as Word paragraphs, honouring the safelist.
    ///
    /// Bullet lists are a paragraph per item with a bullet glyph and a left
    /// indent, NOT a real numbering.xml list. A proper list needs an
    /// abstractNum/num definition in a separate part of the package and a
    /// numId reference per paragraph -- a lot of moving parts to verify blind,
    /// for a description. The glyph-and-indent form reads identically on the
    /// page and is a single self-contained paragraph.
    /// </summary>
    private static void AppendDescription(StringBuilder sb, string description)
    {
        foreach (var block in DescriptionParser.Parse(description))
        {
            switch (block)
            {
                case DescriptionParagraph paragraph:
                    sb.Append("<w:p><w:pPr><w:pStyle w:val=\"QuizMeta\"/></w:pPr>");
                    AppendRuns(sb, paragraph.Runs);
                    sb.Append("</w:p>");
                    break;

                case DescriptionList list:
                    foreach (var item in list.Items)
                    {
                        // ind = left indent in twips; the bullet sits at the
                        // start of the run so it moves with the text.
                        sb.Append("<w:p><w:pPr><w:pStyle w:val=\"QuizMeta\"/>")
                          .Append("<w:ind w:left=\"360\" w:hanging=\"180\"/></w:pPr>");

                        sb.Append("<w:r><w:t xml:space=\"preserve\">\u2022  </w:t></w:r>");
                        AppendRuns(sb, item);
                        sb.Append("</w:p>");
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Emits each run as its own w:r, so bold and italic can change per run.
    /// A run's rPr carries w:b / w:i; a line-break run becomes w:br.
    /// </summary>
    private static void AppendRuns(StringBuilder sb, IReadOnlyList<DescriptionRun> runs)
    {
        foreach (var run in runs)
        {
            if (run.IsLineBreak)
            {
                sb.Append("<w:r><w:br/></w:r>");
                continue;
            }

            sb.Append("<w:r>");

            if (run.Bold || run.Italic)
            {
                sb.Append("<w:rPr>");
                if (run.Bold) sb.Append("<w:b/>");
                if (run.Italic) sb.Append("<w:i/>");
                sb.Append("</w:rPr>");
            }

            sb.Append("<w:t xml:space=\"preserve\">").Append(Esc(run.Text)).Append("</w:t></w:r>");
        }
    }

    private static void AppendParagraph(StringBuilder sb, string styleId, string? text)
    {
        sb.Append("<w:p><w:pPr><w:pStyle w:val=\"").Append(styleId).Append("\"/></w:pPr>");

        if (!string.IsNullOrEmpty(text))
        {
            sb.Append("<w:r>");

            var lines = Clean(text).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append("<w:br/>");

                // xml:space="preserve" always: without it Word strips leading
                // and trailing whitespace, so an indented option loses its
                // indent and "A.  text" becomes "A. text".
                sb.Append("<w:t xml:space=\"preserve\">").Append(Esc(lines[i])).Append("</w:t>");
            }

            sb.Append("</w:r>");
        }

        sb.AppendLine("</w:p>");
    }

    private static string AnswerText(CompiledQuestion compiled) => compiled.Question switch
    {
        MultipleChoiceSingleQuestion q =>
            q.Choices.FirstOrDefault(c => c.IsCorrect)?.Text ?? "(no correct answer marked)",

        MultipleChoiceMultipleQuestion q =>
            q.Choices.Any(c => c.IsCorrect)
                ? string.Join(", ", q.Choices.Where(c => c.IsCorrect).Select(c => c.Text))
                : "(no correct answer marked)",

        TrueFalseQuestion q => q.CorrectAnswer ? "True" : "False",

        NumericQuestion q =>
            (q.Tolerance > 0
                ? $"{q.Target.ToString(System.Globalization.CultureInfo.InvariantCulture)} (± {q.Tolerance.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
                : q.Target.ToString(System.Globalization.CultureInfo.InvariantCulture))
            + (string.IsNullOrWhiteSpace(q.Unit) ? "" : $" {q.Unit}"),

        DropdownQuestion q =>
            q.Choices.FirstOrDefault(c => c.IsCorrect)?.Text ?? "(no correct answer marked)",

        ShortAnswerQuestion q =>
            q.AcceptedAnswers.Count > 0 ? string.Join("  /  ", q.AcceptedAnswers) : "(no accepted answer)",

        FillInTheBlankQuestion q =>
            q.Blanks.Count > 0
                ? string.Join("   ", q.Blanks.Select(b =>
                    $"{{{{{b.Ordinal}}}}} = {(b.AcceptedAnswers.Count > 0 ? string.Join(" / ", b.AcceptedAnswers) : "(none)")}"))
                : "(no blanks)",

        MatchingQuestion q => string.Join("   ", q.Pairs.Select(p => $"{p.Left} -> {p.Right}")),

        SequenceQuestion q => string.Join(" -> ", q.Items),

        EssayQuestion q => string.IsNullOrWhiteSpace(q.RubricNotes) ? "(graded by hand)" : q.RubricNotes,

        _ => string.Empty,
    };

    // --- Text handling ------------------------------------------------------

    /// <summary>
    /// Strips characters XML 1.0 forbids. A prompt pasted from elsewhere can
    /// carry a control character, and a single one makes Word declare the whole
    /// document unreadable rather than skipping that run.
    /// </summary>
    private static string Clean(string text)
    {
        if (text.All(IsXmlSafe)) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (IsXmlSafe(c)) sb.Append(c);

        return sb.ToString();
    }

    private static bool IsXmlSafe(char c) =>
        c is '\t' or '\n' or '\r'
        || (c >= 0x20 && c <= 0xD7FF)
        || (c >= 0xE000 && c <= 0xFFFD);

    private static string Esc(string text) => SecurityElement.Escape(text) ?? string.Empty;

    private static string HexOnly(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        // Word wants a bare hex triplet. A leading '#' makes it silently fall
        // back to automatic, and an 8-digit CSS colour with alpha is not
        // something Word understands at all -- so drop the alpha.
        var hex = value.TrimStart('#');
        if (hex.Length == 8) hex = hex[..6];

        return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? hex.ToUpperInvariant() : fallback;
    }

    private static string FirstFontName(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily)) return "Georgia";

        // A CSS font stack means nothing to Word: it takes one name. Take the
        // first and strip any quotes.
        var first = fontFamily.Split(',')[0].Trim().Trim('"', '\'');

        return string.IsNullOrWhiteSpace(first) ? "Georgia" : first;
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Letter(int index) =>
        index < 26
            ? ((char)('A' + index)).ToString()
            : (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Emits the inline drawing for a question's image. The structure mirrors
    /// what Word writes: an inline wrapper with an EMU extent, a picture with a
    /// blip whose r:embed points at the relationship built for this image. Every
    /// id here comes from the shared plan, so the reference always resolves.
    /// </summary>
    private static void AppendImage(StringBuilder sb, PlannedImage? image)
    {
        if (image is null) return;

        var (cx, cy) = EmuExtent(image.WidthPx, image.HeightPx);

        // docPr id must be unique and non-zero across the document, or Word
        // complains. The plan hands out a fresh id per drawing -- per export, so
        // the output is deterministic and two exports never share a counter.
        var docPrId = image.Owner.NextDrawingId();

        sb.Append("<w:p><w:r><w:drawing>");
        sb.Append($"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">");
        sb.Append($"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/>");
        sb.Append($"<wp:docPr id=\"{docPrId}\" name=\"Picture {docPrId}\"/>");
        sb.Append("<a:graphic>");
        sb.Append("<a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">");
        sb.Append("<pic:pic>");
        sb.Append("<pic:nvPicPr>");
        sb.Append($"<pic:cNvPr id=\"{docPrId}\" name=\"Picture {docPrId}\"/>");
        sb.Append("<pic:cNvPicPr/>");
        sb.Append("</pic:nvPicPr>");
        sb.Append("<pic:blipFill>");
        sb.Append($"<a:blip r:embed=\"{image.RelationshipId}\"/>");
        sb.Append("<a:stretch><a:fillRect/></a:stretch>");
        sb.Append("</pic:blipFill>");
        sb.Append("<pic:spPr>");
        sb.Append($"<a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>");
        sb.Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>");
        sb.Append("</pic:spPr>");
        sb.Append("</pic:pic>");
        sb.Append("</a:graphicData>");
        sb.Append("</a:graphic>");
        sb.Append("</wp:inline>");
        sb.Append("</w:drawing></w:r></w:p>");
    }

    /// <summary>
    /// Pixel size to an EMU extent, capped to the page's text width so a large
    /// image does not overflow the margins. 9525 EMU per pixel at 96 DPI;
    /// 5,486,400 EMU is six inches, a safe content width for a default page.
    /// </summary>
    private static (long Cx, long Cy) EmuExtent(int widthPx, int heightPx)
    {
        const long EmuPerPixel = 9525;
        const long MaxWidth = 5_486_400;

        var cx = widthPx * EmuPerPixel;
        var cy = heightPx * EmuPerPixel;

        if (cx > MaxWidth)
        {
            cy = cy * MaxWidth / cx;
            cx = MaxWidth;
        }

        // Never zero: a zero extent renders nothing and can upset the layout.
        return (Math.Max(cx, 1), Math.Max(cy, 1));
    }

    // --- Image plan ---------------------------------------------------------

    /// <summary>One planned image: its bytes, media filename, relationship id, size.</summary>
    private sealed class PlannedImage
    {
        public required ImagePlan Owner { get; init; }
        public required string QuizPath { get; init; }
        public required byte[] Bytes { get; init; }
        public required string MediaFileName { get; init; }
        public required string RelationshipId { get; init; }
        public required string Extension { get; init; }
        public required int WidthPx { get; init; }
        public required int HeightPx { get; init; }
    }

    /// <summary>
    /// The set of images to embed, keyed by quiz path so a reused image is
    /// planned once. Ids and filenames are assigned in insertion order.
    /// </summary>
    private sealed class ImagePlan
    {
        private readonly Dictionary<string, PlannedImage> _byPath = new(StringComparer.Ordinal);
        private readonly List<PlannedImage> _ordered = new();
        private int _drawingId;

        public IReadOnlyList<PlannedImage> All => _ordered;

        public bool Contains(string quizPath) => _byPath.ContainsKey(quizPath);

        /// <summary>
        /// The next unique, non-zero docPr id for a drawing in this export. Per
        /// plan, so ids are deterministic within a document and never shared
        /// between two exports.
        /// </summary>
        public int NextDrawingId() => ++_drawingId;

        public void Add(string quizPath, byte[] bytes)
        {
            var index = _ordered.Count + 1;
            var ext = ExtensionFor(quizPath);

            // Default to a sensible size when the header cannot be read, so an
            // unrecognised image still lays out rather than collapsing to zero.
            var size = ImageDimensions.Read(bytes) ?? (400, 300);

            var image = new PlannedImage
            {
                Owner = this,
                QuizPath = quizPath,
                Bytes = bytes,
                MediaFileName = $"image{index}.{ext}",
                RelationshipId = $"rIdImg{index}",
                Extension = ext,
                WidthPx = size.Item1,
                HeightPx = size.Item2,
            };

            _byPath[quizPath] = image;
            _ordered.Add(image);
        }

        public PlannedImage? Find(string? quizPath) =>
            quizPath is not null && _byPath.TryGetValue(quizPath, out var image) ? image : null;

        public IEnumerable<string> DistinctExtensions =>
            _ordered.Select(i => i.Extension).Distinct(StringComparer.Ordinal);

        private static string ExtensionFor(string path)
        {
            var ext = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

            return ext switch
            {
                "png" => "png",
                "jpg" or "jpeg" => "jpg",
                "gif" => "gif",
                "bmp" => "bmp",
                "webp" => "webp",
                _ => "png",
            };
        }
    }

    private static string ImageContentType(string extension) => extension switch
    {
        "png" => "image/png",
        "jpg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "webp" => "image/webp",
        _ => "application/octet-stream",
    };
}

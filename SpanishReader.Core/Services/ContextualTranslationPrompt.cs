using System.Text.RegularExpressions;
using SpanishReader.Core.Models;

namespace SpanishReader.Core.Services;

public static class ContextualTranslationPrompt
{
    private const int ParagraphExcerptLength = 900;
    private static readonly Regex ParagraphBreak = new(@"(?:\r\n|\r|\n)[ \t]*(?:\r\n|\r|\n)+", RegexOptions.Compiled);

    public const string System = @"
You are a Spanish-to-English translation engine.

Your task is to translate exactly ONE Spanish sentence into natural, accurate English.

Rules:

- Translate only the sentence marked TARGET_SENTENCE.
- Use the surrounding context only to resolve ambiguity, such as pronouns, word meanings, idioms, and verb tense.
- Never translate, summarize, or reproduce the surrounding context.
- Always produce an English translations, even if the Spanish sentence is ambiguous.
- Preserve the meaning, tone, and intent of the original sentence.
- Return only the English translation, with no labels, explanations, quotation marks, or additional text.
";

    public static string CleanResponse(string response) =>
        Regex.Replace(response, @"<think>\s*</think>", string.Empty, RegexOptions.IgnoreCase).Trim();

    public static string Create(Story story, StorySentence selected)
    {
        var text = string.Concat(story.Sentences.Select(sentence => sentence.SpanishText));
        var offset = 0;
        foreach (var sentence in story.Sentences)
        {
            if (ReferenceEquals(sentence, selected)) break;
            offset += sentence.SpanishText.Length;
        }
        offset += selected.SpanishText.Length - selected.SpanishText.TrimStart().Length;

        // Sentence slices retain paragraph whitespace. Split the reconstructed
        // source so context reflects the exact paragraphs shown to the reader.
        var paragraphs = new List<(int Start, int End)>();
        var start = 0;
        foreach (Match match in ParagraphBreak.Matches(text))
        {
            paragraphs.Add((start, match.Index));
            start = match.Index + match.Length;
        }
        paragraphs.Add((start, text.Length));
        var index = paragraphs.FindIndex(p => offset >= p.Start && offset < p.End);
        if (index < 0) index = paragraphs.Count - 1;
        string At(int i)
        {
            if (i < 0 || i >= paragraphs.Count) return "(none)";
            var paragraph = text[paragraphs[i].Start..paragraphs[i].End].Trim();
            if (paragraph.Length <= ParagraphExcerptLength) return paragraph;

            // Keep nearby text, while the full selected sentence is supplied
            // separately below. Whole chapters in a single paragraph must not
            // overflow the model's fixed inference context.
            var startInParagraph = i == index - 1 ? paragraph.Length - ParagraphExcerptLength : 0;
            if (i == index)
            {
                var selectedOffset = Math.Clamp(offset - paragraphs[i].Start, 0, paragraph.Length);
                startInParagraph = Math.Clamp(selectedOffset - ParagraphExcerptLength / 2,
                    0, paragraph.Length - ParagraphExcerptLength);
            }
            return (startInParagraph > 0 ? "…" : "")
                + paragraph.Substring(startInParagraph, ParagraphExcerptLength)
                + (startInParagraph + ParagraphExcerptLength < paragraph.Length ? "…" : "");
        }

        return $"/no_think\n<CONTEXT>\nPrevious paragraph:\n{At(index - 1)}\n\nCurrent paragraph:\n{At(index)}\n\nNext paragraph:\n{At(index + 1)}\n</CONTEXT>\n\n<TARGET_SENTENCE>\n{selected.SpanishText.Trim()}\n</TARGET_SENTENCE>";
    }
}

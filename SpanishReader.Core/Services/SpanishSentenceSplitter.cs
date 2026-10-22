namespace SpanishReader.Core.Services;

/// <summary>Splits plain Spanish prose into ordered, lossless sentence slices.</summary>
public static class SpanishSentenceSplitter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sr", "Sra", "Srta", "Dr", "Dra", "D", "Dña", "Ud", "Uds", "Vd", "Vds",
        "Srto", "Prof", "Profa", "Ing", "Lic", "núm", "nro", "pág", "págs", "vol",
        "aprox", "av", "Avda", "p", "ej", "p.ej", "EE", "UU"
    };

    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sentences = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return sentences;

        var start = 0;
        var index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                var end = SkipWhitespace(text, index, out var lineBreaks);
                // A paragraph can end without punctuation. A wrapped line alone
                // must not turn a single sentence into two translation requests.
                if (lineBreaks >= 2) Append(end);
                index = end;
                continue;
            }

            if (!IsTerminal(text[index]) || IsNonterminalPeriod(text, index))
            {
                index++;
                continue;
            }

            var punctuationEnd = index + 1;
            while (punctuationEnd < text.Length && IsTerminal(text[punctuationEnd])) punctuationEnd++;
            var endOfSentence = punctuationEnd;
            while (endOfSentence < text.Length && IsClosingMark(text[endOfSentence])) endOfSentence++;
            var next = SkipWhitespace(text, endOfSentence, out var breaks);

            // Ellipses can represent a pause inside a sentence. Likewise, a dot
            // embedded in a word is not a boundary. These are prose heuristics,
            // rather than a linguistic parser; the source text is never rewritten.
            var ellipsis = text[index] == '…' ||
                (text[index] == '.' && punctuationEnd - index >= 2);
            if (text.AsSpan(index, punctuationEnd - index).ContainsAny('?', '!')) ellipsis = false;
            var continues = next < text.Length && breaks < 2 &&
                ((ellipsis && char.IsLower(text[next])) ||
                 (text[index] == '.' && !ellipsis && next == endOfSentence && char.IsLower(text[next])));
            if (!continues) Append(next);
            index = next;
        }
        Append(text.Length);
        return sentences;

        void Append(int end)
        {
            if (end <= start) return;
            var slice = text[start..end];
            if (!string.IsNullOrWhiteSpace(slice))
            {
                sentences.Add(slice);
                start = end;
            }
            else if (sentences.Count > 0)
            {
                sentences[^1] += slice;
                start = end;
            }
            // Leading blank lines remain attached to the first actual sentence.
        }
    }

    private static bool IsTerminal(char value) => value is '.' or '?' or '!' or '…';
    private static bool IsClosingMark(char value) => value is '»' or '”' or '’' or '"' or '\'' or ')' or ']';

    private static bool IsNonterminalPeriod(string text, int index)
    {
        if (text[index] != '.') return false;
        if (index + 1 < text.Length && text[index + 1] == '.') return false;
        if (index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]))
            return true;

        var start = index;
        while (start > 0 && (char.IsLetter(text[start - 1]) || text[start - 1] == '.')) start--;
        var word = text[start..index];
        return Abbreviations.Contains(word) || (word.Length == 1 && char.IsUpper(word[0]));
    }

    private static int SkipWhitespace(string text, int index, out int lineBreaks)
    {
        lineBreaks = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            if (text[index] == '\r')
            {
                lineBreaks++;
                if (index + 1 < text.Length && text[index + 1] == '\n') index++;
            }
            else if (text[index] is '\n' or '\u2028' or '\u2029') lineBreaks++;
            index++;
        }
        return index;
    }
}

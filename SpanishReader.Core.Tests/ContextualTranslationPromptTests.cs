using SpanishReader.Core.Services;
using SpanishReader.Core.Models;

namespace SpanishReader.Core.Tests;

public sealed class ContextualTranslationPromptTests
{
    [Fact]
    public void EmptyThinkingTagsAreRemovedBeforeTranslationIsUsed()
    {
        Assert.Equal("Translated sentence.",
            ContextualTranslationPrompt.CleanResponse("<think></think> Translated sentence. <THINK>\n </THINK>"));
        Assert.Empty(ContextualTranslationPrompt.CleanResponse(" <think> </think> "));
    }

    [Fact]
    public void LongParagraphsKeepNearbyContextAndTheTargetWithinAnExcerptBudget()
    {
        var selected = new StorySentence { SpanishText = "La llave estaba aquí." };
        var story = new Story
        {
            Sentences =
            [
                new() { SpanishText = new string('a', 2500) + " cerca antes\n\n" },
                new() { SpanishText = new string('b', 1500) + " " },
                selected,
                new() { SpanishText = " " + new string('c', 1500) + "\n\n" },
                new() { SpanishText = "después cerca " + new string('d', 2500) }
            ]
        };

        var prompt = ContextualTranslationPrompt.Create(story, selected);

        Assert.Contains("cerca antes", prompt);
        Assert.Contains("después cerca", prompt);
        Assert.Contains("<TARGET_SENTENCE>\nLa llave estaba aquí.\n</TARGET_SENTENCE>", prompt);
        Assert.InRange(prompt.Length, 1, 3000);
    }
}

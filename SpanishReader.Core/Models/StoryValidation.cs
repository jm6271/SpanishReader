namespace SpanishReader.Core.Models;

public static class StoryValidation
{
    public static IReadOnlyDictionary<string, string> Validate(Story story)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(story.Title))
            errors[nameof(Story.Title)] = "Enter a Spanish title.";
        if (!Enum.IsDefined(story.Level))
            errors[nameof(Story.Level)] = "Choose a CEFR level from A1 to C2.";
        if (string.IsNullOrWhiteSpace(story.StoryText))
            errors[nameof(Story.StoryText)] = "Enter the Spanish story text.";
        return errors;
    }
}

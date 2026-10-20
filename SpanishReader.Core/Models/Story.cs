namespace SpanishReader.Core.Models;

public class Story
{
    public string Title { get; set; } = string.Empty;
    public string EnglishTitle { get; set;  } = string.Empty;
    public CEFR Level { get; set; } = CEFR.A1;
    public string Author { get; set;  } = string.Empty;
    public string StoryText { get; set;  } = string.Empty;
    public DateTime DateAdded { get; set; } = DateTime.MinValue;
}

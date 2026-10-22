namespace SpanishReader.Core.Models;

public class Story
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EnglishTitle { get; set;  } = string.Empty;
    public CEFR Level { get; set; } = CEFR.A1;
    public string Author { get; set;  } = string.Empty;
    public List<StorySentence> Sentences { get; set; } = [];
    public DateTime DateAdded { get; set; } = DateTime.MinValue;
}

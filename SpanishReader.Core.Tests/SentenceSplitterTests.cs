using SpanishReader.Core.Services;

namespace SpanishReader.Core.Tests;

public class SentenceSplitterTests
{
    public static TheoryData<string, string[]> Sentences => new()
    {
        { "¡Hola! ¿Cómo estás? Bien.", ["¡Hola!", "¿Cómo estás?", "Bien."] },
        { "—«¡Hola, señor!» —dijo Ana.\n¿Vienes?", ["—«¡Hola, señor!»", "—dijo Ana.", "¿Vienes?"] },
        { "El Dr. Pérez vio a la Sra. Ruiz. J. García llegó.", ["El Dr. Pérez vio a la Sra. Ruiz.", "J. García llegó."] },
        { "Cuesta 3.14 euros. Compró pan, p. ej. integral.", ["Cuesta 3.14 euros.", "Compró pan, p. ej. integral."] },
        { "Vive en EE. UU. desde mayo. Volverá.", ["Vive en EE. UU. desde mayo.", "Volverá."] },
        { "No... todavía no. Espera… Quizá mañana.", ["No... todavía no.", "Espera…", "Quizá mañana."] },
        { "¿De verdad...?! sí. ¡Ven!", ["¿De verdad...?!", "sí.", "¡Ven!"] },
        { "Una frase\nque sigue\n\nOtro párrafo\r\n \r\nFin", ["Una frase\nque sigue", "Otro párrafo", "Fin"] },
        { "  Hola.\r\n\r\n    ¿Qué tal?\nSin punto final  ", ["Hola.", "¿Qué tal?", "Sin punto final"] },
        { "Hola.Mundo.¿Vienes?!Sí.", ["Hola.", "Mundo.", "¿Vienes?!", "Sí."] },
        { "Un fragmento sin puntuación", ["Un fragmento sin puntuación"] },
        { "\n\n  \tHola.\n\n  ", ["Hola."] }
    };

    [Theory]
    [MemberData(nameof(Sentences))]
    public void SplitsSpanishProseWithoutChangingTheOriginal(string text, string[] expected)
    {
        var actual = SpanishSentenceSplitter.Split(text);
        Assert.Equal(expected, actual.Select(sentence => sentence.Trim()));
        Assert.Equal(text, string.Concat(actual));
        Assert.All(actual, sentence => Assert.False(string.IsNullOrWhiteSpace(sentence)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\n\t")]
    public void EmptyDraftProducesNoSentences(string text) => Assert.Empty(SpanishSentenceSplitter.Split(text));
}

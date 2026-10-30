using System.Text;
using SpanishReader.Core.Services;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("""
        Prepare a bundled story (run from the repository root):
          dotnet run --project SpanishReader.ContentTool -- <story.md> [--output <story.json>]
            [--model <config.yml>] [--regenerate]
          dotnet run --project SpanishReader.ContentTool -- --validate <story.json>

        Output defaults to the input path with a .json extension. Existing English
        translations are reused for unchanged Spanish sentences unless --regenerate
        is supplied. Review the resulting English before distributing it.
        """);
    return 0;
}

try
{
    if (args[0] == "--validate" && args.Length == 2)
    {
        var content = BundledStoryContent.Deserialize(await File.ReadAllTextAsync(args[1]));
        BundledStoryContent.Validate(content, requireTranslations: true);
        Console.WriteLine($"Valid: {content.Id} ({content.Sentences.Count} translated sentences).");
        return 0;
    }

    var input = Path.GetFullPath(args[0]);
    if (!string.Equals(Path.GetExtension(input), ".md", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Input must be a UTF-8 Markdown (.md) story.");
    var output = Path.ChangeExtension(input, ".json");
    var model = Path.GetFullPath(Path.Combine("SpanishReader", "TranslationModels", "esen", "config.yml"));
    var regenerate = false;
    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--output" when i + 1 < args.Length: output = Path.GetFullPath(args[++i]); break;
            case "--model" when i + 1 < args.Length: model = Path.GetFullPath(args[++i]); break;
            case "--regenerate": regenerate = true; break;
            default: throw new ArgumentException($"Unknown or incomplete option: {args[i]}");
        }
    }
    if (!string.Equals(Path.GetExtension(output), ".json", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Output must have a .json extension.");

    var story = BundledStoryContent.ParseMarkdown(await File.ReadAllTextAsync(input));
    var previous = File.Exists(output) ? BundledStoryContent.Deserialize(await File.ReadAllTextAsync(output)) : null;
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    using var translator = new BergamotTranslationService(model);
    await BundledStoryContent.TranslateAsync(story, translator, previous, regenerate,
        (done, total) => Console.WriteLine($"Translated/reused {done}/{total}"), cancellation.Token);

    // A failed or cancelled translation never overwrites reviewed output. Write in
    // the destination directory and rename only once the complete JSON is ready.
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        await File.WriteAllTextAsync(temporary, BundledStoryContent.Serialize(story), new UTF8Encoding(false), cancellation.Token);
        File.Move(temporary, output, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporary)) File.Delete(temporary);
    }
    Console.WriteLine($"Wrote {output}. Review the English translations before release.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Content preparation failed: {error.Message}");
    return 1;
}

# Spanish-to-English offline model

The `TranslationModels/esen` directory contains the Mozilla `es-en` release model
`retrain_hr_HbNjJ60BTwmVTbhfFxuduA` selected from the official registry on
2026-10-21. The model, vocabulary, shortlist, and `config.yml` are bundled with
SpanishReader, so translation needs no network connection at runtime.

- Registry: https://storage.googleapis.com/moz-fx-translations-data--303e-prod-translations-data/db/models.json
- Files: https://storage.googleapis.com/moz-fx-translations-data--303e-prod-translations-data/models/es-en/retrain_hr_HbNjJ60BTwmVTbhfFxuduA/exported/
- Mozilla model project: https://github.com/mozilla/firefox-translations-models
- Included `LICENSE`: https://github.com/mozilla/firefox-translations-models/blob/main/LICENSE
- Engine wrapper (MPL-2.0): https://github.com/Freeesia/BergamotTranslatorSharp

Run `./tools/Download-TranslationModel.ps1` from the repository root to restore
the exact model files. The script verifies SHA-256 checksums of all three
decompressed files; the model checksum is published in Mozilla's registry and
the vocabulary/shortlist checksums were recorded from the pinned download.
Model updates must update the pinned URL and hashes together, then rerun the
translation tests. `config.yml` uses the same vocabulary for both languages and
`int8shiftAlphaAll` for this model's `alphas` weights.

`ITranslationService` lives in `SpanishReader.Core` and is registered as a
singleton by `App`. Inject it into a view model and call:

```csharp
string english = await translationService.TranslateAsync(spanishSentence);
```

The engine loads on the first request, runs off the UI thread, and serializes
requests because the wrapper does not promise thread safety. Cancellation can
stop queued work or skip inference after loading, but cannot interrupt a native
call already in progress. The DI container disposes the engine on window close.

Story import splits the original Spanish text into ordered sentences and translates
each one before saving the story and its sentences in a single SQLite transaction.
Sentence slices retain punctuation and whitespace, including paragraph breaks.
Translation failures leave empty English text and do not prevent import; the library
shows a dismissible warning with the number of missing translations. On-demand
translation of these missing values is reserved for future reader functionality.
Schema version 2 deliberately starts an empty library when upgrading from version 1.

Validation on Windows x64:

```powershell
dotnet test SpanishReader.Core.Tests/SpanishReader.Core.Tests.csproj --arch x64
dotnet build SpanishReader/SpanishReader.csproj -p:Platform=x64 -m:1 -p:UseSharedCompilation=false
```

The translation integration test uses these files and the package's native
library directly. Reader UI integration can use the service without referencing
Bergamot or WinUI from the view model.

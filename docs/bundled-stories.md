# Bundled stories

The app imports bundled JSON stories into the local library before its first library
read. A database ledger records each stable story ID separately from the story, so
later launches preserve edits and do not restore deleted stories. New IDs included
in a later release are added once, including for existing installations. Changing
an already imported bundle does not overwrite the user's copy.

Author stories as UTF-8 `.md` files in the repository's `BundledStories/` directory:

```markdown
---
id: a2-la-casa-al-final-del-camino
title: La casa al final del camino
englishTitle: The House at the End of the Road
level: A2
author: ChatGPT
---

Había una vez un joven llamado Daniel.

Daniel vivía en un pueblo pequeño.
```

Metadata uses a flat YAML subset: one `key: value` per line, with plain strings,
single-quoted strings (escape apostrophes by doubling them), or JSON-compatible
double-quoted strings. Full-line comments and blank lines are allowed. `id`,
`title`, and `level` are required; `englishTitle` and `author` are optional. Keep
the ID stable. The body is plain Spanish prose, without Markdown formatting;
blank lines separate paragraphs. The tool uses the app's sentence splitter and
preserves the prose's whitespace and paragraph breaks.

From the repository root, run:

```powershell
dotnet run --project SpanishReader.ContentTool -- BundledStories/a2-la-casa-al-final-del-camino.md
```

The tool uses the app's offline Bergamot engine and model under
`SpanishReader/TranslationModels/esen/`. It writes an adjacent `.json` file with
metadata and Spanish/English sentence pairs. An omitted English title is translated
too. Use `--model <absolute-or-relative-config.yml>` or `--output <story.json>` to
override those paths. The tool currently targets x64, matching the native engine.

Review and edit the English in the JSON before release. Rerunning the command
retains nonempty English for unchanged Spanish sentences, including repeated
sentences matched in occurrence order. It translates new/changed sentences and
fills missing English. `--regenerate` replaces all sentence translations; a
supplied English title is retained. Failed or cancelled runs leave existing output
untouched. Validate a prepared file without loading the engine:

```powershell
dotnet run --project SpanishReader.ContentTool -- --validate BundledStories/a2-la-casa-al-final-del-camino.json
```

Commit both the Markdown source and prepared JSON. Only JSON is copied into the
app's build/publish output; translation never runs during startup or builds. The
import validates all files and commits stories and ledger entries in one transaction,
so failures can be retried without partially importing a collection.

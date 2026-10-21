# SpanishReader

This is a WinUI 3 app that allows users to read stories in Spanish with easy translation of sentences
to English.

## Project layout

The WinUI 3 app is located in `SpanishReader/`. The core code of the app goes in `SpanishReader.Core/`.
All non-winui code goes in the core project. This makes testing easier. When code in the Core project
needs access to WinUI types and features, it should use an abstraction (see the navigation service as
an example).

## Coding conventions

- Use plenty of comments around code that is not immediately obvious.

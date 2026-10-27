namespace SpanishReader.Core.Models;

public enum LibraryChangeKind { Imported, Updated }

/// <summary>Identifies the completed operation when returning to the library.</summary>
public sealed record LibraryChange(Story Story, LibraryChangeKind Kind);

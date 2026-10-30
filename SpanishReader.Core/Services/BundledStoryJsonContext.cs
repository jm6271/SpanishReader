using System.Text.Json.Serialization;
using SpanishReader.Core.Models;

namespace SpanishReader.Core.Services;

// Explicit metadata keeps both installed story loading and Markdown preparation
// working when publishing disables System.Text.Json's reflection fallback.
[JsonSerializable(typeof(BundledStory))]
[JsonSerializable(typeof(string))]
internal partial class BundledStoryJsonContext : JsonSerializerContext;

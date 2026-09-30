using System.Text.Json;
using System.Text.Json.Serialization;

namespace HackerNews.Infrastructure.HackerNewsApi;

/// <summary>Raw item from https://hacker-news.firebaseio.com/v0/item/{id}.json.</summary>
internal sealed record HnItem(
    int Id,
    string? Type,
    string? By,
    string? Title,
    string? Url,
    int? Score,
    long? Time,
    int? Descendants,
    bool Deleted,
    bool Dead);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(HnItem))]
internal sealed partial class HackerNewsJsonContext : JsonSerializerContext;

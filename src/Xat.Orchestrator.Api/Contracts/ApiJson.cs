using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xat.Orchestrator.Api.Contracts;

/// <summary>The JSON settings of the public API. The API uses snake_case names, like the upstream services.</summary>
internal static class ApiJson
{
    /// <summary>
    /// Gets the serializer settings for all requests and responses. The settings are read-only from the start. Many requests share
    /// them, and settings that are still open get their last setup at the first use, which is not safe when two requests come at once.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

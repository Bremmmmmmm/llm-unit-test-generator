using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LlmUnitTestGenerator.Options;
using Microsoft.Extensions.Options;

namespace LlmUnitTestGenerator.Services;

public sealed class OllamaClient(HttpClient httpClient, IOptions<GeneratorOptions> options)
{
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var ollamaOptions = options.Value.Ollama;
        httpClient.BaseAddress = new Uri(ollamaOptions.BaseUrl);

        var request = new OllamaGenerateRequest(ollamaOptions.Model, prompt, false);
        var response = await httpClient.PostAsJsonAsync("/api/generate", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty response.");

        return body.Response;
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; init; } = string.Empty;
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KiwiMind.Application.Common.Interfaces;
using KiwiMind.Application.Common.Settings;
using KiwiMind.Application.Retrieval;
using KiwiMind.Domain.Enums;
using Microsoft.Extensions.Options;

namespace KiwiMind.Infrastructure.Chat;

public class GroqChatCompletionService : IChatCompletionService
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://api.groq.com/openai/v1/"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly GroqSettings _settings;

    public GroqChatCompletionService(IOptions<GroqSettings> options)
    {
        _settings = options.Value;
    }

    public async Task<string> GenerateAnswerAsync(
        IReadOnlyList<ChunkSearchResultDto> context,
        IReadOnlyList<ChatMessage> history,
        string question,
        CancellationToken cancellationToken)
    {
        if (context.Count == 0)
        {
            return $"I couldn't find any relevant passages in this knowledge base to answer: \"{question}\".";
        }

        var messages = new List<GroqMessage>
        {
            new("system",
                "You are a knowledge base assistant. Answer only using the numbered sources below. " +
                "Cite sources inline using their number in square brackets, e.g. [1]. " +
                "If the sources don't contain the answer, say you don't know.")
        };

        foreach (var message in history)
        {
            messages.Add(new GroqMessage(
                message.Role == MessageRole.User ? "user" : "assistant",
                message.Content));
        }

        var sourcesBlock = new StringBuilder();
        for (var i = 0; i < context.Count; i++)
        {
            sourcesBlock.AppendLine($"[{i + 1}] ({context[i].DocumentFileName}) {context[i].Content}");
        }

        messages.Add(new GroqMessage("user", $"Sources:\n{sourcesBlock}\nQuestion: {question}"));

        var requestBody = new GroqChatRequest(
            string.IsNullOrWhiteSpace(_settings.Model) ? "llama-3.3-70b-versatile" : _settings.Model,
            messages,
            0.2);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GroqChatResponse>(cancellationToken: cancellationToken);
        return result?.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
    }

    private sealed record GroqMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record GroqChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<GroqMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record GroqChatResponse(
        [property: JsonPropertyName("choices")] List<GroqChoice> Choices);

    private sealed record GroqChoice(
        [property: JsonPropertyName("message")] GroqMessage Message);
}

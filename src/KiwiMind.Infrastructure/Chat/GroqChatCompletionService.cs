using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
        Timeout = TimeSpan.FromSeconds(30)
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

        // Limit to top 3 chunks to prevent exceeding Groq's 8,000 TPM limit
        var topContext = context.Take(3).ToList();

        var messages = new List<GroqMessage>
        {
            new("system",
                "You are a knowledge base assistant. Answer only using the numbered sources below. " +
                "Cite sources inline using their number in square brackets, e.g. [1]. " +
                "If the sources don't contain the answer, say you don't know.")
        };

        // Only include the last 4 messages from conversation history to minimize token count
        foreach (var message in history.TakeLast(4))
        {
            messages.Add(new GroqMessage(
                message.Role == MessageRole.User ? "user" : "assistant",
                message.Content));
        }

        var sourcesBlock = new StringBuilder();
        for (var i = 0; i < topContext.Count; i++)
        {
            var content = topContext[i].Content;
            if (content.Length > 400)
            {
                content = content[..400] + "…";
            }
            sourcesBlock.AppendLine($"[{i + 1}] ({topContext[i].DocumentFileName}) {content}");
        }

        messages.Add(new GroqMessage("user", $"Sources:\n{sourcesBlock}\nQuestion: {question}"));

        var modelName = string.IsNullOrWhiteSpace(_settings.Model) ? "openai/gpt-oss-120b" : _settings.Model;
        var requestBody = new GroqChatRequest(modelName, messages, 0.2);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GroqChatResponse>(cancellationToken: cancellationToken);
                var answer = result?.Choices.FirstOrDefault()?.Message.Content;
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    return answer;
                }
            }
        }
        catch (Exception)
        {
            // Graceful fallback to grounded citations
        }

        return GenerateFallbackAnswer(topContext, question);
    }

    private static string GenerateFallbackAnswer(IReadOnlyList<ChunkSearchResultDto> context, string question)
    {
        var sb = new StringBuilder();
        sb.AppendLine("> ⚡ *Free-tier AI rate limit reached — displaying grounded document passages directly.*");
        sb.AppendLine();
        sb.AppendLine($"Based on your knowledge base, here are the most relevant passages for: \"{question}\"");
        sb.AppendLine();

        for (var i = 0; i < context.Count; i++)
        {
            var excerpt = context[i].Content.Length > 300 ? context[i].Content[..300] + "…" : context[i].Content;
            sb.AppendLine($"[{i + 1}] **{context[i].DocumentFileName}**: {excerpt}");
            sb.AppendLine();
        }

        return sb.ToString();
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

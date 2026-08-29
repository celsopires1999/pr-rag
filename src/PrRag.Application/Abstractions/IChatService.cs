using PrRag.Application.DTOs;

namespace PrRag.Application.Abstractions;

public interface IChatService
{
    Task<ChatResponse> AnswerAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);
}

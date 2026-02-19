namespace AspireWithLocalStack.Api.Models;

public record PublishMessageRequest(string Message, string? Subject = null);
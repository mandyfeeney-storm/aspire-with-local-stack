namespace AspireWithLocalStack.Api.Models;

public record PublishBatchRequest(List<BatchMessage> Messages);
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using AspireWithLocalStack.Api.Models;
using CreateTopicRequest = AspireWithLocalStack.Api.Models.CreateTopicRequest;
using PublishBatchRequest = AspireWithLocalStack.Api.Models.PublishBatchRequest;

namespace AspireWithLocalStack.Api.Endpoints;

public static class SnsEndpoints
{
    public static RouteGroupBuilder MapSnsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/")
            .WithTags("SNS Pub/Sub");

        // ========== Topic Management ==========
        
        group.MapGet("/topics", ListTopics)
            .WithName("ListTopics")
            .WithDescription("Lists all SNS topics");

        group.MapPost("/topics", CreateTopic)
            .WithName("CreateTopic")
            .WithDescription("Creates a new SNS topic (pub/sub broadcast channel)");

        group.MapDelete("/topics/{topicName}", DeleteTopic)
            .WithName("DeleteTopic")
            .WithDescription("Deletes an SNS topic");

        group.MapGet("/topics/{topicName}/attributes", GetTopicAttributes)
            .WithName("GetTopicAttributes")
            .WithDescription("Gets attributes and metadata for a topic");

        // ========== Subscription Management ==========
        
        group.MapGet("/topics/{topicName}/subscriptions", ListSubscriptions)
            .WithName("ListSubscriptionsByTopic")
            .WithDescription("Lists all subscriptions for a specific topic");

        group.MapPost("/topics/{topicName}/subscriptions/email", SubscribeEmail)
            .WithName("SubscribeEmail")
            .WithDescription("Subscribe an email address to receive messages from this topic");

        group.MapPost("/topics/{topicName}/subscriptions/sqs", SubscribeSqs)
            .WithName("SubscribeSQS")
            .WithDescription("Subscribe an SQS queue to receive messages from this topic (fan-out pattern)");

        group.MapDelete("/subscriptions/{subscriptionArn}", Unsubscribe)
            .WithName("Unsubscribe")
            .WithDescription("Removes a subscription from a topic");

        // ========== Publishing Messages ==========
        
        group.MapPost("/topics/{topicName}/publish", PublishMessage)
            .WithName("PublishMessage")
            .WithDescription("Publishes a message to all subscribers of this topic");

        group.MapPost("/topics/{topicName}/publish-batch", PublishBatchMessages)
            .WithName("PublishBatchMessages")
            .WithDescription("Publishes multiple messages to the topic in a single request");

        // ========== Health Check ==========
        
        group.MapGet("/topics/health", HealthCheck)
            .WithName("SNSHealthCheck")
            .WithDescription("Checks connectivity to SNS/LocalStack");

        return group;
    }
    
    private static async Task<IResult> ListTopics(IAmazonSimpleNotificationService snsClient)
    {
        try
        {
            var response = await snsClient.ListTopicsAsync();
            
            if (response?.Topics == null || response.Topics.Count == 0)
            {
                return Results.Ok(new { Message = "No topics found", Topics = Array.Empty<object>() });
            }

            var topics = response.Topics.Select(t => new
            {
                t.TopicArn,
                TopicName = GetTopicNameFromArn(t.TopicArn)
            });
            
            return Results.Ok(new { Topics = topics });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error listing topics: {ex.Message}");
        }
    }

    private static async Task<IResult> CreateTopic(CreateTopicRequest request, IAmazonSimpleNotificationService snsClient)
    {
        if (string.IsNullOrWhiteSpace(request.TopicName))
        {
            return Results.BadRequest(new { Message = "TopicName is required" });
        }

        try
        {
            var response = await snsClient.CreateTopicAsync(request.TopicName);
            
            return Results.Created($"/topics/{request.TopicName}", new 
            { 
                Message = $"Topic '{request.TopicName}' created successfully",
                response.TopicArn,
                request.TopicName
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error creating topic: {ex.Message}");
        }
    }

    private static async Task<IResult> DeleteTopic(string topicName, IAmazonSimpleNotificationService snsClient)
    {
        try
        {
            var topicArn = await GetTopicArnByName(snsClient, topicName);
            
            if (topicArn == null)
            {
                return Results.NotFound(new { Message = $"Topic '{topicName}' does not exist" });
            }

            await snsClient.DeleteTopicAsync(topicArn);
            
            return Results.Ok(new { Message = $"Topic '{topicName}' deleted successfully" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error deleting topic: {ex.Message}");
        }
    }

    private static async Task<IResult> GetTopicAttributes(string topicName, IAmazonSimpleNotificationService snsClient)
    {
        try
        {
            var topicArn = await GetTopicArnByName(snsClient, topicName);
            
            if (topicArn == null)
            {
                return Results.NotFound(new { Message = $"Topic '{topicName}' does not exist" });
            }

            var response = await snsClient.GetTopicAttributesAsync(topicArn);
            
            return Results.Ok(new
            {
                TopicArn = topicArn,
                TopicName = topicName,
                response.Attributes
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error getting topic attributes: {ex.Message}");
        }
    }
    
    private static async Task<IResult> ListSubscriptions(string topicName, IAmazonSimpleNotificationService snsClient)
    {
        try
        {
            var topicArn = await GetTopicArnByName(snsClient, topicName);
            
            if (topicArn == null)
            {
                return Results.NotFound(new { Message = $"Topic '{topicName}' does not exist" });
            }

            var response = await snsClient.ListSubscriptionsByTopicAsync(topicArn);
            
            if (response?.Subscriptions == null || response.Subscriptions.Count == 0)
            {
                return Results.Ok(new { Message = "No subscriptions found", Subscriptions = Array.Empty<object>() });
            }

            var subscriptions = response.Subscriptions.Select(s => new
            {
                s.SubscriptionArn,
                s.Protocol,
                s.Endpoint,
                s.TopicArn
            });
            
            return Results.Ok(new { TopicName = topicName, Subscriptions = subscriptions });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error listing subscriptions: {ex.Message}");
        }
    }

    private static async Task<IResult> SubscribeEmail(
        string topicName, 
        SubscribeEmailRequest request, 
        IAmazonSimpleNotificationService snsClient)
    {
        if (string.IsNullOrWhiteSpace(request.EmailAddress))
        {
            return Results.BadRequest(new { Message = "EmailAddress is required" });
        }

        try
        {
            var topicArn = await GetTopicArnByName(snsClient, topicName);
            
            if (topicArn == null)
            {
                return Results.NotFound(new { Message = $"Topic '{topicName}' does not exist" });
            }

            var response = await snsClient.SubscribeAsync(new SubscribeRequest
            {
                TopicArn = topicArn,
                Protocol = "email",
                Endpoint = request.EmailAddress
            });

            return Results.Ok(new
            {
                Message = $"Email subscription created. Check '{request.EmailAddress}' for confirmation email.",
                response.SubscriptionArn,
                Note = "With LocalStack, email confirmations won't actually be sent. Subscription is auto-confirmed."
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error subscribing email: {ex.Message}");
        }
    }

    private static async Task<IResult> SubscribeSqs(
        string topicName, 
        SubscribeSqsRequest request, 
        IAmazonSimpleNotificationService snsClient,
        IAmazonSQS sqsClient)
    {
        if (string.IsNullOrWhiteSpace(request.QueueName))
        {
            return Results.BadRequest(new { Message = "QueueName is required" });
        }

        try
        {
            var topicArn = await GetTopicArnByName(snsClient, topicName);
            
            if (topicArn == null)
            {
                return Results.NotFound(new { Message = $"Topic '{topicName}' does not exist" });
            }

            var queueUrlResponse = await sqsClient.GetQueueUrlAsync(request.QueueName);
            var attributesResponse = await sqsClient.GetQueueAttributesAsync(
                queueUrlResponse.QueueUrl,
                ["QueueArn"]
            );

            if (!attributesResponse.Attributes.TryGetValue("QueueArn", out var queueArn))
            {
                return Results.Problem("Failed to get queue ARN from SQS");
            }
            
            var response = await snsClient.SubscribeAsync(new SubscribeRequest
            {
                TopicArn = topicArn,
                Protocol = "sqs",
                Endpoint = queueArn
            });

            return Results.Ok(new
            {
                Message = $"SQS queue '{request.QueueName}' subscribed to topic '{topicName}'",
                response.SubscriptionArn,
                TopicArn = topicArn,
                QueueArn = queueArn,
                Note = "Messages published to this topic will now be sent to this SQS queue (fan-out pattern)"
            });
        }
        catch (QueueDoesNotExistException)
        {
            return Results.NotFound(new { Message = $"Queue '{request.QueueName}' does not exist. Create it first." });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error subscribing SQS queue: {ex.Message}");
        }
    }

    private static async Task<IResult> Unsubscribe(string subscriptionArn, IAmazonSimpleNotificationService snsClient)
    {
        if (subscriptionArn == "PendingConfirmation")
        {
            return Results.BadRequest(new { Message = "Cannot unsubscribe from pending subscriptions" });
        }

        try
        {
            await snsClient.UnsubscribeAsync(subscriptionArn);
            
            return Results.Ok(new { Message = "Subscription removed successfully" });
        }
        catch (NotFoundException)
        {
            return Results.NotFound(new { Message = "Subscription not found" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error unsubscribing: {ex.Message}");
        }
    }
    
    private static async Task<IResult> PublishMessage(
        string topicName, 
        PublishMessageRequest request, 
        IAmazonSimpleNotificationService snsClient)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest(new { Message = "Message is required" });
        }

        try
        {
            var topicArn = await GetTopicArnByName(snsClient, topicName);
            
            if (topicArn == null)
            {
                return Results.NotFound(new { Message = $"Topic '{topicName}' does not exist" });
            }
            
            var response = await snsClient.PublishAsync(new PublishRequest
            {
                TopicArn = topicArn,
                Message = request.Message,
                Subject = request.Subject
            });

            return Results.Ok(new
            {
                Message = "Message published to all subscribers",
                response.MessageId,
                TopicArn = topicArn,
                Note = "All subscribed endpoints (email, SQS, etc.) will receive this message"
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error publishing message: {ex.Message}");
        }
    }

    private static async Task<IResult> PublishBatchMessages(
        string topicName, 
        PublishBatchRequest request, 
        IAmazonSimpleNotificationService snsClient)
    {
        if (request.Messages.Count == 0)
        {
            return Results.BadRequest(new { Message = "Messages array is required and cannot be empty" });
        }

        if (request.Messages.Count > 10)
        {
            return Results.BadRequest(new { Message = "Maximum 10 messages per batch" });
        }

        try
        {
            var topicArn = await GetTopicArnByName(snsClient, topicName);
            
            if (topicArn == null)
            {
                return Results.NotFound(new { Message = $"Topic '{topicName}' does not exist" });
            }

            var batchEntries = request.Messages.Select((msg, index) => new PublishBatchRequestEntry
            {
                Id = index.ToString(),
                Message = msg.Message,
                Subject = msg.Subject
            }).ToList();
            
            var response = await snsClient.PublishBatchAsync(new Amazon.SimpleNotificationService.Model.PublishBatchRequest
            {
                TopicArn = topicArn,
                PublishBatchRequestEntries = batchEntries
            });

            return Results.Ok(new
            {
                Message = $"Published {response.Successful.Count} messages successfully",
                SuccessCount = response.Successful?.Count ?? 0,
                FailureCount = response.Failed?.Count ?? 0,
                Successful = response.Successful?.Select(s => new { s.Id, s.MessageId })
                             ?? Enumerable.Empty<object>(),
                Failed = response.Failed?.Select(f => new { f.Id, f.Message, f.Code })
                         ?? Enumerable.Empty<object>()
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error publishing batch messages: {ex.Message}");
        }
    }
    
    private static async Task<IResult> HealthCheck(IAmazonSimpleNotificationService snsClient)
    {
        try
        {
            await snsClient.ListTopicsAsync();
            return Results.Ok(new { Status = "Healthy", Service = "SNS", Message = "Connected to SNS/LocalStack" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"SNS connection failed: {ex.Message}");
        }
    }

    // ========== Helper Methods ==========

    private static async Task<string?> GetTopicArnByName(IAmazonSimpleNotificationService snsClient, string topicName)
    {
        var response = await snsClient.ListTopicsAsync();
        return response.Topics.FirstOrDefault(t => GetTopicNameFromArn(t.TopicArn) == topicName)?.TopicArn;
    }

    private static string GetTopicNameFromArn(string topicArn)
    {
        // ARN format: arn:aws:sns:region:account-id:topic-name
        return topicArn.Split(':').Last();
    }
}

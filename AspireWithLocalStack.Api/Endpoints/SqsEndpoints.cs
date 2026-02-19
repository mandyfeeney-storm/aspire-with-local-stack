using Amazon.SQS;
using Amazon.SQS.Model;
using AspireWithLocalStack.Api.Models;
using CreateQueueRequest = AspireWithLocalStack.Api.Models.CreateQueueRequest;

namespace AspireWithLocalStack.Api.Endpoints;

public static class SqsEndpoints
{
    public static RouteGroupBuilder MapSqsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/")
            .WithTags("SQS Messaging");

        // List all queues
        group.MapGet("/queues", ListQueues)
            .WithName("ListQueues")
            .WithDescription("Lists all SQS queues");

        // Create a queue
        group.MapPost("/queues", CreateQueue)
            .WithName("CreateQueue")
            .WithDescription("Creates a new SQS queue");

        // Send a message to a queue
        group.MapPost("/queues/{queueName}/messages", SendMessage)
            .WithName("SendMessage")
            .WithDescription("Sends a message to the specified queue");

        // Receive messages from a queue
        group.MapGet("/queues/{queueName}/messages", ReceiveMessages)
            .WithName("ReceiveMessages")
            .WithDescription("Receives messages from the specified queue");

        // Delete a message from a queue
        group.MapDelete("/queues/{queueName}/messages/{receiptHandle}", DeleteMessage)
            .WithName("DeleteMessage")
            .WithDescription("Deletes a message from the queue using its receipt handle");

        // Purge all messages from a queue
        group.MapDelete("/queues/{queueName}/messages", PurgeQueue)
            .WithName("PurgeQueue")
            .WithDescription("Purges all messages from the specified queue");

        // Delete a queue
        group.MapDelete("/queues/{queueName}", DeleteQueue)
            .WithName("DeleteQueue")
            .WithDescription("Deletes the specified queue");

        // Health check
        group.MapGet("queues/health", HealthCheck)
            .WithName("SQSHealthCheck")
            .WithDescription("Checks connectivity to SQS/LocalStack");

        return group;
    }

    private static async Task<IResult> ListQueues(IAmazonSQS sqsClient)
    {
        try
        {
            var response = await sqsClient.ListQueuesAsync(new ListQueuesRequest());
            
            if (response?.QueueUrls == null || response.QueueUrls.Count == 0)
            {
                return Results.Ok(new { Message = "No queues found", Queues = Array.Empty<string>() });
            }
            
            return Results.Ok(new { Queues = response.QueueUrls });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error listing queues: {ex.Message}");
        }
    }

    private static async Task<IResult> CreateQueue(CreateQueueRequest request, IAmazonSQS sqsClient)
    {
        if (string.IsNullOrWhiteSpace(request.QueueName))
        {
            return Results.BadRequest(new { Message = "QueueName is required" });
        }

        try
        {
            var response = await sqsClient.CreateQueueAsync(request.QueueName);
            return Results.Created($"/queues/{request.QueueName}", new 
            { 
                Message = $"Queue '{request.QueueName}' created successfully", 
                response.QueueUrl 
            });
        }
        catch (QueueNameExistsException)
        {
            var urlResponse = await sqsClient.GetQueueUrlAsync(request.QueueName);
            return Results.Ok(new 
            { 
                Message = $"Queue '{request.QueueName}' already exists", 
                urlResponse.QueueUrl 
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error creating queue: {ex.Message}");
        }
    }

    private static async Task<IResult> SendMessage(string queueName, AddMessageToQueueRequest request, IAmazonSQS sqsClient)
    {
        if (string.IsNullOrWhiteSpace(request.MessageBody))
        {
            return Results.BadRequest(new { Message = "MessageBody is required" });
        }

        try
        {
            var queueUrlResponse = await sqsClient.GetQueueUrlAsync(queueName);
            var response = await sqsClient.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = queueUrlResponse.QueueUrl,
                MessageBody = request.MessageBody
            });
            
            return Results.Ok(new 
            { 
                Message = "Message sent successfully",
                response.MessageId,
                QueueName = queueName
            });
        }
        catch (QueueDoesNotExistException)
        {
            return Results.NotFound(new { Message = $"Queue '{queueName}' does not exist" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error sending message: {ex.Message}");
        }
    }

    private static async Task<IResult> ReceiveMessages(string queueName, IAmazonSQS sqsClient, int maxMessages = 10)
    {
        try
        {
            var queueUrlResponse = await sqsClient.GetQueueUrlAsync(queueName);
            
            var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrlResponse.QueueUrl,
                MaxNumberOfMessages = Math.Min(maxMessages, 10),
                WaitTimeSeconds = 5,
                MessageAttributeNames = ["All"],
                MessageSystemAttributeNames = ["All"]
            });
            
            if (response?.Messages == null || response.Messages.Count == 0)
            {
                return Results.Ok(new { Message = "No messages available", Messages = Array.Empty<object>() });
            }
            
            var messages = response.Messages.Select(m => new
            {
                m.MessageId,
                m.Body,
                m.ReceiptHandle,
                m.Attributes
            });
            
            return Results.Ok(new { response.Messages.Count, Messages = messages });
        }
        catch (QueueDoesNotExistException)
        {
            return Results.NotFound(new { Message = $"Queue '{queueName}' does not exist" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error receiving messages: {ex.Message}");
        }
    }

    private static async Task<IResult> DeleteMessage(string queueName, string receiptHandle, IAmazonSQS sqsClient)
    {
        try
        {
            var queueUrlResponse = await sqsClient.GetQueueUrlAsync(queueName);
            
            await sqsClient.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = queueUrlResponse.QueueUrl,
                ReceiptHandle = receiptHandle
            });
            
            return Results.Ok(new { Message = "Message deleted successfully" });
        }
        catch (ReceiptHandleIsInvalidException)
        {
            return Results.BadRequest(new { Message = "Invalid receipt handle" });
        }
        catch (QueueDoesNotExistException)
        {
            return Results.NotFound(new { Message = $"Queue '{queueName}' does not exist" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error deleting message: {ex.Message}");
        }
    }

    private static async Task<IResult> PurgeQueue(string queueName, IAmazonSQS sqsClient)
    {
        try
        {
            var queueUrlResponse = await sqsClient.GetQueueUrlAsync(queueName);
            
            await sqsClient.PurgeQueueAsync(queueUrlResponse.QueueUrl);
            
            return Results.Ok(new { Message = $"All messages purged from queue '{queueName}'" });
        }
        catch (QueueDoesNotExistException)
        {
            return Results.NotFound(new { Message = $"Queue '{queueName}' does not exist" });
        }
        catch (PurgeQueueInProgressException)
        {
            return Results.Conflict(new { Message = "Purge already in progress. Wait 60 seconds before purging again." });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error purging queue: {ex.Message}");
        }
    }

    private static async Task<IResult> DeleteQueue(string queueName, IAmazonSQS sqsClient)
    {
        try
        {
            var queueUrlResponse = await sqsClient.GetQueueUrlAsync(queueName);
            
            await sqsClient.DeleteQueueAsync(queueUrlResponse.QueueUrl);
            
            return Results.Ok(new { Message = $"Queue '{queueName}' deleted successfully" });
        }
        catch (QueueDoesNotExistException)
        {
            return Results.NotFound(new { Message = $"Queue '{queueName}' does not exist" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error deleting queue: {ex.Message}");
        }
    }

    private static async Task<IResult> HealthCheck(IAmazonSQS sqsClient)
    {
        try
        {
            await sqsClient.ListQueuesAsync(new ListQueuesRequest());
            return Results.Ok(new { Status = "Healthy", Service = "SQS", Message = "Connected to SQS/LocalStack" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"SQS connection failed: {ex.Message}");
        }
    }
}

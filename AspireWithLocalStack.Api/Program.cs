using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using AspireWithLocalStack.Api;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAwsServices(builder.Configuration, builder.Environment);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AWS Demo API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

var s3Group = app.MapGroup("/")
    .WithTags("S3 Storage");

var sqsGroup = app.MapGroup("/")
    .WithTags("SQS Messaging");

// Endpoint: List all buckets
s3Group.MapGet("/buckets", async (IAmazonS3 s3Client) =>
{
    try
    {
        var response = await s3Client.ListBucketsAsync();
        
        if (response?.Buckets == null)
        {
            return Results.Ok(new { Message = "No buckets found or response was null", Buckets = Array.Empty<object>() });
        }
        
        var buckets = response.Buckets.Select(b => new { b.BucketName, b.CreationDate });
        return Results.Ok(buckets);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error listing buckets: {ex.Message}");
    }
})
.WithName("ListBuckets")
.WithDescription("Lists all S3 buckets");

// Endpoint: Create a bucket
s3Group.MapPost("/buckets", async (CreateBucketRequest request, IAmazonS3 s3Client) =>
{
    if (string.IsNullOrWhiteSpace(request.BucketName))
    {
        return Results.BadRequest(new { Message = "BucketName is required" });
    }

    // Validate bucket name (AWS S3 bucket naming rules)
    if (!IsValidBucketName(request.BucketName))
    {
        return Results.BadRequest(new { Message = "Invalid bucket name. Must be 3-63 characters, lowercase letters, numbers, dots, and hyphens only." });
    }

    try
    {
        await s3Client.PutBucketAsync(request.BucketName);
        return Results.Created($"/buckets/{request.BucketName}", new { Message = $"Bucket '{request.BucketName}' created successfully", request.BucketName });
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
    {
        return Results.Ok(new { Message = $"Bucket '{request.BucketName}' already exists", request.BucketName });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error creating bucket: {ex.Message}");
    }
})
.WithName("CreateBucket")
.WithDescription("Creates a new S3 bucket with the specified name");

// Endpoint: Delete a bucket
s3Group.MapDelete("/buckets/{bucketName}", async (string bucketName, IAmazonS3 s3Client) =>
{
    try
    {
        // First check if bucket has objects
        var listResponse = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucketName,
            MaxKeys = 1
        });

        if (listResponse?.S3Objects is { Count: > 0 })
        {
            return Results.BadRequest(new { Message = $"Bucket '{bucketName}' is not empty. Delete all files first." });
        }

        await s3Client.DeleteBucketAsync(bucketName);
        return Results.Ok(new { Message = $"Bucket '{bucketName}' deleted successfully" });
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { Message = $"Bucket '{bucketName}' does not exist" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error deleting bucket: {ex.Message}");
    }
})
.WithName("DeleteBucket")
.WithDescription("Deletes an empty S3 bucket");

// Endpoint: Upload a file
s3Group.MapPost("/buckets/{bucketName}/files/upload", async (string bucketName, HttpRequest request, IAmazonS3 s3Client) =>
{
    if (!request.HasFormContentType || request.Form.Files.Count == 0)
    {
        return Results.BadRequest("No file uploaded");
    }

    await EnsureBucketExistsAsync(s3Client, bucketName);

    var file = request.Form.Files[0];
    var key = file.FileName;

    await using var stream = file.OpenReadStream();

    var putRequest = new PutObjectRequest
    {
        BucketName = bucketName,
        Key = key,
        InputStream = stream,
        ContentType = file.ContentType
    };

    var response = await s3Client.PutObjectAsync(putRequest);

    return Results.Ok(new
    {
        Message = "File uploaded successfully",
        BucketName = bucketName,
        FileName = key,
        Size = file.Length,
        response.ETag
    });
})
.WithName("UploadFile")
.DisableAntiforgery() // For demo purposes
.WithDescription("Uploads a file to the specified S3 bucket");

// Endpoint: Upload text content
s3Group.MapPost("/buckets/{bucketName}/files/upload-text", async (string bucketName, string fileName, string content, IAmazonS3 s3Client) =>
{
    if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(content))
    {
        return Results.BadRequest("fileName and content are required");
    }

    await EnsureBucketExistsAsync(s3Client, bucketName);

    var putRequest = new PutObjectRequest
    {
        BucketName = bucketName,
        Key = fileName,
        ContentBody = content,
        ContentType = "text/plain"
    };

    var response = await s3Client.PutObjectAsync(putRequest);

    return Results.Ok(new
    {
        Message = "Text file uploaded successfully",
        BucketName = bucketName,
        FileName = fileName,
        response.ETag
    });
})
.WithName("UploadTextFile")
.WithDescription("Uploads text content as a file to the specified S3 bucket");

// Endpoint: List all files in a bucket
s3Group.MapGet("/buckets/{bucketName}/files", async (string bucketName, IAmazonS3 s3Client) =>
{
    try
    {
        var response = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucketName
        });

        if (response?.S3Objects == null)
        {
            return Results.BadRequest(new { Message = "No objects found or response was null", Files = Array.Empty<object>() });
        }

        var files = response.S3Objects.Select(obj => new
        {
            obj.Key,
            obj.Size,
            obj.LastModified,
            obj.ETag
        });

        return Results.Ok(files);
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { Message = $"Bucket '{bucketName}' doesn't exist. Create it first." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error listing files: {ex.Message}");
    }
})
.WithName("ListFiles")
.WithDescription("Lists all files in the specified bucket");

// Endpoint: Download a file
s3Group.MapGet("/buckets/{bucketName}/files/{fileName}", async (string bucketName, string fileName, IAmazonS3 s3Client) =>
{
    try
    {
        var response = await s3Client.GetObjectAsync(bucketName, fileName);
        return Results.Stream(response.ResponseStream, response.Headers.ContentType);
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { Message = $"File '{fileName}' not found in bucket '{bucketName}'" });
    }
})
.WithName("DownloadFile")
.WithDescription("Downloads a file from the specified S3 bucket");

// Endpoint: Get file metadata
s3Group.MapGet("/buckets/{bucketName}/files/{fileName}/metadata", async (string bucketName, string fileName, IAmazonS3 s3Client) =>
{
    try
    {
        var response = await s3Client.GetObjectMetadataAsync(bucketName, fileName);

        return Results.Ok(new
        {
            BucketName = bucketName,
            FileName = fileName,
            response.ContentLength,
            response.ContentType,
            response.LastModified,
            response.ETag
        });
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { Message = $"File '{fileName}' not found in bucket '{bucketName}'" });
    }
})
.WithName("GetFileMetadata")
.WithDescription("Gets metadata for a file without downloading it");

// Endpoint: Delete all files in a bucket
s3Group.MapDelete("/buckets/{bucketName}/files", async (string bucketName, IAmazonS3 s3Client) =>
{
    try
    {
        var deletedCount = 0;
        var errors = new List<string>();

        // List all objects in the bucket (with pagination support)
        string? continuationToken = null;
        do
        {
            var listResponse = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucketName,
                ContinuationToken = continuationToken
            });

            if (listResponse?.S3Objects == null || listResponse.S3Objects.Count == 0)
            {
                return Results.Ok(new { Message = $"Bucket '{bucketName}' is already empty", DeletedCount = 0 });
            }

            // Delete objects in batches (up to 1000 at a time)
            var objectsToDelete = listResponse.S3Objects
                .Select(obj => new KeyVersion { Key = obj.Key })
                .ToList();

            if (objectsToDelete.Count > 0)
            {
                var deleteResponse = await s3Client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = bucketName,
                    Objects = objectsToDelete
                });

                deletedCount += deleteResponse.DeletedObjects.Count;

                // Collect any errors
                if (deleteResponse.DeleteErrors is { Count: > 0 })
                {
                    foreach (var error in deleteResponse.DeleteErrors)
                    {
                        errors.Add($"{error.Key}: {error.Message}");
                    }
                }
            }

            continuationToken = listResponse.NextContinuationToken;
        }
        while (continuationToken != null);

        if (errors.Count > 0)
        {
            return Results.Ok(new
            {
                Message = $"Deleted {deletedCount} files with {errors.Count} errors",
                DeletedCount = deletedCount,
                Errors = errors
            });
        }

        return Results.Ok(new
        {
            Message = $"Successfully deleted all files from bucket '{bucketName}'",
            DeletedCount = deletedCount
        });
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { Message = $"Bucket '{bucketName}' does not exist" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error deleting files: {ex.Message}");
    }
})
.WithName("DeleteAllFiles")
.WithDescription("Deletes all files from the specified S3 bucket");

// Endpoint: Delete a file
s3Group.MapDelete("/buckets/{bucketName}/files/{fileName}", async (string bucketName, string fileName, IAmazonS3 s3Client) =>
{
    try
    {
        await s3Client.DeleteObjectAsync(bucketName, fileName);
        return Results.Ok(new { Message = $"File '{fileName}' deleted successfully from bucket '{bucketName}'" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error deleting file: {ex.Message}");
    }
})
.WithName("DeleteFile")
.WithDescription("Deletes a file from the specified S3 bucket");

// Health check endpoint
s3Group.MapGet("buckets/health", async (IAmazonS3 s3Client) =>
{
    try
    {
        // Try to list buckets to verify S3 connectivity
        await s3Client.ListBucketsAsync();
        return Results.Ok(new { Status = "Healthy", Service = "S3", Message = "Connected to S3/LocalStack" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"S3 connection failed: {ex.Message}");
    }
})
.WithName("S3HealthCheck")
.WithDescription("Checks connectivity to S3/LocalStack");

// ==================== SQS Endpoints ====================

// Endpoint: List all queues
sqsGroup.MapGet("/queues", async (IAmazonSQS sqsClient) =>
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
})
.WithName("ListQueues")
.WithDescription("Lists all SQS queues");

// Endpoint: Create a queue
sqsGroup.MapPost("/queues", async (CreateQueueRequest request, IAmazonSQS sqsClient) =>
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
        // Queue already exists - get its URL
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
})
.WithName("CreateQueue")
.WithDescription("Creates a new SQS queue");

// Endpoint: Send a message to a queue
sqsGroup.MapPost("/queues/{queueName}/messages", async (string queueName, AddMessageToQueueRequest request, IAmazonSQS sqsClient) =>
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
})
.WithName("SendMessage")
.WithDescription("Sends a message to the specified queue");

// Endpoint: Receive messages from a queue
sqsGroup.MapGet("/queues/{queueName}/messages", async (string queueName, IAmazonSQS sqsClient, int maxMessages = 10) =>
{
    try
    {
        var queueUrlResponse = await sqsClient.GetQueueUrlAsync(queueName);
        
        var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrlResponse.QueueUrl,
            MaxNumberOfMessages = Math.Min(maxMessages, 10), // AWS max is 10
            WaitTimeSeconds = 5, // Long polling
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
})
.WithName("ReceiveMessages")
.WithDescription("Receives messages from the specified queue");

// Endpoint: Delete a message from a queue
sqsGroup.MapDelete("/queues/{queueName}/messages/{receiptHandle}", async (string queueName, string receiptHandle, IAmazonSQS sqsClient) =>
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
})
.WithName("DeleteMessage")
.WithDescription("Deletes a message from the queue using its receipt handle");

// Endpoint: Purge all messages from a queue
sqsGroup.MapDelete("/queues/{queueName}/messages", async (string queueName, IAmazonSQS sqsClient) =>
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
})
.WithName("PurgeQueue")
.WithDescription("Purges all messages from the specified queue");

// Endpoint: Delete a queue
sqsGroup.MapDelete("/queues/{queueName}", async (string queueName, IAmazonSQS sqsClient) =>
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
})
.WithName("DeleteQueue")
.WithDescription("Deletes the specified queue");

sqsGroup.MapGet("queues/health", async (IAmazonSQS sqsClient) =>
    {
        try
        {
            // Try to list queues to verify SQS connectivity
            await sqsClient.ListQueuesAsync(new ListQueuesRequest());
            return Results.Ok(new { Status = "Healthy", Service = "SQS", Message = "Connected to SQS/LocalStack" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"SQS connection failed: {ex.Message}");
        }
    })
    .WithName("SQSHealthCheck")
    .WithDescription("Checks connectivity to SQS/LocalStack");


app.Run();

return;

async Task EnsureBucketExistsAsync(IAmazonS3 s3Client, string bucketName)
{
    try
    {
        await s3Client.PutBucketAsync(bucketName);
    }
    catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
    {
        // Bucket already exists, which is fine
    }
}

static bool IsValidBucketName(string bucketName)
{
    if (string.IsNullOrWhiteSpace(bucketName) || bucketName.Length < 3 || bucketName.Length > 63)
        return false;

    // Must start with a lowercase letter or number
    if (!char.IsLetterOrDigit(bucketName[0]) || char.IsUpper(bucketName[0]))
        return false;

    // Can only contain lowercase letters, numbers, dots, and hyphens
    return bucketName.All(c => char.IsLower(c) || char.IsDigit(c) || c == '.' || c == '-');
}

// Request models
record CreateBucketRequest(string BucketName);
record CreateQueueRequest(string QueueName);
record AddMessageToQueueRequest(string MessageBody);

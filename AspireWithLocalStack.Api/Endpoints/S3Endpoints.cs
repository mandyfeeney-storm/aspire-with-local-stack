using Amazon.S3;
using Amazon.S3.Model;
using AspireWithLocalStack.Api.Models;

namespace AspireWithLocalStack.Api.Endpoints;

public static class S3Endpoints
{
    public static RouteGroupBuilder MapS3Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/")
            .WithTags("S3 Storage");

        // List all buckets
        group.MapGet("/buckets", ListBuckets)
            .WithName("ListBuckets")
            .WithDescription("Lists all S3 buckets");

        // Create a bucket
        group.MapPost("/buckets", CreateBucket)
            .WithName("CreateBucket")
            .WithDescription("Creates a new S3 bucket with the specified name");

        // Delete a bucket
        group.MapDelete("/buckets/{bucketName}", DeleteBucket)
            .WithName("DeleteBucket")
            .WithDescription("Deletes an empty S3 bucket");

        // Upload a file
        group.MapPost("/buckets/{bucketName}/files/upload", UploadFile)
            .WithName("UploadFile")
            .DisableAntiforgery()
            .WithDescription("Uploads a file to the specified S3 bucket");

        // Upload text content
        group.MapPost("/buckets/{bucketName}/files/upload-text", UploadTextFile)
            .WithName("UploadTextFile")
            .WithDescription("Uploads text content as a file to the specified S3 bucket");

        // List all files in a bucket
        group.MapGet("/buckets/{bucketName}/files", ListFiles)
            .WithName("ListFiles")
            .WithDescription("Lists all files in the specified bucket");

        // Download a file
        group.MapGet("/buckets/{bucketName}/files/{fileName}", DownloadFile)
            .WithName("DownloadFile")
            .WithDescription("Downloads a file from the specified S3 bucket");

        // Get file metadata
        group.MapGet("/buckets/{bucketName}/files/{fileName}/metadata", GetFileMetadata)
            .WithName("GetFileMetadata")
            .WithDescription("Gets metadata for a file without downloading it");

        // Delete all files in a bucket
        group.MapDelete("/buckets/{bucketName}/files", DeleteAllFiles)
            .WithName("DeleteAllFiles")
            .WithDescription("Deletes all files from the specified S3 bucket");

        // Delete a file
        group.MapDelete("/buckets/{bucketName}/files/{fileName}", DeleteFile)
            .WithName("DeleteFile")
            .WithDescription("Deletes a file from the specified S3 bucket");

        // Health check
        group.MapGet("buckets/health", HealthCheck)
            .WithName("S3HealthCheck")
            .WithDescription("Checks connectivity to S3/LocalStack");

        return group;
    }

    private static async Task<IResult> ListBuckets(IAmazonS3 s3Client)
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
    }

    private static async Task<IResult> CreateBucket(CreateBucketRequest request, IAmazonS3 s3Client)
    {
        if (string.IsNullOrWhiteSpace(request.BucketName))
        {
            return Results.BadRequest(new { Message = "BucketName is required" });
        }

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
    }

    private static async Task<IResult> DeleteBucket(string bucketName, IAmazonS3 s3Client)
    {
        try
        {
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
    }

    private static async Task<IResult> UploadFile(string bucketName, HttpRequest request, IAmazonS3 s3Client)
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
    }

    private static async Task<IResult> UploadTextFile(string bucketName, string fileName, string content, IAmazonS3 s3Client)
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
    }

    private static async Task<IResult> ListFiles(string bucketName, IAmazonS3 s3Client)
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
    }

    private static async Task<IResult> DownloadFile(string bucketName, string fileName, IAmazonS3 s3Client)
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
    }

    private static async Task<IResult> GetFileMetadata(string bucketName, string fileName, IAmazonS3 s3Client)
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
    }

    private static async Task<IResult> DeleteAllFiles(string bucketName, IAmazonS3 s3Client)
    {
        try
        {
            var deletedCount = 0;
            var errors = new List<string>();

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
    }

    private static async Task<IResult> DeleteFile(string bucketName, string fileName, IAmazonS3 s3Client)
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
    }

    private static async Task<IResult> HealthCheck(IAmazonS3 s3Client)
    {
        try
        {
            await s3Client.ListBucketsAsync();
            return Results.Ok(new { Status = "Healthy", Service = "S3", Message = "Connected to S3/LocalStack" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"S3 connection failed: {ex.Message}");
        }
    }

    // Helper methods
    private static async Task EnsureBucketExistsAsync(IAmazonS3 s3Client, string bucketName)
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

    private static bool IsValidBucketName(string bucketName)
    {
        if (string.IsNullOrWhiteSpace(bucketName) || bucketName.Length < 3 || bucketName.Length > 63)
            return false;

        if (!char.IsLetterOrDigit(bucketName[0]) || char.IsUpper(bucketName[0]))
            return false;

        return bucketName.All(c => char.IsLower(c) || char.IsDigit(c) || c == '.' || c == '-');
    }
}

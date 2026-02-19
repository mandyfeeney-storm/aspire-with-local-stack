# AspireWithLocalStack - AWS Demo

A simple demonstration of using .NET Aspire with LocalStack to develop and test AWS services locally without requiring AWS credentials or incurring costs.

## What This Demo Does

This application shows how to:
- Use LocalStack to emulate different AWS services such as S3 and SQS locally
- Configure .NET Aspire to orchestrate LocalStack and a Web API
- Build a REST API that allows you to:
  - Perform operations on S3 buckets
  - Perform operations on SQS messaging queues

## Prerequisites

- .NET 10.0 SDK
- Docker Desktop
- An IDE (Visual Studio 2026, Rider, or VS Code)

## Getting Started

### 1. Start Docker Desktop

Make sure Docker Desktop is running before you start the application.

### 2. Run the Application

Open `AspireWithLocalStack.slnx` in your IDE and run the AppHost project, or run from the command line:
```bash
cd AspireWithLocalStack.AppHost
dotnet run
```

The Aspire Dashboard will open automatically in your browser.

### 3. Access the API

In the Aspire Dashboard:
1. Find the `api` resource
2. Click on its HTTPS endpoint URL

Example: `https://localhost:7271/swagger`

### 4. Try It Out

In Swagger UI, test these endpoints in order:

**S3 Storage - Create a bucket:**
- `POST /buckets/create`
- Request body: `{"bucketName": "my-test-bucket"}`

**S3 Storage - Upload a text file:**
- `POST /buckets/my-test-bucket/files/upload-text`
- Parameters: `fileName=hello.txt`, `content=Hello World`

**S3 Storage - List files:**
- `GET /buckets/my-test-bucket/files`

**S3 Storage - Download a file:**
- `GET /buckets/my-test-bucket/files/hello.txt`

**S3 Storage - Delete all files:**
- `DELETE /buckets/my-test-bucket/files`

**S3 Storage - Delete the bucket:**
- `DELETE /buckets/my-test-bucket`

**SQS Messaging - Create a queue:**
- `POST /queues/create`
- Request body: `{"queueName": "my-test-queue"}`

**SQS Messaging - Send a message:**
- `POST /queues/my-test-queue/messages`
- Request body: `{"messageBody": "Hello from SQS!"}`

**SQS Messaging - Receive messages:**
- `GET /queues/my-test-queue/messages?maxMessages=10`

**SQS Messaging - Delete a message:**
- `DELETE /queues/my-test-queue/messages/{receiptHandle}`
- (Use the receiptHandle from the receive messages response)

**SQS Messaging - Clean up:**
- `DELETE /queues/my-test-queue/messages/purge` (purge all messages)
- `DELETE /queues/my-test-queue` (delete the queue)

## Available Endpoints

### S3 Storage

#### Buckets
- `GET /buckets` - List all buckets
- `POST /buckets/create` - Create a new bucket
- `DELETE /buckets/{bucketName}` - Delete an empty bucket
- `DELETE /buckets/{bucketName}/files` - Delete all files in a bucket

#### Files
- `POST /buckets/{bucketName}/files/upload` - Upload a file (multipart form data)
- `POST /buckets/{bucketName}/files/upload-text` - Upload text content (query params)
- `GET /buckets/{bucketName}/files` - List files in a bucket
- `GET /buckets/{bucketName}/files/{fileName}` - Download a file
- `DELETE /buckets/{bucketName}/files/{fileName}` - Delete a specific file

#### System
- `GET /buckets/health` - Check S3 connectivity

### SQS Messaging

#### Queues
- `GET /queues` - List all queues
- `POST /queues` - Create a new queue
- `DELETE /queues/{queueName}` - Delete a queue

#### Messages
- `POST /queues/{queueName}/messages` - Send a message to a queue
- `GET /queues/{queueName}/messages` - Receive messages from a queue
- `DELETE /queues/{queueName}/messages/{receiptHandle}` - Delete a specific message
- `DELETE /queues/{queueName}/messages` - Purge all messages from a queue

#### System
- `GET /queues/health` - Check SQS connectivity

## Using the API with curl

### S3 Examples
```bash
# Create a bucket
curl -X POST https://localhost:7271/buckets \
  -H "Content-Type: application/json" \
  -d '{"bucketName":"my-bucket"}'

# Upload a file
curl -F "file=@myfile.txt" https://localhost:7271/buckets/my-bucket/files/upload

# List files
curl https://localhost:7271/buckets/my-bucket/files

# Download a file
curl https://localhost:7271/buckets/my-bucket/files/myfile.txt -o downloaded.txt

# Delete all files
curl -X DELETE https://localhost:7271/buckets/my-bucket/files

# Delete bucket
curl -X DELETE https://localhost:7271/buckets/my-bucket
```

### SQS Examples
```bash
# Create a queue
curl -X POST https://localhost:7271/queues \
  -H "Content-Type: application/json" \
  -d '{"queueName":"my-queue"}'

# Send a message
curl -X POST https://localhost:7271/queues/my-queue/messages \
  -H "Content-Type: application/json" \
  -d '{"messageBody":"Hello from curl!"}'

# Receive messages
curl https://localhost:7271/queues/my-queue/messages?maxMessages=5

# Purge all messages
curl -X DELETE https://localhost:7271/queues/my-queue/messages

# Delete queue
curl -X DELETE https://localhost:7271/queues/my-queue
```

## Inspecting LocalStack Directly

You can use AWS CLI within the LocalStack docker container to inspect S3 and SQS directly:

### S3 Commands
```bash
# Get the ID of your running LocalStack container
docker ps | grep localstack

# List buckets
docker exec -it <<localstack_container_id>> awslocal s3 ls

# List files in a bucket
docker exec -it <<localstack_container_id>> awslocal s3 ls s3://my-bucket
```

### SQS Commands
```bash
# List queues
docker exec -it <<localstack_container_id>> awslocal sqs list-queues

# Send a message
docker exec -it <<localstack_container_id>> awslocal sqs send-message \
  --queue-url http://localhost:4566/000000000000/my-queue \
  --message-body "Hello from CLI"

# Receive messages
docker exec -it <<localstack_container_id>> awslocal sqs receive-message \
  --queue-url http://localhost:4566/000000000000/my-queue
```

## Switching to Real AWS

To use real AWS instead of LocalStack, change `appsettings.json` in the API project:
```json
{
  "LocalStack": {
    "UseLocalStack": false
  },
  "AWS": {
    "Profile": "your-aws-profile",
    "Region": "us-east-1"
  }
}
```

No code changes needed - just configuration!

## Troubleshooting

**LocalStack won't start:**
- Make sure Docker Desktop is running
- Check that no other LocalStack instances are running: `docker ps`

**File upload in Swagger doesn't work:**
- Use Postman or Insomnia instead, with "Form Data" body type and a `file` field
- Or use curl: `curl -F "file=@myfile.txt" https://localhost:7271/buckets/bucket-name/files/upload`

**Connection errors:**
- Verify LocalStack container is running (should be green in Aspire Dashboard)
- Check the logs in the Aspire Dashboard for more details

**SQS message not appearing:**
- Wait a few seconds - there may be slight delay in LocalStack
- Check visibility timeout hasn't hidden the message
- Try receiving again with long polling (default behaviour)

**Receipt handle invalid:**
- Receipt handles expire after the visibility timeout
- Receive the message again to get a new receipt handle

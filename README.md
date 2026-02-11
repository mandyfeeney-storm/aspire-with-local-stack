# AspireWithLocalStack - S3 Demo

A simple demonstration of using .NET Aspire with LocalStack to develop and test AWS S3 operations locally without requiring AWS credentials or incurring costs.

## What This Demo Does

This application shows how to:
- Use LocalStack to emulate AWS S3 locally
- Configure .NET Aspire to orchestrate LocalStack and a Web API
- Build a REST API that allows you to:
  - List buckets 
  - Create buckets
  - Delete buckets
  - Upload files
  - List files
  - Download files
  - Delete files

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

**Create a bucket:**
- `POST /buckets/create`
- Request body: `{"bucketName": "my-test-bucket"}`

**Upload a text file:**
- `POST /buckets/my-test-bucket/files/upload-text`
- Parameters: `fileName=hello.txt`, `content=Hello World`

**List files:**
- `GET /buckets/my-test-bucket/files`

**Download a file:**
- `GET /buckets/my-test-bucket/files/hello.txt`

**Delete all files:**
- `DELETE /buckets/my-test-bucket/files`

**Delete the bucket:**
- `DELETE /buckets/my-test-bucket`

## Available Endpoints

### Buckets
- `GET /buckets` - List all buckets
- `POST /buckets/create` - Create a new bucket
- `DELETE /buckets/{bucketName}` - Delete an empty bucket
- `DELETE /buckets/{bucketName}/files` - Delete all files in a bucket

### Files
- `POST /buckets/{bucketName}/files/upload` - Upload a file (multipart form data)
- `POST /buckets/{bucketName}/files/upload-text` - Upload text content (query params)
- `GET /buckets/{bucketName}/files` - List files in a bucket
- `GET /buckets/{bucketName}/files/{fileName}` - Download a file
- `DELETE /buckets/{bucketName}/files/{fileName}` - Delete a specific file

### System
- `GET /health` - Check S3 connectivity

## Using the API with curl

```bash
# Create a bucket
curl -X POST https://localhost:7271/buckets/create \
  -H "Content-Type: application/json" \
  -d '{"bucketName":"my-bucket"}'

# Upload a file
curl -F "file=@myfile.txt" https://localhost:7271/buckets/my-bucket/files/upload

# List files
curl https://localhost:7271/buckets/my-bucket/files

# Download a file
curl https://localhost:7271/buckets/my-bucket/files/myfile.txt -o downloaded.txt
```

## Inspecting LocalStack Directly

You can use AWS CLI within the LocalStack docker container to inspect the S3 bucket directly:

1. Get the ID of your running LocalStack container
2. Use AWS CLI with the endpoint URL:

```bash
# Replace <<localstack_container_id>> with your actual container ID running in Docker
docker exec -it <<localstack_container_id>> awslocal --endpoint-url=http://localhost:4566 s3
docker exec -it <<localstack_container_id>> awslocal --endpoint-url=http://localhost:4566 s3 ls s3://my-bucket
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

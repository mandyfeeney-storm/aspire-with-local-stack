using Amazon;
using Aspire.Hosting.LocalStack.Container;
using LocalStack.Client.Enums;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var awsConfig = builder.AddAWSSDKConfig()
    .WithProfile("default")
    .WithRegion(RegionEndpoint.USEast1);

// Add LocalStack for local development only
var useLocalStack = builder.Configuration.GetValue("LocalStack:UseLocalStack", defaultValue: false);
IResourceBuilder<ILocalStackResource>? localstack = null;

if (useLocalStack)
{

    localstack = builder.AddLocalStack(awsConfig: awsConfig,
        configureContainer: container =>
        {
            container.EagerLoadedServices = [AwsService.S3];
            container.Lifetime = ContainerLifetime.Persistent;
            container.DebugLevel = 1;
            container.LogLevel = LocalStackLogLevel.Debug;
        });
}

var api = builder.AddProject<Projects.AspireWithLocalStack_Api>("api")
    .WithReference(awsConfig)
    .WithExternalHttpEndpoints();

if (localstack != null)
{
    api.WithReference(localstack);
    builder.UseLocalStack(localstack);
}

builder.Build().Run();

using Amazon;
using Aspire.Hosting.LocalStack.Container;
using LocalStack.Client.Enums;

var builder = DistributedApplication.CreateBuilder(args);

var awsConfig = builder.AddAWSSDKConfig()
    .WithProfile("default")
    .WithRegion(RegionEndpoint.USEast1);

var localstack = builder.AddLocalStack(awsConfig: awsConfig,
    configureContainer: container =>
    {
        container.EagerLoadedServices = [AwsService.S3];
        container.Lifetime = ContainerLifetime.Persistent;
        container.DebugLevel = 1;
        container.LogLevel = LocalStackLogLevel.Debug;
    });

builder.AddProject<Projects.AspireWithLocalStack_Api>("api")
    .WithReference(localstack)
    .WithReference(awsConfig);


builder.UseLocalStack(localstack);

builder.Build().Run();

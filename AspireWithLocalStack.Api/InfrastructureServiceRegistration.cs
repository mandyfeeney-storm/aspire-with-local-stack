using Amazon.S3;
using LocalStack.Client.Extensions;

namespace AspireWithLocalStack.Api;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddAwsServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Add S3 Storage
        services.AddS3Storage(configuration, environment);
        
        return services;
    }

    private static IServiceCollection AddS3Storage(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            bool.TryParse(configuration["LocalStack:UseLocalStack"], out var useLocalStack);
            
            if (useLocalStack)
            {
                // LocalStack mode: Configure connection from Aspire
                var localStackConnectionString = configuration.GetConnectionString("localstack");
                
                if (string.IsNullOrEmpty(localStackConnectionString))
                {
                    throw new InvalidOperationException(
                        "LocalStack configuration exists but no connection string found. " +
                        "Ensure the AppHost is running and LocalStack is configured correctly.");
                }

                // Extract LocalStack host and port from the connection string
                var uri = new Uri(localStackConnectionString);
                configuration["LocalStack:Config:LocalStackHost"] = uri.Host;
                configuration["LocalStack:Config:EdgePort"] = uri.Port.ToString();
                
                // Add LocalStack.NET client configuration
                services.AddLocalStack(configuration);
                services.AddDefaultAWSOptions(configuration.GetAWSOptions());
                services.AddAwsService<IAmazonS3>();
                
                return services;
            }
        }

        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonS3>();
        
        return services;
    }
}

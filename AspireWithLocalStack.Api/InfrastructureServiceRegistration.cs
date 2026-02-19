using Amazon.S3;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using LocalStack.Client.Extensions;

namespace AspireWithLocalStack.Api;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddAwsServices(
        this IServiceCollection services, 
        IConfiguration configuration, 
        IHostEnvironment environment)
    {
        var useLocalStack = ConfigureLocalStack(services, configuration, environment);
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());

        // Add individual AWS services
        services.AddS3Storage(useLocalStack);
        services.AddSqsMessaging(useLocalStack);
        services.AddSnsMessaging(useLocalStack);
        
        return services;
    }

    /// <summary>
    /// Configures LocalStack connection if running in Development with LocalStack enabled.
    /// Returns true if LocalStack should be used, false otherwise.
    /// </summary>
    private static bool ConfigureLocalStack(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return false;
        }

        if (!bool.TryParse(configuration["LocalStack:UseLocalStack"], out var useLocalStack) || !useLocalStack)
        {
            return false;
        }

        var localStackConnectionString = configuration.GetConnectionString("localstack");
        
        if (string.IsNullOrEmpty(localStackConnectionString))
        {
            throw new InvalidOperationException(
                "LocalStack configuration exists but no connection string found. " +
                "Ensure the AppHost is running and LocalStack is configured correctly.");
        }

        var uri = new Uri(localStackConnectionString);
        configuration["LocalStack:Config:LocalStackHost"] = uri.Host;
        configuration["LocalStack:Config:EdgePort"] = uri.Port.ToString();
        
        services.AddLocalStack(configuration);
        
        return true;
    }

    private static IServiceCollection AddS3Storage(
        this IServiceCollection services,
        bool useLocalStack)
    {
        if (useLocalStack)
        {
            services.AddAwsService<IAmazonS3>();
            Console.WriteLine("[S3 Storage] - Using LocalStack");
        }
        else
        {
            services.AddAWSService<IAmazonS3>();
            Console.WriteLine("[S3 Storage] Using real AWS");
        }
        
        return services;
    }
    
    private static IServiceCollection AddSqsMessaging(
        this IServiceCollection services,
        bool useLocalStack)
    {
        if (useLocalStack)
        {
            services.AddAwsService<IAmazonSQS>();
            Console.WriteLine("[SQS Messaging] - Using LocalStack");
        }
        else
        {
            services.AddAWSService<IAmazonSQS>();
            Console.WriteLine("[SQS Messaging] Using real AWS");
        }
        
        return services;
    }
    
    private static IServiceCollection AddSnsMessaging(
        this IServiceCollection services,
        bool useLocalStack)
    {
        if (useLocalStack)
        {
            services.AddAwsService<IAmazonSimpleNotificationService>();
            Console.WriteLine("[SNS Messaging] - Using LocalStack");
        }
        else
        {
            services.AddAWSService<IAmazonSimpleNotificationService>();
            Console.WriteLine("[SNS Messaging] Using real AWS");
        }
        
        return services;
    }
}

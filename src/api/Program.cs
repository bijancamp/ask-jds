using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Messaging.ServiceBus;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure;
using Api.Models;
using System.Linq;

class Program
{
    static async Task Main(string[] args)
    {
        var credential = new DefaultAzureCredential();
        var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices(services =>
            {
                // Configure Service Bus client
                services.AddSingleton<ServiceBusClient>(provider =>
                {
                    var connectionString = Environment.GetEnvironmentVariable("ServiceBusConnectionString");
                    if (!string.IsNullOrEmpty(connectionString))
                    {
                        // Check if the connection string specifies managed identity authentication
                        if (connectionString.Contains("Authentication=ManagedIdentity", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract the namespace from the connection string
                            var uri = connectionString.Split(';')[0].Replace("Endpoint=sb://", "");
                            return new ServiceBusClient(uri, credential);
                        }
                        
                        // Use connection string with shared access key
                        return new ServiceBusClient(connectionString);
                    }
                    
                    // Fallback to managed identity if no connection string
                    var serviceBusNamespace = Environment.GetEnvironmentVariable("ServiceBusNamespace");
                    if (!string.IsNullOrEmpty(serviceBusNamespace))
                    {
                        return new ServiceBusClient($"{serviceBusNamespace}.servicebus.windows.net", credential);
                    }
                    
                    throw new InvalidOperationException("ServiceBusConnectionString or ServiceBusNamespace environment variable must be set");
                });
                
                // Configure Azure AI Search clients
                services.AddSingleton<SearchIndexClient>(provider =>
                {
                    var searchServiceEndpoint = Environment.GetEnvironmentVariable("SearchServiceEndpoint");
                    if (string.IsNullOrEmpty(searchServiceEndpoint))
                    {
                        throw new InvalidOperationException("SearchServiceEndpoint environment variable must be set");
                    }
                    
                    var searchApiKey = Environment.GetEnvironmentVariable("SearchServiceApiKey");
                    
                    // In production, prefer managed identity authentication
                    var isProduction = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
                    
                    if (isProduction || string.IsNullOrEmpty(searchApiKey))
                    {
                        // Use managed identity in production or when no API key is available
                        return new SearchIndexClient(new Uri(searchServiceEndpoint), credential);
                    }
                    else
                    {
                        // Use API key authentication for local development
                        return new SearchIndexClient(new Uri(searchServiceEndpoint), new AzureKeyCredential(searchApiKey));
                    }
                });

                // Add SearchClient for querying the index
                services.AddSingleton<SearchClient>(provider =>
                {
                    var indexClient = provider.GetRequiredService<SearchIndexClient>();
                    var appSettings = provider.GetRequiredService<AppSettings>();
                    return indexClient.GetSearchClient(appSettings.SearchIndexName);
                });
                
                // Add configuration values as singleton services for easy access
                services.AddSingleton(provider => 
                {
                    return new AppSettings
                    {
                        ServiceBusQueueName = Environment.GetEnvironmentVariable("ServiceBusQueueName") ?? "job-descriptions",
                        SearchIndexName = Environment.GetEnvironmentVariable("SearchIndexName") ?? "job-descriptions"
                    };
                });
            })
            // .ConfigureAppConfiguration(config => 
            //     config.AddAzureKeyVault(new Uri(Environment.GetEnvironmentVariable("AZURE_KEY_VAULT_ENDPOINT")!), credential))
        .Build();

        // Check and create search index if needed
        await EnsureSearchIndexExists(host.Services);

        await host.RunAsync();
    }

    /// <summary>
    /// Ensures that the search index exists, creating it if it doesn't
    /// </summary>
    private static async Task EnsureSearchIndexExists(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var indexClient = services.GetRequiredService<SearchIndexClient>();
        var appSettings = services.GetRequiredService<AppSettings>();
        var indexName = appSettings.SearchIndexName;

        try
        {
            logger.LogInformation("Checking if search index '{IndexName}' exists...", indexName);
            
            // Check if the index exists
            bool indexExists = false;
            await foreach (var page in indexClient.GetIndexesAsync().AsPages())
            {
                if (page.Values.Any(index => index.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase)))
                {
                    indexExists = true;
                    break;
                }
            }

            // Create a new index definition
            var fieldBuilder = new FieldBuilder();
            var searchFields = fieldBuilder.Build(typeof(JobDescriptionDocument));

            var definition = new SearchIndex(indexName, searchFields);
            
            // Add suggesters for autocomplete functionality
            definition.Suggesters.Add(new SearchSuggester(
                "sg-job-descriptions",
                new[] { "Title", "Company", "Location" }
            ));

            if (!indexExists)
            {
                logger.LogInformation("Search index '{IndexName}' not found. Creating index...", indexName);
                await indexClient.CreateOrUpdateIndexAsync(definition);
                logger.LogInformation("Search index '{IndexName}' created successfully", indexName);
            }
            else
            {
                logger.LogInformation("Search index '{IndexName}' already exists. Updating index definition...", indexName);
                
                // Update the existing index with the new field definitions
                try {
                    await indexClient.CreateOrUpdateIndexAsync(definition);
                    logger.LogInformation("Search index '{IndexName}' updated successfully", indexName);
                }
                catch (RequestFailedException ex) when (ex.Status == 400)
                {
                    // If we can't update the index (e.g., because we're trying to make a non-filterable field filterable),
                    // we need to recreate it
                    logger.LogWarning("Cannot update index '{IndexName}'. Attempting to delete and recreate...", indexName);
                    
                    try
                    {
                        // Delete the existing index
                        await indexClient.DeleteIndexAsync(indexName);
                        logger.LogInformation("Deleted existing index '{IndexName}'", indexName);
                        
                        // Create a new index with the updated definition
                        await indexClient.CreateIndexAsync(definition);
                        logger.LogInformation("Recreated index '{IndexName}' with updated schema", indexName);
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogError(deleteEx, "Failed to delete and recreate index '{IndexName}'", indexName);
                        throw;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking or creating search index '{IndexName}'", indexName);
            throw; // Rethrow to fail startup if we can't create the index
        }
    }
}
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
                    var searchApiKey = Environment.GetEnvironmentVariable("SearchServiceApiKey");
                    
                    if (!string.IsNullOrEmpty(searchServiceEndpoint) && !string.IsNullOrEmpty(searchApiKey))
                    {
                        // Use API key authentication if available
                        return new SearchIndexClient(new Uri(searchServiceEndpoint), new AzureKeyCredential(searchApiKey));
                    }
                    
                    if (!string.IsNullOrEmpty(searchServiceEndpoint))
                    {
                        // Fallback to managed identity
                        return new SearchIndexClient(new Uri(searchServiceEndpoint), credential);
                    }
                    
                    throw new InvalidOperationException("SearchServiceEndpoint environment variable must be set");
                });

                // Add SearchClient for querying the index
                services.AddSingleton<SearchClient>(provider =>
                {
                    var indexClient = provider.GetRequiredService<SearchIndexClient>();
                    var indexName = Environment.GetEnvironmentVariable("SearchIndexName") ?? "job-descriptions";
                    return indexClient.GetSearchClient(indexName);
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
        var indexName = Environment.GetEnvironmentVariable("SearchIndexName") ?? "job-descriptions";

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

            if (!indexExists)
            {
                logger.LogInformation("Search index '{IndexName}' not found. Creating index...", indexName);
                
                // Create a new index
                var fieldBuilder = new FieldBuilder();
                var searchFields = fieldBuilder.Build(typeof(JobDescriptionDocument));

                var definition = new SearchIndex(indexName, searchFields);
                
                // Add suggesters for autocomplete functionality
                definition.Suggesters.Add(new SearchSuggester(
                    "sg-job-descriptions",
                    new[] { "Title", "Company", "Location" }
                ));

                await indexClient.CreateOrUpdateIndexAsync(definition);
                logger.LogInformation("Search index '{IndexName}' created successfully", indexName);
            }
            else
            {
                logger.LogInformation("Search index '{IndexName}' already exists", indexName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking or creating search index '{IndexName}'", indexName);
            throw; // Rethrow to fail startup if we can't create the index
        }
    }
}
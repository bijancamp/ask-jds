namespace Api.Models;

/// <summary>
/// Application settings loaded from environment variables
/// </summary>
public class AppSettings
{
    /// <summary>
    /// The name of the Service Bus queue for job descriptions
    /// </summary>
    public string ServiceBusQueueName { get; set; } = "job-descriptions";
    
    /// <summary>
    /// The name of the Azure AI Search index for job descriptions
    /// </summary>
    public string SearchIndexName { get; set; } = "job-descriptions";
    
    /// <summary>
    /// Flag indicating whether the application is running in production
    /// </summary>
    public bool IsProduction => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
}
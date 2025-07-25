using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Api.Models;
using Microsoft.Azure.Functions.Worker.Extensions.ServiceBus;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure;

namespace Api.Functions;

public class JobDescriptionProcessingFunctions
{
    private readonly ILogger<JobDescriptionProcessingFunctions> logger;
    private readonly SearchClient searchClient;

    public JobDescriptionProcessingFunctions(
        ILogger<JobDescriptionProcessingFunctions> logger,
        SearchClient searchClient)
    {
        this.logger = logger;
        this.searchClient = searchClient;
    }

    [Function("ProcessJobDescription")]
    public async Task ProcessJobDescription(
        [ServiceBusTrigger("%ServiceBusQueueName%", Connection = "ServiceBusConnectionString")] 
        string messageJson)
    {
        var correlationId = Guid.NewGuid().ToString();
        logger.LogInformation("[{CorrelationId}] Processing job description message from queue", correlationId);
        
        try
        {
            // Deserialize the message
            JobDescriptionMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<JobDescriptionMessage>(messageJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (message == null)
                {
                    logger.LogError("[{CorrelationId}] Failed to deserialize message: message is null", correlationId);
                    return; // Message will be sent to dead-letter queue after retry policy is exhausted
                }
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "[{CorrelationId}] Failed to deserialize message: invalid JSON format", correlationId);
                throw; // Rethrow to trigger retry policy
            }
            
            logger.LogInformation("[{CorrelationId}] Processing job description with ID: {MessageId}", correlationId, message.Id);
            
            // Validate the message payload
            if (string.IsNullOrWhiteSpace(message.Payload.Title))
            {
                logger.LogError("[{CorrelationId}] Invalid message payload: Title is required", correlationId);
                throw new InvalidOperationException("Title is required");
            }
            
            if (string.IsNullOrWhiteSpace(message.Payload.Company))
            {
                logger.LogError("[{CorrelationId}] Invalid message payload: Company is required", correlationId);
                throw new InvalidOperationException("Company is required");
            }
            
            if (string.IsNullOrWhiteSpace(message.Payload.Description))
            {
                logger.LogError("[{CorrelationId}] Invalid message payload: Description is required", correlationId);
                throw new InvalidOperationException("Description is required");
            }
            
            // Transform message to search document
            var document = JobDescriptionDocument.FromMessage(message);
            
            // Check for duplicates before indexing
            bool isDuplicate = await CheckForDuplicateDocument(document, correlationId);
            if (isDuplicate)
            {
                logger.LogWarning("[{CorrelationId}] Duplicate job description detected with ID: {DocumentId}. Skipping indexing.", 
                    correlationId, document.Id);
                return;
            }
            
            // Index the document in Azure AI Search
            await IndexDocumentAsync(document, correlationId);
            
            logger.LogInformation("[{CorrelationId}] Job description processed and indexed successfully with ID: {MessageId}", 
                correlationId, message.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{CorrelationId}] Error processing job description message", correlationId);
            throw; // Rethrow to trigger Service Bus retry policy
        }
    }
    
    /// <summary>
    /// Checks if a document with similar content already exists in the search index
    /// </summary>
    /// <param name="document">The document to check for duplicates</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <returns>True if a duplicate is found, false otherwise</returns>
    private async Task<bool> CheckForDuplicateDocument(JobDescriptionDocument document, string correlationId)
    {
        try
        {
            logger.LogInformation("[{CorrelationId}] Checking for duplicate job description: {Title} at {Company}", 
                correlationId, document.Title, document.Company);
            
            // First, check if the exact document ID already exists
            try
            {
                var existingDoc = await searchClient.GetDocumentAsync<JobDescriptionDocument>(document.Id);
                if (existingDoc != null)
                {
                    logger.LogInformation("[{CorrelationId}] Document with ID {DocumentId} already exists in the index", 
                        correlationId, document.Id);
                    return true;
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Document not found, which is expected for new documents
                logger.LogInformation("[{CorrelationId}] No existing document found with ID {DocumentId}", 
                    correlationId, document.Id);
            }
            
            // Next, check for similar documents based on title, company, and description similarity
            var searchOptions = new SearchOptions
            {
                Filter = $"Title eq '{document.Title.Replace("'", "''")}' and Company eq '{document.Company.Replace("'", "''")}'",
                Size = 1
            };
            
            var searchResults = await searchClient.SearchAsync<JobDescriptionDocument>("*", searchOptions);
            
            if (searchResults.Value.TotalCount > 0)
            {
                logger.LogInformation("[{CorrelationId}] Found potential duplicate with same title and company", correlationId);
                return true;
            }
            
            // If WorkdayId is provided, check for duplicates by WorkdayId
            if (!string.IsNullOrEmpty(document.WorkdayId))
            {
                searchOptions = new SearchOptions
                {
                    Filter = $"WorkdayId eq '{document.WorkdayId.Replace("'", "''")}'",
                    Size = 1
                };
                
                searchResults = await searchClient.SearchAsync<JobDescriptionDocument>("*", searchOptions);
                
                if (searchResults.Value.TotalCount > 0)
                {
                    logger.LogInformation("[{CorrelationId}] Found duplicate with same WorkdayId: {WorkdayId}", 
                        correlationId, document.WorkdayId);
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{CorrelationId}] Error checking for duplicate document", correlationId);
            // In case of error checking for duplicates, we'll proceed with indexing
            // to avoid losing data, but log the error
            return false;
        }
    }
    
    /// <summary>
    /// Indexes a document in Azure AI Search
    /// </summary>
    /// <param name="document">The document to index</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    private async Task IndexDocumentAsync(JobDescriptionDocument document, string correlationId)
    {
        try
        {
            logger.LogInformation("[{CorrelationId}] Indexing job description document with ID: {DocumentId}", 
                correlationId, document.Id);
            
            // Create a batch with a single document
            var batch = IndexDocumentsBatch.Upload(new[] { document });
            
            // Index the document with retry logic
            int maxRetries = 3;
            int retryCount = 0;
            bool indexed = false;
            
            while (!indexed && retryCount < maxRetries)
            {
                try
                {
                    IndexDocumentsResult result = await searchClient.IndexDocumentsAsync(batch);
                    
                    if (result.Results.All(r => r.Succeeded))
                    {
                        logger.LogInformation("[{CorrelationId}] Successfully indexed document with ID: {DocumentId}", 
                            correlationId, document.Id);
                        indexed = true;
                    }
                    else
                    {
                        var failedResults = result.Results.Where(r => !r.Succeeded).ToList();
                        logger.LogWarning("[{CorrelationId}] Failed to index document with ID: {DocumentId}. Errors: {Errors}", 
                            correlationId, document.Id, string.Join(", ", failedResults.Select(r => r.ErrorMessage)));
                        
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            // Exponential backoff: 1s, 2s, 4s
                            int delayMs = (int)Math.Pow(2, retryCount - 1) * 1000;
                            logger.LogInformation("[{CorrelationId}] Retrying indexing in {DelayMs}ms (attempt {RetryCount}/{MaxRetries})", 
                                correlationId, delayMs, retryCount, maxRetries);
                            await Task.Delay(delayMs);
                        }
                    }
                }
                catch (RequestFailedException ex)
                {
                    logger.LogError(ex, "[{CorrelationId}] Azure Search request failed while indexing document with ID: {DocumentId}. Status: {Status}", 
                        correlationId, document.Id, ex.Status);
                    
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        // Exponential backoff: 1s, 2s, 4s
                        int delayMs = (int)Math.Pow(2, retryCount - 1) * 1000;
                        logger.LogInformation("[{CorrelationId}] Retrying indexing in {DelayMs}ms (attempt {RetryCount}/{MaxRetries})", 
                            correlationId, delayMs, retryCount, maxRetries);
                        await Task.Delay(delayMs);
                    }
                    else
                    {
                        throw; // Rethrow after max retries
                    }
                }
            }
            
            if (!indexed)
            {
                throw new InvalidOperationException($"Failed to index document after {maxRetries} attempts");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{CorrelationId}] Error indexing document with ID: {DocumentId}", correlationId, document.Id);
            throw; // Rethrow to trigger Service Bus retry policy
        }
    }
}
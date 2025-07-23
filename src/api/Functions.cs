using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Azure.Messaging.ServiceBus;
using Api.Models;
using Microsoft.Azure.Functions.Worker.Extensions.ServiceBus;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure;
using Azure.AI.OpenAI;

namespace Api;
public class Functions
{
    private readonly ILogger logger;
    private readonly ServiceBusClient serviceBusClient;
    private readonly SearchClient searchClient;
    private readonly OpenAIClient openAIClient;
    private readonly AppSettings appSettings;

    public Functions(ILoggerFactory _loggerFactory, ServiceBusClient serviceBusClient, SearchClient searchClient, OpenAIClient openAIClient, AppSettings appSettings)
    {
        logger = _loggerFactory.CreateLogger<Functions>();
        this.serviceBusClient = serviceBusClient;
        this.searchClient = searchClient;
        this.openAIClient = openAIClient;
        this.appSettings = appSettings;
    }

    [Function("Hello")]
    public HttpResponseData Hello(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hello")]
        HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString("hello");
        return response;
    }
    
    [Function("GetJobDescriptions")]
    public async Task<HttpResponseData> GetJobDescriptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobdescriptions")]
        HttpRequestData req)
    {
        var correlationId = Guid.NewGuid().ToString();
        logger.LogInformation("[{CorrelationId}] Processing request to get job descriptions", correlationId);
        
        try
        {
            // Parse query parameters
            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            int pageSize = 50;
            int pageNumber = 1;
            
            if (int.TryParse(queryParams["pageSize"], out int parsedPageSize) && parsedPageSize > 0 && parsedPageSize <= 100)
            {
                pageSize = parsedPageSize;
            }
            
            if (int.TryParse(queryParams["pageNumber"], out int parsedPageNumber) && parsedPageNumber > 0)
            {
                pageNumber = parsedPageNumber;
            }
            
            string? searchText = queryParams["search"];
            
            // Create search options
            var searchOptions = new SearchOptions
            {
                Size = pageSize,
                Skip = (pageNumber - 1) * pageSize,
                IncludeTotalCount = true,
                OrderBy = { "IngestionTime desc" }
            };
            
            // Perform search
            SearchResults<JobDescriptionDocument> searchResults;
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                searchResults = await searchClient.SearchAsync<JobDescriptionDocument>("*", searchOptions);
            }
            else
            {
                searchResults = await searchClient.SearchAsync<JobDescriptionDocument>(searchText, searchOptions);
            }
            
            // Extract results
            var documents = new List<JobDescriptionDocument>();
            await foreach (var result in searchResults.GetResultsAsync())
            {
                documents.Add(result.Document);
            }
            
            // Create response
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                totalCount = searchResults.TotalCount,
                pageSize = pageSize,
                pageNumber = pageNumber,
                items = documents
            });
            
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{CorrelationId}] Error retrieving job descriptions", correlationId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }
    
    [Function("DeleteJobDescription")]
    public async Task<HttpResponseData> DeleteJobDescription(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "jobdescription/{id}")]
        HttpRequestData req,
        string id)
    {
        var correlationId = Guid.NewGuid().ToString();
        logger.LogInformation("[{CorrelationId}] Processing request to delete job description with ID: {Id}", correlationId, id);
        
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                logger.LogWarning("[{CorrelationId}] Invalid job description ID provided", correlationId);
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Valid job description ID is required" });
                return badResponse;
            }
            
            // Create a batch with a single delete operation
            var batch = IndexDocumentsBatch.Delete("id", new List<string> {id});
            
            // Delete the document
            IndexDocumentsResult result = await searchClient.IndexDocumentsAsync(batch);
            
            if (result.Results.All(r => r.Succeeded))
            {
                logger.LogInformation("[{CorrelationId}] Successfully deleted job description with ID: {Id}", correlationId, id);
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Job description deleted successfully" });
                return response;
            }
            else
            {
                var failedResults = result.Results.Where(r => !r.Succeeded).ToList();
                logger.LogWarning("[{CorrelationId}] Failed to delete job description with ID: {Id}. Errors: {Errors}", 
                    correlationId, id, string.Join(", ", failedResults.Select(r => r.ErrorMessage)));
                
                // Check if document not found
                if (failedResults.Any(r => r.ErrorMessage?.Contains("not found") == true))
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteAsJsonAsync(new { error = "Job description not found" });
                    return notFoundResponse;
                }
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "Failed to delete job description" });
                return errorResponse;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{CorrelationId}] Error deleting job description with ID: {Id}", correlationId, id);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    [Function("SubmitJobDescription")]
    public async Task<HttpResponseData> SubmitJobDescription(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobdescription")]
        HttpRequestData req)
    {
        logger.LogInformation("Processing job description submission request");

        try
        {
            // Read and deserialize the request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                logger.LogWarning("Empty request body received");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Request body is required" });
                return badResponse;
            }

            JobDescriptionSubmission? submission;
            try
            {
                submission = JsonSerializer.Deserialize<JobDescriptionSubmission>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Invalid JSON in request body");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Invalid JSON format" });
                return badResponse;
            }

            if (submission == null)
            {
                logger.LogWarning("Null submission after deserialization");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Invalid request data" });
                return badResponse;
            }

            // Validate the submission
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(submission);
            bool isValid = Validator.TryValidateObject(submission, validationContext, validationResults, true);

            if (!isValid)
            {
                logger.LogWarning("Validation failed for job description submission");
                var errors = validationResults.Select(vr => vr.ErrorMessage).ToArray();
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { errors });
                return badResponse;
            }

            // Create message for Service Bus
            var message = JobDescriptionMessage.Create(submission);
            var messageJson = JsonSerializer.Serialize(message);

            // Send message to Service Bus queue
            var sender = serviceBusClient.CreateSender(appSettings.ServiceBusQueueName);
            var serviceBusMessage = new ServiceBusMessage(messageJson)
            {
                ContentType = "application/json",
                MessageId = message.Id.ToString()
            };

            await sender.SendMessageAsync(serviceBusMessage);
            logger.LogInformation("Job description message sent to queue with ID: {MessageId}", message.Id);

            // Return success response
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new { id = message.Id, message = "Job description accepted for processing" });
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing job description submission");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
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

    [Function("Chat")]
    public async Task<HttpResponseData> Chat(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")]
        HttpRequestData req)
    {
        var correlationId = Guid.NewGuid().ToString();
        logger.LogInformation("[{CorrelationId}] Processing chat request", correlationId);

        try
        {
            // Read and deserialize the request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                logger.LogWarning("[{CorrelationId}] Empty request body received", correlationId);
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Request body is required" });
                return badResponse;
            }

            ChatRequest? chatRequest;
            try
            {
                chatRequest = JsonSerializer.Deserialize<ChatRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "[{CorrelationId}] Invalid JSON in request body", correlationId);
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Invalid JSON format" });
                return badResponse;
            }

            if (chatRequest == null)
            {
                logger.LogWarning("[{CorrelationId}] Null chat request after deserialization", correlationId);
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Invalid request data" });
                return badResponse;
            }

            // Validate the request
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(chatRequest);
            bool isValid = Validator.TryValidateObject(chatRequest, validationContext, validationResults, true);

            if (!isValid)
            {
                logger.LogWarning("[{CorrelationId}] Validation failed for chat request", correlationId);
                var errors = validationResults.Select(vr => vr.ErrorMessage).ToArray();
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { errors });
                return badResponse;
            }

            // Search for relevant job descriptions
            var searchResults = await SearchJobDescriptions(chatRequest.Message, correlationId);
            
            // Generate chat response using OpenAI
            var chatResponse = await GenerateChatResponse(chatRequest, searchResults, correlationId);

            // Return success response
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(chatResponse);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{CorrelationId}] Error processing chat request", correlationId);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Searches for relevant job descriptions based on the user's query
    /// </summary>
    private async Task<List<DocumentSource>> SearchJobDescriptions(string query, string correlationId)
    {
        try
        {
            logger.LogInformation("[{CorrelationId}] Searching for job descriptions with query: {Query}", correlationId, query);

            var searchOptions = new SearchOptions
            {
                Size = 5, // Limit to top 5 most relevant results
                IncludeTotalCount = false
            };

            var searchResults = await searchClient.SearchAsync<JobDescriptionDocument>(query, searchOptions);
            var sources = new List<DocumentSource>();

            await foreach (var result in searchResults.Value.GetResultsAsync())
            {
                var document = result.Document;
                var excerpt = TruncateText(document.Description, 200);
                
                sources.Add(new DocumentSource
                {
                    Id = document.Id,
                    Title = document.Title,
                    Company = document.Company,
                    Location = document.Location,
                    Excerpt = excerpt,
                    Score = result.Score ?? 0.0
                });
            }

            logger.LogInformation("[{CorrelationId}] Found {Count} relevant job descriptions", correlationId, sources.Count);
            return sources;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{CorrelationId}] Error searching job descriptions", correlationId);
            return new List<DocumentSource>();
        }
    }

    /// <summary>
    /// Generates a chat response using OpenAI based on the user's query and search results
    /// </summary>
    private async Task<ChatResponse> GenerateChatResponse(ChatRequest request, List<DocumentSource> sources, string correlationId)
    {
        try
        {
            logger.LogInformation("[{CorrelationId}] Generating chat response using OpenAI", correlationId);

            // Build context from search results
            var context = BuildContextFromSources(sources);
            
            // Build conversation messages for Azure OpenAI
            var chatMessages = new List<ChatRequestMessage>();
            
            // Add system message
            chatMessages.Add(new ChatRequestSystemMessage(
                "You are a helpful assistant that answers questions about job descriptions. " +
                "Use the provided job description context to answer the user's question. " +
                "If the context doesn't contain relevant information, say so politely. " +
                "Always be specific and reference the job titles and companies when relevant. " +
                "Keep your responses concise and helpful."
            ));
            
            // Add conversation history if provided
            foreach (var historyMessage in request.History)
            {
                if (historyMessage.Role.ToLower() == "user")
                {
                    chatMessages.Add(new ChatRequestUserMessage(historyMessage.Content));
                }
                else if (historyMessage.Role.ToLower() == "assistant")
                {
                    chatMessages.Add(new ChatRequestAssistantMessage(historyMessage.Content));
                }
            }

            // Add current user message with context
            var userMessageWithContext = $"Context from job descriptions:\n{context}\n\nUser question: {request.Message}";
            chatMessages.Add(new ChatRequestUserMessage(userMessageWithContext));

            // Create chat completion options
            var chatCompletionsOptions = new ChatCompletionsOptions(appSettings.OpenAIDeploymentName, chatMessages)
            {
                MaxTokens = 1000,
                Temperature = 0.7f
            };

            // Get chat completion from OpenAI
            var completionResponse = await openAIClient.GetChatCompletionsAsync(chatCompletionsOptions);
            var responseContent = completionResponse.Value.Choices[0].Message.Content;
            
            logger.LogInformation("[{CorrelationId}] Generated chat response successfully", correlationId);

            return new ChatResponse
            {
                Response = responseContent,
                Sources = sources,
                ConversationId = correlationId
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{CorrelationId}] Error generating chat response", correlationId);
            
            // Return a fallback response
            return new ChatResponse
            {
                Response = "I'm sorry, I encountered an error while processing your question. Please try again.",
                Sources = sources,
                ConversationId = correlationId
            };
        }
    }

    /// <summary>
    /// Builds context string from document sources
    /// </summary>
    private string BuildContextFromSources(List<DocumentSource> sources)
    {
        if (!sources.Any())
        {
            return "No relevant job descriptions found.";
        }

        var contextBuilder = new System.Text.StringBuilder();
        
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            contextBuilder.AppendLine($"Job {i + 1}:");
            contextBuilder.AppendLine($"Title: {source.Title}");
            contextBuilder.AppendLine($"Company: {source.Company}");
            contextBuilder.AppendLine($"Location: {source.Location}");
            contextBuilder.AppendLine($"Description: {source.Excerpt}");
            contextBuilder.AppendLine();
        }

        return contextBuilder.ToString();
    }

    /// <summary>
    /// Truncates text to a specified length with ellipsis
    /// </summary>
    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength - 3) + "...";
    }
}
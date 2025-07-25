using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Api.Models;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.AI.OpenAI;

namespace Api.Functions;

public class ChatFunctions
{
    private readonly ILogger<ChatFunctions> logger;
    private readonly SearchClient searchClient;
    private readonly OpenAIClient openAIClient;
    private readonly AppSettings appSettings;

    public ChatFunctions(
        ILogger<ChatFunctions> logger,
        SearchClient searchClient,
        OpenAIClient openAIClient,
        AppSettings appSettings)
    {
        this.logger = logger;
        this.searchClient = searchClient;
        this.openAIClient = openAIClient;
        this.appSettings = appSettings;
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
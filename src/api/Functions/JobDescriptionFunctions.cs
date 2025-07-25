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

namespace Api.Functions;

public class JobDescriptionFunctions
{
    private readonly ILogger<JobDescriptionFunctions> logger;
    private readonly ServiceBusClient serviceBusClient;
    private readonly SearchClient searchClient;
    private readonly AppSettings appSettings;

    public JobDescriptionFunctions(
        ILogger<JobDescriptionFunctions> logger,
        ServiceBusClient serviceBusClient,
        SearchClient searchClient,
        AppSettings appSettings)
    {
        this.logger = logger;
        this.serviceBusClient = serviceBusClient;
        this.searchClient = searchClient;
        this.appSettings = appSettings;
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
}
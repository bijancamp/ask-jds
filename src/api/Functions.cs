using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Azure.Messaging.ServiceBus;
using Api.Models;
using Microsoft.Azure.Functions.Worker.Extensions.ServiceBus;

namespace Api;
public class Functions
{
    private readonly ILogger logger;
    private readonly ServiceBusClient serviceBusClient;

    public Functions(ILoggerFactory _loggerFactory, ServiceBusClient serviceBusClient)
    {
        logger = _loggerFactory.CreateLogger<Functions>();
        this.serviceBusClient = serviceBusClient;
    }

    [Function("Hello")]
    public async Task<HttpResponseData> Hello(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hello")]
        HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString("hello");
        return response;
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
            var sender = serviceBusClient.CreateSender("job-descriptions");
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
        [ServiceBusTrigger("job-descriptions", Connection = "ServiceBusConnectionString")] 
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
            
            // TODO: In task #5, implement Azure AI Search indexing functionality
            // This will be implemented in the next task
            logger.LogInformation("[{CorrelationId}] Job description processed successfully with ID: {MessageId}", correlationId, message.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{CorrelationId}] Error processing job description message", correlationId);
            throw; // Rethrow to trigger Service Bus retry policy
        }
    }
}
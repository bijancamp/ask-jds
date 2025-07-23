using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Azure.Messaging.ServiceBus;
using Api.Models;

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
}
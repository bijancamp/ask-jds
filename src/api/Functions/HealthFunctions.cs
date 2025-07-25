using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Api.Functions;

public class HealthFunctions
{
    private readonly ILogger<HealthFunctions> logger;

    public HealthFunctions(ILogger<HealthFunctions> logger)
    {
        this.logger = logger;
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
}
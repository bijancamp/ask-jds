using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Api;
public class Functions
{
    private readonly ILogger logger;

    public Functions(ILoggerFactory _loggerFactory)
    {
        logger = _loggerFactory.CreateLogger<Functions>();
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
}
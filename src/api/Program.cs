using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Azure.Messaging.ServiceBus;

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
            })
            // .ConfigureAppConfiguration(config => 
            //     config.AddAzureKeyVault(new Uri(Environment.GetEnvironmentVariable("AZURE_KEY_VAULT_ENDPOINT")!), credential))
        .Build();

        await host.RunAsync();
    }
}
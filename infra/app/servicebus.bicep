param name string
param location string = resourceGroup().location
param tags object = {}
param queueName string = 'job-descriptions'

// Create Service Bus namespace
module serviceBusNamespace 'br/public:avm/res/service-bus/namespace:0.9.0' = {
  name: 'serviceBusNamespace'
  params: {
    name: name
    location: location
    tags: tags
    skuObject: {
      name: 'Basic'
    }
    disableLocalAuth: false
    queues: [
      {
        name: queueName
        maxDeliveryCount: 10
        lockDuration: 'PT5M'
        defaultMessageTimeToLive: 'P14D'
        deadLetteringOnMessageExpiration: true
        enablePartitioning: false
        requiresDuplicateDetection: false
        requiresSession: false
      }
    ]
  }
}

output serviceBusNamespaceName string = serviceBusNamespace.outputs.name
output serviceBusNamespaceId string = serviceBusNamespace.outputs.resourceId
output serviceBusConnectionString string = 'Endpoint=sb://${serviceBusNamespace.outputs.name}.servicebus.windows.net/;Authentication=Managed Identity'
output queueName string = queueName

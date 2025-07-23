@description('Name of the Azure OpenAI service')
param name string

@description('Location for the Azure OpenAI service')
param location string

@description('Tags to apply to the resource')
param tags object = {}

@description('SKU for the Azure OpenAI service')
param sku object = {
  name: 'S0'
}

@description('Model deployments to create')
param deployments array = [
  {
    name: 'gpt-4o'
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-08-06'
    }
    sku: {
      name: 'Standard'
      capacity: 10
    }
  }
]

resource openAIService 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: sku
  properties: {
    customSubDomainName: name
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

resource openAIDeployments 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = [for deployment in deployments: {
  parent: openAIService
  name: deployment.name
  properties: {
    model: deployment.model
  }
  sku: deployment.sku
}]

output openAIServiceName string = openAIService.name
output openAIServiceEndpoint string = openAIService.properties.endpoint
output openAIServiceId string = openAIService.id

param name string
param location string = resourceGroup().location
param tags object = {}
param sku string = 'basic'

// Create Azure AI Search service
module searchService 'br/public:avm/res/search/search-service:0.7.1' = {
  name: 'searchService'
  params: {
    name: name
    location: location
    tags: tags
    sku: sku
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'Enabled'
    semanticSearch: 'disabled'
    disableLocalAuth: false
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
  }
}

output searchServiceName string = searchService.outputs.name
output searchServiceId string = searchService.outputs.resourceId
output searchServiceEndpoint string = 'https://${searchService.outputs.name}.search.windows.net'

targetScope = 'resourceGroup'

@minLength(1)
@maxLength(64)
@description('Name of the the environment which is used to generate a short unique hash used in all resources.')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@minLength(1)
@description('Location for web frontend')
param locationWeb string = location

@description('Optional custom domain for web frontend')
param customDomain string = ''

param apiServiceName string = ''
param apiUserAssignedIdentityName string = ''
param applicationInsightsName string = ''
param appServicePlanName string = ''
param logAnalyticsName string = ''
param storageAccountName string = ''
param serviceBusConnectionString string = ''
param serviceBusNamespaceName string = ''
param searchServiceName string = ''
param searchServiceApiKey string = ''

@description('Id of the user or app to assign application roles')
param principalId string = ''

var abbrs = loadJsonContent('./abbreviations.json')

var tags = {
  'azd-env-name': environmentName
}

var functionAppName = !empty(apiServiceName) ? apiServiceName : '${abbrs.webSitesFunctions}api-${resourceToken}'
var deploymentStorageContainerName = 'app-package-${take(functionAppName, 32)}-${take(toLower(uniqueString(functionAppName, resourceToken)), 7)}'
var webUri = 'https://${web.outputs.defaultHostname}'

// Generate unique tokens to be used in naming resources
#disable-next-line no-unused-vars
var resourceToken = toLower(uniqueString(resourceGroup().id, environmentName, location))
var resourceTokenWeb = toLower(uniqueString(resourceGroup().id, environmentName, locationWeb))

// Service names defined in azure.yaml
var webServiceName = 'web'

// User assigned managed identity to be used by the function app to reach storage and other dependencies
// Assign specific roles to this identity in the RBAC module
module apiUserAssignedIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.4.1' = {
  name: 'apiUserAssignedIdentity'
  params: {
    location: location
    tags: tags
    name: !empty(apiUserAssignedIdentityName) ? apiUserAssignedIdentityName : '${abbrs.managedIdentityUserAssignedIdentities}api-${resourceToken}'
  }
}

// Create an App Service Plan to group applications under the same payment plan and SKU
module appServicePlan 'br/public:avm/res/web/serverfarm:0.1.1' = {
  name: 'appserviceplan'
  params: {
    name: !empty(appServicePlanName) ? appServicePlanName : '${abbrs.webServerFarms}${resourceToken}'
    sku: {
      name: 'FC1'
      tier: 'FlexConsumption'
    }
    reserved: true
    location: location
    tags: tags
  }
}

module api './app/api.bicep' = {
  name: 'api'
  params: {
    name: functionAppName
    location: location
    tags: tags
    applicationInsightsName: monitoring.outputs.name
    appServicePlanId: appServicePlan.outputs.resourceId
    runtimeName: 'dotnet-isolated'
    runtimeVersion: '8.0'
    storageAccountName: storage.outputs.name
    enableBlob: storageEndpointConfig.enableBlob
    enableQueue: storageEndpointConfig.enableQueue
    enableTable: storageEndpointConfig.enableTable
    deploymentStorageContainerName: deploymentStorageContainerName
    identityId: apiUserAssignedIdentity.outputs.resourceId
    identityClientId: apiUserAssignedIdentity.outputs.clientId
    sqlAdminIdentityId: ''
    appSettings: {
      AZURE_KEY_VAULT_ENDPOINT: ''
      ServiceBusConnectionString: serviceBusConnectionString
      ServiceBusNamespace: serviceBus.outputs.serviceBusNamespaceName
      ServiceBusQueueName: serviceBus.outputs.queueName
      SearchServiceEndpoint: search.outputs.searchServiceEndpoint
      SearchServiceName: search.outputs.searchServiceName
      SearchServiceApiKey: searchServiceApiKey
      SearchIndexName: 'job-descriptions'
      APPLICATIONINSIGHTS_CONNECTION_STRING: monitoring.outputs.connectionString
    }
    virtualNetworkSubnetId: ''
    allowedOrigins: [ webUri ]
  }
}

module storage 'br/public:avm/res/storage/storage-account:0.8.3' = {
  name: 'storage'
  params: {
    name: !empty(storageAccountName) ? storageAccountName : '${abbrs.storageStorageAccounts}${resourceToken}'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // Disable local authentication methods as per policy
    dnsEndpointType: 'Standard'
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
    blobServices: {
      containers: [{name: deploymentStorageContainerName}]
    }
    minimumTlsVersion: 'TLS1_2'  // Enforcing TLS 1.2 for better security
    location: location
    tags: tags
  }
}

// Define the configuration object locally to pass to the modules
var storageEndpointConfig = {
  enableBlob: true  // Required for AzureWebJobsStorage, .zip deployment, Event Hubs trigger and Timer trigger checkpointing
  enableQueue: false  // Required for Durable Functions and MCP trigger
  enableTable: false  // Required for Durable Functions and OpenAI triggers and bindings
  enableFiles: false   // Not required, used in legacy scenarios
  allowUserIdentityPrincipal: true   // Allow interactive user identity to access for testing and debugging
}

// Create Service Bus namespace and queue for job description processing
module serviceBus './app/servicebus.bicep' = {
  name: 'serviceBus'
  params: {
    name: !empty(serviceBusNamespaceName) ? serviceBusNamespaceName : '${abbrs.serviceBusNamespaces}${resourceToken}'
    location: location
    tags: tags
    queueName: 'job-descriptions'
  }
}

// Create Azure AI Search service for job description indexing
module search './app/search.bicep' = {
  name: 'search'
  params: {
    name: !empty(searchServiceName) ? searchServiceName : '${abbrs.searchSearchServices}${resourceToken}'
    location: location
    tags: tags
    sku: 'free'
  }
}

// Consolidated Role Assignments
module rbac 'app/rbac.bicep' = {
  name: 'rbacAssignments'
  params: {
    storageAccountName: storage.outputs.name
    appInsightsName: monitoring.outputs.name
    managedIdentityPrincipalId: apiUserAssignedIdentity.outputs.principalId
    userIdentityPrincipalId: principalId
    enableBlob: storageEndpointConfig.enableBlob
    enableQueue: storageEndpointConfig.enableQueue
    enableTable: storageEndpointConfig.enableTable
    allowUserIdentityPrincipal: storageEndpointConfig.allowUserIdentityPrincipal
    serviceBusNamespaceName: serviceBus.outputs.serviceBusNamespaceName
    searchServiceName: search.outputs.searchServiceName
  }
}

// The web frontend
module web 'br/public:avm/res/web/static-site:0.9.0' = {
  name: 'web'
  params: {
    name: '${abbrs.webStaticSites}${resourceTokenWeb}'
    location: locationWeb
    tags: union(tags, { 'azd-service-name': webServiceName })
    sku: 'Free'
    provider: 'Custom'
    customDomains: !empty(customDomain) ? [customDomain] : []
  }
}

// Monitor application with Azure Monitor - Log Analytics and Application Insights
module logAnalytics 'br/public:avm/res/operational-insights/workspace:0.11.1' = {
  name: '${uniqueString(deployment().name, location)}-loganalytics'
  params: {
    name: !empty(logAnalyticsName) ? logAnalyticsName : '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    location: location
    tags: tags
    dataRetention: 30
  }
}

module monitoring 'br/public:avm/res/insights/component:0.6.0' = {
  name: '${uniqueString(deployment().name, location)}-appinsights'
  params: {
    name: !empty(applicationInsightsName) ? applicationInsightsName : '${abbrs.insightsComponents}${resourceToken}'
    location: location
    tags: tags
    workspaceResourceId: logAnalytics.outputs.resourceId
    disableLocalAuth: true
  }
}

// Data outputs
output USER_ASSIGNED_IDENTITY_CLIENT_ID string = apiUserAssignedIdentity.outputs.clientId

// App outputs
output APPLICATIONINSIGHTS_CONNECTION_STRING string = monitoring.outputs.connectionString
output AZURE_LOCATION string = location
output AZURE_LOCATION_WEB string = locationWeb
output AZURE_TENANT_ID string = tenant().tenantId
output API_BASE_URL string = api.outputs.SERVICE_API_URI
output REACT_APP_WEB_BASE_URL string = webUri
output SERVICE_API_ENDPOINTS array = []

param storageAccountName string
param appInsightsName string
param managedIdentityPrincipalId string // Principal ID for the Managed Identity
param userIdentityPrincipalId string = '' // Principal ID for the User Identity
param allowUserIdentityPrincipal bool = false // Flag to enable user identity role assignments
param enableBlob bool = true
param enableQueue bool = false
param enableTable bool = false
param serviceBusNamespaceName string = ''
param searchServiceName string = ''
param openAIServiceName string = ''

// Define Role Definition IDs internally
var storageRoleDefinitionId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b' //Storage Blob Data Owner role
var queueRoleDefinitionId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor role
var tableRoleDefinitionId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor role
var monitoringRoleDefinitionId = '3913510d-42f4-4e42-8a64-420c390055eb' // Monitoring Metrics Publisher role ID
var serviceBusDataOwnerRoleDefinitionId = '090c5cfd-751d-490a-894a-3ce6f1109419' // Azure Service Bus Data Owner role
var searchIndexDataContributorRoleDefinitionId = '8ebe5a00-799e-43f5-93ac-243d3dce84a7' // Search Index Data Contributor role
var searchServiceContributorRoleDefinitionId = '7ca78c08-252a-4471-8644-bb5ff32d4ba0' // Search Service Contributor role
var cognitiveServicesOpenAIUserRoleDefinitionId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' // Cognitive Services OpenAI User role

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
  name: storageAccountName
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = if (!empty(serviceBusNamespaceName)) {
  name: serviceBusNamespaceName
}

resource searchService 'Microsoft.Search/searchServices@2023-11-01' existing = if (!empty(searchServiceName)) {
  name: searchServiceName
}

resource openAIService 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = if (!empty(openAIServiceName)) {
  name: openAIServiceName
}

// Role assignment for Storage Account (Blob) - Managed Identity
resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableBlob) {
  name: guid(storageAccount.id, managedIdentityPrincipalId, storageRoleDefinitionId) // Use managed identity ID
  scope: storageAccount
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', storageRoleDefinitionId)
    principalId: managedIdentityPrincipalId // Use managed identity ID
    principalType: 'ServicePrincipal' // Managed Identity is a Service Principal
  }
}

// Role assignment for Storage Account (Blob) - User Identity
resource storageRoleAssignment_User 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableBlob && allowUserIdentityPrincipal && !empty(userIdentityPrincipalId)) {
  name: guid(storageAccount.id, userIdentityPrincipalId, storageRoleDefinitionId)
  scope: storageAccount
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', storageRoleDefinitionId)
    principalId: userIdentityPrincipalId // Use user identity ID
    principalType: 'User' // User Identity is a User Principal
  }
}

// Role assignment for Storage Account (Queue) - Managed Identity
resource queueRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableQueue) {
  name: guid(storageAccount.id, managedIdentityPrincipalId, queueRoleDefinitionId) // Use managed identity ID
  scope: storageAccount
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', queueRoleDefinitionId)
    principalId: managedIdentityPrincipalId // Use managed identity ID
    principalType: 'ServicePrincipal' // Managed Identity is a Service Principal
  }
}

// Role assignment for Storage Account (Queue) - User Identity
resource queueRoleAssignment_User 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableQueue && allowUserIdentityPrincipal && !empty(userIdentityPrincipalId)) {
  name: guid(storageAccount.id, userIdentityPrincipalId, queueRoleDefinitionId)
  scope: storageAccount
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', queueRoleDefinitionId)
    principalId: userIdentityPrincipalId // Use user identity ID
    principalType: 'User' // User Identity is a User Principal
  }
}

// Role assignment for Storage Account (Table) - Managed Identity
resource tableRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableTable) {
  name: guid(storageAccount.id, managedIdentityPrincipalId, tableRoleDefinitionId) // Use managed identity ID
  scope: storageAccount
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', tableRoleDefinitionId)
    principalId: managedIdentityPrincipalId // Use managed identity ID
    principalType: 'ServicePrincipal' // Managed Identity is a Service Principal
  }
}

// Role assignment for Storage Account (Table) - User Identity
resource tableRoleAssignment_User 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableTable && allowUserIdentityPrincipal && !empty(userIdentityPrincipalId)) {
  name: guid(storageAccount.id, userIdentityPrincipalId, tableRoleDefinitionId)
  scope: storageAccount
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', tableRoleDefinitionId)
    principalId: userIdentityPrincipalId // Use user identity ID
    principalType: 'User' // User Identity is a User Principal
  }
}

// Role assignment for Application Insights - Managed Identity
resource appInsightsRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(applicationInsights.id, managedIdentityPrincipalId, monitoringRoleDefinitionId) // Use managed identity ID
  scope: applicationInsights
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', monitoringRoleDefinitionId)
    principalId: managedIdentityPrincipalId // Use managed identity ID
    principalType: 'ServicePrincipal' // Managed Identity is a Service Principal
  }
}

// Role assignment for Application Insights - User Identity
resource appInsightsRoleAssignment_User 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (allowUserIdentityPrincipal && !empty(userIdentityPrincipalId)) {
  name: guid(applicationInsights.id, userIdentityPrincipalId, monitoringRoleDefinitionId)
  scope: applicationInsights
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', monitoringRoleDefinitionId)
    principalId: userIdentityPrincipalId // Use user identity ID
    principalType: 'User' // User Identity is a User Principal
  }
}

// Role assignment for Service Bus - Managed Identity
resource serviceBusRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(serviceBusNamespaceName)) {
  name: guid(serviceBusNamespace.id, managedIdentityPrincipalId, serviceBusDataOwnerRoleDefinitionId)
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataOwnerRoleDefinitionId)
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Role assignment for Service Bus - User Identity
resource serviceBusRoleAssignment_User 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(serviceBusNamespaceName) && allowUserIdentityPrincipal && !empty(userIdentityPrincipalId)) {
  name: guid(serviceBusNamespace.id, userIdentityPrincipalId, serviceBusDataOwnerRoleDefinitionId)
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataOwnerRoleDefinitionId)
    principalId: userIdentityPrincipalId
    principalType: 'User'
  }
}

// Role assignment for Search Service - Managed Identity (Index Data Contributor)
resource searchIndexRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(searchServiceName)) {
  name: guid(searchService.id, managedIdentityPrincipalId, searchIndexDataContributorRoleDefinitionId)
  scope: searchService
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataContributorRoleDefinitionId)
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Role assignment for Search Service - Managed Identity (Service Contributor)
resource searchServiceRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(searchServiceName)) {
  name: guid(searchService.id, managedIdentityPrincipalId, searchServiceContributorRoleDefinitionId)
  scope: searchService
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributorRoleDefinitionId)
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Role assignment for Search Service - User Identity (Index Data Contributor)
resource searchIndexRoleAssignment_User 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(searchServiceName) && allowUserIdentityPrincipal && !empty(userIdentityPrincipalId)) {
  name: guid(searchService.id, userIdentityPrincipalId, searchIndexDataContributorRoleDefinitionId)
  scope: searchService
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataContributorRoleDefinitionId)
    principalId: userIdentityPrincipalId
    principalType: 'User'
  }
}

// Role assignment for Search Service - User Identity (Service Contributor)
resource searchServiceRoleAssignment_User 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(searchServiceName) && allowUserIdentityPrincipal && !empty(userIdentityPrincipalId)) {
  name: guid(searchService.id, userIdentityPrincipalId, searchServiceContributorRoleDefinitionId)
  scope: searchService
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributorRoleDefinitionId)
    principalId: userIdentityPrincipalId
    principalType: 'User'
  }
}

// Role assignment for OpenAI Service - Managed Identity
resource openAIRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(openAIServiceName)) {
  name: guid(openAIService.id, managedIdentityPrincipalId, cognitiveServicesOpenAIUserRoleDefinitionId)
  scope: openAIService
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAIUserRoleDefinitionId)
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Role assignment for OpenAI Service - User Identity
resource openAIRoleAssignment_User 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(openAIServiceName) && allowUserIdentityPrincipal && !empty(userIdentityPrincipalId)) {
  name: guid(openAIService.id, userIdentityPrincipalId, cognitiveServicesOpenAIUserRoleDefinitionId)
  scope: openAIService
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAIUserRoleDefinitionId)
    principalId: userIdentityPrincipalId
    principalType: 'User'
  }
}

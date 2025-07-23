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

var abbrs = loadJsonContent('./abbreviations.json')

var tags = {
  'azd-env-name': environmentName
}

// Generate unique tokens to be used in naming resources
#disable-next-line no-unused-vars
var resourceToken = toLower(uniqueString(resourceGroup().id, environmentName, location))
var resourceTokenWeb = toLower(uniqueString(resourceGroup().id, environmentName, locationWeb))

// Service names defined in azure.yaml
var webServiceName = 'web'

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

output AZURE_LOCATION string = location
output AZURE_LOCATION_WEB string = locationWeb
output AZURE_TENANT_ID string = tenant().tenantId

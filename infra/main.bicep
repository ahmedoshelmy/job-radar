// Job Radar on Azure Container Apps (consumption). Optional and manual: CI only compiles this file.
// Cost note: Container Apps consumption has a monthly free grant and Postgres Flexible B1ms is the smallest tier, but
// Service Bus *Standard* (needed for topics) is billed. Deploy with: az deployment group create -g <rg> -f infra/main.bicep -p ...
targetScope = 'resourceGroup'

@description('Short prefix for resource names.')
@maxLength(12)
param prefix string = 'jobradar'
param location string = resourceGroup().location

@description('Images, for example ghcr.io/<owner>/job-radar-api:<tag>.')
param apiImage string
param workerImage string

@description('Postgres administrator login.')
param postgresAdmin string = 'jobradar'
@secure()
@description('Postgres administrator password (pass it at deploy time; never commit it).')
param postgresPassword string

@allowed(['Fixtures', 'Live'])
param feedsMode string = 'Fixtures'

var suffix = uniqueString(resourceGroup().id)

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs-${suffix}'
  location: location
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${prefix}-pg-${suffix}'
  location: location
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '17'
    administratorLogin: postgresAdmin
    administratorLoginPassword: postgresPassword
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
  }

  resource db 'databases' = {
    name: 'jobradar'
  }

  resource allowAzure 'firewallRules' = {
    name: 'allow-azure-services'
    properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
  }
}

resource bus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: '${prefix}-sb-${suffix}'
  location: location
  sku: { name: 'Standard', tier: 'Standard' }
}

resource busRule 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2024-01-01' = {
  parent: bus
  name: 'jobradar-app'
  properties: { rights: ['Listen', 'Send', 'Manage'] }
}

var pgConnection = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=jobradar;Username=${postgresAdmin};Password=${postgresPassword};Ssl Mode=Require'
var messagingEnv = [
  { name: 'Messaging__Transport', value: 'AzureServiceBus' }
  { name: 'ConnectionStrings__servicebus', secretRef: 'servicebus' }
]

module api 'modules/container-app.bicep' = {
  name: 'api'
  params: {
    name: '${prefix}-api'
    location: location
    environmentId: environment.id
    image: apiImage
    external: true
    secrets: {
      servicebus: busRule.listKeys().primaryConnectionString
      postgres: pgConnection
    }
    env: concat(messagingEnv, [{ name: 'ConnectionStrings__jobradar', secretRef: 'postgres' }])
  }
}

module worker 'modules/container-app.bicep' = {
  name: 'worker'
  params: {
    name: '${prefix}-worker'
    location: location
    environmentId: environment.id
    image: workerImage
    external: false
    minReplicas: 1
    secrets: {
      servicebus: busRule.listKeys().primaryConnectionString
    }
    env: concat(messagingEnv, [{ name: 'Feeds__Mode', value: feedsMode }])
  }
}

output apiUrl string = 'https://${api.outputs.fqdn}'

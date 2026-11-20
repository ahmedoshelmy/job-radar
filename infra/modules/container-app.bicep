@description('Container app name.')
param name string
param location string
param environmentId string
param image string
param targetPort int = 8080
@description('true for the public API and web; false for the worker (internal health only).')
param external bool
param env array = []
@secure()
param secrets object = {}
param minReplicas int = 0
param maxReplicas int = 1

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      ingress: {
        external: external
        targetPort: targetPort
        transport: 'auto'
      }
      #disable-next-line use-secure-value-for-secure-inputs
      secrets: [for s in items(secrets): { name: s.key, value: s.value }]
    }
    template: {
      containers: [
        {
          name: name
          image: image
          env: env
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: targetPort }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: targetPort }, periodSeconds: 10 }
          ]
        }
      ]
      scale: { minReplicas: minReplicas, maxReplicas: maxReplicas }
    }
  }
}

output fqdn string = app.properties.configuration.ingress.fqdn

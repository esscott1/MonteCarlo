output "foundry_endpoint" {
  description = "Base URL for the Anthropic SDK; it appends /v1/messages."
  value       = "https://${azapi_resource.foundry.name}.services.ai.azure.com/anthropic"
}

output "deployment_name" {
  description = "The model id the app sends."
  value       = azapi_resource.deployment.name
}

output "project_endpoint" {
  description = "Foundry project endpoint (Foundry Projects / Agents SDK; the app doesn't use it yet)."
  value       = "https://${azapi_resource.foundry.name}.services.ai.azure.com/api/projects/${azapi_resource.project.name}"
}

output "next_step" {
  description = "Points the Web App at the deployment. Neither value is a secret."
  value       = "az webapp config appsettings set -g ${var.web_app_resource_group} -n ${var.web_app_name} --settings Foundry__Endpoint=https://${azapi_resource.foundry.name}.services.ai.azure.com/anthropic Foundry__Deployment=${azapi_resource.deployment.name}"
}

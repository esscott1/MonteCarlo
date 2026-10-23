# The Foundry account, its project, the Claude deployment and who may call it. Bodies and the API
# version follow Azure-Samples/claude's infra-terraform. The Web App and the resource group are read,
# never managed, so a destroy here can't touch the live site.

locals {
  api_version = "2025-10-01-preview"
  tags = {
    project      = "montecarlo"
    component    = "gordon"
    "managed-by" = "terraform"
  }
}

data "azurerm_resource_group" "rg" {
  name = var.resource_group_name
}

data "azurerm_linux_web_app" "app" {
  name                = var.web_app_name
  resource_group_name = var.web_app_resource_group
}

resource "azapi_resource" "foundry" {
  type      = "Microsoft.CognitiveServices/accounts@${local.api_version}"
  name      = var.foundry_account_name
  parent_id = data.azurerm_resource_group.rg.id
  location  = var.location
  tags      = local.tags

  identity {
    type = "SystemAssigned"
  }

  body = {
    kind = "AIServices"
    sku = {
      name = "S0"
    }
    properties = {
      customSubDomainName    = var.foundry_account_name
      allowProjectManagement = true
      publicNetworkAccess    = "Enabled"
      # Entra only: the app uses its managed identity, so there are no keys to store or leak.
      disableLocalAuth = true
    }
  }

  response_export_values = ["name"]
}

resource "azapi_resource" "project" {
  type      = "Microsoft.CognitiveServices/accounts/projects@${local.api_version}"
  name      = var.project_name
  parent_id = azapi_resource.foundry.id
  location  = var.location
  tags      = local.tags

  identity {
    type = "SystemAssigned"
  }

  body = {
    properties = {}
  }

  response_export_values = ["name"]
}

# Cognitive Services User grants the inference data action (accounts/MaaS/*) and nothing else.
resource "azurerm_role_assignment" "app_to_foundry" {
  scope                = azapi_resource.foundry.id
  role_definition_name = "Cognitive Services User"
  principal_id         = data.azurerm_linux_web_app.app.identity[0].principal_id
  principal_type       = "ServicePrincipal"
}

resource "azurerm_role_assignment" "operator_to_foundry" {
  count                = var.operator_principal_id == "" ? 0 : 1
  scope                = azapi_resource.foundry.id
  role_definition_name = "Cognitive Services User"
  principal_id         = var.operator_principal_id
  principal_type       = "User"
}

resource "azapi_resource" "deployment" {
  type      = "Microsoft.CognitiveServices/accounts/deployments@${local.api_version}"
  name      = var.model
  parent_id = azapi_resource.foundry.id
  # The schema doesn't know modelProviderData yet.
  schema_validation_enabled = false

  body = {
    sku = {
      name     = "GlobalStandard"
      capacity = var.capacity
    }
    properties = {
      model = {
        format  = "Anthropic"
        name    = var.model
        version = var.model_version
      }
      modelProviderData = {
        organizationName = var.claude_organization_name
        countryCode      = var.claude_country_code
        industry         = var.claude_industry
      }
      versionUpgradeOption = "OnceNewDefaultVersionAvailable"
      raiPolicyName        = "Microsoft.DefaultV2"
    }
  }

  response_export_values = ["name"]

  # Role assignments first: their data-plane propagation takes minutes, and the deployment's own
  # long-running create covers most of that, so the first call after apply usually works.
  depends_on = [
    azapi_resource.project,
    azurerm_role_assignment.app_to_foundry,
    azurerm_role_assignment.operator_to_foundry,
  ]
}

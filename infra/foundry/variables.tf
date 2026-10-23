variable "subscription_id" {
  description = "Subscription that holds rg-montecarlo (az-303 learn)."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group the Foundry account goes in. It already exists and isn't managed here."
  type        = string
  default     = "rg-montecarlo"
}

variable "location" {
  description = "Region for the Foundry account. The resource group is in westus2; Claude on Foundry is offered in eastus2 and swedencentral."
  type        = string
  default     = "eastus2"
}

variable "foundry_account_name" {
  description = "Foundry account name, also its custom subdomain, so globally unique: https://<name>.services.ai.azure.com."
  type        = string
  default     = "aifoundry-montecarlo"
}

variable "project_name" {
  description = "Foundry project under the account."
  type        = string
  default     = "montecarlo"
}

variable "model" {
  description = "Claude model to deploy. The deployment is named after it, so this is also the model id the app sends."
  type        = string
  default     = "claude-haiku-4-5"
}

variable "model_version" {
  description = "Model version as Foundry lists it (az cognitiveservices model list -l eastus2). claude-haiku-4-5 offers 2 (the default) and 20251001."
  type        = string
  default     = "2"
}

variable "capacity" {
  description = "GlobalStandard capacity in thousands of tokens per minute. A rate cap, not a reservation: no standing charge."
  type        = number
  default     = 25
}

variable "web_app_name" {
  description = "The live Web App, read (never managed) for its system-assigned identity."
  type        = string
  default     = "montecarlo-otsconsulting"
}

variable "web_app_resource_group" {
  description = "Resource group of the Web App."
  type        = string
  default     = "rg-montecarlo"
}

variable "operator_principal_id" {
  description = "Object id of the person running the app locally (az ad signed-in-user show --query id -o tsv). Empty = no local-dev role."
  type        = string
  default     = ""
}

# modelProviderData: Foundry requires it on every Claude deployment, and applying it accepts the
# Anthropic Marketplace offer terms (https://www.anthropic.com/legal/commercial-terms).
variable "claude_organization_name" {
  description = "Organization name sent to Anthropic with the Marketplace subscription."
  type        = string
}

variable "claude_country_code" {
  description = "Two-letter ISO country code of the organization."
  type        = string
  default     = "US"

  validation {
    condition     = length(var.claude_country_code) == 2
    error_message = "Must be a two-letter country code."
  }
}

variable "claude_industry" {
  description = "Industry, lowercase, as in the Foundry portal's list."
  type        = string
  default     = "technology"

  validation {
    condition = contains([
      "technology", "finance", "healthcare", "education",
      "retail", "manufacturing", "government", "media", "other",
    ], var.claude_industry)
    error_message = "Must be one of the Foundry portal's lowercase industries."
  }
}

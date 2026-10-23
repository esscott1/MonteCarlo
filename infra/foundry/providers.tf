terraform {
  # The 1.16 line; move to 1.17.x once it has a couple of patch releases (docs/plan-gordon-chat-bot.md, 1.3).
  required_version = ">= 1.16.0, < 2.0.0"

  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
    azapi   = { source = "Azure/azapi", version = "~> 2.0" }
  }

  # Created by hand once (infra/README.md, "Bootstrap"); shared keys are off, so Entra auth only.
  backend "azurerm" {
    resource_group_name  = "rg-terraform-state"
    storage_account_name = "esmontecarloterrastate"
    container_name       = "tfstate"
    key                  = "foundry.tfstate"
    use_azuread_auth     = true
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}

# azapi because azurerm's Cognitive Services resources don't expose allowProjectManagement
# or the modelProviderData block a Claude deployment requires.
provider "azapi" {
  subscription_id = var.subscription_id
}

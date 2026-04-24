terraform {
  required_version = "~> 1.10.0"

  required_providers {
    azapi = {
      source  = "Azure/azapi"
      version = "~> 2.9"
    }

    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.67"
    }
  }

  backend "azurerm" {}
}

provider "azapi" {}

provider "azurerm" {
  features {}
}

provider "azurerm" {
  alias           = "shared"
  subscription_id = var.key_vault_subscription_id

  features {}
}

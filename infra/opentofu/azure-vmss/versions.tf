terraform {
  required_version = "~> 1.10.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.67"
    }
  }

  backend "azurerm" {}
}

provider "azurerm" {
  features {}
}

provider "azurerm" {
  alias           = "shared"
  subscription_id = var.key_vault_subscription_id

  features {}
}

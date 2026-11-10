terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  # Estado remoto: configure por -backend-config (sin claves; use Entra ID).
  #   terraform init -backend-config="resource_group_name=..." -backend-config="storage_account_name=..." \
  #                  -backend-config="container_name=tfstate" -backend-config="key=infraagent.tfstate" \
  #                  -backend-config="use_azuread_auth=true"
  backend "azurerm" {}
}

provider "azurerm" {
  features {}
  subscription_id     = var.subscription_id
  storage_use_azuread = true
}

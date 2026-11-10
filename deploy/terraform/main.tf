data "azurerm_client_config" "current" {}

resource "azurerm_resource_group" "main" {
  name     = "rg-${local.base_name}"
  location = var.location
  tags     = local.tags
}

# ---- Observabilidad (la auditoría de InfraAgent llega a Application Insights) -----------------
resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.base_name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 90
  tags                = local.tags
}

resource "azurerm_application_insights" "main" {
  name                = "appi-${local.base_name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"
  tags                = local.tags
}

# ---- Identidad administrada (accede a Storage, Key Vault y Foundry sin claves) -----------------
resource "azurerm_user_assigned_identity" "main" {
  name                = "id-${local.base_name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.tags
}

# ---- Almacenamiento del host de la Function (sin claves de acceso) --------------------
resource "azurerm_storage_account" "func" {
  # 3-24 caracteres, solo minúsculas y números
  name                = substr(replace("st${var.project_name}${var.environment}${var.location_short}", "-", ""), 0, 24)
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  account_tier             = "Standard"
  account_replication_type = "LRS"

  min_tls_version                 = "TLS1_2"
  https_traffic_only_enabled      = true
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false

  tags = local.tags
}

resource "azurerm_role_assignment" "storage_blob" {
  scope                = azurerm_storage_account.func.id
  role_definition_name = "Storage Blob Data Owner"
  principal_id         = azurerm_user_assigned_identity.main.principal_id
}

resource "azurerm_role_assignment" "storage_queue" {
  scope                = azurerm_storage_account.func.id
  role_definition_name = "Storage Queue Data Contributor"
  principal_id         = azurerm_user_assigned_identity.main.principal_id
}

resource "azurerm_role_assignment" "storage_table" {
  scope                = azurerm_storage_account.func.id
  role_definition_name = "Storage Table Data Contributor"
  principal_id         = azurerm_user_assigned_identity.main.principal_id
}

# ---- Key Vault para secretos (PAT de Azure DevOps). Solo si azure_devops_pat_secret_name no está vacío ----
resource "azurerm_key_vault" "main" {
  count               = local.use_key_vault ? 1 : 0
  name                = substr("kv-${local.base_name}", 0, 24)
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  rbac_authorization_enabled = true
  purge_protection_enabled   = true
  soft_delete_retention_days = 90

  tags = local.tags
}

resource "azurerm_role_assignment" "kv_secrets_user" {
  count                = local.use_key_vault ? 1 : 0
  scope                = azurerm_key_vault.main[0].id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.main.principal_id
}

# ---- Acceso al agente de Foundry ------------------------------------------------------------------
resource "azurerm_role_assignment" "foundry_user" {
  count                = var.foundry_resource_id == "" ? 0 : 1
  scope                = var.foundry_resource_id
  role_definition_name = "Azure AI User"
  principal_id         = azurerm_user_assigned_identity.main.principal_id
}

# ---- Function App --------------------------------------------------------------------------------
resource "azurerm_service_plan" "main" {
  name                = "asp-${local.base_name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  os_type             = "Linux"
  sku_name            = var.plan_sku
  tags                = local.tags
}

resource "azurerm_linux_function_app" "main" {
  name                = "func-${local.base_name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  service_plan_id     = azurerm_service_plan.main.id

  storage_account_name          = azurerm_storage_account.func.name
  storage_uses_managed_identity = true

  https_only                      = true
  public_network_access_enabled   = var.public_network_access_enabled
  virtual_network_subnet_id       = var.integration_subnet_id
  key_vault_reference_identity_id = azurerm_user_assigned_identity.main.id

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.main.id]
  }

  site_config {
    always_on           = true
    minimum_tls_version = "1.2"
    ftps_state          = "Disabled"
    http2_enabled       = true

    application_stack {
      dotnet_version              = "8.0"
      use_dotnet_isolated_runtime = true
    }
  }

  # ÚNICO punto de configuración de variables de entorno: ver locals.tf
  app_settings = local.app_settings

  tags = local.tags

  depends_on = [
    azurerm_role_assignment.storage_blob,
    azurerm_role_assignment.storage_queue,
    azurerm_role_assignment.storage_table,
    azurerm_role_assignment.kv_secrets_user
  ]

  lifecycle {
    # El despliegue por pipeline administra estos valores.
    ignore_changes = [app_settings["WEBSITE_RUN_FROM_PACKAGE"]]
  }
}

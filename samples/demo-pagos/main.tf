# Proyecto de DEMO con fallas de seguridad a propósito para probar la revisión de InfraAgent.
# Reemplace esta carpeta por su proyecto real (ver docs/CONFIGURACION.md, sección Projects).

resource "azurerm_resource_group" "rg" {
  name     = "rg-pagos-dev"
  location = "eastus2"
}

resource "azurerm_storage_account" "datos" {
  name                          = "stpagosdev001"
  resource_group_name           = azurerm_resource_group.rg.name
  location                      = azurerm_resource_group.rg.location
  account_tier                  = "Standard"
  account_replication_type      = "LRS"
  public_network_access_enabled = true
  min_tls_version               = "TLS1_0"
}

resource "azurerm_mssql_server" "sql" {
  name                         = "sql-pagos-dev"
  resource_group_name          = azurerm_resource_group.rg.name
  location                     = azurerm_resource_group.rg.location
  version                      = "12.0"
  administrator_login          = "sqladmin"
  administrator_login_password = "Clave-De-Demo-123!"
}

resource "azurerm_key_vault" "kv" {
  name                = "kv-pagos-dev"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  tenant_id           = "00000000-0000-0000-0000-000000000000"
  sku_name            = "standard"
}

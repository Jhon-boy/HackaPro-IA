# Azure SQL seguro

```hcl
resource "azurerm_mssql_server" "sql" {
  name                = local.sql_server_name
  resource_group_name = var.resource_group_name
  location            = var.location
  version             = "12.0"

  minimum_tls_version           = "1.2"
  public_network_access_enabled = false

  azuread_administrator {
    login_username              = var.sql_aad_admin_login
    object_id                   = var.sql_aad_admin_object_id
    azuread_authentication_only = true
  }

  identity {
    type = "SystemAssigned"
  }

  tags = local.tags
}

resource "azurerm_mssql_database" "app" {
  name      = "app"
  server_id = azurerm_mssql_server.sql.id
  sku_name  = "S1"

  tags = local.tags
}
```

Notas:
- Con `azuread_authentication_only = true` no hay usuario/contraseña SQL: no declares `administrator_login_password`.
- Acceso solo por Private Endpoint con `subresource_names = ["sqlServer"]`.

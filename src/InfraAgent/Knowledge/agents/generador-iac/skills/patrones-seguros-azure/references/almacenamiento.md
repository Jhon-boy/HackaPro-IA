# Almacenamiento seguro

```hcl
resource "azurerm_storage_account" "datos" {
  name                = local.storage_account_name   # 3-24 caracteres, minúsculas y números
  resource_group_name = var.resource_group_name
  location            = var.location

  account_tier             = "Standard"
  account_replication_type = "GRS"

  min_tls_version                 = "TLS1_2"
  https_traffic_only_enabled      = true
  public_network_access_enabled   = false
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false   # requiere storage_use_azuread = true en el provider

  blob_properties {
    versioning_enabled = true
    delete_retention_policy {
      days = 30
    }
  }

  tags = local.tags
}

resource "azurerm_storage_container" "datos" {
  name                  = "datos"
  storage_account_name  = azurerm_storage_account.datos.name
  container_access_type = "private"
}
```

Notas:
- El acceso a los datos es por Entra ID (RBAC) y por Private Endpoint (`subresource_names = ["blob"]`), ver `red-privada.md`.
- No generes ni expongas claves de acceso ni SAS en variables u outputs.

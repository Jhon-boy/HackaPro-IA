# Key Vault seguro

```hcl
data "azurerm_client_config" "current" {}

resource "azurerm_key_vault" "kv" {
  name                = local.key_vault_name
  resource_group_name = var.resource_group_name
  location            = var.location
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  rbac_authorization_enabled    = true
  purge_protection_enabled      = true
  soft_delete_retention_days    = 90
  public_network_access_enabled = false

  network_acls {
    default_action = "Deny"
    bypass         = "AzureServices"
  }

  tags = local.tags
}
```

Notas:
- Con RBAC habilitado no se usan `access_policy`; asigna roles (`azurerm_role_assignment`) a identidades administradas.
- Los secretos se cargan fuera de Terraform o con variables `sensitive`; nunca como `default`.
- Acceso por Private Endpoint con `subresource_names = ["vault"]`.

# Red privada

## Private Endpoint con DNS privado
```hcl
resource "azurerm_private_endpoint" "storage_blob" {
  name                = "pe-${local.base_name}-blob"
  resource_group_name = var.resource_group_name
  location            = var.location
  subnet_id           = var.private_endpoint_subnet_id

  private_service_connection {
    name                           = "psc-${local.base_name}-blob"
    private_connection_resource_id = azurerm_storage_account.datos.id
    subresource_names              = ["blob"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "default"
    private_dns_zone_ids = [var.private_dns_zone_blob_id]
  }

  tags = local.tags
}
```

Subrecursos habituales: storage `blob`/`file`/`dfs`, Key Vault `vault`, SQL `sqlServer`, Cosmos DB `Sql`.

## NSG: reglas seguras
```hcl
resource "azurerm_network_security_rule" "deny_internet_inbound" {
  name                        = "deny-internet-inbound"
  priority                    = 4000
  direction                   = "Inbound"
  access                      = "Deny"
  protocol                    = "*"
  source_port_range           = "*"
  destination_port_range      = "*"
  source_address_prefix       = "Internet"
  destination_address_prefix  = "*"
  resource_group_name         = var.resource_group_name
  network_security_group_name = azurerm_network_security_group.app.name
}
```
Nunca permitas `source_address_prefix = "*"` ni `0.0.0.0/0` hacia puertos de administración (22, 3389, 1433, 5432, 3306).

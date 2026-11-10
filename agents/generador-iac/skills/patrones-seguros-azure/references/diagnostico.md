# Diagnóstico hacia Log Analytics

```hcl
resource "azurerm_monitor_diagnostic_setting" "kv" {
  name                       = "diag-${local.base_name}-kv"
  target_resource_id         = azurerm_key_vault.kv.id
  log_analytics_workspace_id = var.log_analytics_workspace_id

  enabled_log {
    category_group = "allLogs"
  }
}
```

Notas:
- Aplica el mismo patrón a Key Vault, SQL (`azurerm_mssql_server`/base de datos), Storage (por servicio: `.../blobServices/default`) y App Service.
- No declares el bloque de métricas si dudas de la sintaxis vigente del proveedor: anótalo en `notes`.

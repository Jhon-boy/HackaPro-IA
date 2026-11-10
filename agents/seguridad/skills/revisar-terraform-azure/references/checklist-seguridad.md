# Checklist de seguridad por tipo de recurso (azurerm)

## `azurerm_storage_account`
- `public_network_access_enabled = false`, `allow_nested_items_to_be_public = false`
- `min_tls_version = "TLS1_2"`, `https_traffic_only_enabled = true`
- `shared_access_key_enabled = false` (preferir Entra ID)
- Private Endpoint presente; reglas de red con `default_action = "Deny"` si hay `network_rules`
- Versionado y retención de borrado en blobs

## `azurerm_storage_container`
- `container_access_type = "private"`

## `azurerm_key_vault`
- `purge_protection_enabled = true`, `soft_delete_retention_days >= 90`
- `public_network_access_enabled = false`, `network_acls.default_action = "Deny"`
- RBAC habilitado (`rbac_authorization_enabled = true`) en lugar de `access_policy`

## `azurerm_mssql_server` / `azurerm_mssql_database`
- `minimum_tls_version = "1.2"`, `public_network_access_enabled = false`
- `azuread_administrator` con `azuread_authentication_only = true`
- Sin `administrator_login_password` literal
- Reglas de firewall `0.0.0.0` - `255.255.255.255` o `AllowAllWindowsAzureIps` indiscriminadas

## `azurerm_linux_web_app`, `azurerm_windows_web_app`, `azurerm_*_function_app`
- `https_only = true`, `minimum_tls_version = "1.2"` (en `site_config`)
- `identity` administrada; sin credenciales en `app_settings` ni `connection_string`
- `public_network_access_enabled = false` o restricciones de acceso configuradas

## `azurerm_network_security_group` / `azurerm_network_security_rule`
- Sin `source_address_prefix = "*"`, `"0.0.0.0/0"` o `"Internet"` con `access = "Allow"` hacia 22, 3389, 1433, 3306, 5432
- Sin `destination_port_range = "*"` con origen público

## `azurerm_public_ip`, `azurerm_application_gateway`
- IP pública solo si el diseño lo requiere; Application Gateway con WAF (`waf_configuration` / política) en modo Prevention

## `azurerm_kubernetes_cluster`
- RBAC y Entra ID habilitados, `private_cluster_enabled = true`, políticas de red

## Transversal
- Secretos literales (`password`, `secret`, `key`, `token`, `connection_string`)
- Diagnostic settings hacia Log Analytics en recursos críticos
- Etiquetas obligatorias y región permitida (según catálogo)

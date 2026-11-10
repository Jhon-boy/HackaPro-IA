output "function_app_name" {
  description = "Nombre de la Function App (variable FUNCTION_APP_NAME del pipeline de despliegue)."
  value       = azurerm_linux_function_app.main.name
}

output "function_app_url" {
  description = "URL base de la API (POST /api/chat, GET /api/status ...)."
  value       = "https://${azurerm_linux_function_app.main.default_hostname}"
}

output "managed_identity_client_id" {
  description = "Client Id de la identidad administrada (agréguela a la organización de Azure DevOps si usa ese proveedor sin PAT)."
  value       = azurerm_user_assigned_identity.main.client_id
}

output "managed_identity_principal_id" {
  description = "Principal Id de la identidad (para asignarle el rol en Foundry si no se pasó foundry_resource_id)."
  value       = azurerm_user_assigned_identity.main.principal_id
}

output "key_vault_name" {
  description = "Key Vault donde se crea el secreto del PAT de Azure DevOps (null si no se usa PAT)."
  value       = one(azurerm_key_vault.main[*].name)
}

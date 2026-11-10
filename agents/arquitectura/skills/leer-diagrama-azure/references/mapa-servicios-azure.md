# Mapa de servicios de Azure → tipo de recurso Terraform

| Servicio (como aparece en el diagrama) | Tipo Terraform |
|---|---|
| Storage Account / Blob / Data Lake Gen2 | `azurerm_storage_account` |
| Key Vault | `azurerm_key_vault` |
| Azure SQL Server / SQL Database | `azurerm_mssql_server`, `azurerm_mssql_database` |
| PostgreSQL / MySQL Flexible Server | `azurerm_postgresql_flexible_server`, `azurerm_mysql_flexible_server` |
| Cosmos DB | `azurerm_cosmosdb_account` |
| App Service (Linux / Windows) | `azurerm_linux_web_app`, `azurerm_windows_web_app` |
| App Service Plan | `azurerm_service_plan` |
| Azure Functions | `azurerm_linux_function_app`, `azurerm_windows_function_app` |
| AKS | `azurerm_kubernetes_cluster` |
| Virtual Network / Subnet | `azurerm_virtual_network`, `azurerm_subnet` |
| Network Security Group | `azurerm_network_security_group`, `azurerm_network_security_rule` |
| Private Endpoint | `azurerm_private_endpoint` |
| Private DNS Zone | `azurerm_private_dns_zone` |
| Application Gateway / WAF | `azurerm_application_gateway` |
| Front Door | `azurerm_cdn_frontdoor_profile` |
| Public IP | `azurerm_public_ip` |
| Load Balancer | `azurerm_lb` |
| Azure Firewall | `azurerm_firewall` |
| Log Analytics | `azurerm_log_analytics_workspace` |
| Application Insights | `azurerm_application_insights` |
| Service Bus | `azurerm_servicebus_namespace` |
| Event Hubs | `azurerm_eventhub_namespace` |
| API Management | `azurerm_api_management` |
| Container Registry | `azurerm_container_registry` |
| User Assigned Managed Identity | `azurerm_user_assigned_identity` |

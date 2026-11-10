locals {
  base_name = "${var.project_name}-${var.environment}-${var.location_short}"

  tags = merge({
    ambiente     = var.environment
    proyecto     = var.project_name
    responsable  = var.owner
    centro_costo = var.cost_center
  }, var.extra_tags)

  # Key Vault solo si hay un secreto que guardar (PAT de Azure DevOps).
  use_key_vault = var.azure_devops_pat_secret_name != ""

  # --------------------------------------------------------------------------
  # CONFIGURACIÓN CENTRALIZADA DE LA FUNCTION
  # Todas las variables de entorno de InfraAgent salen de este único bloque.
  # Convención: InfraAgent__<Sección>__<Clave>  (el doble guion bajo equivale a ":").
  # Referencia completa: docs/CONFIGURACION.md
  # --------------------------------------------------------------------------
  core_settings = {
    FUNCTIONS_WORKER_RUNTIME = "dotnet-isolated"

    # Almacenamiento del host con identidad administrada (sin claves).
    AzureWebJobsStorage__accountName = azurerm_storage_account.func.name
    AzureWebJobsStorage__credential  = "managedidentity"
    AzureWebJobsStorage__clientId    = azurerm_user_assigned_identity.main.client_id

    APPLICATIONINSIGHTS_CONNECTION_STRING = azurerm_application_insights.main.connection_string
  }

  infraagent_settings = {
    "InfraAgent__Identity__ManagedIdentityClientId" = azurerm_user_assigned_identity.main.client_id

    # Foundry
    "InfraAgent__Foundry__Endpoint"       = var.foundry_endpoint
    "InfraAgent__Foundry__DefaultAgentId" = var.foundry_default_agent_id

    # Capacidades
    "InfraAgent__Features__Images"               = tostring(var.features.images)
    "InfraAgent__Features__RepositoryAccess"     = tostring(var.features.repository_access)
    "InfraAgent__Features__ArchitectureAnalysis" = tostring(var.features.architecture_analysis)
    "InfraAgent__Features__PolicyEvaluation"     = tostring(var.features.policy_evaluation)
    "InfraAgent__Features__TerraformGeneration"  = tostring(var.features.terraform_generation)
    "InfraAgent__Features__SecurityReview"       = tostring(var.features.security_review)

    # Conocimiento
    "InfraAgent__Knowledge__OrganizationName" = var.organization_name

    # Chat
    "InfraAgent__Chat__ResponseLanguage"  = var.response_language
    "InfraAgent__Chat__TimeoutSeconds"    = tostring(var.chat.timeout_seconds)
    "InfraAgent__Chat__MaxMessageChars"   = tostring(var.chat.max_message_chars)
    "InfraAgent__Chat__MaxImages"         = tostring(var.chat.max_images)
    "InfraAgent__Chat__MaxImageBytes"     = tostring(var.chat.max_image_bytes)
    "InfraAgent__Chat__AllowedImageTypes" = var.chat.allowed_image_types

    # Generación de Terraform
    "InfraAgent__Generation__AutoSecurityReview"    = tostring(var.generation.auto_security_review)
    "InfraAgent__Generation__MaxFixRounds"          = tostring(var.generation.max_fix_rounds)
    "InfraAgent__Generation__BlockFrom"             = var.generation.block_from
    "InfraAgent__Generation__TerraformVersion"      = var.generation.terraform_version
    "InfraAgent__Generation__AzurermVersion"        = var.generation.azurerm_version
    "InfraAgent__Generation__DefaultRegion"         = var.generation.default_region
    "InfraAgent__Generation__AllowedRegions"        = var.generation.allowed_regions
    "InfraAgent__Generation__NamingPattern"         = var.generation.naming_pattern
    "InfraAgent__Generation__RequiredTags"          = var.generation.required_tags
    "InfraAgent__Generation__AllowedFileExtensions" = var.generation.allowed_file_extensions

    # Repositorios
    "InfraAgent__Repositories__Provider" = var.repository_provider
  }

  # Un agente de Foundry por responsabilidad (solo los que tengan valor).
  agent_settings = {
    for role, id in var.foundry_agent_ids :
    "InfraAgent__Foundry__Agents__${role}" => id if id != ""
  }

  # Azure DevOps (solo con repository_provider = AzureDevOps).
  azure_devops_settings = {
    for k, v in {
      "InfraAgent__AzureDevOps__OrganizationUrl" = var.azure_devops_organization_url
    } : k => v if var.repository_provider == "AzureDevOps"
  }

  # El PAT es un secreto: se resuelve desde Key Vault en tiempo de ejecución (nunca en el estado ni en el código).
  pat_settings = {
    for k, v in {
      "InfraAgent__AzureDevOps__Pat" = "@Microsoft.KeyVault(VaultName=${try(azurerm_key_vault.main[0].name, "")};SecretName=${var.azure_devops_pat_secret_name})"
    } : k => v if local.use_key_vault && var.repository_provider == "AzureDevOps"
  }

  # Lista cerrada de proyectos -> InfraAgent__Projects__0__Id, ...
  project_settings = merge([
    for i, p in var.projects : {
      "InfraAgent__Projects__${i}__Id"          = p.id
      "InfraAgent__Projects__${i}__DisplayName" = p.display_name
      "InfraAgent__Projects__${i}__Path"        = p.path
      "InfraAgent__Projects__${i}__Project"     = p.project
      "InfraAgent__Projects__${i}__Repository"  = p.repository
      "InfraAgent__Projects__${i}__Branch"      = p.branch
    }
  ]...)

  app_settings = merge(
    local.core_settings,
    local.infraagent_settings,
    local.agent_settings,
    local.azure_devops_settings,
    local.pat_settings,
    local.project_settings,
    var.extra_app_settings
  )
}

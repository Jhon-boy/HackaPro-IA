# ============================================================================
# TODA la configuración del despliegue de InfraAgent se declara aquí.
# Se copia terraform.tfvars.example -> terraform.tfvars y se ajustan los valores.
# Nada de esto es secreto: los secretos viven en Key Vault (ver README).
# ============================================================================

# ---- Plataforma -------------------------------------------------------------
variable "subscription_id" {
  description = "Suscripción de Azure donde se despliega."
  type        = string
}

variable "location" {
  description = "Región de Azure (debe ser una región permitida por las políticas del banco)."
  type        = string
  default     = "eastus2"
}

variable "location_short" {
  description = "Abreviatura de la región para los nombres (p. ej. eus2)."
  type        = string
  default     = "eus2"
}

variable "environment" {
  description = "Ambiente: dev, test o prod."
  type        = string
  default     = "dev"

  validation {
    condition     = contains(["dev", "test", "prod"], var.environment)
    error_message = "environment debe ser dev, test o prod."
  }
}

variable "project_name" {
  description = "Nombre corto del proyecto para los nombres de recursos."
  type        = string
  default     = "infraagent"
}

variable "owner" {
  description = "Responsable (etiqueta obligatoria)."
  type        = string
}

variable "cost_center" {
  description = "Centro de costo (etiqueta obligatoria)."
  type        = string
}

variable "extra_tags" {
  description = "Etiquetas adicionales."
  type        = map(string)
  default     = {}
}

variable "plan_sku" {
  description = "SKU del plan de App Service que aloja la Function (plan dedicado; admite identidad administrada para el almacenamiento)."
  type        = string
  default     = "P1v3"
}

variable "public_network_access_enabled" {
  description = "Acceso público a la Function. En producción bancaria: false + integración con red privada."
  type        = bool
  default     = true
}

variable "integration_subnet_id" {
  description = "Subred para la integración de red regional (opcional)."
  type        = string
  default     = null
}

# ---- Azure AI Foundry -------------------------------------------------------
variable "foundry_endpoint" {
  description = "Endpoint del proyecto de Azure AI Foundry."
  type        = string
}

variable "foundry_resource_id" {
  description = "Id del recurso/proyecto de Foundry para asignar el rol 'Azure AI User' a la identidad de la Function (vacío = asignarlo manualmente)."
  type        = string
  default     = ""
}

variable "foundry_default_agent_id" {
  description = "Agente de Foundry usado por los roles que no tengan uno propio."
  type        = string
}

variable "foundry_agent_ids" {
  description = "Un agente de Foundry por responsabilidad (vacío = usa el agente por defecto)."
  type = object({
    Orchestrator = optional(string, "")
    Architecture = optional(string, "")
    Policy       = optional(string, "")
    IacGenerator = optional(string, "")
    Security     = optional(string, "")
  })
  default = {}
}

# ---- Comportamiento de InfraAgent ---------------------------------------------
# Cada variable se traduce a un App Setting InfraAgent__* (ver locals.tf). Los valores por defecto
# coinciden con config/infraagent.settings.json.

variable "organization_name" {
  description = "Nombre con el que los agentes se refieren a la organización dueña de las políticas."
  type        = string
  default     = "el banco"
}

variable "response_language" {
  description = "Idioma de las respuestas del chat."
  type        = string
  default     = "español"
}

variable "features" {
  description = "Interruptores de capacidades (solo pueden restringir)."
  type = object({
    images                = optional(bool, true)
    repository_access     = optional(bool, true)
    architecture_analysis = optional(bool, true)
    policy_evaluation     = optional(bool, true)
    terraform_generation  = optional(bool, true)
    security_review       = optional(bool, true)
  })
  default = {}
}

variable "generation" {
  description = "Parámetros del generador de Terraform."
  type = object({
    auto_security_review    = optional(bool, true)
    max_fix_rounds          = optional(number, 1)
    block_from              = optional(string, "High")
    terraform_version       = optional(string, ">= 1.6.0")
    azurerm_version         = optional(string, "~> 4.0")
    default_region          = optional(string, "eastus2")
    allowed_regions         = optional(string, "")
    naming_pattern          = optional(string, "{tipo}-{app}-{ambiente}-{region}")
    required_tags           = optional(string, "ambiente,proyecto,responsable,centro_costo")
    allowed_file_extensions = optional(string, ".tf,.tfvars,.md")
  })
  default = {}
}

variable "chat" {
  description = "Límites del chat."
  type = object({
    timeout_seconds     = optional(number, 200)
    max_message_chars   = optional(number, 8000)
    max_images          = optional(number, 4)
    max_image_bytes     = optional(number, 5000000)
    allowed_image_types = optional(string, "image/png,image/jpeg")
  })
  default = {}
}

# ---- Repositorios (opcional) --------------------------------------------------------
variable "repository_provider" {
  description = "None (solo texto e imágenes), Local (carpetas accesibles desde la Function) o AzureDevOps (solo lectura)."
  type        = string
  default     = "None"

  validation {
    condition     = contains(["None", "Local", "AzureDevOps"], var.repository_provider)
    error_message = "repository_provider debe ser None, Local o AzureDevOps."
  }
}

variable "azure_devops_organization_url" {
  description = "URL de la organización, p. ej. https://dev.azure.com/mi-banco (solo con repository_provider = AzureDevOps)."
  type        = string
  default     = ""
}

variable "azure_devops_pat_secret_name" {
  description = "Nombre del secreto en Key Vault con el PAT. Vacío = no se crea Key Vault y se usa la identidad administrada (agréguela a la organización de Azure DevOps)."
  type        = string
  default     = ""
}

variable "projects" {
  description = "Proyectos habilitados (lista cerrada, máx. 20). Con Local use path; con AzureDevOps use project, repository y branch."
  type = list(object({
    id           = string
    display_name = string
    path         = optional(string, "")
    project      = optional(string, "")
    repository   = optional(string, "")
    branch       = optional(string, "main")
  }))
  default = []

  validation {
    condition     = length(var.projects) <= 20
    error_message = "Se permiten como máximo 20 proyectos."
  }
}

variable "extra_app_settings" {
  description = "Cualquier otro App Setting, p. ej. InfraAgent__Analysis__MaxFiles = \"60\". Ver docs/CONFIGURACION.md."
  type        = map(string)
  default     = {}
}

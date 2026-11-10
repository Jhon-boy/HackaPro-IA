---
name: estructura-terraform
description: Úsala al generar Terraform para decidir qué archivos crear, cómo nombrar recursos, etiquetar y declarar variables, versiones del proveedor y estado remoto.
---

# Estructura estándar de un proyecto Terraform

## Archivos
| Archivo | Contenido |
|---|---|
| `versions.tf` | `required_version >= 1.6` y `azurerm ~> 4.0` |
| `providers.tf` | `provider "azurerm"` con `features {}`, `subscription_id = var.subscription_id` y `storage_use_azuread = true` |
| `variables.tf` | Todas las entradas, con `description` y `type`; los secretos con `sensitive = true` y sin `default` |
| `locals.tf` | Prefijo de nombres y mapa de etiquetas obligatorias |
| `main.tf` | Recursos (o varios `*.tf` por dominio: `network.tf`, `data.tf`, `app.tf`) |
| `outputs.tf` | Solo identificadores y nombres; nunca secretos |
| `terraform.tfvars.example` | Ejemplo de valores **sin** secretos |
| `README.md` | Qué crea, variables requeridas, pasos manuales pendientes |

## Convenciones
- **Nombres**: `<tipo>-<proyecto>-<ambiente>-<región>`; construye el prefijo en `locals` a partir de variables. Respeta los límites de cada servicio (p. ej. la cuenta de almacenamiento: 3-24 caracteres, solo minúsculas y números, sin guiones).
- **Etiquetas obligatorias** (según el catálogo de políticas): define `local.tags` una vez y aplícalo a todos los recursos con `tags = local.tags`.
- **Región**: variable con validación para permitir solo las regiones autorizadas por el catálogo.
- **Estado remoto**: no incluyas claves; indica en el README el backend en Azure Storage con autenticación de Entra ID (`use_azuread_auth = true`) configurado por `-backend-config`.
- **Dependencias**: prefiere referencias entre recursos (`azurerm_x.y.id`) a `depends_on`.

## Ejemplo mínimo de `versions.tf`
```hcl
terraform {
  required_version = ">= 1.6.0"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }
}
```

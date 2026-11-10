# Despliegue de InfraAgent en Azure Functions (Terraform)

Crea: grupo de recursos, Log Analytics + Application Insights, identidad administrada, almacenamiento
(sin claves, acceso por identidad), plan dedicado y la **Function App** de InfraAgent.
El Key Vault (RBAC) se crea **solo** si define `azure_devops_pat_secret_name`.

> Este Terraform no se pudo ejecutar con `terraform validate` en el entorno donde se escribió.
> Antes del primer `apply`, corra `terraform fmt`, `terraform validate` y revise el `plan`.

## Configuración centralizada
Todas las variables de entorno de la Function salen de **un solo bloque**: `locals.tf` → `app_settings`.
Usted solo edita `terraform.tfvars`:

| Variable de Terraform | App Settings que genera |
|---|---|
| `foundry_*` | `InfraAgent__Foundry__*` |
| `features` | `InfraAgent__Features__*` |
| `generation` | `InfraAgent__Generation__*` |
| `chat`, `response_language` | `InfraAgent__Chat__*` |
| `organization_name` | `InfraAgent__Knowledge__OrganizationName` |
| `repository_provider`, `projects`, `azure_devops_*` | `InfraAgent__Repositories__*`, `InfraAgent__Projects__*`, `InfraAgent__AzureDevOps__*` |
| `extra_app_settings` | cualquier otra (ver `docs/CONFIGURACION.md`) |

## Pasos
```powershell
cd deploy/terraform
copy terraform.tfvars.example terraform.tfvars      # editar valores (sin secretos)

terraform init `
  -backend-config="resource_group_name=<rg-estado>" `
  -backend-config="storage_account_name=<sa-estado>" `
  -backend-config="container_name=tfstate" `
  -backend-config="key=infraagent.tfstate" `
  -backend-config="use_azuread_auth=true"

terraform plan -out tfplan
terraform apply tfplan
```

## Después del `apply`
1. **Foundry**: si no pasó `foundry_resource_id`, asigne a `managed_identity_principal_id` el rol **Azure AI User**
   sobre el proyecto de Foundry.
2. **Código**: publique la Function (`pipelines/deploy-function.yml` o manualmente):
   ```powershell
   dotnet publish src/InfraAgent.Functions -c Release -o publish
   Compress-Archive publish\* publish.zip -Force
   az functionapp deployment source config-zip -g <rg> -n <function_app_name> --src publish.zip
   ```
3. **Verificación**: `GET {function_app_url}/api/health` (anónimo), `GET /api/status` (diagnóstico) y
   `GET /api/config` (parámetros efectivos), estos dos con clave de función (`?code=<clave>`).
4. **PAT** (solo con `repository_provider = AzureDevOps` y PAT): cree en el Key Vault el secreto
   `azure_devops_pat_secret_name` (permiso *Code: Read*). Sin PAT, agregue la identidad administrada
   (`managed_identity_client_id`) a la organización de Azure DevOps.

> El proveedor `Local` lee carpetas del sistema de archivos donde corre la Function: sirve para desarrollo y demos
> locales; en Azure use `None` o `AzureDevOps`.

## Endurecimiento recomendado para producción
- `public_network_access_enabled = false` + `integration_subnet_id` + Private Endpoint de la Function.
- Autenticación de la Function con Entra ID (App Service Authentication) además de las claves.
- Private Endpoint para Storage y Key Vault.

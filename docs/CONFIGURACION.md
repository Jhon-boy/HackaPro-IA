# Configuración de InfraAgent

Toda la configuración vive en **una sola sección: `InfraAgent`**. Casi todo tiene un valor por defecto
razonable: solo son obligatorios el endpoint y el agente de Foundry.

## Dónde se configura (de menor a mayor prioridad)
| Origen | Uso |
|---|---|
| `src/InfraAgent/infraagent.settings.json` (o la ruta de `INFRAAGENT_CONFIG`) | Valores por defecto versionados. **Sin secretos.** |
| Variables de entorno / App Settings de la Function | Valores por ambiente. Formato `InfraAgent__Seccion__Clave` (`__` equivale a `:`). |
| Referencias a Key Vault (`@Microsoft.KeyVault(...)`) como App Setting | Secretos. |
| `local.settings.json` ignorado por Git | Configuración y secretos de desarrollo local. |

- **En Azure**: se edita `infra/terraform/terraform.tfvars`; `locals.tf` genera todos los App Settings.
- **Function local**: copie `src/InfraAgent.Functions/local.settings.sample.json` a `local.settings.json`.
- **Diagnóstico**: `GET /api/status` (qué falta, solo nombres) y `GET /api/config` (parámetros efectivos no secretos).

## Qué se puede cambiar sin tocar código
| Quiero… | Configuración |
|---|---|
| Apagar una capacidad (imágenes, generar Terraform, revisar seguridad…) | `Features:*` |
| Usar un agente de Foundry distinto por responsabilidad | `Foundry:Agents:*` |
| Cambiar región, nombres, etiquetas o versiones de Terraform | `Generation:*` |
| Cambiar la severidad que dispara la autocorrección | `Generation:BlockFrom` |
| Desactivar la revisión de seguridad automática del código generado | `Generation:AutoSecurityReview = false` |
| Cambiar idioma / nombre de la organización | `Chat:ResponseLanguage`, `Knowledge:OrganizationName` |
| Aceptar otros formatos de imagen | `Chat:AllowedImageTypes` |
| Leer carpetas locales, Azure DevOps o ningún repositorio | `Repositories:Provider` |
| Cambiar qué archivos se leen / revisan / generan | `Analysis:ReadableExtensions`, `Analysis:FileExtensions`, `Generation:AllowedFileExtensions` |
| Cambiar las políticas | `src/InfraAgent/Knowledge/policies/catalogo.yaml` (o `Knowledge:PolicyCatalogPath`) |
| Cambiar el comportamiento de un agente | `src/InfraAgent/Knowledge/agents/<agente>/instructions.md` y sus `skills/` |
| Añadir proyectos | `Projects:*` |

> Los interruptores **solo pueden restringir**: la lista cerrada de herramientas de solo lectura vive en código y
> ninguna variable puede añadir herramientas ni habilitar extensiones ejecutables.

## Variables
Todas se escriben como `InfraAgent__<Sección>__<Clave>`.

### Identity / Foundry
| Variable | Defecto | Secreto | Descripción |
|---|---|---|---|
| `Identity__ManagedIdentityClientId` | vacío | No | Client Id de la identidad administrada de usuario. Vacío = identidad del sistema / `az login`. |
| `Foundry__Endpoint` | — **(obligatoria)** | No | Endpoint del proyecto de Foundry. |
| `Foundry__DefaultAgentId` | — **(obligatoria)** | No | Agente usado por los roles sin agente propio. |
| `Foundry__Agents__Orchestrator` / `Architecture` / `Policy` / `IacGenerator` / `Security` | vacío | No | Agente propio de cada responsabilidad. |
| `Foundry__RunTimeoutSeconds` | 180 | No | Tiempo máximo de una ejecución de agente. |
| `Foundry__PollIntervalMs` | 800 | No | Intervalo de sondeo. |
| `Foundry__MaxToolRounds` | 8 | No | Rondas máximas de herramientas por ejecución. |

### Features (todas `true` por defecto)
| Variable | Efecto al poner `false` |
|---|---|
| `Features__Images` | El chat rechaza imágenes. |
| `Features__RepositoryAccess` | Los agentes no leen repositorios (`get_repo_tree`, `get_file`); el chat rechaza `projectId`. |
| `Features__ArchitectureAnalysis` | Se oculta `analizar_arquitectura`. |
| `Features__PolicyEvaluation` | Se oculta `evaluar_politicas`. |
| `Features__TerraformGeneration` | Se oculta `generar_terraform`. |
| `Features__SecurityReview` | Se oculta `revisar_seguridad`. |

El orquestador recibe en cada mensaje la lista de capacidades deshabilitadas para no ofrecerlas.

### Repositories / Projects / AzureDevOps
| Variable | Defecto | Secreto | Descripción |
|---|---|---|---|
| `Repositories__Provider` | `None` | No | `None` (solo texto e imágenes), `Local` (carpetas) o `AzureDevOps` (solo lectura). |
| `Repositories__LocalRoot` | vacío | No | Base para rutas `Path` relativas (relativa a la carpeta de la app si no es absoluta). |
| `Projects__{i}__Id` | `demo-pagos` | No | Id corto (a-z, 0-9, guion). Máx. 20 proyectos. |
| `Projects__{i}__DisplayName` | — | No | Nombre visible. |
| `Projects__{i}__Path` | vacío | No | **Local**: carpeta del proyecto. |
| `Projects__{i}__Project` / `Repository` / `Branch` | — / — / `main` | No | **AzureDevOps**: proyecto, repositorio y rama. |
| `AzureDevOps__OrganizationUrl` | vacío | No | `https://dev.azure.com/<org>` (solo con `AzureDevOps`). |
| `AzureDevOps__Pat` | vacío | **Sí** | PAT. Vacío = token de Entra de la identidad. En Azure: referencia a Key Vault. |
| `AzureDevOps__ApiVersion` / `TimeoutSeconds` | 7.1 / 60 | No | API REST y tiempo de espera. |

> Los proyectos se enlazan por índice: el `demo-pagos` del JSON por defecto se reemplaza por el índice 0 de las variables
> de entorno. Si define menos proyectos que los del JSON, los restantes del JSON siguen activos.

### Knowledge
| Variable | Defecto | Descripción |
|---|---|---|
| `Knowledge__AgentsDirectory` | `knowledge/agents` | Carpeta publicada de instrucciones y skills. |
| `Knowledge__PolicyCatalogPath` | `knowledge/policies/catalogo.yaml` | Catálogo publicado de políticas. |
| `Knowledge__OrganizationName` | `el banco` | Cómo se nombra a la organización en los prompts. |

### Analysis
| Variable | Defecto | Descripción |
|---|---|---|
| `Analysis__MaxFiles` | 40 | Archivos máximos por revisión. |
| `Analysis__MaxFileBytes` | 100000 | Tamaño máximo por archivo. |
| `Analysis__MaxTotalBytes` | 400000 | Tamaño total máximo (si se excede: `TRUNCADO`). |
| `Analysis__MaxToolOutputChars` | 30000 | Límite de salida de una herramienta. |
| `Analysis__MaxTreeItems` | 300 | Elementos máximos al listar el repositorio. |
| `Analysis__FileExtensions` | `.tf,.tfvars` | Extensiones de Terraform que se revisan. |
| `Analysis__ReadableExtensions` | `.tf,.tfvars,.hcl,.md,.json,.yaml,.yml,.txt` | Lista blanca de lo que el agente puede leer (excluye `.tfstate`, claves, etc.). |
| `Analysis__MaxComponents` / `MaxFlows` | 60 / 120 | Tamaño del modelo de arquitectura. |

### Chat
| Variable | Defecto | Descripción |
|---|---|---|
| `Chat__TimeoutSeconds` | 200 | Presupuesto del chat (máx. 225; el límite HTTP de Azure es ~230 s). |
| `Chat__MaxMessageChars` | 8000 | Largo máximo del mensaje. |
| `Chat__ResponseLanguage` | `español` | Idioma de respuesta. |
| `Chat__MaxImages` / `MaxImageBytes` | 4 / 5000000 | Imágenes por mensaje y tamaño. |
| `Chat__AllowedImageTypes` | `image/png,image/jpeg` | También `image/webp`, `image/gif` (siempre se verifica la firma del archivo). |
| `Chat__MaxGeneratedFiles` / `MaxGeneratedFileBytes` | 20 / 100000 | Límites de archivos Terraform por conversación. |

### Generation
| Variable | Defecto | Descripción |
|---|---|---|
| `Generation__AutoSecurityReview` | `true` | Revisa con el agente de Seguridad el Terraform generado. |
| `Generation__MaxFixRounds` | 1 | Rondas de autocorrección (0-3). |
| `Generation__BlockFrom` | `High` | Severidad desde la que un hallazgo dispara la autocorrección. |
| `Generation__MaxPoliciesInline` | 40 | Políticas completas en el prompt; si hay más, se filtran por recurso. |
| `Generation__TerraformVersion` / `AzurermVersion` | `>= 1.6.0` / `~> 4.0` | Versiones en el código generado. |
| `Generation__DefaultRegion` / `AllowedRegions` | `eastus2` / vacío | Región por defecto y lista permitida (vacío = la del catálogo). |
| `Generation__NamingPattern` | `{tipo}-{app}-{ambiente}-{region}` | Patrón de nombres. |
| `Generation__RequiredTags` | `ambiente,proyecto,responsable,centro_costo` | Etiquetas obligatorias. |
| `Generation__AllowedFileExtensions` | `.tf,.tfvars,.md` | Solo texto declarativo; se rechazan ejecutables (`.sh`, `.ps1`…). |

> Estos parámetros guían al modelo; **las políticas del catálogo siempre mandan** y se verifican por código.

### Plataforma (no son de InfraAgent)
| Variable | Descripción |
|---|---|
| `INFRAAGENT_CONFIG` | Ruta alternativa del JSON de configuración. |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `AzureWebJobsStorage__accountName`, `__credential`, `__clientId` | Almacenamiento del host por identidad. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetría y auditoría. |

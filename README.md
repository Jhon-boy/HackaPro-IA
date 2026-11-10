# InfraAgent

Asistente de **Infraestructura como Código (Terraform) para Azure** con un agente de **Azure AI Foundry** como cerebro.
En el chat, el usuario describe una solución (y puede subir imágenes de arquitectura); el agente:

1. **Interpreta** la arquitectura (texto, imágenes y, opcionalmente, un proyecto/repositorio).
2. **Explica** cómo funcionaría bajo las políticas de la organización.
3. **Genera Terraform** conforme a esas políticas y lo **revisa** con un agente de seguridad (autocorrección acotada).
4. **Revisa la seguridad** de Terraform existente, con evidencia verificada por código.

El Terraform se entrega **solo en el chat**; nada se ejecuta ni se aplica en Azure. Todas las herramientas corren en
nuestro código (Azure Functions), de solo lectura y con lista cerrada por agente.

## Arquitectura
```
Core (sin dependencias de nube/modelo) → Application (agentes, herramientas, flujos, puertos)
   → Infrastructure (Foundry, repositorios, auditoría, DI) → Functions (host HTTP) + Cli + Tests
```
- Cinco agentes: **Orquestador, Arquitectura, Políticas, Generador IaC, Seguridad**. Pueden compartir un agente de Foundry o tener uno cada uno.
- El **repositorio es la fuente de verdad** del conocimiento: `agents/` (instrucciones y skills) y `policies/catalogo.yaml`.
- El modelo no decide a ciegas: `EvidenceVerifier` descarta hallazgos sin evidencia literal verificable y fuerza la severidad desde el catálogo.
- Seguridad: herramientas de solo lectura, parámetros validados, secretos enmascarados antes del modelo, lista blanca de extensiones legibles, límites con `TRUNCADO`, auditoría en Application Insights.

## Configurable en todo
Una sola sección (`InfraAgent`) con JSON + variables de entorno + Key Vault. Se puede cambiar sin tocar código: capacidades
(`Features`), proveedor de repositorios (`None`/`Local`/`AzureDevOps`), región, nombres, etiquetas y versiones de Terraform
(`Generation`), idioma, tipos de imagen, límites, políticas y agentes. Lista completa: **[docs/CONFIGURACION.md](docs/CONFIGURACION.md)**.

## Estructura
```
src/InfraAgent.Core | .Application | .Infrastructure | .Functions | .Cli
tests/InfraAgent.Tests          xUnit (dotnet test)
agents/                         instrucciones y skills por agente
policies/catalogo.yaml          catálogo de políticas (EJEMPLO: reemplazar por el de la organización)
config/infraagent.settings.json configuración por defecto
samples/demo-pagos/             Terraform con fallas a propósito para la demo
deploy/terraform/               despliegue de la Function y su infraestructura
pipelines/deploy-function.yml   (opcional) despliegue del código
```

## Demo local (5 minutos)
```powershell
dotnet build InfraAgent.sln
dotnet test  InfraAgent.sln

cd src/InfraAgent.Cli
dotnet user-secrets set "InfraAgent:Foundry:Endpoint" "https://<recurso>.services.ai.azure.com/api/projects/<proyecto>"
dotnet user-secrets set "InfraAgent:Foundry:DefaultAgentId" "asst_..."
az login

dotnet run -- status            # configuración, capacidades y catálogo
dotnet run -- policies          # políticas vigentes
dotnet run -- chat --project demo-pagos
dotnet run -- chat --image .\arquitectura.png --out .\salida-terraform
```
Ejemplos para el chat:
- «Analiza la arquitectura del proyecto y dime qué incumple las políticas» (`--project demo-pagos`).
- «Genera el Terraform de una API con Key Vault y SQL privados según esta imagen» (`--image`).

## API (Azure Functions)
| Método y ruta | Auth | Descripción |
|---|---|---|
| `GET /api/health` | Anónimo | Salud. |
| `GET /api/status` | Función | Diagnóstico de configuración (solo nombres). |
| `GET /api/config` | Función | Parámetros efectivos no secretos. |
| `GET /api/projects` | Función | Proyectos habilitados. |
| `POST /api/chat` | Función | `{ message, conversationId?, projectId?, images[], architecture?, files[] }` |
| `DELETE /api/chat/{conversationId}` | Función | Borra la conversación en Foundry. |

El servicio no guarda estado: el cliente devuelve `conversationId`, `architecture` y `files` en el siguiente turno.
Functions local: copie `src/InfraAgent.Functions/local.settings.sample.json` a `local.settings.json` y use `func start`.

## Despliegue
`deploy/terraform` (ver su README) crea la infraestructura y **todos los App Settings centralizados**.

## Pendientes antes de producción
- Reemplazar `policies/catalogo.yaml` (16 políticas de **ejemplo**) por las reales.
- Registrar los ids reales de los agentes de Foundry y validar el comportamiento contra el servicio real.
- Validar `deploy/terraform` con `terraform validate`/`plan` y endurecer la red (Private Endpoints).

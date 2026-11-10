# InfraAgent

Chat para analizar arquitecturas Azure, evaluar políticas, revisar seguridad y generar Terraform mediante
un agente desplegado en **Azure AI Foundry**. El código generado se entrega en el chat: InfraAgent nunca
ejecuta comandos ni aplica infraestructura.

## Arquitectura simple

```text
src/
├── InfraAgent/              lógica, agentes, Foundry, seguridad y conocimiento
└── InfraAgent.Functions/    API HTTP de Azure Functions
tests/
└── InfraAgent.Tests/        pruebas xUnit
infra/
└── terraform/               despliegue opcional en Azure
docs/                        documentación
```

Solo hay tres proyectos .NET:

1. `InfraAgent`: lógica organizada por capacidades internas.
2. `InfraAgent.Functions`: entrada HTTP, sin lógica de negocio.
3. `InfraAgent.Tests`: pruebas.

La separación interna mediante interfaces e inyección de dependencias permite extraer módulos a proyectos
independientes cuando el tamaño real lo justifique.

## Capacidades

- Agentes especializados: Orquestador, Arquitectura, Políticas, Generador IaC y Seguridad.
- Texto e imágenes de arquitectura.
- Terraform conforme al catálogo de políticas con revisión y autocorrección acotada.
- Lectura opcional de carpetas locales o repositorios Azure DevOps.
- Evidencia verificable: se descartan hallazgos inventados por el modelo.
- Herramientas cerradas de solo lectura, validación de entradas y enmascarado de secretos.

El conocimiento está junto al código:

- `src/InfraAgent/Knowledge/agents`: instrucciones y skills.
- `src/InfraAgent/Knowledge/policies/catalogo.yaml`: catálogo de ejemplo que debe reemplazarse.
- `src/InfraAgent/infraagent.settings.json`: valores predeterminados sin secretos.

## Configuración

Una sola sección `InfraAgent`, configurable mediante JSON, variables de entorno y referencias de Key Vault.
Consulte [docs/CONFIGURACION.md](docs/CONFIGURACION.md).

Los únicos datos imprescindibles para usar Foundry son:

```text
InfraAgent__Foundry__Endpoint
InfraAgent__Foundry__DefaultAgentId
```

## Desarrollo

```powershell
dotnet build InfraAgent.sln
dotnet test InfraAgent.sln
```

Para ejecutar la Function localmente, copie:

```text
src/InfraAgent.Functions/local.settings.sample.json
```

como `local.settings.json`, configure Foundry y ejecute `func start`.

## API

| Método y ruta | Descripción |
|---|---|
| `GET /api/health` | Salud del servicio. |
| `GET /api/status` | Diagnóstico sin exponer valores secretos. |
| `GET /api/config` | Configuración efectiva no secreta. |
| `GET /api/projects` | Proyectos habilitados. |
| `POST /api/chat` | Chat, imágenes, arquitectura y archivos Terraform. |
| `DELETE /api/chat/{conversationId}` | Elimina el hilo de Foundry. |

## Despliegue

`infra/terraform` crea la Function App, identidad administrada, almacenamiento y observabilidad.
Terraform no fue validado localmente porque la herramienta no está instalada en este entorno.

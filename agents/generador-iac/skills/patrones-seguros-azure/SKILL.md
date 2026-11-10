---
name: patrones-seguros-azure
description: Úsala al generar recursos de Azure (storage, Key Vault, SQL, red privada, diagnóstico) para partir de fragmentos Terraform conformes a las políticas.
---

# Patrones seguros de Azure (azurerm ~> 4.0)

Los fragmentos viven en `references/`. Cárgalos con `load_skill` indicando `name` = `patrones-seguros-azure` y `resource` = el archivo:

- `almacenamiento.md`: cuenta de almacenamiento y contenedores privados.
- `key-vault.md`: Key Vault con RBAC, purge protection y red cerrada.
- `sql.md`: Azure SQL Server con solo Entra ID y sin acceso público.
- `red-privada.md`: Private Endpoint con zona DNS privada y reglas de NSG seguras.
- `diagnostico.md`: diagnostic settings hacia Log Analytics.

## Reglas al usarlos
- Son **puntos de partida**: adapta nombres, variables y etiquetas; conserva los atributos de seguridad.
- Combina siempre el recurso con su Private Endpoint cuando el servicio sea PaaS de datos.
- No agregues atributos que no conozcas con certeza; anota la duda en `notes`.

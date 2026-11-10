---
name: inferir-desde-terraform
description: Úsala cuando la arquitectura deba deducirse de archivos Terraform o del repositorio (recursos, módulos, referencias y exposición de red).
---

# Inferir la arquitectura desde Terraform

## Método
1. **Inventario**: lista cada `resource "azurerm_*"` y cada `module`. Cada uno es un componente; el nombre local (`"main"`) es parte de su `name`.
2. **Flujos**: una referencia de un recurso a otro (`azurerm_subnet.app.id`, `azurerm_key_vault.kv.id`) implica una dependencia o un flujo. Un `azurerm_private_endpoint` conecta una subred con un servicio.
3. **Exposición**:
   - `public_network_access_enabled = true` (o ausente en servicios que lo permiten por defecto) → `public`.
   - Recurso con `azurerm_public_ip` asociado → `public`.
   - Solo accesible por Private Endpoint o subred → `private`.
4. **Datos**: nombres como `pii`, `clientes`, `tarjetas`, `backup` sugieren datos sensibles → `confidencial` (como supuesto, no como hecho).
5. **Módulos externos**: si un módulo no está en el repositorio, regístralo en `openQuestions` (no sabes qué crea por dentro).

## Reglas
- Usa solo lo que está en los archivos entregados. Lo que no puedas ver, va a `assumptions` u `openQuestions`.
- Un valor `***MASKED***` es un secreto protegido; no cambia la arquitectura.

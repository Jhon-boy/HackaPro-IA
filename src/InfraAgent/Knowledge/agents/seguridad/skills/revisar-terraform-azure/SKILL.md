---
name: revisar-terraform-azure
description: Úsala al revisar Terraform de Azure para seguir un checklist de seguridad (exposición, cifrado, identidad, secretos, red, monitoreo) y redactar hallazgos con evidencia.
---

# Checklist de revisión de seguridad de Terraform (Azure)

Revisa por **recurso**, no por archivo. Para cada tipo de recurso aplica la lista del archivo `references/checklist-seguridad.md` (cárgalo con `load_skill`, `resource` = `checklist-seguridad.md`).

## Orden recomendado
1. **Secretos en el código** (siempre primero): literales en `password`, `secret`, `key`, `token`, `connection_string`; valores `***MASKED***` donde debería haber una referencia.
2. **Exposición de red**: `public_network_access_enabled`, reglas NSG abiertas, IP públicas, `default_action = "Allow"` en firewalls.
3. **Cifrado y TLS**: `min_tls_version`, tráfico solo HTTPS, cifrado en reposo.
4. **Identidad y acceso**: identidad administrada, RBAC, autenticación solo con Entra ID.
5. **Resiliencia de datos**: soft delete, purge protection, versionado, retención.
6. **Registro y monitoreo**: diagnostic settings, alertas.

## Cómo redactar un hallazgo
- **Título** corto y específico ("Storage account con acceso público habilitado").
- **Descripción**: hecho observado + riesgo en una frase.
- **Recomendación**: atributo y valor exactos a cambiar.
- **Evidencia**: copia literal del bloque o atributo, sin números de línea.

## Qué no es un hallazgo
- Una buena práctica general sin regla en el catálogo (menciónala en `summary`).
- Algo que no puedes comprobar en los archivos entregados.
- Un atributo ausente cuyo valor por defecto del proveedor ya cumple la regla.

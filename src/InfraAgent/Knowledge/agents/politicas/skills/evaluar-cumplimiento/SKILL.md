---
name: evaluar-cumplimiento
description: Úsala para evaluar política por política con un método repetible (aplicabilidad, evidencia, estado y ajuste), sea sobre arquitectura o sobre código.
---

# Evaluar cumplimiento de políticas

## Método por cada política del catálogo
1. **¿Aplica?** Revisa `Aplica a` (tipos de recurso). Si no hay recursos de ese tipo y no es general → `NoAplica`.
2. **Busca la evidencia**:
   - En código: localiza el recurso y el atributo exacto que la regla exige.
   - En arquitectura: usa `components`, `exposure`, `flows` y los supuestos.
3. **Decide el estado**:
   - `Cumple`: evidencia positiva clara.
   - `NoCumple`: evidencia negativa clara (atributo con valor prohibido, recurso expuesto, ausencia de un componente obligatorio).
   - `Parcial`: se cumple en parte (p. ej. 2 de 3 recursos).
   - `Desconocido`: falta información; **no asumas cumplimiento**.
4. **Ajuste**: la acción mínima y concreta (atributo y valor, o componente a agregar).

## Errores comunes a evitar
- Marcar `Cumple` por no ver el problema: si no ves el atributo, es `Desconocido` salvo que el valor por defecto del proveedor cumpla la regla (y entonces dilo).
- Citar un fragmento que no está literalmente en el archivo.
- Reportar hallazgos de seguridad generales que no tienen una política en el catálogo.
- Mezclar varias políticas en un solo hallazgo: un hallazgo por política y recurso.

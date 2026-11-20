# AGENTS.md — Home Manager

## Estado actual

El proyecto se encuentra en la **Fase 4: funciones complementarias**, con un primer vertical funcional de incidencias domésticas terminado.

Fecha de actualización: 20 de noviembre de 2026.

## Fases completadas

### Fase 1 — Base funcional

Implementado:

- Aplicación Expo/React Native con TypeScript.
- API REST ASP.NET Core y PostgreSQL.
- Registro e inicio de sesión mediante JWT.
- Contraseñas almacenadas mediante hash.
- Creación de hogares.
- Invitaciones con token y caducidad.
- Miembros y aislamiento de datos por hogar.
- Swagger/OpenAPI.
- Credenciales móviles con SecureStore y alternativa web con `localStorage`.

Pendiente de endurecimiento:

- Refresh tokens rotatorios.
- Permisos configurables y autorización granular.
- Recuperación de contraseña.
- Edición completa del perfil y cambio entre varios hogares desde la interfaz.

### Fase 2 — Organización doméstica

Implementado:

- Agenda mensual con eventos y tareas.
- Navegación entre meses y detalle por día.
- Tareas con fecha, prioridad, responsable y estado.
- Estados pendiente, en proceso, completada y aplazada.
- Checklists persistentes.
- Conversión de incidencias en tareas sin duplicarlas.
- Formularios de creación mediante modales.

Pendiente:

- Recurrencias de eventos y tareas.
- Rotaciones automáticas.
- Comentarios y fotografías.
- Participantes y recordatorios configurables.
- Edición y borrado completos.
- Estadísticas por integrante.

### Fase 3 — Alimentación, inventario y compras

Implementado:

- Productos de inventario.
- Cantidad, unidad, mínimo y ubicación básica.
- Movimientos atómicos con claves de idempotencia.
- Prevención de stock negativo.
- Aviso visual de stock bajo.
- Listas y artículos de compra.
- Marcado de artículos comprados.
- Confirmación separada de recepción.
- Actualización del inventario al confirmar la entrada.
- Creación automática de productos recibidos que aún no existen.
- Modelo y API de recetas e ingredientes.
- Relación opcional entre entradas de menú y recetas.

Pendiente:

- Editor móvil completo de recetas.
- Lotes y fechas de caducidad.
- Categorías y ubicaciones configurables.
- Reservas de ingredientes.
- Cálculo de faltantes para menús.
- Consumo confirmado al cocinar.
- Unidades compatibles y ajuste de raciones.
- Sugerencias de recetas y generación inteligente de compras.

## Fase actual — Fase 4

Implementado en esta fase:

- Registro de incidencias domésticas.
- Descripción, ubicación, prioridad y responsable.
- Estados pendiente, en reparación y resuelta.
- Fecha de resolución.
- Conversión idempotente de una incidencia en tarea.
- Acceso secundario a incidencias, sin añadir una sexta pestaña principal.

Siguiente trabajo recomendado, en este orden:

1. Fotografías y archivos asociados a incidencias.
2. Documentos compartidos con permisos privados por hogar y usuario.
3. Alertas de stock bajo y caducidad.
4. Notificaciones push y recordatorios.
5. Escaneo de códigos de barras.
6. SignalR para actualizaciones entre dispositivos.
7. Cola offline e idempotencia del cliente.

## Fase 5 — Calidad y publicación

Todavía no iniciada.

Debe incluir:

- Pruebas unitarias, de integración y de interfaz.
- Pruebas de aislamiento entre hogares.
- Pruebas de concurrencia e idempotencia.
- Pruebas multiusuario.
- Accesibilidad y adaptación móvil.
- Seguridad, observabilidad y copias de seguridad.
- Preparación para Android e iOS.

## Arquitectura y ejecución

- Móvil: `apps/mobile`
- Backend: `src/backend/HomeManager.Api`
- Documentación: `docs/ARCHITECTURE.md`
- PostgreSQL local: `docker-compose.yml`
- Migraciones: `src/backend/HomeManager.Api/Migrations`

Comandos habituales:

```powershell
docker compose up -d
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/backend/HomeManager.Api
dotnet run --project src/backend/HomeManager.Api --urls http://0.0.0.0:5080
```

En otra terminal:

```powershell
cd apps/mobile
npx expo start --web --clear
```

## Reglas para continuar el desarrollo

- Mantener el aislamiento por `HomeId` en todas las consultas y operaciones.
- Comprobar la membresía en el backend; nunca confiar solo en el cliente.
- No devolver entidades de Entity Framework con navegaciones bidireccionales. Usar DTOs o proyecciones planas para evitar ciclos JSON.
- Aplicar transacciones e idempotencia en movimientos de inventario y confirmaciones de compra.
- Marcar un artículo como comprado no debe incrementar el inventario; solo la confirmación de recepción puede hacerlo.
- No descontar ingredientes hasta que el usuario confirme el consumo.
- Toda creación contextual en la interfaz debe abrir un modal.
- Mantener Inicio, Agenda, Tareas, Menú e Inventario como navegación principal. Compras, incidencias, documentos y configuración deben ser accesos secundarios o contextuales.
- No presentar funciones pendientes mediante botones decorativos o datos simulados.
- Al añadir entidades, generar y aplicar una migración de EF Core.
- Antes de entregar cambios, ejecutar:

```powershell
dotnet build HomeManager.sln --configuration Release
cd apps/mobile
npx tsc --noEmit
npx expo export --platform web --output-dir dist-verification
```

## Problemas corregidos que no deben reintroducirse

- En web, la API debe usar `http://localhost:5080`; `10.0.2.2` es solo para el emulador Android.
- SecureStore no funciona en web; usar el adaptador existente con `localStorage`.
- Mantener `MapInboundClaims = false` o aceptar tanto `sub` como `NameIdentifier` al leer el usuario del JWT.
- No serializar directamente relaciones como `Task → ChecklistItem → Task`, `ShoppingList → Item → ShoppingList` o `Recipe → Ingredient → Recipe`.
- Esperar a que exista un hogar activo antes de acceder a `home.id`.

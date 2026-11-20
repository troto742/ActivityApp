# Home Manager — arquitectura y plan

## Arquitectura general

Monolito modular: Expo/React Native consume una API REST ASP.NET Core. PostgreSQL es la fuente de verdad. Cada recurso compartido lleva `HomeId`; la API obtiene el usuario del JWT y comprueba `HomeMembers` antes de leer o escribir. La evolución prevista separa cada módulo en Dominio, Aplicación, Infraestructura y API sin introducir microservicios.

```text
Móvil (Expo) -> API REST + SignalR (previsto) -> módulos -> EF Core -> PostgreSQL
                                              -> objetos privados (previsto)
```

## Carpetas

- `apps/mobile`: app TypeScript, caché remota con TanStack Query y credenciales en SecureStore.
- `src/backend/HomeManager.Api`: API, autenticación, entidades y persistencia de la primera entrega.
- `docs`: decisiones, contrato y modelo objetivo.
- `docker-compose.yml`: PostgreSQL local.

Cuando aumente el dominio, el backend se dividirá en `Domain`, `Application`, `Infrastructure` y `Api`, manteniendo módulos Agenda, Tasks, Meals, Inventory, Shopping y Documents.

## Modelo relacional

El vertical slice actual implementa `Users`, `Homes`, `HomeMembers`, `HomeInvitations`, `Tasks`, `TaskChecklistItems`, `Events`, `MealPlanEntries`, `Recipes`, `RecipeIngredients`, `Products`, `InventoryMovements`, `ShoppingLists`, `ShoppingListItems` y `HouseholdIssues`. Índices únicos protegen correo, token de invitación e idempotencia de movimientos; la clave compuesta de membresía evita duplicados; todos los recursos tienen índice por hogar.

El modelo objetivo añade las tablas solicitadas: participantes/recordatorios/recurrencias de evento; asignaciones/checklists/recurrencias/ejecuciones/comentarios de tareas; recetas/ingredientes/preferencias; categorías/ubicaciones/lotes/movimientos/umbrales/reservas; items y registros de compra; incidencias, documentos/permisos, notificaciones y auditoría. Los movimientos usarán transacción, idempotency key única por hogar y control de concurrencia. Las entidades privadas añadirán ACL explícita; no se confiará solo en filtros del cliente.

Relaciones esenciales: `User N—N Home` mediante `HomeMember`; `Home 1—N` para todo dato compartido; una receta tiene ingredientes y se referencia desde una entrada de menú; un producto tiene lotes por ubicación; una compra confirmada genera movimientos de inventario; eventos e incidencias pueden referenciar tareas sin duplicarlas.

## Navegación y pantallas

Barra principal: Inicio, Agenda, Tareas, Menú e Inventario. Compras, miembros, incidencias, documentos y ajustes son accesos contextuales. Flujo inicial: acceso/registro → crear o seleccionar hogar → resumen → listas de cada módulo → formulario sencillo. La entrega actual incluye acceso, creación de hogar y vistas funcionales de los cinco recursos iniciales; la navegación Expo Router completa es el siguiente incremento.

## Contratos HTTP implementados

- `POST /api/auth/register`, `POST /api/auth/login`
- `GET|POST /api/homes`
- `POST /api/homes/{homeId}/invitations`, `POST /api/invitations/{token}/accept`
- `GET|POST /api/homes/{homeId}/tasks`
- `PATCH /api/homes/{homeId}/tasks/{taskId}`
- `POST /api/homes/{homeId}/tasks/{taskId}/checklist`
- `PATCH /api/homes/{homeId}/tasks/{taskId}/checklist/{itemId}`
- `GET /api/homes/{homeId}/members`
- `GET|POST /api/homes/{homeId}/events`
- `GET|POST /api/homes/{homeId}/meals`
- `GET|POST /api/homes/{homeId}/products`
- `GET|POST /api/homes/{homeId}/shopping-lists`
- `POST /api/homes/{homeId}/shopping-lists/{listId}/items`
- `PATCH /api/homes/{homeId}/shopping-lists/{listId}/items/{itemId}`
- `POST /api/homes/{homeId}/shopping-lists/{listId}/receive`
- `POST /api/homes/{homeId}/products/{productId}/movements`
- `GET|POST /api/homes/{homeId}/recipes`
- `GET|POST /api/homes/{homeId}/issues`
- `PATCH /api/homes/{homeId}/issues/{issueId}`
- `POST /api/homes/{homeId}/issues/{issueId}/create-task`

Swagger en `/swagger`. Los errores usan códigos HTTP; antes de producción se normalizarán con Problem Details, validación Zod/FluentValidation y refresh tokens rotatorios.

## Plan incremental

1. Endurecer Fase 1: migraciones, refresh token, validación, permisos configurables, invitación por enlace, dashboard agregado y pruebas de aislamiento.
2. Agenda y tareas: checklists, participantes, recurrencias inmutables, ejecuciones y rotación.
3. Inventario/alimentación: lotes, unidades compatibles, recetas, reservas, movimientos atómicos y confirmación de compra.
4. SignalR, cola offline idempotente, resolución visible de conflictos, push, documentos privados y escáner.
5. Pruebas E2E/multiusuario, accesibilidad, observabilidad, copias/exportación/borrado y publicación.

## Límites explícitos de esta versión

No están implementados aún: refresh token, borrado, recurrencias, rotaciones, comentarios, fotografías de incidencias, lotes por caducidad, reservas de ingredientes, sugerencias automáticas, SignalR, cola offline, documentos, archivos, notificaciones, escáner y autorización granular. La API de recetas ya existe; su editor completo en la app queda para el siguiente incremento.

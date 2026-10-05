# Gestión de Pedidos — Challenge Fullstack

Aplicación web para gestionar **clientes y pedidos**, con consulta a una **API externa de seguimiento** y un **webhook** que recibe cambios de estado.

**Stack:** .NET 10 · ASP.NET Core MVC · Entity Framework Core · PostgreSQL · Bootstrap 5 · Docker

## Enlaces de entrega

| Qué | URL |
|---|---|
| Aplicación | `https://TU-APP.up.railway.app` |
| Webhook | `POST https://TU-APP.up.railway.app/api/webhooks/orders` |
| API de seguimiento (mock) | `GET https://TU-APP.up.railway.app/api/mock/tracking/{orderId}` |
| Historial de webhooks recibidos | `https://TU-APP.up.railway.app/Home/Webhooks` |
| Repositorio | `https://github.com/TU-USUARIO/gestion-pedidos` |

> Reemplazar `TU-APP` y `TU-USUARIO` por los valores reales después de publicar.

## Funcionalidades

- **Clientes:** alta, edición, ficha con todos sus pedidos, búsqueda por nombre/email/teléfono y filtro por ciudad. El email es único.
- **Pedidos:** alta asociada a un cliente, listado con búsqueda (N° de pedido, descripción o cliente) y filtros por estado y rango de fechas, cambio de estado manual.
- **Estados:** `Pendiente`, `EnPreparacion`, `Enviado`, `Entregado`, `Cancelado`.
- **API externa:** el detalle de cada pedido consulta, desde el backend y por HTTP, una API de seguimiento (código, transportista, fecha estimada). Si la API falla, la pantalla muestra un aviso y no se rompe (timeout de 5 s).
- **Webhook:** recibe `order.status.changed`, actualiza el pedido y guarda cada evento recibido (visible en *Webhooks recibidos*).
- **Datos de ejemplo:** al iniciar con la base vacía se cargan 5 clientes y 20 pedidos (los pedidos 1 a 15 quedan en *Pendiente*).

## Probar el webhook

```bash
curl -X POST https://TU-APP.up.railway.app/api/webhooks/orders \
  -H "Content-Type: application/json" \
  -d '{"event":"order.status.changed","orderId":15,"status":"DELIVERED"}'
```

Respuesta: `{"ok":true,"mensaje":"Pedido 15: Pendiente → Entregado"}`

**Estados aceptados en `status`** (sin importar mayúsculas): `PENDING`, `PROCESSING`, `IN_PREPARATION`, `SHIPPED`, `IN_TRANSIT`, `DELIVERED`, `CANCELLED`/`CANCELED`, y también los nombres en español.

| Código | Cuándo |
|---|---|
| 200 | Pedido actualizado |
| 400 | JSON inválido, evento distinto de `order.status.changed`, falta `orderId` o estado desconocido |
| 401 | Se configuró una clave y el header `X-Webhook-Secret` falta o no coincide |
| 404 | El pedido no existe |

**Clave opcional:** si se define la variable de entorno `Webhook__Secret`, el webhook exige el header `X-Webhook-Secret` con ese valor. Si no se define, acepta cualquier envío (más simple para la revisión).

## Configuración

| Variable | Para qué |
|---|---|
| `DATABASE_URL` | Conexión a PostgreSQL en formato `postgresql://usuario:clave@host:puerto/base` (la entrega Railway/Render) |
| `ConnectionStrings__Default` | Alternativa en formato Npgsql (`Host=...;Database=...`); se usa si no hay `DATABASE_URL` |
| `Tracking__BaseUrl` | URL de una API de seguimiento real. Vacío = usa el mock incluido |
| `Webhook__Secret` | Clave opcional del webhook |
| `PORT` | Puerto donde escucha la app (lo define la plataforma; por defecto 8080) |

Las tablas se crean solas al iniciar (`EnsureCreated`), no hace falta correr migraciones.

## Correr localmente

Requisitos: .NET 10 SDK y PostgreSQL.

```bash
dotnet run
```

Usa la conexión de `appsettings.json` (`localhost`, usuario y clave `postgres`). Abrir `http://localhost:8080`.

Con Docker:

```bash
docker build -t gestion-pedidos .
docker run -p 8080:8080 -e DATABASE_URL=postgresql://postgres:postgres@host.docker.internal:5432/gestion_pedidos gestion-pedidos
```

## Estructura

```
Program.cs                         Arranque: servicios, base, rutas
Models/                            Cliente, Pedido, EstadoPedido, EventoWebhook, DTOs y ViewModels
Data/AppDbContext.cs               Tablas y reglas de la base (Entity Framework)
Data/ConexionDb.cs                 Convierte DATABASE_URL al formato de Npgsql
Data/DatosIniciales.cs             Datos de ejemplo
Services/TrackingService.cs        Cliente HTTP de la API de seguimiento
Services/EstadoMapper.cs           Traducción de estados externos → internos
Controllers/ClientesController.cs  Pantallas de clientes
Controllers/PedidosController.cs   Pantallas de pedidos
Controllers/Api/WebhookController.cs        POST /api/webhooks/orders
Controllers/Api/TrackingMockController.cs   GET /api/mock/tracking/{id}
Views/                             Pantallas Razor (Bootstrap)
Dockerfile                         Build y ejecución en contenedor
```

## Decisiones de diseño

- **MVC en un solo proyecto:** el alcance es chico; separar en capas/proyectos agregaría complejidad sin beneficio.
- **Estado guardado como texto** en la base: las consultas SQL y los datos son legibles.
- **API externa detrás de una interfaz (`ITrackingService`)** y con URL configurable: se puede reemplazar el mock por un proveedor real sin tocar los controladores.
- **Registro de cada evento del webhook:** permite auditar qué llegó y qué se respondió.
- **Fechas en UTC** en la base y mostradas en hora de Argentina (UTC-3).

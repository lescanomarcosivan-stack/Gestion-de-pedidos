# Gestión de Pedidos — Challenge Fullstack

Aplicación web para gestionar **clientes y pedidos**, con usuarios y roles, consulta a una **API externa de seguimiento**, un **webhook firmado** que recibe cambios de estado, dashboard, historial y bitácora de actividad.

**Stack:** .NET 10 · ASP.NET Core MVC · Entity Framework Core · PostgreSQL · Bootstrap 5 · Docker

## Enlaces

| Qué | URL |
|---|---|
| Aplicación | `https://gestion-de-pedidos-production.up.railway.app` |
| Webhook | `POST https://gestion-de-pedidos-production.up.railway.app/api/webhooks/orders` |
| API de seguimiento (mock) | `GET https://gestion-de-pedidos-production.up.railway.app/api/mock/tracking/{orderId}` |
| Repositorio | `https://github.com/TU-USUARIO/Gestion-de-pedidos` |

## Usuarios de demostración

Se crean solos la primera vez. Contraseña: la de la variable `Demo__Password` (por defecto `Demo2026!`).

| Email | Rol | Puede |
|---|---|---|
| `admin@demo.com` | Administrador | Todo, más aprobar usuarios, cambiar roles y ver el registro de actividad |
| `operador@demo.com` | Operador | Crear y editar clientes y pedidos, cambiar estados, probar el webhook |
| `consulta@demo.com` | Consulta | Solo ver |

El administrador puede **invitar** usuarios con el rol que elija (*Usuarios → Invitar*): les llega un email con un enlace de un solo uso (vence en 72 horas, en la base se guarda solo su hash) para elegir su contraseña y entrar.

Las cuentas nuevas (con **Crear cuenta** o **Ingresar con Google**) quedan **inactivas con rol Consulta** hasta que un administrador las activa en *Usuarios*. Con la variable `Registro__AprobacionAutomatica=true` se activarían solas (siempre con rol Consulta).

## Funcionalidades

**Datos**
- Clientes: alta, edición, ficha con pedidos e historial de cambios, búsqueda y filtro por ciudad.
- Pedidos: alta, listado **paginado** (10 por página) con búsqueda, filtro por estado y rango de fechas, cambio de estado.
- **Historial de estados** por pedido: quién, cuándo, de qué estado a cuál y por qué vía (Alta / Manual / Webhook).
- **Dashboard**: cantidad y monto por estado, totales **sin cancelados** (los cancelados van en un cuadro separado, en rojo), pedidos abiertos y últimos cambios.
- Los pedidos **cancelados** se muestran en rojo y con el monto tachado en todas las pantallas.
- Montos en formato argentino (`$ 1.234,56`) y fechas en hora de Argentina.

**Productos y stock (versión 3)**
- Catálogo de **productos** con código, precio, stock y stock mínimo; alta, edición y activar/desactivar.
- Pedidos con **varios productos**: cantidad, **precio unitario** (copiado del catálogo al vender), **descuento por renglón** y **descuento general**. El total se calcula en pantalla y se recalcula en el servidor con los precios de la base.
- **Control de stock**: al crear el pedido se descuenta; si se **cancela** (manual o webhook) se devuelve; si se **reabre**, se vuelve a descontar (si no alcanza, no deja). No permite vender más de lo que hay.
- **Movimientos de stock** (ingresos, ventas, devoluciones y ajustes con motivo), con historial por producto.
- **Control de concurrencia** en productos (columna de sistema `xmin` de PostgreSQL): si dos usuarios modifican el mismo producto o stock a la vez, el segundo recibe un aviso en lugar de pisar al primero.
- **Notificaciones por email** en segundo plano (cola + worker): al **cliente** (si aceptó recibirlas) cuando se crea, envía, entrega o cancela su pedido; al **equipo** (`Notificaciones__EmailsInternos`) por pedido nuevo, cancelación y **stock bajo**. Cada envío queda en *Registro* (tipo Email).
- **Reportes Excel (.xlsx) y PDF** de pedidos (con los filtros del listado) y de stock, generados por la propia app sin librerías externas.

**Google Drive y Calendar (versión 4)**
- Se conecta **una cuenta de Google de la empresa** (OAuth 2.0 *offline*, una sola vez, desde el menú *Google*, solo administradores). La llave (refresh token) se guarda **cifrada con AES-GCM**.
- **Archivos adjuntos** en cada pedido (PDF, imágenes, Excel, Word, TXT, CSV; hasta 10 MB), guardados en la carpeta "Gestión de Pedidos" del Drive de la empresa. Permiso `drive.file`: la app solo ve los archivos que ella creó. La descarga pasa por la app (requiere sesión).
- **Fecha de entrega** en el pedido, publicada en **Google Calendar** como evento de día completo. Se actualiza sola al cambiar fecha o estado y se borra si el pedido se cancela (cola + trabajador en segundo plano, idempotente).
- Sin librerías de Google: llamadas HTTP directas a las APIs REST.

**Usabilidad**
- Si la fecha "Desde" es posterior a "Hasta" se muestra un error, sin listar resultados.
- Botón **Limpiar filtros** en todos los listados.
- Mensajes distintos para "todavía no hay datos" y "los filtros no encontraron resultados".
- Los pedidos **cancelados** no consultan ni muestran seguimiento; los **entregados** muestran la fecha real de entrega.

**Seguridad**
- Login con email y contraseña. Las contraseñas se guardan con **PBKDF2** (`PasswordHasher` de ASP.NET Core: sal aleatoria y 100.000 iteraciones); nunca en texto plano.
- **Login con Google** (OAuth 2.0), opcional según configuración.
- **Todas las pantallas requieren sesión** (política global `FallbackPolicy`). Solo son públicas: login, registro, recuperación, webhook y API mock.
- **Roles** Administrador / Operador / Consulta, verificados en el servidor (`[Authorize(Roles = ...)]`), no solo ocultando botones.
- Si un administrador desactiva una cuenta o le cambia el rol, su sesión se cierra en el siguiente clic.
- **Bloqueo** de la cuenta por 15 minutos tras 5 contraseñas incorrectas, y **límite** de 10 intentos de login por minuto por IP.
- **Recuperación de contraseña** por email con enlace de un solo uso que vence a los 30 minutos (en la base se guarda solo el hash del código).
- Cookies `HttpOnly`, protección **anti-CSRF** en todos los formularios y protección contra *open redirect*.
- **Webhook firmado con HMAC-SHA256**, timestamp contra reenvíos y límite de 60 avisos por minuto por IP.

**Registro (bitácora)**
- Accesos (logins, fallos, bloqueos, logout, accesos denegados), actividad de usuarios, webhooks y errores no controlados. Pantalla *Registro* (solo administradores) con filtros y paginación.

## Webhook

### Autenticación (obligatoria)

El servidor rechaza todo si no tiene la variable `Webhook__Secret`. Hay dos formas de autenticarse:

**1. Firma HMAC (recomendada).** La clave nunca viaja por la red.

```
X-Webhook-Timestamp: <segundos Unix actuales>
X-Webhook-Signature: sha256=<HMAC-SHA256 en hex de "timestamp.cuerpo", con la clave como llave>
```

Ejemplo en bash:

```bash
URL=https://gestion-de-pedidos-production.up.railway.app/api/webhooks/orders
CLAVE='tu-clave'
BODY='{"event":"order.status.changed","orderId":15,"status":"DELIVERED"}'
TS=$(date +%s)
SIG="sha256=$(printf '%s' "$TS.$BODY" | openssl dgst -sha256 -hmac "$CLAVE" | awk '{print $2}')"
curl -X POST $URL -H "Content-Type: application/json" -H "X-Webhook-Timestamp: $TS" -H "X-Webhook-Signature: $SIG" -d "$BODY"
```

Script *Pre-request* para Postman (Body → raw → JSON; agregar la variable `webhookSecret`):

```javascript
const ts = Math.floor(Date.now() / 1000).toString();
const firma = CryptoJS.HmacSHA256(ts + "." + pm.request.body.raw, pm.variables.get("webhookSecret")).toString(CryptoJS.enc.Hex);
pm.request.headers.upsert({ key: "X-Webhook-Timestamp", value: ts });
pm.request.headers.upsert({ key: "X-Webhook-Signature", value: "sha256=" + firma });
```

**2. Clave en header (para pruebas manuales rápidas):** `X-Webhook-Secret: <clave>`.

**3. Desde la propia app:** *Webhooks → Probar webhook* arma, firma y envía el evento por HTTP real, y muestra la respuesta. También permite enviar uno con firma inválida o sin firma para ver el rechazo.

### Respuestas

| Código | Cuándo |
|---|---|
| 200 | Pedido actualizado (o ya estaba en ese estado) |
| 400 | JSON inválido, evento distinto de `order.status.changed`, falta `orderId` o estado desconocido |
| 401 | Falta autenticación, firma inválida, clave incorrecta o timestamp con más de 5 minutos |
| 404 | El pedido no existe |
| 429 | Más de 60 avisos por minuto desde la misma IP |
| 503 | El servidor no tiene configurada la clave |

**Estados aceptados** (sin importar mayúsculas): `PENDING`, `PROCESSING`, `IN_PREPARATION`, `SHIPPED`, `IN_TRANSIT`, `DELIVERED`, `CANCELLED`/`CANCELED`, y los nombres en español.

Cada evento queda en *Webhooks* con una marca **✓ Procesado** o **✗ Rechazado**, el código, el método de autenticación y el motivo.

## Configuración (variables de entorno)

| Variable | Obligatoria | Para qué |
|---|---|---|
| `DATABASE_URL` | Sí | PostgreSQL (`postgresql://usuario:clave@host:puerto/base`); Railway la entrega |
| `Webhook__Secret` | Sí | Clave compartida del webhook |
| `Demo__Password` | Recomendada | Contraseña de los usuarios demo (por defecto `Demo2026!`) |
| `Demo__AdminEmail` | No | Email del administrador inicial (por defecto `admin@demo.com`) |
| `App__UrlPublica` | Recomendada | URL pública, para armar los enlaces de los emails |
| `Autenticacion__Google__ClientId` / `__ClientSecret` | No | Activa "Ingresar con Google" |
| `Email__BrevoApiKey` / `Email__Remitente` | No | Envío de emails por la API de Brevo. Sin esto, el enlace de recuperación queda en los logs del servidor |
| `Registro__AprobacionAutomatica` | No | `false` por defecto: las cuentas nuevas requieren aprobación del administrador. `true`: se activan solas con rol Consulta |
| `Notificaciones__EmailsInternos` | No | Emails del equipo separados por coma (pedido nuevo, cancelado, stock bajo) |
| `Google__CalendarioId` | No | Calendario para las entregas (vacío = el principal de la cuenta conectada) |
| `Tracking__BaseUrl` | No | API de seguimiento real (vacío = mock incluido) |

> Los emails se envían por la API HTTP de Brevo porque Railway bloquea SMTP en los planes que no son Pro.

## Base de datos

Las tablas se crean solas al iniciar (`EnsureCreated`). Si la app detecta tablas de una versión anterior, las recrea con datos de ejemplo (es una base de demostración). En un sistema productivo se usarían **migraciones** de EF Core.

Tablas: `Clientes`, `Pedidos`, `PedidoItems`, `Productos`, `MovimientosStock`, `HistorialEstados`, `EventosWebhook`, `Usuarios`, `RegistrosActividad`, `ArchivosPedido`, `IntegracionesGoogle`.

Las tablas y columnas de las versiones 3 y 4 se agregan a una base existente con `Data/ActualizacionesBase.cs` (SQL con `IF NOT EXISTS`), **sin borrar datos**.

## Estructura

```
Program.cs                              Arranque: servicios, seguridad, base, rutas
Models/                                 Cliente, Pedido, Usuario, HistorialEstado, RegistroActividad, EventoWebhook, DTOs, ViewModels
Data/                                   AppDbContext, conexión, datos iniciales
Services/TrackingService.cs             Cliente HTTP de la API de seguimiento
Services/FirmaWebhook.cs                Firma HMAC-SHA256
Services/Auditoria.cs                   Bitácora
Services/EnviadorEmail.cs               Emails (Brevo)
Services/ManejadorErrores.cs            Registro de errores no controlados
Services/EstadoMapper.cs, Formato.cs    Estados, colores y formatos
Controllers/CuentaController.cs         Login, Google, registro, recuperación
Controllers/UsuariosController.cs       Aprobación y roles (admin)
Controllers/RegistroController.cs       Bitácora (admin)
Controllers/HomeController.cs           Dashboard, webhooks y probador
Controllers/ClientesController.cs       Clientes
Controllers/PedidosController.cs        Pedidos
Controllers/IntegracionesController.cs  Conexión con Google (admin)
Services/GoogleEmpresa.cs               OAuth, Google Drive y Google Calendar
Services/ColaCalendario.cs              Cola y trabajador de Calendar
Controllers/Api/WebhookController.cs    POST /api/webhooks/orders
Controllers/Api/TrackingMockController.cs  GET /api/mock/tracking/{id}
Views/                                  Pantallas Razor (paleta de azules en Shared/_Layout.cshtml)
```

## Correr localmente

Requisitos: .NET 10 SDK y PostgreSQL.

```bash
dotnet run
```

Usa la conexión de `appsettings.json`. Para probar el webhook localmente hay que definir `Webhook__Secret`.

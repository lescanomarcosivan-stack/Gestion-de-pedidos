# Guía de estudio para la revisión técnica

En la revisión te van a pedir que **expliques, modifiques y corrijas** el código. Esta guía te prepara para eso. Leela junto con el código: cada línea tiene un comentario explicando qué hace.

Orden sugerido de estudio (1–2 días):
1. Conceptos básicos (sección 1)
2. Recorrer los archivos en el orden de la sección 2
3. Los 3 flujos completos (sección 3)
4. Practicar las preguntas (sección 4) en voz alta
5. Hacer al menos 2 de los ejercicios de modificación (sección 5)
6. **Versión 2 (seguridad, roles, webhook firmado, dashboard): sección 7.** Es lo más probable que te pregunten, porque fueron las observaciones que te hicieron.

---

## 1. Conceptos que tenés que poder explicar con tus palabras

| Concepto | Explicación simple |
|---|---|
| **ASP.NET Core** | Framework de Microsoft para hacer aplicaciones web con C#. |
| **MVC** | Patrón Modelo–Vista–Controlador. **Modelo** = los datos (Cliente, Pedido). **Vista** = la pantalla HTML (`.cshtml`). **Controlador** = recibe el pedido del navegador, busca datos y elige qué vista mostrar. |
| **Razor** | Sintaxis de las vistas `.cshtml`: HTML mezclado con C# usando `@`. |
| **Entity Framework Core (EF)** | ORM: traduce código C# (LINQ) a SQL. Evita escribir SQL a mano. |
| **DbContext** | La clase que representa la base. Cada `DbSet` es una tabla. |
| **LINQ** | Forma de consultar colecciones en C#: `.Where()`, `.OrderBy()`, `.Select()`. EF lo convierte en SQL. |
| **Npgsql** | El "driver" que permite a .NET hablar con PostgreSQL. |
| **Inyección de dependencias** | En vez de crear objetos con `new`, el controlador los pide en el constructor y ASP.NET Core se los entrega. Se registran en `Program.cs`. |
| **async / await** | Mientras se espera a la base o a la API, el servidor no queda bloqueado y puede atender a otros usuarios. |
| **GET / POST** | GET = pedir/ver información (no cambia nada). POST = enviar datos que cambian algo (crear, editar). |
| **Códigos HTTP** | 200 OK, 302 redirección, 400 datos inválidos, 401 no autorizado, 404 no existe, 500 error del servidor. |
| **API** | Punto de acceso que devuelve datos (JSON) en lugar de una página. |
| **JSON** | Formato de texto para intercambiar datos: `{"orderId": 15}`. |
| **Webhook** | Una URL nuestra que **otro sistema llama** cuando pasa algo (al revés de una API, donde nosotros llamamos). |
| **Mock** | Simulación de un servicio externo para poder desarrollar y probar sin el real. |
| **DTO** | Clase "molde" para leer o escribir JSON. No es una tabla. |
| **ViewModel** | Clase que junta los datos que necesita una pantalla puntual. |
| **Docker / Dockerfile** | Receta para empaquetar la app con todo lo que necesita y ejecutarla igual en cualquier servidor. |
| **Variables de entorno** | Configuración que se define en el servidor (Railway), no en el código. Ej: `DATABASE_URL`. Así las claves no quedan en el repositorio. |
| **Anti-forgery token (CSRF)** | Código oculto en cada formulario que prueba que el envío viene de nuestra página. |
| **Model binding / validación** | ASP.NET Core arma el objeto C# a partir del formulario y revisa los atributos `[Required]`, `[Range]`, etc. El resultado queda en `ModelState`. |

---

## 2. Recorrido por los archivos (en este orden)

1. **`GestionPedidos.csproj`** — versión de .NET y el único paquete externo (Npgsql para EF Core).
2. **`Models/`**
   - `EstadoPedido.cs`: los 5 estados posibles.
   - `Cliente.cs` y `Pedido.cs`: las tablas. Mirá los atributos de validación y la relación (`Cliente.Pedidos` ↔ `Pedido.ClienteId`).
   - `EventoWebhook.cs`: historial de avisos recibidos.
   - `Dtos.cs`: moldes del JSON de la API y del webhook.
   - `ViewModels.cs`: datos armados para pantallas puntuales.
3. **`Data/`**
   - `AppDbContext.cs`: tablas + reglas (email único, estado como texto, precisión del monto, relación 1 a muchos).
   - `ConexionDb.cs`: convierte la URL de Railway al formato de Npgsql.
   - `DatosIniciales.cs`: datos de ejemplo.
4. **`Program.cs`** — el arranque. Leelo con atención: registra servicios, crea las tablas, define rutas.
5. **`Services/`**
   - `TrackingService.cs`: llamada HTTP a la API externa, con manejo de errores.
   - `EstadoMapper.cs`: traduce "DELIVERED" → `Entregado`, colores y formato de fecha.
6. **`Controllers/`**
   - `ClientesController.cs`, `PedidosController.cs`: pantallas.
   - `Api/WebhookController.cs`: el webhook.
   - `Api/TrackingMockController.cs`: la API simulada.
   - `HomeController.cs`: inicio, historial de webhooks, error.
7. **`Views/`** — las pantallas. Empezá por `Shared/_Layout.cshtml` (el marco común).
8. **`Dockerfile`** — cómo se construye y ejecuta en Railway.

---

## 3. Los tres flujos que más te van a preguntar

### Flujo A — Crear un pedido
1. El usuario entra a `/Pedidos/Create` → `PedidosController.Create()` (GET) llena el combo de clientes y muestra `Views/Pedidos/Create.cshtml`.
2. Completa y envía → POST a `/Pedidos/Create`.
3. `[ValidateAntiForgeryToken]` verifica el token.
4. `[Bind("ClienteId,Descripcion,Monto")]` toma solo esos campos del formulario.
5. Se revisan validaciones (`ModelState.IsValid`) y que el cliente exista.
6. Se fija estado `Pendiente` y fechas, `_db.Pedidos.Add(pedido)` y `SaveChangesAsync()` → **INSERT** en PostgreSQL.
7. `RedirectToAction(Details)` → el navegador va al detalle del pedido nuevo (patrón POST-Redirect-GET: evita que al recargar se cree dos veces).

### Flujo B — Ver detalle con la API externa
1. `/Pedidos/Details/15` → `PedidosController.Details(15)`.
2. Busca el pedido con su cliente (`Include` = JOIN).
3. Llama a `_tracking.ObtenerAsync(15)` → `TrackingService` hace **GET** a `.../api/mock/tracking/15`.
4. La API responde JSON → se convierte en `TrackingInfo`.
5. Si falla (timeout de 5 s, error, sin conexión) se devuelve un mensaje y la pantalla muestra un aviso amarillo en vez de romperse.
6. Se arma `PedidoDetalleViewModel` y se muestra la vista.

**Clave:** la consulta se hace **desde el backend** (como pide el PDF), no desde el navegador. Ventajas: no se exponen claves de la API al usuario, se evitan problemas de CORS y se controla el manejo de errores en un solo lugar.

### Flujo C — Webhook
1. El sistema externo hace **POST** a `/api/webhooks/orders` con `{"event":"order.status.changed","orderId":15,"status":"DELIVERED"}` y los headers de firma.
2. El controlador lee el **texto exacto** del cuerpo (la firma se calcula sobre ese texto) y lo convierte a `WebhookRequest` con `JsonSerializer`.
3. Primero la **seguridad**: ¿hay clave en el servidor? (si no, 503) → ¿la firma o la clave son correctas? (si no, 401).
4. Después el **contenido**: JSON válido → tipo de evento → `orderId` → estado conocido → pedido existente.
5. `EstadoMapper.TryTraducir("DELIVERED")` → `Entregado`.
6. Se actualiza el pedido, se agrega una fila a `HistorialEstados`, un `EventoWebhook` y un `RegistroActividad`, todo en el **mismo** `SaveChangesAsync()` (o se guarda todo o nada).
7. Responde JSON con 200 / 400 / 401 / 404 / 503.

---

## 4. Preguntas probables y cómo responderlas

**¿Por qué MVC y no Razor Pages?**
Los dos están permitidos. Elegí MVC porque separa claramente la lógica (controladores) de las pantallas (vistas) y es el patrón más conocido; facilita explicar el recorrido de cada pedido HTTP.

**¿Por qué todo en un solo proyecto y no en capas (API, dominio, infraestructura)?**
El alcance es chico. Separar en varios proyectos agregaría complejidad sin beneficio real. Igual hay separación por carpetas: Models, Data, Services, Controllers, Views. Si creciera, el primer paso sería mover la lógica de los controladores a servicios.

**¿Cómo se crean las tablas? ¿Usaste migraciones?**
Uso `EnsureCreatedAsync()` al arrancar: crea las tablas si no existen. Es simple y suficiente para el challenge. Su límite: no aplica cambios a tablas ya creadas. Por eso, en la versión 2, `Program.cs` revisa si faltan tablas nuevas y, si la base es de la versión anterior, borra las tablas de la app y las vuelve a crear (son datos de demostración). En un proyecto real usaría **migraciones** (`dotnet ef migrations add ...` y `Database.MigrateAsync()`), que versionan cada cambio del esquema.

**¿Por qué el estado se guarda como texto?**
Con `HasConversion<string>()` la base guarda "Entregado" en vez de 3. Es más legible y si se reordena el enum no se corrompen los datos.

**¿Por qué `decimal` para el monto?**
`double` tiene errores de redondeo (0.1 + 0.2 ≠ 0.3). Para dinero siempre `decimal`. En la base es `numeric(12,2)`.

**¿Por qué las fechas en UTC?**
Es la práctica estándar (no depende de dónde esté el servidor) y Npgsql lo exige para columnas `timestamp with time zone`. Se convierten a hora argentina solo al mostrarlas.

**¿Qué pasa si llega el mismo webhook dos veces?**
El resultado es el mismo: el pedido queda en el estado indicado. Es **idempotente**. Si el pedido ya estaba en ese estado, responde 200 "sin cambios" y no agrega una fila repetida al historial de estados (el evento sí queda en la lista de webhooks).

**¿Qué pasa si llegan webhooks desordenados (primero DELIVERED, después SHIPPED)?**
Hoy gana el último que llega. Una mejora sería guardar la fecha del evento y descartar los más viejos, o definir transiciones válidas (no volver de Entregado a Enviado).

**¿Cómo se protege el webhook?**
Con una **firma HMAC-SHA256** del cuerpo más un timestamp (ver sección 7). También acepta la clave en el header `X-Webhook-Secret` para pruebas manuales. Si el servidor no tiene clave configurada, rechaza todo (503).

**¿Qué pasa si la API externa se cae?**
El `HttpClient` tiene timeout de 5 segundos y el servicio captura el error: el detalle del pedido se muestra igual con un aviso. Mejoras posibles: reintentos con Polly y caché de la respuesta.

**¿Por qué la API de seguimiento es un mock dentro de la misma app?**
El PDF lo permite. Igual se consume por HTTP real, como si fuera externa, y la URL es configurable (`Tracking__BaseUrl`): para usar una API real solo cambia esa variable, sin tocar código. Además está detrás de la interfaz `ITrackingService`.

**¿Para qué sirve la interfaz `ITrackingService`?**
El controlador depende del "contrato", no de la implementación. Permite cambiar de proveedor o usar una versión falsa en tests unitarios.

**¿Qué es `[Bind]` y por qué lo usás?**
Limita qué campos del formulario se aceptan. Evita *overposting*: que alguien agregue un campo `Estado=Entregado` al crear un pedido.

**¿Para qué sirve `[ValidateNever]` en `Pedido.Cliente`?**
Con nullable activado, MVC trataría la navegación como obligatoria y daría error de validación, aunque el formulario solo manda `ClienteId`.

**¿Cómo funciona la búsqueda sin importar mayúsculas?**
Paso todo a minúsculas (`ToLower()`) en ambos lados. EF lo traduce a `lower(...)` en SQL. En PostgreSQL también se podría usar `ILIKE`.

**¿Qué hace `Include`?**
Le dice a EF que traiga también la entidad relacionada (JOIN). Sin él, `pedido.Cliente` sería null.

**¿Por qué en el listado de clientes usás `Select` con un ViewModel?**
Para que la base calcule la cantidad de pedidos (`COUNT`) y traiga solo las columnas necesarias, en vez de traer todos los pedidos de todos los clientes.

**¿Cómo se conecta a la base en Railway?**
Railway da `DATABASE_URL` (formato URL). `ConexionDb.cs` la convierte al formato que entiende Npgsql. Localmente se usa la cadena de `appsettings.json`.

**¿Qué mejorarías con más tiempo?**
Migraciones de EF Core, tests automáticos (webhook, firma, roles), reglas de transición de estados (no volver de Entregado a Pendiente), guardar las claves de Data Protection en la base (para que las sesiones sobrevivan a un redeploy), autenticación en dos pasos, reintentos con Polly en la API externa y exportar el registro a un servicio de logs.

---

## 5. Ejercicios de modificación (practicá antes de la revisión)

Para probar cambios en tu PC necesitás .NET 10 SDK y PostgreSQL (o Docker). Si no querés instalar nada, podés editar en GitHub (lápiz ✏️) y Railway vuelve a publicar solo en 2–3 minutos.

> Ojo: como las tablas se crean con `EnsureCreated`, **si agregás una columna nueva** en una base que ya existe, no se agrega sola. En Railway: borrá la base Postgres y creala de nuevo (pierde los datos, vuelven los de ejemplo), o pasá a migraciones.

### Ejercicio 1 — Agregar un estado nuevo "Devuelto"
1. `Models/EstadoPedido.cs`: agregar `Devuelto` a la lista.
2. `Services/EstadoMapper.cs`: agregar `["RETURNED"] = EstadoPedido.Devuelto,` y `["DEVUELTO"] = EstadoPedido.Devuelto,` en el diccionario.
3. En `ColorBadge` agregar `EstadoPedido.Devuelto => "bg-warning text-dark",`.
4. Listo: aparece solo en los combos (se arman con `Enum.GetValues`). No requiere cambiar la base porque se guarda como texto.

### Ejercicio 2 — Agregar el campo "Dirección" al cliente
1. `Models/Cliente.cs`: copiar el bloque de `Ciudad` y cambiar a `Direccion` (`[StringLength(200)]`, `[Display(Name = "Dirección")]`).
2. `ClientesController.cs`: agregar `Direccion` en los dos `[Bind("...")]` y en Edit `cliente.Direccion = datos.Direccion;`.
3. `Views/Clientes/Formulario.cshtml`: copiar el bloque de Ciudad y cambiar `asp-for="Direccion"`.
4. `Views/Clientes/Details.cshtml`: agregar una fila `<dt>Dirección</dt><dd>@Model.Direccion</dd>`.
5. Recrear la base (ver la nota de arriba).

### Ejercicio 3 — Filtrar pedidos por monto mínimo
1. `PedidosController.Index`: agregar el parámetro `decimal? montoMin`.
2. Agregar: `if (montoMin.HasValue) consulta = consulta.Where(p => p.Monto >= montoMin.Value);`
3. Agregar `ViewBag.MontoMin = montoMin;`
4. En `Views/Pedidos/Index.cshtml`: `<input type="number" name="montoMin" value="@ViewBag.MontoMin" class="form-control" placeholder="Monto mín." />` dentro del formulario.

### Ejercicio 4 — Que el webhook no permita "des-entregar" un pedido
En `WebhookController`, antes de actualizar:
```csharp
if (pedido.Estado == EstadoPedido.Entregado && nuevoEstado != EstadoPedido.Entregado)
    return await RegistrarAsync(solicitud, StatusCodes.Status409Conflict, "El pedido ya fue entregado");
```
(409 = conflicto con el estado actual.)

### Ejercicio 5 — "Corregir un bug" (te lo pueden plantear así)
Probá romper algo a propósito y encontrar el error: por ejemplo, sacá el `.Include(p => p.Cliente)` del detalle del pedido y fijate que el nombre del cliente aparece vacío. Entender **por qué** pasa es lo que evalúan.

---

## 6. Tips para la revisión
- Si no sabés algo, decilo y explicá cómo lo averiguarías. Inventar se nota.
- Tené abiertas la app, el repositorio y Railway (logs) antes de que empiece.
- Si te piden un cambio, primero explicá **dónde** lo harías y **por qué**, después escribí el código.
- Usar IA está permitido: lo que evalúan es que entiendas el resultado.


---

## 7. Versión 2 — lo que agregaste y cómo explicarlo

### 7.1 Archivos nuevos (recorrelos en este orden)

1. `Models/Usuario.cs` — la tabla de usuarios y la clase `Roles` con los 3 roles.
2. `Models/HistorialEstado.cs` y `Models/RegistroActividad.cs` — historial de cada pedido y bitácora general.
3. `Program.cs` — leé de nuevo la parte de **Autenticación**, **Autorización**, **Rate limiting** y **ForwardedHeaders**.
4. `Controllers/CuentaController.cs` — login, Google, registro, recuperación.
5. `Services/FirmaWebhook.cs` y `Controllers/Api/WebhookController.cs` — el webhook firmado.
6. `Services/Auditoria.cs` y `Services/ManejadorErrores.cs` — la bitácora.
7. `Controllers/HomeController.cs` (dashboard y probador), `UsuariosController.cs`, `RegistroController.cs`.
8. `Services/Formato.cs` — montos `$ 1.234,56`.
9. `Views/Shared/_Layout.cshtml` (paleta azul) y `Views/Shared/_Paginacion.cshtml`.

### 7.2 Conceptos nuevos

| Concepto | Explicación simple |
|---|---|
| **Autenticación** | Averiguar **quién sos** (login). |
| **Autorización** | Decidir **qué podés hacer** (roles). |
| **Hash de contraseña** | Transformación de una sola vía: de la contraseña sale el hash, pero del hash no se puede volver a la contraseña. Al hacer login se calcula el hash de lo que escribiste y se compara. |
| **Sal (salt)** | Valor aleatorio que se mezcla con la contraseña antes del hash. Dos usuarios con la misma contraseña tienen hashes distintos, y no sirven las tablas precalculadas. |
| **PBKDF2** | Algoritmo de hash **lento a propósito** (100.000 repeticiones): para un atacante, probar millones de contraseñas se vuelve carísimo. Lo usa `PasswordHasher` de Microsoft. |
| **Cookie de sesión** | Después del login, el servidor manda una cookie **cifrada y firmada** con tu Id, email y rol. El navegador la devuelve en cada pedido. `HttpOnly` impide que JavaScript la lea. |
| **Claims** | Los datos del usuario que van dentro de la cookie (Id, email, nombre, rol). |
| **OAuth 2.0 (Google)** | Google verifica tu identidad y nos devuelve tus datos (email, nombre, id). Nosotros nunca vemos tu contraseña de Google. |
| **FallbackPolicy** | Regla que se aplica a **toda** pantalla que no diga otra cosa: "exigir sesión". Así ninguna pantalla nueva queda pública por olvido. `[AllowAnonymous]` marca las excepciones. |
| **[Authorize(Roles = ...)]** | Atributo que exige un rol. Se verifica **en el servidor**: aunque alguien escriba la URL a mano, no pasa. Ocultar botones es solo comodidad visual. |
| **Rate limiting** | Límite de pedidos por minuto por IP. Frena ataques de fuerza bruta (probar contraseñas) y abusos del webhook. Responde 429. |
| **Bloqueo de cuenta** | Después de 5 contraseñas incorrectas, la cuenta se bloquea 15 minutos. Complementa al rate limiting (que es por IP). |
| **HMAC** | "Firma" de un mensaje usando una clave secreta compartida. Si cambian una coma del mensaje, la firma ya no coincide. Prueba **quién** lo mandó y que **no fue modificado**, sin enviar la clave. |
| **Timestamp / replay** | Si alguien captura un aviso válido y lo reenvía más tarde (*replay*), el timestamp viejo (más de 5 minutos) lo delata. El timestamp va dentro de la firma, así que no se puede cambiar. |
| **Comparación en tiempo constante** | `FixedTimeEquals` tarda lo mismo aunque los textos difieran en el primer carácter o en el último. Evita que un atacante adivine la firma midiendo tiempos de respuesta. |
| **Token de recuperación** | Código aleatorio de 32 bytes que va en el enlace del email. En la base se guarda solo su hash (SHA-256), vence a los 30 minutos y se usa una sola vez. |
| **Enumeración de usuarios** | Que un atacante descubra qué emails existen. Por eso el login dice siempre "Email o contraseña incorrectos" y la recuperación siempre "si existe, te enviamos un enlace". |
| **Open redirect** | Que un enlace de login te devuelva a un sitio malicioso. `Url.IsLocalUrl` solo permite volver a páginas de nuestra app. |
| **ForwardedHeaders** | Railway recibe el HTTPS y le pasa el pedido a la app por HTTP interno, avisando por headers la IP real y que era HTTPS. Sin esto, Google rechazaría la dirección de vuelta y las IPs del registro serían todas iguales. |
| **Paginación (Skip/Take)** | `Skip` = saltear N filas (OFFSET en SQL) y `Take` = traer N (LIMIT). Más un `COUNT` para saber cuántas páginas hay. |
| **GROUP BY** | El dashboard agrupa los pedidos por estado y la base calcula `COUNT` y `SUM` en una sola consulta. |

### 7.3 Cómo resolviste cada observación

| Observación | Dónde está | Cómo |
|---|---|---|
| Desde posterior a Hasta | `PedidosController.Index` | Si `desde > hasta`, se arma `ErrorFiltro`, no se consulta la base y la vista muestra el error en rojo con los campos marcados. |
| Cancelados con seguimiento | `PedidosController.Details` + `Details.cshtml` | Si está cancelado, no se llama a la API. Si está entregado, se muestra la fecha real de entrega y no la estimada. |
| Formato de montos | `Services/Formato.cs` | `Moneda()` usa punto para miles y coma para decimales. Se definió a mano (no con la cultura "es-AR") para que funcione igual en cualquier servidor. Ojo: el **ingreso** de montos sigue usando punto, porque `<input type="number">` siempre envía con punto. |
| Limpiar filtros | Vistas `Index` | Enlace a la acción sin parámetros. Se deshabilita si no hay filtros. |
| Probar el webhook | `HomeController.ProbarWebhook` | Arma el JSON, lo firma y lo envía por **HTTP real** al propio endpoint. En *Webhooks*, cada evento tiene "✓ Procesado" o "✗ Rechazado" con código y motivo, y filtros. |
| Vacío vs. sin resultados | `ExistenPedidos` / `ExistenClientes` | Si la base no tiene ningún registro: "todavía no hay…". Si tiene registros pero el filtro no encontró ninguno: "ningún… coincide". |
| Login seguro | `CuentaController` + `PasswordHasher` | Ver conceptos de hash, sal y PBKDF2. |
| Google | `Program.cs` (`AddGoogle`) + `CuentaController.GoogleRespuesta` | Se activa solo si están las credenciales. Si el usuario es nuevo, se crea pendiente de aprobación. |
| Pantallas protegidas | `FallbackPolicy` en `Program.cs` | Todo exige sesión salvo lo marcado con `[AllowAnonymous]`. |
| Roles | `Models/Usuario.cs` (`Roles`) + `[Authorize(Roles=...)]` | Administrador: todo. Operador: carga datos. Consulta: solo ve. |
| Webhooks no autorizados | `WebhookController.Verificar` | HMAC + timestamp + rate limit. Sin clave configurada, rechaza todo. |
| Historial | `HistorialEstados` (pedidos) + `RegistrosActividad` (clientes) | Cada cambio guarda antes/después, quién y origen. |
| Dashboard | `HomeController.Index` | GROUP BY por estado; se completan con 0 los estados sin pedidos. |
| Paginación | `PedidosController.Index` + `_Paginacion.cshtml` | 10 por página; los enlaces conservan los filtros. |
| Recuperación de contraseña | `CuentaController.OlvideClave` / `RestablecerClave` | Token aleatorio, hash en la base, 30 minutos, un solo uso, email por la API de Brevo. |
| Registro de accesos, errores, etc. | `Auditoria` + `ManejadorErrores` + pantalla *Registro* | 4 tipos: Acceso, Actividad, Webhook, Error. |

### 7.4 Preguntas probables de la versión 2

**¿Dónde se guardan las contraseñas y cómo?**
En `Usuarios.PasswordHash`, como hash PBKDF2 con sal aleatoria y 100.000 iteraciones (formato de `PasswordHasher` de ASP.NET Core). La contraseña real no se guarda nunca. Si la base se filtrara, no se podrían leer.

**¿Por qué no usaste ASP.NET Core Identity completo?**
Identity agrega muchas tablas y pantallas generadas. Para este alcance preferí un modelo propio y simple que puedo explicar línea por línea, pero usando las **piezas seguras de Microsoft**: `PasswordHasher` para las contraseñas y la autenticación por cookies del framework. No inventé criptografía propia.

**¿Qué pasa si alguien escribe a mano /Usuarios siendo Operador?**
`[Authorize(Roles = Roles.Administrador)]` lo rechaza en el servidor y lo manda a *Acceso denegado*. El intento queda en el registro.

**Si desactivo a un usuario que está conectado, ¿sigue adentro?**
No. En cada pedido, `OnValidatePrincipal` (en `Program.cs`) revisa en la base que el usuario siga activo y con el mismo rol. Si no, invalida la cookie.

**¿Cómo funciona la firma del webhook? Explicalo con un ejemplo.**
El emisor y nosotros compartimos una clave secreta. El emisor calcula `HMAC-SHA256(clave, "1728230000.{json}")` y lo manda en `X-Webhook-Signature`, junto con el timestamp `1728230000`. Nosotros hacemos el mismo cálculo con nuestra copia de la clave y comparamos. Si coincide, el mensaje es auténtico y no fue modificado. La clave nunca viaja por la red.

**¿Por qué el timestamp?**
Para que no puedan reenviar un aviso viejo capturado (*replay attack*). Como el timestamp está dentro de lo firmado, tampoco se puede cambiar sin romper la firma.

**¿Por qué leés el cuerpo a mano en vez de usar [FromBody]?**
Porque la firma se calcula sobre el texto **exacto** recibido. Si dejara que el framework lo convierta y lo volviera a serializar, un espacio o el orden de los campos cambiaría la firma.

**¿Qué pasa si llegan muchos webhooks o muchos intentos de login?**
El rate limiter corta en 60 por minuto por IP (webhook) y 10 por minuto por IP (login, registro, recuperación). Responde 429.

**¿Por qué la recuperación de contraseña usa Brevo y no SMTP de Gmail?**
Railway bloquea el puerto SMTP en los planes que no son Pro. Brevo tiene una API HTTP (puerto 443) y un plan gratuito. El envío está detrás de la interfaz `IEnviadorEmail`, así que cambiar de proveedor no toca los controladores.

**¿Por qué los usuarios nuevos quedan inactivos?**
Porque cualquiera puede crear una cuenta o entrar con Google. Si quedaran activos, cualquier persona de internet vería los datos de clientes. Con aprobación, el administrador decide quién entra y con qué rol. Si algún día se quisiera que se activen solas (siempre con rol Consulta), alcanza con la variable `Registro__AprobacionAutomatica=true`: es configuración, no código.

**¿Cómo se registran los errores?**
`ManejadorErrores` implementa `IExceptionHandler`: ASP.NET Core lo llama ante cualquier error no controlado. Guarda tipo, mensaje, pantalla, usuario e IP usando un `DbContext` **nuevo** (el del pedido puede haber quedado en mal estado) y deja que se muestre la página de error.

**¿Por qué la bitácora se guarda en el mismo SaveChanges que el cambio?**
Para que sea consistente: si el cambio falla, tampoco queda un registro de algo que no pasó, y viceversa.

**¿Cómo hiciste la paginación sin perder los filtros?**
`_Paginacion.cshtml` copia todos los parámetros de la URL actual y solo reemplaza `pagina`. El controlador hace `COUNT` para el total y `Skip/Take` para la página. Ordena por fecha y desempata por Id para que no se repitan filas entre páginas.

**¿Por qué los estados son azules y Cancelado es rojo?**
La app usa una gama de azules, y cada etiqueta lleva además el **nombre** del estado para no depender solo del color. Cancelado va en **rojo** a propósito: es un pedido anulado y tiene que saltar a la vista. Su monto aparece **tachado en rojo**.

**¿Los pedidos cancelados suman en el total del dashboard?**
No. Un pedido anulado no es plata real, así que `HomeController.Index` calcula el total y los porcentajes solo con los estados vigentes. Los cancelados se muestran en un **cuadro separado** más abajo (`UltimosCancelados`), en rojo y con el monto tachado.

### 7.5 Ejercicios de la versión 2

**Ejercicio A — Que el rol Consulta no vea la pantalla de Webhooks.**
En `HomeController.Webhooks` agregá `[Authorize(Roles = Roles.Edicion)]` arriba del método. En `_Layout.cshtml`, envolvé el `<li>` de Webhooks en `@if (User.IsInRole(Roles.Administrador) || User.IsInRole(Roles.Operador)) { ... }`.

**Ejercicio B — Bloquear la cuenta con 3 intentos en vez de 5.**
`CuentaController`: cambiá `private const int MaximoIntentos = 5;` por `3`.

**Ejercicio C — Mostrar 20 pedidos por página.**
`PedidosController`: `private const int TamanoPagina = 10;` → `20`.

**Ejercicio D — Registrar también cuando alguien abre el detalle de un pedido.**
En `PedidosController.Details`, antes del `return View(modelo);`:
```csharp
await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Actividad, "Consulta de pedido", null, "Pedido", id);
```

**Ejercicio E — Explicá qué pasa si borrás `[AllowAnonymous]` de `TrackingMockController`.**
El detalle del pedido muestra un aviso amarillo de error en el seguimiento: el servidor llama a la API **sin cookie de sesión**, la regla global exige sesión y la llamada se redirige al login, que devuelve una página HTML en lugar del JSON esperado. Es un buen ejemplo de por qué la FallbackPolicy afecta a todo.


---

## 8. Versión 3 — productos, stock, emails y reportes

### 8.1 Archivos nuevos
1. `Models/Producto.cs`, `Models/PedidoItem.cs`, `Models/MovimientoStock.cs` — las tablas nuevas.
2. `Models/Pedido.cs` — ahora tiene `Items` (renglones) y `DescuentoPorcentaje`.
3. `Services/Calculos.cs` — **el único lugar** donde se calcula el total (pantalla, reportes y emails dan siempre lo mismo).
4. `Services/StockService.cs` — todas las entradas y salidas de stock.
5. `Controllers/PedidosController.cs` (`Create` y `CambiarEstado`) y `Controllers/ProductosController.cs`.
6. `Services/ColaEmails.cs` y `Services/Notificador.cs` — emails en segundo plano.
7. `Services/Reporte.cs`, `ExportadorExcel.cs`, `ExportadorPdf.cs` — reportes.
8. `Data/ActualizacionesBase.cs` — cómo se actualizó la base sin borrar datos.
9. `Views/Pedidos/Create.cshtml` — el formulario con renglones dinámicos (JavaScript).

### 8.2 Preguntas probables

**¿Por qué el pedido guarda una copia del nombre y del precio del producto?**
Porque los precios cambian. Si mañana la yerba sube, el pedido de ayer tiene que seguir mostrando el precio al que se vendió. Por eso `PedidoItem` guarda `PrecioUnitario` y `ProductoNombre` del momento de la venta.

**¿El precio lo manda el navegador?**
No. El navegador solo manda qué producto y cuántas unidades. El servidor lee el precio de la base (`productos[r.ProductoId].Precio`). Si alguien modificara el HTML para poner un precio menor, no serviría de nada. El cálculo en pantalla es solo para que el usuario vea el total.

**¿Cómo se calcula el total?**
Por renglón: `cantidad × precio × (1 − descuento %)`. Después se suman los renglones y se aplica el descuento general: `subtotal × (1 − descuento general %)`. Todo con `decimal` y redondeado a centavos (`Calculos.TotalPedido`).

**¿Cuándo se mueve el stock?**
- Al **crear** el pedido: se descuenta (venta).
- Al **cancelar** (desde la pantalla o por webhook): se devuelve (devolución).
- Al **reabrir** un cancelado: se vuelve a descontar. Si ya no alcanza, no deja reabrir (por webhook responde 409).
- Los demás cambios (Pendiente → Enviado → Entregado) no mueven stock: ya se descontó al crear.

**¿Qué pasa si piden más de lo que hay?**
`StockService.DescontarAsync` revisa **todos** los productos antes de tocar nada. Si uno no alcanza, no descuenta ninguno y devuelve los errores ("pediste 20 y hay 12").

**¿Por qué hay una tabla de movimientos si ya está el campo Stock?**
Para poder auditar: cada número de stock tiene su explicación (quién, cuándo, por qué, qué pedido). Es como un libro contable: el saldo (Stock) siempre se puede reconstruir sumando los movimientos.

**¿Cómo evitás que dos usuarios vendan la última unidad al mismo tiempo?**
Con **concurrencia optimista**. `Producto.Version` está marcado con `[Timestamp]`, y PostgreSQL lo mapea a su columna de sistema `xmin`, que cambia cada vez que se modifica la fila. Al guardar, EF agrega `WHERE xmin = <valor que leí>`. Si otro usuario modificó el producto en el medio, no se actualiza ninguna fila, EF lanza `DbUpdateConcurrencyException` y la app avisa "el stock cambió, volvé a intentar" en vez de dejar el stock mal. Lo mismo protege la edición de productos: el formulario lleva la versión en un campo oculto.

**¿Por qué todo se guarda en un solo SaveChanges?**
Pedido + renglones + movimientos de stock + historial se guardan juntos en una transacción: o se guarda todo o nada. Nunca puede quedar un pedido sin descontar stock, ni stock descontado sin pedido.

**¿Cómo funcionan los emails?**
El controlador **no envía**: deja el mensaje en una cola (`ColaEmails`, un `Channel` en memoria) y responde enseguida. Un `BackgroundService` (`EnvioEmailsWorker`) toma los mensajes de a uno y los envía por la API de Brevo. Así el usuario no espera, y si Brevo falla, el pedido igual se guarda. Cada envío queda en el Registro.
Limitación: la cola está en memoria. Si la app se reinicia justo con emails pendientes, esos se pierden. En un sistema grande se usaría una cola persistente (una tabla, RabbitMQ, etc.).

**¿Por qué el cliente tiene la casilla "Recibe notificaciones"?**
Por consentimiento: solo se escribe a quien aceptó. Además, los clientes de ejemplo tienen emails de dominios reales, y no corresponde mandarles correos a desconocidos.

**¿Cómo se genera el Excel sin librerías?**
Un `.xlsx` es un ZIP con archivos XML adentro (formato Office Open XML). `ExportadorExcel` arma ese ZIP con `ZipArchive`: el libro, una hoja y los estilos (negrita, fondo celeste, formato de moneda). Los números se guardan como números de verdad, así en Excel se pueden sumar y filtrar.

**¿Y el PDF?**
`ExportadorPdf` escribe el formato PDF directamente: texto con la fuente Helvetica (incluida en todos los lectores de PDF, por eso no hay que adjuntar fuentes), rectángulos y líneas, con paginación y "Página X de Y". Se eligió esto en vez de una librería para no depender de componentes nativos en el servidor. La contra: el diseño es más simple que con una librería de reportes.

**¿Cómo actualizaste la base sin borrar datos?**
`EnsureCreated` no modifica tablas que ya existen. Por eso `ActualizacionesBase` ejecuta SQL con `CREATE TABLE IF NOT EXISTS` y `ADD COLUMN IF NOT EXISTS`: en una base vieja agrega lo que falta, en una base nueva no hace nada. Es una "migración manual". Lo profesional sería usar **migraciones de EF Core** (`dotnet ef migrations add`), que generan este SQL automáticamente y llevan registro de qué versión tiene cada base.

### 8.3 Ejercicios
**A — Que los ajustes de stock solo los pueda hacer el administrador.** En `ProductosController.Mover`, cambiá `Authorize(Roles = Roles.Edicion)` por `Authorize(Roles = Roles.Administrador)`.

**B — Limitar el descuento por renglón a 30 %.** En `Models/ViewModels.cs`, en `ItemFormulario`, cambiá `[Range(0, 100, ...)]` por `[Range(0, 30, ErrorMessage = "Máximo 30 %")]`.

**C — Avisar también al cliente cuando el pedido pasa a "En preparación".** En `Services/Notificador.cs`, agregá `EstadoPedido.EnPreparacion` al arreglo `EstadosQueAvisanAlCliente` y un caso en el `switch` del texto.

## 9. Versión 4 (etapa 2) — Google Drive y Google Calendar

### 9.1 Qué hace
- **Drive:** en el detalle del pedido se adjuntan archivos (PDF, imágenes, Excel, Word, TXT, CSV; máximo 10 MB). Se guardan en el Drive de la empresa, en la carpeta "Gestión de Pedidos", con el nombre `Pedido 15 - remito.pdf`. Se descargan **a través de la app**.
- **Calendar:** cada pedido puede tener **fecha de entrega**. Si la tiene, aparece en el Google Calendar de la empresa como evento de día completo. Si cambia la fecha o el estado, el evento se actualiza (verde con ✓ al entregarse); si se cancela o se quita la fecha, se borra.
- **Pantalla Google** (solo admin): conectar, probar, sincronizar y desconectar la cuenta.

### 9.2 Archivos nuevos
| Archivo | Qué hace |
|---|---|
| `Models/ArchivoPedido.cs` | Datos del adjunto (el contenido vive en Drive; acá solo el id de Drive, nombre, tipo, tamaño, quién y cuándo) |
| `Models/IntegracionGoogle.cs` | La conexión con la cuenta de la empresa (una fila), con la llave **cifrada** |
| `Services/GoogleEmpresa.cs` | Todo lo que habla con Google: permiso (OAuth), Drive y Calendar, con llamadas HTTP directas |
| `Services/ColaCalendario.cs` | Cola + trabajador en segundo plano que pone al día los eventos de Calendar |
| `Controllers/IntegracionesController.cs` | Pantalla Google (conectar, probar, sincronizar, desconectar) |
| `Views/Integraciones/Index.cshtml` | Esa pantalla |

Cambios: `Pedido` tiene `FechaEntrega` y `CalendarioEventoId`; `PedidosController` suma `CambiarFechaEntrega`, `SubirArchivo`, `Archivo` (descarga) y `BorrarArchivo`; el webhook también actualiza Calendar.

### 9.3 Preguntas probables

**¿Cómo accede la app al Drive y al Calendar de la empresa?**
Con **OAuth 2.0 en modo "offline"**. Un administrador toca *Conectar*, Google le pregunta si autoriza a la app a usar Drive y Calendar, y Google devuelve un **código**. La app canjea ese código por un **refresh token** (una llave permanente) y lo guarda cifrado. Cada vez que necesita Google, canjea esa llave por un **access token** que dura una hora (lo guarda en memoria para no pedirlo en cada llamada) y lo manda en el header `Authorization: Bearer ...`.

**¿Por qué no una "cuenta de servicio" (service account)?**
Fue la primera idea, pero las cuentas de servicio **no tienen espacio propio en Drive**: los archivos que suben a una cuenta de Gmail común fallan por falta de cuota. Solo funcionan con *unidades compartidas* de Google Workspace (pago). Conectar la cuenta de la empresa con OAuth cumple lo mismo ("una cuenta de la empresa") y funciona con un Gmail gratuito.

**¿Qué permisos pide y por qué esos?**
`drive.file`: la app **solo ve los archivos que ella misma creó**, no el resto del Drive de la empresa (principio de mínimo privilegio). `calendar.events`: crear, cambiar y borrar eventos. Además `openid email` para saber qué cuenta se conectó. Si el admin destilda una casilla en Google, la app lo detecta y pide reconectar.

**¿Cómo protegés la llave guardada?**
Se cifra con **AES-GCM** (cifrado autenticado: si alguien modifica el dato, se detecta). La clave de cifrado se deriva con SHA-256 del `ClientSecret` de Google, que vive solo en las variables de Railway. Si alguien roba una copia de la base, no puede usar la llave. Si se cambia el ClientSecret, la llave guardada ya no se puede descifrar y hay que reconectar (la app lo avisa).

**¿Qué es el parámetro `state`?**
Un código al azar que la app guarda en una cookie antes de ir a Google y compara cuando Google vuelve. Evita que alguien fuerce a un admin a conectar **otra** cuenta (ataque CSRF sobre OAuth).

**¿Por qué la descarga pasa por la app y no es un enlace directo a Drive?**
Porque el Drive de la empresa es privado: un enlace directo no lo podrían abrir los usuarios. Pasando por la app, se aplica la misma seguridad que al resto (hay que tener sesión) y el archivo se transmite en flujo, sin guardarlo en el servidor.

**¿Cómo validás los archivos subidos?**
Tamaño máximo 10 MB (en el navegador, en el controlador y con `RequestSizeLimit` en el servidor), lista de extensiones permitidas, y el tipo de contenido lo decide el servidor según la extensión (no se confía en el que manda el navegador). Se descarga siempre como adjunto, así un archivo no puede ejecutar código en la página.

**¿Por qué Calendar va por una cola y Drive no?**
Al subir un archivo el usuario necesita saber si salió bien, así que se espera la respuesta. El evento de Calendar es una consecuencia secundaria: si Google tarda o falla, el pedido igual se tiene que guardar. Por eso se encola el número de pedido y un `BackgroundService` lo procesa. El trabajador es **idempotente**: no "agrega" un evento, sino que deja el evento **igual al pedido** (crear si falta, actualizar si existe, borrar si no corresponde). Así no importa cuántas veces se encole el mismo pedido. Si alguien borró el evento a mano en Calendar, se vuelve a crear.

**¿Qué pasa si Google revoca el permiso?**
Al pedir un token, Google responde `invalid_grant`. La app guarda el aviso en `UltimoError` y la pantalla Google muestra "Con problemas — volvé a conectar". Los errores de Calendar quedan en Registro, tipo Google.

**¿Por qué subida "reanudable" (resumable)?**
Drive tiene dos formas de subir. La simple solo se recomienda hasta 5 MB; la reanudable funciona con cualquier tamaño: primero se avisa nombre, carpeta y tamaño, Google devuelve una dirección de subida, y después se manda el contenido.

### 9.4 Ejercicios
**A — Aceptar también archivos ZIP.** En `PedidosController`, agregá `[".zip"] = "application/zip"` a `TiposPermitidos`, y `.zip` al `accept` del input en `Views/Pedidos/Details.cshtml`.

**B — Subir el límite a 20 MB.** Cambiá `MaximoBytes` a `20 * 1024 * 1024`, los dos atributos `RequestSizeLimit`/`RequestFormLimits` a `22 * 1024 * 1024` y el control de JavaScript en `Details.cshtml`.

**C — Que el evento de Calendar dure de 9 a 18 en vez de todo el día.** En `GoogleEmpresa.ArmarEvento`, cambiá `start = new { date = ... }` por `start = new { dateTime = dia.ToString("yyyy-MM-dd") + "T09:00:00", timeZone = "America/Argentina/Buenos_Aires" }` y lo mismo en `end` con `T18:00:00`.

## 10. Versión 5 — Invitar usuarios por email

### 10.1 Cómo funciona
1. El admin completa **nombre, email y rol** en *Usuarios → Invitar*.
2. Se crea la cuenta **inactiva** con `InvitacionPendiente = true` y el rol elegido. Todavía no tiene contraseña, así que nadie puede entrar con ella.
3. Se genera un **código al azar** (32 bytes). En la base se guarda **solo su hash SHA-256** y el vencimiento (72 horas). El código real viaja únicamente en el enlace del email.
4. La persona hace clic → `/Cuenta/AceptarInvitacion?email=...&token=...` → la app verifica que la invitación esté pendiente, que no haya vencido y que el hash coincida (comparación en tiempo constante).
5. Elige su contraseña (se guarda con PBKDF2), la cuenta se activa, el código se borra (un solo uso) y entra directo.

### 10.2 Archivos
| Archivo | Cambio |
|---|---|
| `Models/Usuario.cs` | `InvitacionPendiente` e `InvitadoPor` |
| `Services/CodigosSeguros.cs` | Generar el código y calcular su hash |
| `Controllers/UsuariosController.cs` | `Invitar`, `ReenviarInvitacion`, `CancelarInvitacion` y el armado del email |
| `Controllers/CuentaController.cs` | `AceptarInvitacion` (GET y POST); con Google, una invitación pendiente se acepta sola si el email coincide |
| `Views/Usuarios/Invitar.cshtml`, `Views/Cuenta/AceptarInvitacion.cshtml`, `Views/Cuenta/InvitacionInvalida.cshtml` | Pantallas nuevas |

### 10.3 Preguntas probables

**¿Por qué no le mandás una contraseña provisoria por email?**
Porque el email no es un canal seguro (queda guardado en bandejas y servidores) y la persona tendría que cambiarla después. Con el enlace, la contraseña la elige ella y nunca viaja por email ni la conoce el admin.

**¿Qué pasa si alguien roba la base de datos?**
Tiene el hash del código, no el código. Con un hash no se puede armar el enlace.

**¿Y si el email no llega?**
La cuenta igual queda creada y la pantalla muestra el enlace al admin para que lo mande por otro medio. El admin es de confianza: es quien está dando el acceso. Además puede tocar **Reenviar**: se genera un código nuevo y el anterior deja de servir.

**¿Se reutiliza el mecanismo de "olvidé mi contraseña"?**
Sí, se usan las mismas columnas (`TokenRecuperacionHash` y `TokenRecuperacionVence`), pero los flujos no se mezclan: la recuperación solo funciona con cuentas **activas**, y la invitación solo con cuentas con **invitación pendiente**. Un enlace de un tipo no sirve para el otro.

**¿Por qué el admin no puede "Activar" una cuenta invitada?**
Porque no tiene contraseña: quedaría activa pero sin forma de entrar. Se activa sola cuando la persona elige su contraseña (o cuando entra con Google con ese mismo email, porque Google ya verificó que el email es suyo).

### 10.4 Ejercicios
**A — Que la invitación dure 7 días.** En `UsuariosController`, cambiá `VigenciaInvitacion` a `TimeSpan.FromDays(7)` y el texto "72 horas" en el email y en las vistas.

**B — Que los operadores también puedan invitar, pero solo con rol Consulta.** Hay que sacar `Invitar` del `[Authorize(Roles = Roles.Administrador)]` de la clase (ponerle `[Authorize(Roles = Roles.Edicion)]`) y, en el POST, si quien invita no es admin, forzar `modelo.Rol = Roles.Consulta`.

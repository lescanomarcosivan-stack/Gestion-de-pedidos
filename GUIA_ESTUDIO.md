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
Porque cualquiera puede crear una cuenta o entrar con Google. Si quedaran activos, cualquier persona de internet vería los datos de clientes. Con aprobación, el administrador decide quién entra y con qué rol.

**¿Cómo se registran los errores?**
`ManejadorErrores` implementa `IExceptionHandler`: ASP.NET Core lo llama ante cualquier error no controlado. Guarda tipo, mensaje, pantalla, usuario e IP usando un `DbContext` **nuevo** (el del pedido puede haber quedado en mal estado) y deja que se muestre la página de error.

**¿Por qué la bitácora se guarda en el mismo SaveChanges que el cambio?**
Para que sea consistente: si el cambio falla, tampoco queda un registro de algo que no pasó, y viceversa.

**¿Cómo hiciste la paginación sin perder los filtros?**
`_Paginacion.cshtml` copia todos los parámetros de la URL actual y solo reemplaza `pagina`. El controlador hace `COUNT` para el total y `Skip/Take` para la página. Ordena por fecha y desempata por Id para que no se repitan filas entre páginas.

**¿Por qué los colores de estado son todos azules? ¿No se confunden?**
Fue un pedido de diseño (gama de azules). Para no depender solo del color, cada etiqueta tiene el **nombre** del estado, y Cancelado además está tachado con borde punteado. Los errores siguen en rojo a propósito, para que se distingan.

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

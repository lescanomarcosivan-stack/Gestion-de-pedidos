# Guía de estudio para la revisión técnica

En la revisión te van a pedir que **expliques, modifiques y corrijas** el código. Esta guía te prepara para eso. Leela junto con el código: cada línea tiene un comentario explicando qué hace.

Orden sugerido de estudio (1–2 días):
1. Conceptos básicos (sección 1)
2. Recorrer los archivos en el orden de la sección 2
3. Los 3 flujos completos (sección 3)
4. Practicar las preguntas (sección 4) en voz alta
5. Hacer al menos 2 de los ejercicios de modificación (sección 5)

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
1. El sistema externo hace **POST** a `/api/webhooks/orders` con `{"event":"order.status.changed","orderId":15,"status":"DELIVERED"}`.
2. `[ApiController]` + `[FromBody]` convierten el JSON en `WebhookRequest`.
3. Validaciones en orden: clave (si está configurada) → cuerpo presente → tipo de evento → `orderId` → estado conocido → pedido existente.
4. `EstadoMapper.TryTraducir("DELIVERED")` → `Entregado`.
5. Se actualiza el pedido y se guarda un `EventoWebhook` en el **mismo** `SaveChangesAsync()` (o se guardan los dos o ninguno).
6. Responde JSON con 200 / 400 / 401 / 404.

---

## 4. Preguntas probables y cómo responderlas

**¿Por qué MVC y no Razor Pages?**
Los dos están permitidos. Elegí MVC porque separa claramente la lógica (controladores) de las pantallas (vistas) y es el patrón más conocido; facilita explicar el recorrido de cada pedido HTTP.

**¿Por qué todo en un solo proyecto y no en capas (API, dominio, infraestructura)?**
El alcance es chico. Separar en varios proyectos agregaría complejidad sin beneficio real. Igual hay separación por carpetas: Models, Data, Services, Controllers, Views. Si creciera, el primer paso sería mover la lógica de los controladores a servicios.

**¿Cómo se crean las tablas? ¿Usaste migraciones?**
Uso `EnsureCreatedAsync()` al arrancar: crea las tablas si no existen. Es simple y suficiente para el challenge. Su límite: no aplica cambios a tablas ya creadas. En un proyecto real usaría **migraciones** (`dotnet ef migrations add ...` y `Database.MigrateAsync()`), que versionan cada cambio del esquema.

**¿Por qué el estado se guarda como texto?**
Con `HasConversion<string>()` la base guarda "Entregado" en vez de 3. Es más legible y si se reordena el enum no se corrompen los datos.

**¿Por qué `decimal` para el monto?**
`double` tiene errores de redondeo (0.1 + 0.2 ≠ 0.3). Para dinero siempre `decimal`. En la base es `numeric(12,2)`.

**¿Por qué las fechas en UTC?**
Es la práctica estándar (no depende de dónde esté el servidor) y Npgsql lo exige para columnas `timestamp with time zone`. Se convierten a hora argentina solo al mostrarlas.

**¿Qué pasa si llega el mismo webhook dos veces?**
El resultado es el mismo: el pedido queda en el estado indicado. Es **idempotente** para este caso. Igual queda registrado dos veces en el historial.

**¿Qué pasa si llegan webhooks desordenados (primero DELIVERED, después SHIPPED)?**
Hoy gana el último que llega. Una mejora sería guardar la fecha del evento y descartar los más viejos, o definir transiciones válidas (no volver de Entregado a Enviado).

**¿Cómo se protege el webhook?**
Opcionalmente con una clave compartida en el header `X-Webhook-Secret` (variable `Webhook__Secret`). En producción se usaría además una **firma HMAC** del cuerpo y HTTPS (Railway ya da HTTPS).

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
Migraciones, tests unitarios (webhook y mapeo de estados), paginación de listados, firma HMAC del webhook, reglas de transición de estados, historial de cambios por pedido, reintentos en la API externa, autenticación de usuarios.

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

using System.Security.Claims; // Para ClaimTypes (datos del usuario dentro de la cookie de sesión)
using System.Threading.RateLimiting; // Para limitar cantidad de pedidos por minuto (protección contra abusos)
using GestionPedidos.Data; // Para AppDbContext, ConexionDb y DatosIniciales
using GestionPedidos.Models; // Para Usuario y Roles
using GestionPedidos.Services; // Para los servicios propios
using Microsoft.AspNetCore.Authentication; // Para SignOutAsync
using Microsoft.AspNetCore.Authentication.Cookies; // Autenticación con cookie (la sesión del usuario)
using Microsoft.AspNetCore.Authentication.Google; // Inicio de sesión con Google
using Microsoft.AspNetCore.Authorization; // Para las políticas de autorización
using Microsoft.AspNetCore.HttpOverrides; // Para ForwardedHeaders (Railway pasa la IP real y el https por headers)
using Microsoft.AspNetCore.Identity; // Para PasswordHasher (cifrado de contraseñas)
using Microsoft.EntityFrameworkCore; // Para UseNpgsql, EnsureCreatedAsync, SqlQuery

// Program.cs es el punto de entrada: arma y arranca la aplicación web

var builder = WebApplication.CreateBuilder(args); // Crea el "constructor" de la app; lee appsettings.json y variables de entorno

var puerto = Environment.GetEnvironmentVariable("PORT") ?? "8080"; // Railway indica el puerto en la variable PORT; si no, usamos 8080
builder.WebHost.UseUrls($"http://0.0.0.0:{puerto}"); // Escucha en todas las interfaces de red en ese puerto (necesario dentro de Docker)

builder.Services.AddControllersWithViews(); // Activa MVC: controladores + vistas Razor
builder.Services.AddHttpContextAccessor(); // Permite que los servicios sepan qué usuario e IP hicieron el pedido actual

var cadenaConexion = ConexionDb.ObtenerCadena(builder.Configuration); // Obtiene la cadena de conexión a PostgreSQL (nube o local)
builder.Services.AddDbContext<AppDbContext>(opciones => opciones.UseNpgsql(cadenaConexion)); // Registra el DbContext usando el proveedor de PostgreSQL

var urlTracking = builder.Configuration["Tracking:BaseUrl"]; // Dirección de la API externa de seguimiento (configurable)
if (string.IsNullOrWhiteSpace(urlTracking)) // Si no se configuró ninguna...
    urlTracking = $"http://localhost:{puerto}/api/mock/tracking/"; // ...usamos la API simulada que trae esta misma app
builder.Services.AddHttpClient<ITrackingService, TrackingService>(cliente => // Registra el servicio con un HttpClient propio
{ // Inicio de la configuración del HttpClient
    cliente.BaseAddress = new Uri(urlTracking); // Todas las llamadas parten de esta dirección
    cliente.Timeout = TimeSpan.FromSeconds(5); // Si la API tarda más de 5 segundos, se corta (no cuelga la pantalla)
}); // Fin de la configuración del HttpClient

builder.Services.AddHttpClient<IEnviadorEmail, EnviadorEmail>(c => c.Timeout = TimeSpan.FromSeconds(10)); // Cliente HTTP para enviar emails (Brevo)
builder.Services.AddHttpClient("interno", c => c.BaseAddress = new Uri($"http://localhost:{puerto}/")); // Cliente HTTP para que la pantalla "Probar webhook" llame a nuestro propio webhook
builder.Services.AddScoped<IAuditoria, Auditoria>(); // Bitácora: una instancia por pedido HTTP (comparte el DbContext)
builder.Services.AddScoped<IStockService, StockService>(); // Stock: una instancia por pedido HTTP (comparte el DbContext, así todo se guarda junto)
builder.Services.AddSingleton<ColaEmails>(); // Cola de emails: una sola para toda la app
builder.Services.AddHostedService<EnvioEmailsWorker>(); // Trabajador en segundo plano que envía los emails de la cola
builder.Services.AddScoped<INotificador, Notificador>(); // Arma los emails y los deja en la cola
builder.Services.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>(); // Cifrador de contraseñas de Microsoft (PBKDF2 + sal aleatoria + 100.000 iteraciones)
builder.Services.AddExceptionHandler<ManejadorErrores>(); // Guarda en la bitácora los errores no controlados
builder.Services.AddProblemDetails(); // Formato estándar de errores (lo requiere el manejador de excepciones)

// ---------- Autenticación: quién sos ----------
var autenticacion = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme) // Por defecto, la sesión vive en una cookie
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, opciones => // Configuración de la cookie de sesión
    { // Inicio de la configuración
        opciones.LoginPath = "/Cuenta/Login"; // Si no iniciaste sesión, te manda acá
        opciones.LogoutPath = "/Cuenta/Logout"; // Ruta de cierre de sesión
        opciones.AccessDeniedPath = "/Cuenta/AccesoDenegado"; // Si tu rol no alcanza, te manda acá
        opciones.Cookie.Name = "gp_sesion"; // Nombre de la cookie
        opciones.Cookie.HttpOnly = true; // JavaScript no puede leerla (protege contra robo de sesión)
        opciones.Cookie.SameSite = SameSiteMode.Lax; // No se envía desde otros sitios en pedidos peligrosos (protección CSRF extra)
        opciones.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // En https (Railway) viaja solo cifrada
        opciones.ExpireTimeSpan = TimeSpan.FromHours(8); // La sesión dura 8 horas...
        opciones.SlidingExpiration = true; // ...y se renueva mientras el usuario la use
        opciones.Events.OnValidatePrincipal = async contexto => // En cada pedido verificamos que el usuario siga habilitado
        { // Inicio del evento
            var id = contexto.Principal?.FindFirstValue(ClaimTypes.NameIdentifier); // Id del usuario guardado en la cookie
            var rolEnCookie = contexto.Principal?.FindFirstValue(ClaimTypes.Role); // Rol guardado en la cookie
            var db = contexto.HttpContext.RequestServices.GetRequiredService<AppDbContext>(); // Acceso a la base
            var usuario = int.TryParse(id, out var idNum) ? await db.Usuarios.FindAsync(idNum) : null; // Buscamos el usuario actual
            if (usuario is null || !usuario.Activo || usuario.Rol != rolEnCookie) // Si lo borraron, lo desactivaron o le cambiaron el rol...
            { // Inicio del bloque
                contexto.RejectPrincipal(); // ...la cookie deja de valer
                await contexto.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // ...y se cierra la sesión (tiene que volver a entrar)
            } // Fin del bloque
        }; // Fin del evento
    }) // Fin de la cookie de sesión
    .AddCookie("Externa", opciones => // Cookie temporal que se usa solo durante el ida y vuelta con Google
    { // Inicio de la configuración
        opciones.Cookie.Name = "gp_externa"; // Nombre de la cookie temporal
        opciones.ExpireTimeSpan = TimeSpan.FromMinutes(10); // Vive 10 minutos como máximo
    }); // Fin de la cookie externa

var googleId = builder.Configuration["Autenticacion:Google:ClientId"]; // Credencial de Google (variable Autenticacion__Google__ClientId)
var googleSecreto = builder.Configuration["Autenticacion:Google:ClientSecret"]; // Secreto de Google (variable Autenticacion__Google__ClientSecret)
if (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecreto)) // Solo activamos Google si está configurado
{ // Inicio del bloque
    autenticacion.AddGoogle(GoogleDefaults.AuthenticationScheme, opciones => // Registra el proveedor de Google
    { // Inicio de la configuración
        opciones.ClientId = googleId; // Identificador de nuestra app en Google Cloud
        opciones.ClientSecret = googleSecreto; // Secreto de nuestra app (nunca va en el código)
        opciones.SignInScheme = "Externa"; // Google deja el resultado en la cookie temporal; después lo procesa CuentaController
        opciones.CallbackPath = "/signin-google"; // Dirección a la que Google vuelve (debe estar cargada en Google Cloud)
    }); // Fin de la configuración
} // Fin del bloque

// ---------- Autorización: qué podés hacer ----------
builder.Services.AddAuthorization(opciones => // Reglas de acceso
{ // Inicio de la configuración
    opciones.FallbackPolicy = new AuthorizationPolicyBuilder() // Política que se aplica a TODAS las pantallas que no digan otra cosa...
        .RequireAuthenticatedUser() // ...exige haber iniciado sesión (ninguna pantalla queda pública por olvido)
        .Build(); // Construye la política
}); // Fin de la configuración

// ---------- Límite de pedidos (rate limiting) ----------
builder.Services.AddRateLimiter(opciones => // Evita ataques de fuerza bruta y abusos
{ // Inicio de la configuración
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests; // Si se pasa del límite, responde 429 "demasiados pedidos"
    opciones.OnRejected = async (contexto, cancelacion) => // Qué ve el usuario cuando se pasa del límite
    { // Inicio del evento
        contexto.HttpContext.Response.ContentType = "text/plain; charset=utf-8"; // Respuesta de texto simple
        await contexto.HttpContext.Response.WriteAsync("Demasiados intentos seguidos. Esperá un minuto y volvé a probar.", cancelacion); // Mensaje claro en vez de una página en blanco
    }; // Fin del evento
    opciones.AddPolicy("login", contexto => RateLimitPartition.GetFixedWindowLimiter( // Política "login": se cuenta por IP
        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida", // Clave de partición: la IP del cliente
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) })); // Máximo 10 intentos por minuto
    opciones.AddPolicy("webhook", contexto => RateLimitPartition.GetFixedWindowLimiter( // Política "webhook": también por IP
        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida", // Clave de partición
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) })); // Máximo 60 avisos por minuto
}); // Fin de la configuración

builder.Services.Configure<ForwardedHeadersOptions>(opciones => // Railway recibe el https y nos reenvía el pedido por http interno
{ // Inicio de la configuración
    opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto; // Tomamos la IP real del cliente y si era https
    opciones.KnownIPNetworks.Clear(); // Railway no tiene IPs fijas conocidas...
    opciones.KnownProxies.Clear(); // ...así que aceptamos los headers del proxy (la app solo es accesible a través de él)
}); // Fin de la configuración

var app = builder.Build(); // Construye la aplicación con todo lo registrado arriba

using (var scope = app.Services.CreateScope()) // Crea un "ámbito" temporal para pedir servicios al arrancar
{ // Inicio del bloque de arranque de la base
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // Obtiene una conexión a la base
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>(); // Obtiene el logger para dejar mensajes
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>(); // Cifrador de contraseñas (para los usuarios demo)
    for (var intento = 1; ; intento++) // Reintenta: al publicar, a veces la app arranca antes que la base
    { // Inicio del ciclo de reintentos
        try // Intentamos preparar la base
        { // Inicio del try
            var faltantes = new List<string>(); // Tablas que no existen todavía
            foreach (var tabla in AppDbContext.TablasEsperadas) // Revisamos cada tabla que necesita esta versión
            { // Inicio del ciclo
                var existe = (await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_name = {tabla}").ToListAsync()).Single() > 0; // Consulta parametrizada al catálogo de PostgreSQL
                if (!existe) faltantes.Add(tabla); // Si no está, la anotamos
            } // Fin del ciclo
            if (faltantes.Count > 0 && faltantes.Count < AppDbContext.TablasEsperadas.Length) // Hay tablas de una versión anterior pero faltan las nuevas
            { // Inicio del bloque de actualización
                logger.LogWarning("Base de una versión anterior (faltan: {Tablas}). Se recrean las tablas con datos de ejemplo.", string.Join(", ", faltantes)); // Avisamos en el log
                await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"HistorialEstados\", \"RegistrosActividad\", \"Usuarios\", \"EventosWebhook\", \"Pedidos\", \"Clientes\" CASCADE"); // Borra solo las tablas de esta app (son datos de demostración)
            } // Fin del bloque de actualización
            await db.Database.EnsureCreatedAsync(); // Crea las tablas si no existen (no borra nada si ya están)
            await ActualizacionesBase.AplicarAsync(db); // Agrega tablas y columnas nuevas a una base existente, SIN borrar datos
            await DatosIniciales.CargarUsuariosAsync(db, hasher, app.Configuration); // Crea los usuarios demo si no hay usuarios
            await DatosIniciales.CargarProductosAsync(db); // Crea productos de ejemplo si no hay productos
            await DatosIniciales.CargarAsync(db); // Carga clientes y pedidos de ejemplo si la base está vacía
            break; // Salió bien: salimos del ciclo
        } // Fin del try
        catch (Exception ex) when (intento < 10) // Si falló y todavía quedan intentos...
        { // Inicio del catch
            logger.LogWarning(ex, "La base todavía no responde (intento {Intento}/10). Reintentando...", intento); // Avisamos en el log
            await Task.Delay(TimeSpan.FromSeconds(3)); // Esperamos 3 segundos antes de volver a probar
        } // Fin del catch
    } // Fin del ciclo de reintentos
} // Fin del bloque de arranque de la base

app.UseForwardedHeaders(); // Primero: corrige IP y https según los headers de Railway (lo necesita Google y el registro de IPs)
app.UseExceptionHandler("/Home/Error"); // Errores no controlados → se registran (ManejadorErrores) y se muestra la página de error
app.UseRouting(); // Activa el sistema de rutas (qué URL va a qué controlador)
app.UseRateLimiter(); // Aplica los límites de pedidos por minuto
app.UseAuthentication(); // Lee la cookie y averigua quién es el usuario
app.UseAuthorization(); // Verifica si ese usuario puede entrar a la pantalla pedida

app.MapControllerRoute( // Define la ruta por defecto de MVC
    name: "default", // Nombre interno de la ruta
    pattern: "{controller=Home}/{action=Index}/{id?}"); // /Controlador/Acción/Id; la página inicial es el Dashboard

app.Run(); // Arranca el servidor y queda escuchando pedidos HTTP

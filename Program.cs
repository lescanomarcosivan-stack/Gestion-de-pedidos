using GestionPedidos.Data; // Para AppDbContext, ConexionDb y DatosIniciales
using GestionPedidos.Services; // Para ITrackingService y TrackingService
using Microsoft.EntityFrameworkCore; // Para UseNpgsql y EnsureCreatedAsync

// Program.cs es el punto de entrada: arma y arranca la aplicación web

var builder = WebApplication.CreateBuilder(args); // Crea el "constructor" de la app; lee appsettings.json y variables de entorno

var puerto = Environment.GetEnvironmentVariable("PORT") ?? "8080"; // Railway/Render indican el puerto en la variable PORT; si no, usamos 8080
builder.WebHost.UseUrls($"http://0.0.0.0:{puerto}"); // Escucha en todas las interfaces de red en ese puerto (necesario dentro de Docker)

builder.Services.AddControllersWithViews(); // Activa MVC: controladores + vistas Razor

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

var app = builder.Build(); // Construye la aplicación con todo lo registrado arriba

using (var scope = app.Services.CreateScope()) // Crea un "ámbito" temporal para pedir servicios al arrancar
{ // Inicio del bloque de arranque de la base
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // Obtiene una conexión a la base
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>(); // Obtiene el logger para dejar mensajes
    for (var intento = 1; ; intento++) // Reintenta: al publicar, a veces la app arranca antes que la base
    { // Inicio del ciclo de reintentos
        try // Intentamos preparar la base
        { // Inicio del try
            await db.Database.EnsureCreatedAsync(); // Crea las tablas si no existen (no borra nada si ya están)
            await DatosIniciales.CargarAsync(db); // Carga datos de ejemplo si la base está vacía
            break; // Salió bien: salimos del ciclo
        } // Fin del try
        catch (Exception ex) when (intento < 10) // Si falló y todavía quedan intentos...
        { // Inicio del catch
            logger.LogWarning(ex, "La base todavía no responde (intento {Intento}/10). Reintentando...", intento); // Avisamos en el log
            await Task.Delay(TimeSpan.FromSeconds(3)); // Esperamos 3 segundos antes de volver a probar
        } // Fin del catch
    } // Fin del ciclo de reintentos
} // Fin del bloque de arranque de la base

if (!app.Environment.IsDevelopment()) // En producción (publicada en internet)...
    app.UseExceptionHandler("/Home/Error"); // ...si algo explota, mostramos una página de error amigable

app.UseRouting(); // Activa el sistema de rutas (qué URL va a qué controlador)

app.MapControllerRoute( // Define la ruta por defecto de MVC
    name: "default", // Nombre interno de la ruta
    pattern: "{controller=Clientes}/{action=Index}/{id?}"); // /Controlador/Acción/Id; la página inicial es el listado de Clientes

app.Run(); // Arranca el servidor y queda escuchando pedidos HTTP

using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para RegistroActividad y TipoRegistro
using Microsoft.AspNetCore.Diagnostics; // Para IExceptionHandler

namespace GestionPedidos.Services; // Namespace de los servicios

// Se ejecuta cuando ocurre un error no controlado: lo guarda en la bitácora y deja que se muestre la página de error
public class ManejadorErrores : IExceptionHandler // IExceptionHandler = interfaz de ASP.NET Core para manejar errores globales
{ // Inicio de la clase
    private readonly IServiceScopeFactory _scopes; // Permite crear una conexión NUEVA a la base (la del pedido puede haber quedado en mal estado)
    private readonly ILogger<ManejadorErrores> _logger; // Log del servidor

    public ManejadorErrores(IServiceScopeFactory scopes, ILogger<ManejadorErrores> logger) // Inyección de dependencias
    { // Inicio del constructor
        _scopes = scopes; // Guardamos la fábrica de ámbitos
        _logger = logger; // Guardamos el logger
    } // Fin del constructor

    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception error, CancellationToken cancelacion) // Lo llama ASP.NET Core ante un error
    { // Inicio del método
        _logger.LogError(error, "Error no controlado en {Ruta}", contexto.Request.Path); // Siempre queda en el log del servidor
        try // Guardar en la base también podría fallar (por ejemplo, si la base está caída)
        { // Inicio del try
            using var scope = _scopes.CreateScope(); // Ámbito nuevo = DbContext nuevo y limpio
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // Obtenemos la base
            var detalle = $"{error.GetType().Name}: {error.Message}"; // Tipo y mensaje del error (sin datos sensibles)
            if (detalle.Length > 2000) detalle = detalle[..2000]; // Recortamos si es muy largo para que entre en la columna
            db.RegistrosActividad.Add(new RegistroActividad // Nuevo registro de error
            { // Inicio de los datos
                Tipo = TipoRegistro.Error, // Tipo Error
                Accion = $"{contexto.Request.Method} {contexto.Request.Path}", // Qué pantalla falló (ej. "GET /Pedidos/Details/3")
                Detalle = detalle, // Descripción del error
                Usuario = contexto.User.Identity?.Name ?? "anónimo", // Quién estaba usando la app
                Ip = contexto.Connection.RemoteIpAddress?.ToString() // Desde dónde
            }); // Fin del registro
            await db.SaveChangesAsync(cancelacion); // Guardamos
        } // Fin del try
        catch (Exception ex) // Si no se pudo guardar...
        { // Inicio del catch
            _logger.LogError(ex, "No se pudo registrar el error en la base"); // ...al menos queda en el log
        } // Fin del catch
        return false; // false = "no lo resolví": ASP.NET Core sigue y muestra la página /Home/Error
    } // Fin del método
} // Fin de la clase

using System.Net.Http.Json; // Trae GetFromJsonAsync: hace un GET y convierte el JSON en objeto
using GestionPedidos.Models; // Para usar TrackingInfo

namespace GestionPedidos.Services; // Namespace de los servicios

// Interfaz = "contrato": dice QUÉ hace el servicio sin decir CÓMO. Facilita cambiar la API o hacer pruebas
public interface ITrackingService // Los controladores dependen de esta interfaz, no de la clase concreta
{ // Inicio de la interfaz
    Task<(TrackingInfo? Datos, string? Error)> ObtenerAsync(int pedidoId); // Devuelve los datos o un mensaje de error
} // Fin de la interfaz

// Implementación real: consulta la API externa de seguimiento por HTTP desde el backend
public class TrackingService : ITrackingService // Cumple el contrato ITrackingService
{ // Inicio de la clase
    private readonly HttpClient _http; // Cliente HTTP ya configurado en Program.cs (dirección base y timeout)
    private readonly ILogger<TrackingService> _logger; // Para escribir mensajes en el log del servidor

    public TrackingService(HttpClient http, ILogger<TrackingService> logger) // ASP.NET Core le pasa estos objetos automáticamente (inyección de dependencias)
    { // Inicio del constructor
        _http = http; // Guardamos el HttpClient para usarlo después
        _logger = logger; // Guardamos el logger
    } // Fin del constructor

    public async Task<(TrackingInfo? Datos, string? Error)> ObtenerAsync(int pedidoId) // Consulta el seguimiento de un pedido
    { // Inicio del método
        try // Intentamos la llamada; si algo falla vamos al "catch"
        { // Inicio del try
            var respuesta = await _http.GetAsync(pedidoId.ToString()); // GET a {BaseUrl}/{pedidoId}, ej. .../api/mock/tracking/15
            if (!respuesta.IsSuccessStatusCode) // Si la API respondió con error (404, 500...)
                return (null, $"La API de seguimiento respondió {(int)respuesta.StatusCode}"); // Devolvemos el código para mostrarlo
            var datos = await respuesta.Content.ReadFromJsonAsync<TrackingInfo>(); // Convierte el JSON recibido en un TrackingInfo
            return (datos, datos is null ? "La API devolvió una respuesta vacía" : null); // Devolvemos los datos (o error si vino vacío)
        } // Fin del try
        catch (Exception ex) // Si no hubo conexión, se pasó el tiempo de espera o el JSON vino mal
        { // Inicio del catch
            _logger.LogWarning(ex, "No se pudo consultar el tracking del pedido {PedidoId}", pedidoId); // Lo registramos en el log
            return (null, "No se pudo conectar con la API de seguimiento"); // La pantalla muestra un aviso en vez de romperse
        } // Fin del catch
    } // Fin del método
} // Fin de la clase

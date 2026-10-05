using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para WebhookRequest y EventoWebhook
using GestionPedidos.Services; // Para EstadoMapper
using Microsoft.AspNetCore.Mvc; // Para ControllerBase, [ApiController], [HttpPost], etc.

namespace GestionPedidos.Controllers.Api; // Namespace de los controladores de API (devuelven JSON, no páginas)

// Recibe avisos externos: POST /api/webhooks/orders con el JSON del PDF
[ApiController] // Indica que es una API: lee JSON del cuerpo y responde JSON
[Route("api/webhooks")] // Todas las rutas de esta clase empiezan con /api/webhooks
public class WebhookController : ControllerBase // ControllerBase = controlador sin vistas (solo datos)
{ // Inicio de la clase
    private const string EventoEsperado = "order.status.changed"; // Único tipo de evento que procesamos
    private readonly AppDbContext _db; // Acceso a la base
    private readonly IConfiguration _config; // Acceso a la configuración (para leer la clave secreta)
    private readonly ILogger<WebhookController> _logger; // Para registrar cada aviso en el log

    public WebhookController(AppDbContext db, IConfiguration config, ILogger<WebhookController> logger) // Inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos el DbContext
        _config = config; // Guardamos la configuración
        _logger = logger; // Guardamos el logger
    } // Fin del constructor

    [HttpPost("orders")] // Responde a POST /api/webhooks/orders
    public async Task<IActionResult> RecibirEvento([FromBody] WebhookRequest? solicitud) // [FromBody] = el objeto viene del JSON del cuerpo
    { // Inicio del método
        var claveEsperada = _config["Webhook:Secret"]; // Lee la clave configurada (variable de entorno Webhook__Secret)
        if (!string.IsNullOrWhiteSpace(claveEsperada) && // Si hay una clave configurada...
            Request.Headers["X-Webhook-Secret"] != claveEsperada) // ...y el header recibido no coincide...
            return await RegistrarAsync(solicitud, StatusCodes.Status401Unauthorized, "Clave del webhook inválida o ausente"); // ...rechazamos con 401

        if (solicitud is null) // Si no llegó JSON o no se pudo leer...
            return await RegistrarAsync(null, StatusCodes.Status400BadRequest, "El cuerpo debe ser un JSON válido"); // ...error 400

        if (!string.Equals(solicitud.Event, EventoEsperado, StringComparison.OrdinalIgnoreCase)) // Si el tipo de evento no es el esperado...
            return await RegistrarAsync(solicitud, StatusCodes.Status400BadRequest, $"Evento no soportado: '{solicitud.Event}'. Se espera '{EventoEsperado}'"); // ...error 400

        if (solicitud.OrderId is null or <= 0) // Si falta el orderId o es inválido...
            return await RegistrarAsync(solicitud, StatusCodes.Status400BadRequest, "Falta 'orderId' o no es válido"); // ...error 400

        if (!EstadoMapper.TryTraducir(solicitud.Status, out var nuevoEstado)) // Si el estado no es uno conocido...
            return await RegistrarAsync(solicitud, StatusCodes.Status400BadRequest, $"Estado desconocido: '{solicitud.Status}'"); // ...error 400

        var pedido = await _db.Pedidos.FindAsync(solicitud.OrderId.Value); // Buscamos el pedido en la base
        if (pedido is null) // Si no existe...
            return await RegistrarAsync(solicitud, StatusCodes.Status404NotFound, $"No existe el pedido {solicitud.OrderId}"); // ...error 404

        var estadoAnterior = pedido.Estado; // Guardamos el estado viejo para informarlo
        pedido.Estado = nuevoEstado; // Actualizamos el estado
        pedido.FechaActualizacion = DateTime.UtcNow; // Registramos cuándo cambió
        return await RegistrarAsync(solicitud, StatusCodes.Status200OK, // Guardamos el cambio + el registro del evento y respondemos 200
            $"Pedido {pedido.Id}: {EstadoMapper.Nombre(estadoAnterior)} → {EstadoMapper.Nombre(nuevoEstado)}"); // Mensaje con el cambio realizado
    } // Fin del método

    // Guarda el evento en la tabla EventosWebhook (y cualquier cambio pendiente) y arma la respuesta JSON
    private async Task<IActionResult> RegistrarAsync(WebhookRequest? solicitud, int codigo, string mensaje) // Método auxiliar
    { // Inicio del método
        _db.EventosWebhook.Add(new EventoWebhook // Nuevo registro de auditoría
        { // Inicio de los datos
            Evento = solicitud?.Event, // Tipo de evento recibido
            PedidoId = solicitud?.OrderId, // Pedido indicado
            EstadoRecibido = solicitud?.Status, // Estado recibido
            CodigoRespuesta = codigo, // Código HTTP que vamos a devolver
            Resultado = mensaje // Explicación del resultado
        }); // Fin del registro
        await _db.SaveChangesAsync(); // Un solo guardado: el cambio del pedido (si hubo) + el registro del evento

        _logger.LogInformation("Webhook recibido → {Codigo}: {Mensaje}", codigo, mensaje); // Deja constancia en el log del servidor
        return StatusCode(codigo, new { ok = codigo == StatusCodes.Status200OK, mensaje }); // Responde JSON: { "ok": true/false, "mensaje": "..." }
    } // Fin del método
} // Fin de la clase

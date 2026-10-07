using System.Text.Json; // Para leer el JSON a mano (necesitamos el texto exacto para verificar la firma)
using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para WebhookRequest, EventoWebhook, HistorialEstado
using GestionPedidos.Services; // Para EstadoMapper, FirmaWebhook e IAuditoria
using Microsoft.AspNetCore.Authorization; // Para [AllowAnonymous]
using Microsoft.AspNetCore.Mvc; // Para ControllerBase, [ApiController], [HttpPost], etc.
using Microsoft.AspNetCore.RateLimiting; // Para [EnableRateLimiting]
using Microsoft.EntityFrameworkCore; // Para Include y FirstOrDefaultAsync

namespace GestionPedidos.Controllers.Api; // Namespace de los controladores de API (devuelven JSON, no páginas)

// Recibe avisos externos: POST /api/webhooks/orders con el JSON del PDF.
// No usa la sesión de usuario (lo llama otro sistema): se protege con una clave secreta compartida (firma HMAC)
[ApiController] // Indica que es una API: responde JSON
[Route("api/webhooks")] // Todas las rutas de esta clase empiezan con /api/webhooks
[AllowAnonymous] // No exige login de usuario: la seguridad la da la firma (ver abajo)
[EnableRateLimiting("webhook")] // Máximo 60 avisos por minuto por IP
public class WebhookController : ControllerBase // ControllerBase = controlador sin vistas (solo datos)
{ // Inicio de la clase
    private const string EventoEsperado = "order.status.changed"; // Único tipo de evento que procesamos
    private static readonly JsonSerializerOptions OpcionesJson = new() { PropertyNameCaseInsensitive = true }; // Lectura de JSON sin importar mayúsculas en los nombres
    private readonly AppDbContext _db; // Acceso a la base
    private readonly IConfiguration _config; // Acceso a la configuración (para leer la clave secreta)
    private readonly IAuditoria _auditoria; // Bitácora general
    private readonly ILogger<WebhookController> _logger; // Para registrar cada aviso en el log
    private readonly IStockService _stock; // Control de stock (cancelar devuelve, reabrir descuenta)
    private readonly INotificador _notificador; // Emails
    private readonly ColaCalendario _calendario; // Cola de Google Calendar

    public WebhookController(AppDbContext db, IConfiguration config, IAuditoria auditoria, ILogger<WebhookController> logger, IStockService stock, INotificador notificador, ColaCalendario calendario) // Inyección de dependencias
    { // Inicio del constructor
        _calendario = calendario; // Guardamos la cola de Calendar
        _stock = stock; // Guardamos el servicio de stock
        _notificador = notificador; // Guardamos el notificador
        _db = db; // Guardamos el DbContext
        _config = config; // Guardamos la configuración
        _auditoria = auditoria; // Guardamos la bitácora
        _logger = logger; // Guardamos el logger
    } // Fin del constructor

    [HttpPost("orders")] // Responde a POST /api/webhooks/orders
    public async Task<IActionResult> RecibirEvento() // Sin parámetros: leemos el cuerpo nosotros mismos
    { // Inicio del método
        using var lector = new StreamReader(Request.Body); // Lector del cuerpo del pedido
        var cuerpo = await lector.ReadToEndAsync(); // Texto exacto recibido (la firma se calcula sobre este texto)

        WebhookRequest? solicitud = null; // Acá quedará el JSON convertido a objeto
        try { solicitud = string.IsNullOrWhiteSpace(cuerpo) ? null : JsonSerializer.Deserialize<WebhookRequest>(cuerpo, OpcionesJson); } // Intentamos convertir el JSON
        catch (JsonException) { /* JSON mal formado: solicitud queda en null y se responde 400 más abajo */ } // Si el JSON está roto no explotamos

        // 1) ¿Quién lo manda? Sin clave correcta, no se procesa nada
        var clave = _config["Webhook:Secret"]; // Clave secreta compartida (variable de entorno Webhook__Secret)
        if (string.IsNullOrWhiteSpace(clave)) // Si el servidor no tiene clave configurada...
            return await RegistrarAsync(solicitud, StatusCodes.Status503ServiceUnavailable, "El webhook no está configurado en el servidor (falta Webhook__Secret)", "Ninguna"); // ...rechazamos todo (más seguro que aceptar todo)

        var (autorizado, metodo, motivo) = Verificar(clave, cuerpo); // Verificamos firma o clave
        if (!autorizado) // Si no es una fuente autorizada...
            return await RegistrarAsync(solicitud, StatusCodes.Status401Unauthorized, motivo, metodo); // ...401 No autorizado

        // 2) ¿El contenido es válido?
        if (solicitud is null) // JSON vacío o mal formado
            return await RegistrarAsync(null, StatusCodes.Status400BadRequest, "El cuerpo debe ser un JSON válido", metodo); // 400

        if (!string.Equals(solicitud.Event, EventoEsperado, StringComparison.OrdinalIgnoreCase)) // Tipo de evento distinto
            return await RegistrarAsync(solicitud, StatusCodes.Status400BadRequest, $"Evento no soportado: '{solicitud.Event}'. Se espera '{EventoEsperado}'", metodo); // 400

        if (solicitud.OrderId is null or <= 0) // Falta orderId o es inválido
            return await RegistrarAsync(solicitud, StatusCodes.Status400BadRequest, "Falta 'orderId' o no es válido", metodo); // 400

        if (!EstadoMapper.TryTraducir(solicitud.Status, out var nuevoEstado)) // Estado desconocido
            return await RegistrarAsync(solicitud, StatusCodes.Status400BadRequest, $"Estado desconocido: '{solicitud.Status}'", metodo); // 400

        var pedido = await _db.Pedidos.Include(p => p.Cliente).Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == solicitud.OrderId.Value); // Buscamos el pedido con su cliente y productos
        if (pedido is null) // No existe
            return await RegistrarAsync(solicitud, StatusCodes.Status404NotFound, $"No existe el pedido {solicitud.OrderId}", metodo); // 404

        // 3) Todo bien: actualizamos
        var estadoAnterior = pedido.Estado; // Guardamos el estado viejo para informarlo
        if (estadoAnterior == nuevoEstado) // Si ya estaba en ese estado (aviso repetido)...
            return await RegistrarAsync(solicitud, StatusCodes.Status200OK, $"Pedido {pedido.Id}: ya estaba en {EstadoMapper.Nombre(nuevoEstado)} (sin cambios)", metodo); // ...respondemos OK sin duplicar el historial

        var erroresStock = await _stock.AplicarCambioEstadoAsync(pedido, estadoAnterior, nuevoEstado, "webhook"); // Cancelar devuelve stock; reabrir vuelve a descontar
        if (erroresStock.Count > 0) // Reabrir sin stock suficiente
            return await RegistrarAsync(solicitud, StatusCodes.Status409Conflict, $"No se puede reabrir el pedido {pedido.Id}: falta stock ({string.Join("; ", erroresStock)})", metodo); // 409 = conflicto con el estado actual

        pedido.Estado = nuevoEstado; // Actualizamos el estado
        pedido.FechaActualizacion = DateTime.UtcNow; // Registramos cuándo cambió
        _db.HistorialEstados.Add(new HistorialEstado { PedidoId = pedido.Id, EstadoAnterior = estadoAnterior, EstadoNuevo = nuevoEstado, Origen = "Webhook", Usuario = "webhook" }); // Historial del pedido
        var respuesta = await RegistrarAsync(solicitud, StatusCodes.Status200OK, // Guardamos todo junto y respondemos 200
            $"Pedido {pedido.Id}: {EstadoMapper.Nombre(estadoAnterior)} → {EstadoMapper.Nombre(nuevoEstado)}", metodo); // Mensaje con el cambio realizado
        _notificador.EstadoCambiado(pedido, estadoAnterior, nuevoEstado); // Emails (después de guardar)
        _notificador.StockBajo(_stock.CruzaronMinimo); // Aviso de stock bajo si corresponde
        if (pedido.FechaEntrega is not null || pedido.CalendarioEventoId is not null) _calendario.Encolar(pedido.Id); // Calendar: actualiza o borra el evento
        return respuesta; // Respuesta al sistema externo
    } // Fin del método

    // Verifica que el aviso venga de alguien que conoce la clave secreta
    private (bool Autorizado, string Metodo, string Motivo) Verificar(string clave, string cuerpo) // Devuelve 3 valores (tupla)
    { // Inicio del método
        var firma = Request.Headers[FirmaWebhook.HeaderFirma].ToString(); // Header X-Webhook-Signature
        var timestamp = Request.Headers[FirmaWebhook.HeaderTimestamp].ToString(); // Header X-Webhook-Timestamp
        if (!string.IsNullOrEmpty(firma)) // Opción recomendada: firma HMAC
        { // Inicio del bloque
            if (!long.TryParse(timestamp, out var segundos)) // El timestamp es obligatorio con la firma
                return (false, "Firma HMAC", $"Falta el header {FirmaWebhook.HeaderTimestamp} o no es un número"); // Rechazo
            var antiguedad = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(segundos); // Cuánto hace que se firmó
            if (antiguedad.Duration() > FirmaWebhook.Tolerancia) // Más de 5 minutos (para atrás o adelante)
                return (false, "Firma HMAC", "Timestamp fuera de tolerancia (más de 5 minutos): posible reenvío de un aviso viejo"); // Rechazo
            var esperada = FirmaWebhook.Calcular(clave, timestamp, cuerpo); // Firma que debería haber llegado
            return FirmaWebhook.SonIguales(esperada, firma) // Comparación en tiempo constante
                ? (true, "Firma HMAC", string.Empty) // Coincide: autorizado
                : (false, "Firma HMAC", "Firma inválida: el contenido fue modificado o la clave no es correcta"); // No coincide: rechazo
        } // Fin del bloque

        var claveRecibida = Request.Headers[FirmaWebhook.HeaderClave].ToString(); // Opción simple para pruebas: header X-Webhook-Secret
        if (!string.IsNullOrEmpty(claveRecibida)) // Si vino...
            return FirmaWebhook.SonIguales(clave, claveRecibida) // ...la comparamos de forma segura
                ? (true, "Clave", string.Empty) // Coincide: autorizado
                : (false, "Clave", "Clave del webhook incorrecta"); // No coincide: rechazo

        return (false, "Ninguna", $"Falta autenticación: enviá {FirmaWebhook.HeaderFirma} + {FirmaWebhook.HeaderTimestamp} (recomendado) o {FirmaWebhook.HeaderClave}"); // No vino nada
    } // Fin del método

    // Guarda el evento (y cualquier cambio pendiente) y arma la respuesta JSON
    private async Task<IActionResult> RegistrarAsync(WebhookRequest? solicitud, int codigo, string mensaje, string autenticacion) // Método auxiliar
    { // Inicio del método
        _db.EventosWebhook.Add(new EventoWebhook // Registro detallado del aviso (pantalla "Webhooks")
        { // Inicio de los datos
            Evento = solicitud?.Event, // Tipo de evento recibido
            PedidoId = solicitud?.OrderId, // Pedido indicado
            EstadoRecibido = solicitud?.Status, // Estado recibido
            CodigoRespuesta = codigo, // Código HTTP que vamos a devolver
            Resultado = mensaje, // Explicación del resultado
            Ip = HttpContext.Connection.RemoteIpAddress?.ToString(), // Quién lo mandó
            Autenticacion = autenticacion // Cómo se identificó
        }); // Fin del registro
        _auditoria.Registrar(TipoRegistro.Webhook, codigo == StatusCodes.Status200OK ? "Webhook procesado" : $"Webhook rechazado ({codigo})", mensaje, solicitud?.OrderId is > 0 ? "Pedido" : null, solicitud?.OrderId, "webhook"); // Bitácora general
        await _db.SaveChangesAsync(); // Un solo guardado: cambio del pedido + historial + registros (todo o nada)

        _logger.LogInformation("Webhook recibido → {Codigo}: {Mensaje}", codigo, mensaje); // Deja constancia en el log del servidor
        return StatusCode(codigo, new { ok = codigo == StatusCodes.Status200OK, mensaje }); // Responde JSON: { "ok": true/false, "mensaje": "..." }
    } // Fin del método
} // Fin de la clase

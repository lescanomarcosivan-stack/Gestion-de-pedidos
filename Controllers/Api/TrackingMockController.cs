using GestionPedidos.Models; // Para TrackingInfo
using Microsoft.AspNetCore.Mvc; // Para ControllerBase y atributos de API

namespace GestionPedidos.Controllers.Api; // Namespace de los controladores de API

// API externa SIMULADA ("mock") de una empresa de envíos. El PDF permite que sea mockeada.
// La app la consulta por HTTP como si fuera de otra empresa; se puede reemplazar por una real cambiando Tracking:BaseUrl
[ApiController] // Es una API (responde JSON)
[Route("api/mock/tracking")] // Rutas: /api/mock/tracking/...
public class TrackingMockController : ControllerBase // Sin vistas, solo datos
{ // Inicio de la clase
    private static readonly string[] Transportistas = { "Fast Delivery", "Andreani", "OCA", "Correo Argentino" }; // Empresas inventadas/de ejemplo

    [HttpGet("{orderId:int}")] // Responde a GET /api/mock/tracking/15 (":int" = solo números)
    public IActionResult Obtener(int orderId) // Devuelve el seguimiento de un pedido
    { // Inicio del método
        if (orderId <= 0) return NotFound(new { mensaje = "Pedido inexistente" }); // Ids inválidos → 404

        var respuesta = new TrackingInfo // Armamos la respuesta con el mismo formato del PDF
        { // Inicio de los datos
            OrderId = orderId, // El mismo pedido consultado
            TrackingCode = $"TRK-{(orderId * 7919 % 90000) + 10000}", // Código "inventado" pero siempre igual para el mismo pedido
            Carrier = Transportistas[orderId % Transportistas.Length], // Elige un transportista según el número de pedido
            EstimatedDelivery = DateTime.UtcNow.Date.AddDays(orderId % 5 + 1).ToString("yyyy-MM-dd") // Entre 1 y 5 días desde hoy
        }; // Fin de los datos
        return Ok(respuesta); // Responde 200 con el JSON
    } // Fin del método
} // Fin de la clase

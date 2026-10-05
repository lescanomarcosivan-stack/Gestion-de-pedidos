using System.Text.Json.Serialization; // Trae [JsonPropertyName] para indicar el nombre exacto del campo en el JSON

namespace GestionPedidos.Models; // Namespace de los modelos

// DTO = "objeto de transferencia de datos": molde para leer/escribir JSON, no es una tabla de la base

// Molde de la respuesta de la API externa de seguimiento (igual al ejemplo del PDF)
public class TrackingInfo // Se llena automáticamente al leer el JSON que devuelve la API
{ // Inicio de la clase
    [JsonPropertyName("orderId")] public int OrderId { get; set; } // Id del pedido consultado
    [JsonPropertyName("trackingCode")] public string TrackingCode { get; set; } = string.Empty; // Código de seguimiento, ej. "TRK-98231"
    [JsonPropertyName("carrier")] public string Carrier { get; set; } = string.Empty; // Empresa de transporte, ej. "Fast Delivery"
    [JsonPropertyName("estimatedDelivery")] public string EstimatedDelivery { get; set; } = string.Empty; // Fecha estimada de entrega (texto "aaaa-mm-dd")
} // Fin de la clase

// Molde del JSON que llega al webhook (igual al ejemplo del PDF)
public class WebhookRequest // ASP.NET Core convierte el JSON recibido en este objeto
{ // Inicio de la clase
    [JsonPropertyName("event")] public string? Event { get; set; } // Tipo de evento; esperamos "order.status.changed"
    [JsonPropertyName("orderId")] public int? OrderId { get; set; } // Id del pedido a actualizar
    [JsonPropertyName("status")] public string? Status { get; set; } // Nuevo estado, ej. "DELIVERED"
} // Fin de la clase

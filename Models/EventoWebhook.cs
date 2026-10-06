namespace GestionPedidos.Models; // Namespace de los modelos

// Registro de cada aviso que llega al webhook. Sirve para auditar y mostrar en pantalla qué se recibió
public class EventoWebhook // Entity Framework crea la tabla "EventosWebhook"
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria autonumérica
    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow; // Momento en que llegó el aviso (UTC)
    public string? Evento { get; set; } // Tipo de evento recibido, por ejemplo "order.status.changed"
    public int? PedidoId { get; set; } // Id del pedido que vino en el aviso (puede faltar si el JSON está mal)
    public string? EstadoRecibido { get; set; } // Estado que vino en el aviso, por ejemplo "DELIVERED"
    public int CodigoRespuesta { get; set; } // Código HTTP que devolvimos: 200 ok, 400 datos inválidos, 401 no autorizado, 404 no existe
    public string Resultado { get; set; } = string.Empty; // Mensaje explicando qué pasó (para leerlo en pantalla)
    public string? Ip { get; set; } // Dirección IP de quien envió el aviso
    public string? Autenticacion { get; set; } // Cómo se identificó el emisor: "Firma HMAC", "Clave" o "Ninguna"
    public bool Procesado => CodigoRespuesta == 200; // Calculado (no es columna): true si el pedido se actualizó
} // Fin de la clase

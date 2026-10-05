namespace GestionPedidos.Models; // Namespace de los modelos

// ViewModel = paquete de datos armado a medida para una pantalla (vista)

// Datos que necesita la pantalla de detalle de un pedido
public class PedidoDetalleViewModel // Junta el pedido de la base con la info de la API externa
{ // Inicio de la clase
    public Pedido Pedido { get; set; } = null!; // El pedido con su cliente (sale de PostgreSQL)
    public TrackingInfo? Tracking { get; set; } // Datos de seguimiento (salen de la API externa); null si falló
    public string? ErrorTracking { get; set; } // Mensaje de error si la API externa no respondió
} // Fin de la clase

// Una fila del listado de clientes (con la cantidad de pedidos ya calculada por la base)
public class ClienteListadoViewModel // Evita traer todos los pedidos solo para contarlos
{ // Inicio de la clase
    public int Id { get; set; } // Id del cliente
    public string Nombre { get; set; } = string.Empty; // Nombre del cliente
    public string Email { get; set; } = string.Empty; // Email del cliente
    public string? Telefono { get; set; } // Teléfono (opcional)
    public string? Ciudad { get; set; } // Ciudad (opcional)
    public int CantidadPedidos { get; set; } // Cuántos pedidos tiene en total
    public int PedidosAbiertos { get; set; } // Cuántos pedidos no están ni entregados ni cancelados
} // Fin de la clase

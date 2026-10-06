using System.ComponentModel.DataAnnotations; // Atributos de tamaño de columnas

namespace GestionPedidos.Models; // Namespace de los modelos

// Cada cambio de estado de un pedido queda guardado acá (quién, cuándo, de qué estado a cuál y por qué vía)
public class HistorialEstado // Entity Framework crea la tabla "HistorialEstados"
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria autonumérica
    public int PedidoId { get; set; } // Pedido al que pertenece el cambio
    public Pedido? Pedido { get; set; } // Navegación al pedido
    public DateTime Fecha { get; set; } = DateTime.UtcNow; // Momento del cambio (UTC)
    public EstadoPedido? EstadoAnterior { get; set; } // Estado antes del cambio (null cuando el pedido recién se crea)
    public EstadoPedido EstadoNuevo { get; set; } // Estado después del cambio

    [StringLength(20)] // Texto corto
    public string Origen { get; set; } = string.Empty; // Por dónde llegó el cambio: "Alta", "Manual" o "Webhook"

    [StringLength(150)] // Email del usuario
    public string Usuario { get; set; } = string.Empty; // Quién lo hizo (email del usuario o "webhook")
} // Fin de la clase

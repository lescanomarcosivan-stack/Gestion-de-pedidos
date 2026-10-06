using System.ComponentModel.DataAnnotations; // Atributos de tamaño de columnas

namespace GestionPedidos.Models; // Namespace de los modelos

// Tipos de movimiento de stock
public enum TipoMovimiento // Se guarda como texto
{ // Inicio de la lista
    Ingreso,    // Entrada de mercadería (compra a proveedor)
    Venta,      // Salida por un pedido
    Devolucion, // Vuelve al stock porque se canceló un pedido
    Ajuste      // Corrección manual (rotura, pérdida, inventario)
} // Fin de la lista

// Cada entrada o salida de stock queda registrada (como un libro): permite saber por qué el stock es el que es
public class MovimientoStock // Entity Framework crea la tabla "MovimientosStock"
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria autonumérica
    public int ProductoId { get; set; } // Producto afectado
    public Producto? Producto { get; set; } // Navegación al producto
    public DateTime Fecha { get; set; } = DateTime.UtcNow; // Cuándo (UTC)
    public TipoMovimiento Tipo { get; set; } // Ingreso, Venta, Devolución o Ajuste
    public int Cantidad { get; set; } // Positivo = entra, negativo = sale
    public int StockResultante { get; set; } // Cómo quedó el stock después del movimiento
    public int? PedidoId { get; set; } // Pedido relacionado (en ventas y devoluciones)
    public Pedido? Pedido { get; set; } // Navegación al pedido (permite registrar el movimiento antes de que el pedido tenga Id)

    [StringLength(150)] // Email
    public string Usuario { get; set; } = string.Empty; // Quién lo hizo

    [StringLength(300)] // Texto
    public string? Motivo { get; set; } // Explicación (obligatoria en ajustes)
} // Fin de la clase

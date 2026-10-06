using System.ComponentModel.DataAnnotations; // Atributos de tamaño de columnas

namespace GestionPedidos.Models; // Namespace de los modelos

// Un renglón del pedido: qué producto, cuántas unidades, a qué precio y con qué descuento
public class PedidoItem // Entity Framework crea la tabla "PedidoItems"
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria autonumérica
    public int PedidoId { get; set; } // Pedido al que pertenece el renglón
    public Pedido? Pedido { get; set; } // Navegación al pedido
    public int ProductoId { get; set; } // Producto vendido
    public Producto? Producto { get; set; } // Navegación al producto

    [StringLength(150)] // Tamaño máximo
    public string ProductoNombre { get; set; } = string.Empty; // Copia del nombre al momento de la venta (si después lo renombran, el pedido no cambia)

    public int Cantidad { get; set; } // Unidades pedidas
    public decimal PrecioUnitario { get; set; } // Copia del precio al momento de la venta (si después sube, el pedido no cambia)
    public decimal DescuentoPorcentaje { get; set; } // Descuento de este renglón (0 a 100)

    public decimal Bruto => Cantidad * PrecioUnitario; // Calculado: cantidad × precio, sin descuento
    public decimal Subtotal => Math.Round(Bruto * (1 - DescuentoPorcentaje / 100m), 2); // Calculado: con el descuento del renglón, redondeado a centavos
} // Fin de la clase

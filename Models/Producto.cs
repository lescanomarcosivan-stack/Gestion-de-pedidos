using System.ComponentModel.DataAnnotations; // Atributos de validación

namespace GestionPedidos.Models; // Namespace de los modelos

// Producto que se vende. Entity Framework crea la tabla "Productos"
public class Producto // Clase pública
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria autonumérica

    [Required(ErrorMessage = "El código es obligatorio")] // Obligatorio
    [StringLength(30, ErrorMessage = "Máximo 30 caracteres")] // Tamaño máximo
    [Display(Name = "Código")] // Etiqueta
    public string Codigo { get; set; } = string.Empty; // Código interno (SKU), único

    [Required(ErrorMessage = "El nombre es obligatorio")] // Obligatorio
    [StringLength(150, ErrorMessage = "Máximo 150 caracteres")] // Tamaño máximo
    [Display(Name = "Nombre")] // Etiqueta
    public string Nombre { get; set; } = string.Empty; // Nombre del producto

    [Range(0.01, 999999999, ErrorMessage = "El precio debe ser mayor a 0")] // Precio positivo
    [Display(Name = "Precio unitario")] // Etiqueta
    public decimal Precio { get; set; } // Precio de lista actual (los pedidos guardan una copia del precio del momento)

    [Display(Name = "Stock")] // Etiqueta
    public int Stock { get; set; } // Unidades disponibles. Solo cambia con movimientos (ventas, ingresos, ajustes)

    [Range(0, 1000000, ErrorMessage = "Debe ser 0 o más")] // No negativo
    [Display(Name = "Stock mínimo")] // Etiqueta
    public int StockMinimo { get; set; } // Si el stock queda en este número o menos, se avisa "stock bajo"

    [Display(Name = "Activo")] // Etiqueta
    public bool Activo { get; set; } = true; // Si es false, no aparece para nuevos pedidos (pero se conserva el historial)

    [Timestamp] // Control de concurrencia: PostgreSQL lo mapea a su columna de sistema "xmin" (no se crea columna nueva)
    public uint Version { get; set; } // Si dos usuarios modifican el mismo producto a la vez, el segundo recibe un error en vez de pisar al primero

    public bool StockBajo => Stock <= StockMinimo; // Calculado (no es columna): true si hay que reponer
} // Fin de la clase

using System.ComponentModel.DataAnnotations; // Atributos de validación ([Required], [Range], etc.)
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation; // [ValidateNever] para no validar la navegación

namespace GestionPedidos.Models; // Namespace de los modelos

// Representa un pedido. Entity Framework crea la tabla "Pedidos" a partir de esta clase
public class Pedido // Clase pública
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria autonumérica; es el "orderId" que usan la API externa y el webhook

    [Required(ErrorMessage = "Elegí un cliente")] // Validación: el pedido tiene que pertenecer a un cliente
    [Range(1, int.MaxValue, ErrorMessage = "Elegí un cliente")] // Evita que llegue 0 (opción vacía del combo)
    [Display(Name = "Cliente")] // Etiqueta del formulario
    public int ClienteId { get; set; } // Clave foránea: guarda el Id del cliente dueño del pedido

    [ValidateNever] // MVC no valida este objeto (en el formulario solo viaja ClienteId)
    public Cliente? Cliente { get; set; } // Propiedad de navegación: permite hacer pedido.Cliente.Nombre

    [Required(ErrorMessage = "La descripción es obligatoria")] // Validación: no puede quedar vacía
    [StringLength(300, ErrorMessage = "Máximo 300 caracteres")] // Tamaño máximo
    [Display(Name = "Descripción")] // Etiqueta del formulario
    public string Descripcion { get; set; } = string.Empty; // Qué se pidió (productos, detalle, etc.)

    [Range(0.01, 999999999, ErrorMessage = "El monto debe ser mayor a 0")] // Validación: monto positivo
    [Display(Name = "Monto")] // Etiqueta del formulario
    public decimal Monto { get; set; } // Importe del pedido; decimal evita errores de redondeo con dinero

    [Display(Name = "Estado")] // Etiqueta para mostrar
    public EstadoPedido Estado { get; set; } = EstadoPedido.Pendiente; // Estado actual; todo pedido nuevo arranca "Pendiente"

    [Display(Name = "Creado")] // Etiqueta para mostrar
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow; // Cuándo se creó el pedido (UTC)

    [Display(Name = "Última actualización")] // Etiqueta para mostrar
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow; // Cuándo cambió por última vez (UTC)

    [Range(0, 100)] // Entre 0 % y 100 %
    [Display(Name = "Descuento general (%)")] // Etiqueta
    public decimal DescuentoPorcentaje { get; set; } // Descuento sobre el total del pedido (además del descuento de cada producto)

    [ValidateNever] // MVC no valida esta lista
    public List<PedidoItem> Items { get; set; } = new(); // Productos del pedido (los pedidos viejos, de antes de esta versión, no tienen)

    [DataType(DataType.Date)] // Solo fecha, sin hora
    [Display(Name = "Fecha de entrega")] // Etiqueta
    public DateOnly? FechaEntrega { get; set; } // Día programado para entregar (opcional). Se publica en Google Calendar

    [StringLength(200)] // Tamaño de la columna
    public string? CalendarioEventoId { get; set; } // Id del evento en Google Calendar (para poder modificarlo o borrarlo después)

    public decimal Subtotal => Items.Sum(i => i.Subtotal); // Calculado (no es columna): suma de los renglones antes del descuento general
} // Fin de la clase

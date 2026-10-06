using GestionPedidos.Models; // Para Pedido

namespace GestionPedidos.Services; // Namespace de los servicios

// Cuentas del pedido en un solo lugar (así la pantalla, los reportes y los emails dan siempre el mismo número)
public static class Calculos // Clase estática de ayuda
{ // Inicio de la clase
    // Total = suma de renglones (cada uno con su descuento) menos el descuento general del pedido
    public static decimal TotalPedido(Pedido pedido) // Recibe el pedido con sus renglones
        => Math.Round(pedido.Subtotal * (1 - pedido.DescuentoPorcentaje / 100m), 2); // Redondeado a centavos

    // Importe del descuento general (para mostrarlo en pantalla y en el PDF)
    public static decimal ImporteDescuentoGeneral(Pedido pedido) // Recibe el pedido
        => pedido.Subtotal - TotalPedido(pedido); // Diferencia entre subtotal y total

    // Texto corto con los productos, ej. "2 × Yerba 1kg x12, 1 × Cable 2.5mm x100m" (máximo 300 caracteres)
    public static string Resumen(Pedido pedido) // Recibe el pedido
    { // Inicio del método
        var texto = string.Join(", ", pedido.Items.Select(i => $"{i.Cantidad} × {i.ProductoNombre}")); // Une los renglones con comas
        return texto.Length <= 300 ? texto : texto[..297] + "..."; // Recorta si es muy largo (la columna admite 300)
    } // Fin del método
} // Fin de la clase

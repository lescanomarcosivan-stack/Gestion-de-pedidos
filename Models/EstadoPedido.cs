namespace GestionPedidos.Models; // Namespace = "carpeta lógica" donde vive esta clase; agrupa los modelos

// Enum: lista cerrada de valores posibles para el estado de un pedido
public enum EstadoPedido // Se guarda en la base como texto (ver AppDbContext) para que sea legible
{ // Inicio de la lista de estados
    Pendiente,      // El pedido fue creado y todavía no se empezó a preparar
    EnPreparacion,  // El pedido se está armando / preparando
    Enviado,        // El pedido salió y está en camino
    Entregado,      // El pedido llegó al cliente (el webhook manda "DELIVERED" para este caso)
    Cancelado       // El pedido se anuló
} // Fin de la lista de estados

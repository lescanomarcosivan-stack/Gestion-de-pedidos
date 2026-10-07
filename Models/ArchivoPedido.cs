using System.ComponentModel.DataAnnotations; // Para [StringLength]

namespace GestionPedidos.Models; // Namespace de los modelos

// Archivo adjunto a un pedido. El contenido vive en Google Drive; acá guardamos solo los datos para encontrarlo
public class ArchivoPedido // Entity Framework crea la tabla "ArchivosPedido"
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria autonumérica
    public int PedidoId { get; set; } // Pedido al que pertenece
    public Pedido? Pedido { get; set; } // Navegación al pedido

    [StringLength(200)] // Tamaño de la columna
    public string DriveId { get; set; } = string.Empty; // Id del archivo en Google Drive (con esto se descarga o se borra)

    [StringLength(255)] // Tamaño de la columna
    public string Nombre { get; set; } = string.Empty; // Nombre original del archivo (ej. "remito.pdf")

    [StringLength(150)] // Tamaño de la columna
    public string TipoMime { get; set; } = "application/octet-stream"; // Tipo de contenido (ej. "application/pdf"); lo necesita el navegador al descargar

    public long Tamano { get; set; } // Tamaño en bytes
    public DateTime Fecha { get; set; } = DateTime.UtcNow; // Cuándo se subió (UTC)

    [StringLength(150)] // Email
    public string Usuario { get; set; } = string.Empty; // Quién lo subió
} // Fin de la clase

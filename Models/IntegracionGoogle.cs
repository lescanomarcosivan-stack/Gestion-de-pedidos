using System.ComponentModel.DataAnnotations; // Para [StringLength]

namespace GestionPedidos.Models; // Namespace de los modelos

// Conexión con la cuenta de Google de la EMPRESA (una sola fila). La app usa esa cuenta para Drive y Calendar
public class IntegracionGoogle // Entity Framework crea la tabla "IntegracionesGoogle"
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria (siempre habrá una sola fila)

    [StringLength(150)] // Email
    public string Email { get; set; } = string.Empty; // Cuenta de Google conectada (ej. pedidos@empresa.com)

    public string RefreshTokenCifrado { get; set; } = string.Empty; // "Llave permanente" que entrega Google, guardada CIFRADA (nunca en texto plano)

    [StringLength(200)] // Tamaño de la columna
    public string? CarpetaDriveId { get; set; } // Carpeta "Gestión de Pedidos" que la app crea en el Drive de la empresa

    public DateTime FechaConexion { get; set; } = DateTime.UtcNow; // Cuándo se conectó

    [StringLength(150)] // Email
    public string ConectadoPor { get; set; } = string.Empty; // Qué administrador la conectó

    [StringLength(500)] // Texto
    public string? UltimoError { get; set; } // Último problema al usar Google (ej. permiso revocado); null si anda bien
} // Fin de la clase

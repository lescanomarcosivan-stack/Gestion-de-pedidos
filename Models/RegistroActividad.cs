using System.ComponentModel.DataAnnotations; // Atributos de tamaño de columnas

namespace GestionPedidos.Models; // Namespace de los modelos

// Tipos de registro que guarda el sistema
public enum TipoRegistro // Se guarda como texto en la base
{ // Inicio de la lista
    Acceso,    // Inicios y cierres de sesión, intentos fallidos, accesos denegados
    Actividad, // Cambios que hacen los usuarios (altas, ediciones, cambios de estado, roles)
    Webhook,   // Avisos recibidos desde sistemas externos
    Error,     // Errores inesperados de la aplicación
    Email,     // Notificaciones enviadas (o que no se pudieron enviar)
    Google     // Conexión con Google, archivos de Drive y eventos de Calendar
} // Fin de la lista

// Bitácora general: todo lo importante que pasa en el sistema queda registrado acá
public class RegistroActividad // Entity Framework crea la tabla "RegistrosActividad"
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria autonumérica
    public DateTime Fecha { get; set; } = DateTime.UtcNow; // Cuándo pasó (UTC)
    public TipoRegistro Tipo { get; set; } // Acceso, Actividad, Webhook o Error

    [StringLength(150)] // Email
    public string Usuario { get; set; } = string.Empty; // Quién lo hizo ("anónimo" si no había sesión)

    [StringLength(120)] // Texto corto
    public string Accion { get; set; } = string.Empty; // Qué pasó, en pocas palabras (ej. "Login exitoso")

    [StringLength(2000)] // Texto largo
    public string? Detalle { get; set; } // Información extra (ej. qué campos cambiaron)

    [StringLength(60)] // Dirección IP
    public string? Ip { get; set; } // Desde qué dirección IP vino el pedido

    [StringLength(30)] // Texto corto
    public string? EntidadTipo { get; set; } // Sobre qué tipo de dato fue ("Cliente", "Pedido", "Usuario")
    public int? EntidadId { get; set; } // Id de ese dato (sirve para mostrar los cambios en la ficha del cliente)
} // Fin de la clase

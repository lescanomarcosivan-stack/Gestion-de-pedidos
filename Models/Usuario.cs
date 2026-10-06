using System.ComponentModel.DataAnnotations; // Atributos de validación y tamaño de columnas

namespace GestionPedidos.Models; // Namespace de los modelos

// Nombres de los roles. Se usan como constantes para no escribir el texto a mano (evita errores de tipeo)
public static class Roles // Clase estática: solo guarda valores fijos
{ // Inicio de la clase
    public const string Administrador = "Administrador"; // Puede todo: datos, usuarios y registro de actividad
    public const string Operador = "Operador"; // Puede cargar y modificar clientes y pedidos
    public const string Consulta = "Consulta"; // Solo puede mirar (no modifica nada)
    public const string Edicion = Administrador + "," + Operador; // Atajo para [Authorize(Roles = Roles.Edicion)] = admin u operador
    public static readonly string[] Todos = { Administrador, Operador, Consulta }; // Lista completa (para el combo de la pantalla de usuarios)
} // Fin de la clase

// Usuario que puede entrar al sistema. Entity Framework crea la tabla "Usuarios"
public class Usuario // Clase pública
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria autonumérica

    [Required, StringLength(150)] // Obligatorio, máximo 150 caracteres
    public string Email { get; set; } = string.Empty; // Email con el que inicia sesión (único, guardado en minúsculas)

    [Required, StringLength(120)] // Obligatorio, máximo 120 caracteres
    public string Nombre { get; set; } = string.Empty; // Nombre para mostrar en pantalla

    [StringLength(500)] // Espacio suficiente para el hash
    public string? PasswordHash { get; set; } // Contraseña CIFRADA con PBKDF2 (nunca se guarda la contraseña real). Null si solo entra con Google

    [StringLength(100)] // Id de Google
    public string? GoogleId { get; set; } // Identificador que entrega Google; se completa la primera vez que entra con Google

    [Required, StringLength(20)] // Obligatorio
    public string Rol { get; set; } = Roles.Consulta; // Rol del usuario; por seguridad arranca con el menor permiso

    public bool Activo { get; set; } // Si es false no puede entrar (cuentas nuevas esperan aprobación del administrador)
    public DateTime FechaAlta { get; set; } = DateTime.UtcNow; // Cuándo se creó la cuenta (UTC)
    public DateTime? UltimoAcceso { get; set; } // Último inicio de sesión exitoso
    public int IntentosFallidos { get; set; } // Contraseñas incorrectas seguidas (protección contra adivinar claves)
    public DateTime? BloqueadoHasta { get; set; } // Si tiene fecha futura, la cuenta está bloqueada temporalmente

    [StringLength(100)] // Hash del token
    public string? TokenRecuperacionHash { get; set; } // Hash del código de "olvidé mi contraseña" (el código real solo viaja por email)
    public DateTime? TokenRecuperacionVence { get; set; } // Hasta cuándo sirve ese código (30 minutos)
} // Fin de la clase

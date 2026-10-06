using System.ComponentModel.DataAnnotations; // Atributos de validación para los formularios

namespace GestionPedidos.Models; // Namespace de los modelos

// ViewModel = paquete de datos armado a medida para una pantalla (vista)

// Datos que necesita la pantalla de detalle de un pedido
public class PedidoDetalleViewModel // Junta el pedido de la base con la info de la API externa
{ // Inicio de la clase
    public Pedido Pedido { get; set; } = null!; // El pedido con su cliente (sale de PostgreSQL)
    public TrackingInfo? Tracking { get; set; } // Datos de seguimiento (salen de la API externa); null si falló o no corresponde
    public string? ErrorTracking { get; set; } // Mensaje de error si la API externa no respondió
    public List<HistorialEstado> Historial { get; set; } = new(); // Todos los cambios de estado del pedido, del más nuevo al más viejo
} // Fin de la clase

// Una fila del listado de clientes (con la cantidad de pedidos ya calculada por la base)
public class ClienteListadoViewModel // Evita traer todos los pedidos solo para contarlos
{ // Inicio de la clase
    public int Id { get; set; } // Id del cliente
    public string Nombre { get; set; } = string.Empty; // Nombre del cliente
    public string Email { get; set; } = string.Empty; // Email del cliente
    public string? Telefono { get; set; } // Teléfono (opcional)
    public string? Ciudad { get; set; } // Ciudad (opcional)
    public int CantidadPedidos { get; set; } // Cuántos pedidos tiene en total
    public int PedidosAbiertos { get; set; } // Cuántos pedidos no están ni entregados ni cancelados
} // Fin de la clase

// Datos de paginación que usa la vista parcial _Paginacion
public class InfoPaginacion // Se usa en cualquier listado paginado
{ // Inicio de la clase
    public int Pagina { get; set; } // Página actual (empieza en 1)
    public int TamanoPagina { get; set; } // Cuántas filas por página
    public int Total { get; set; } // Cantidad total de filas que cumplen los filtros
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(Total / (double)TamanoPagina)); // Páginas necesarias (mínimo 1)
    public int Desde => Total == 0 ? 0 : (Pagina - 1) * TamanoPagina + 1; // N° de la primera fila mostrada (ej. 11)
    public int Hasta => Math.Min(Pagina * TamanoPagina, Total); // N° de la última fila mostrada (ej. 20)
} // Fin de la clase

// Pantalla de listado de pedidos: filas de la página actual + datos de paginación + estado de los filtros
public class PedidosListadoViewModel // Agrupa todo lo que necesita Views/Pedidos/Index.cshtml
{ // Inicio de la clase
    public List<Pedido> Pedidos { get; set; } = new(); // Pedidos de la página actual
    public InfoPaginacion Paginacion { get; set; } = new(); // Página, total, etc.
    public bool HayFiltros { get; set; } // true si el usuario aplicó algún filtro (para mostrar "Limpiar filtros")
    public bool ExistenPedidos { get; set; } // true si hay al menos un pedido en la base (distingue "no hay pedidos" de "no hubo resultados")
    public string? ErrorFiltro { get; set; } // Mensaje si los filtros son inválidos (ej. Desde posterior a Hasta)
} // Fin de la clase

// Una fila del dashboard: un estado con su cantidad y monto
public class ResumenEstado // Resultado del GROUP BY por estado
{ // Inicio de la clase
    public EstadoPedido Estado { get; set; } // Estado agrupado
    public int Cantidad { get; set; } // Cantidad de pedidos en ese estado
    public decimal Monto { get; set; } // Suma de montos de esos pedidos
} // Fin de la clase

// Todo lo que muestra el dashboard
public class DashboardViewModel // Se arma en HomeController.Index
{ // Inicio de la clase
    public List<ResumenEstado> PorEstado { get; set; } = new(); // Una fila por cada estado (incluye los que tienen 0)
    public int TotalPedidos { get; set; } // Cantidad total de pedidos
    public decimal MontoTotal { get; set; } // Suma de todos los montos
    public int PedidosAbiertos { get; set; } // Pedidos que no están entregados ni cancelados
    public decimal MontoAbierto { get; set; } // Monto de esos pedidos abiertos
    public int TotalClientes { get; set; } // Cantidad de clientes
    public List<HistorialEstado> UltimosCambios { get; set; } = new(); // Últimos cambios de estado (actividad reciente)
} // Fin de la clase

// Formulario de inicio de sesión
public class LoginViewModel // Datos que escribe el usuario en la pantalla de login
{ // Inicio de la clase
    [Required(ErrorMessage = "Ingresá tu email")] // Obligatorio
    [EmailAddress(ErrorMessage = "Email inválido")] // Formato de email
    [Display(Name = "Email")] // Etiqueta
    public string Email { get; set; } = string.Empty; // Email

    [Required(ErrorMessage = "Ingresá tu contraseña")] // Obligatorio
    [DataType(DataType.Password)] // Hace que el campo se muestre con puntitos
    [Display(Name = "Contraseña")] // Etiqueta
    public string Password { get; set; } = string.Empty; // Contraseña escrita (nunca se guarda tal cual)

    public string? ReturnUrl { get; set; } // Página a la que quería ir antes de que le pidiéramos login
} // Fin de la clase

// Formulario para crear una cuenta nueva
public class RegistroViewModel // Datos de la pantalla "Crear cuenta"
{ // Inicio de la clase
    [Required(ErrorMessage = "Ingresá tu nombre"), StringLength(120)] // Obligatorio
    [Display(Name = "Nombre")] // Etiqueta
    public string Nombre { get; set; } = string.Empty; // Nombre

    [Required(ErrorMessage = "Ingresá tu email"), EmailAddress(ErrorMessage = "Email inválido"), StringLength(150)] // Obligatorio y con formato de email
    [Display(Name = "Email")] // Etiqueta
    public string Email { get; set; } = string.Empty; // Email

    [Required(ErrorMessage = "Ingresá una contraseña")] // Obligatorio
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mínimo 8 caracteres")] // Largo mínimo
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Debe tener al menos una letra y un número")] // Exige letras y números
    [DataType(DataType.Password)] // Campo oculto
    [Display(Name = "Contraseña")] // Etiqueta
    public string Password { get; set; } = string.Empty; // Contraseña nueva

    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden")] // Debe ser igual a la anterior
    [DataType(DataType.Password)] // Campo oculto
    [Display(Name = "Repetir contraseña")] // Etiqueta
    public string ConfirmarPassword { get; set; } = string.Empty; // Confirmación
} // Fin de la clase

// Formulario "Olvidé mi contraseña"
public class OlvideClaveViewModel // Solo pide el email
{ // Inicio de la clase
    [Required(ErrorMessage = "Ingresá tu email"), EmailAddress(ErrorMessage = "Email inválido")] // Obligatorio y con formato
    [Display(Name = "Email")] // Etiqueta
    public string Email { get; set; } = string.Empty; // Email de la cuenta
} // Fin de la clase

// Formulario para elegir una contraseña nueva (se llega desde el enlace del email)
public class RestablecerClaveViewModel // Datos de la pantalla "Nueva contraseña"
{ // Inicio de la clase
    [Required] public string Email { get; set; } = string.Empty; // Email (viene oculto en el formulario)
    [Required] public string Token { get; set; } = string.Empty; // Código secreto que llegó por email (viene oculto)

    [Required(ErrorMessage = "Ingresá una contraseña")] // Obligatorio
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mínimo 8 caracteres")] // Largo mínimo
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Debe tener al menos una letra y un número")] // Letras y números
    [DataType(DataType.Password)] // Campo oculto
    [Display(Name = "Nueva contraseña")] // Etiqueta
    public string Password { get; set; } = string.Empty; // Contraseña nueva

    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden")] // Igual a la anterior
    [DataType(DataType.Password)] // Campo oculto
    [Display(Name = "Repetir contraseña")] // Etiqueta
    public string ConfirmarPassword { get; set; } = string.Empty; // Confirmación
} // Fin de la clase

// Formulario de la pantalla "Probar webhook"
public class ProbarWebhookViewModel // Datos para armar y enviar un evento de prueba
{ // Inicio de la clase
    [Range(1, int.MaxValue, ErrorMessage = "Ingresá un número de pedido")] // Debe ser positivo
    [Display(Name = "N° de pedido")] // Etiqueta
    public int PedidoId { get; set; } = 15; // Pedido a modificar (15 como en el ejemplo del PDF)

    [Display(Name = "Estado a enviar")] // Etiqueta
    public string Estado { get; set; } = "DELIVERED"; // Estado en el formato del sistema externo

    [Display(Name = "Seguridad")] // Etiqueta
    public string Modo { get; set; } = "firma"; // "firma" = HMAC correcto, "sin-firma" = sin credenciales, "firma-invalida" = firma falsa

    public int? CodigoRespuesta { get; set; } // Resultado del envío: código HTTP recibido
    public string? RespuestaJson { get; set; } // Resultado del envío: cuerpo de la respuesta
    public string? CuerpoEnviado { get; set; } // JSON que se envió (para mostrarlo)
    public string? FirmaEnviada { get; set; } // Firma que se envió (para mostrarla)
} // Fin de la clase

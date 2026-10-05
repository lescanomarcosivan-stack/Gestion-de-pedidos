using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Pedido, EstadoPedido y PedidoDetalleViewModel
using GestionPedidos.Services; // Para ITrackingService y EstadoMapper
using Microsoft.AspNetCore.Mvc; // Para Controller y atributos MVC
using Microsoft.AspNetCore.Mvc.Rendering; // Para SelectList (listas desplegables)
using Microsoft.EntityFrameworkCore; // Para Include, ToListAsync, etc.

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Controlador de pedidos: listar con filtros, crear, ver detalle (con tracking) y cambiar estado
public class PedidosController : Controller // Hereda de Controller
{ // Inicio de la clase
    private readonly AppDbContext _db; // Acceso a la base
    private readonly ITrackingService _tracking; // Servicio que consulta la API externa de seguimiento

    public PedidosController(AppDbContext db, ITrackingService tracking) // Recibe ambos por inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos el DbContext
        _tracking = tracking; // Guardamos el servicio de tracking
    } // Fin del constructor

    // GET /Pedidos?q=...&estado=...&desde=...&hasta=... → listado con filtros
    public async Task<IActionResult> Index(string? q, EstadoPedido? estado, DateTime? desde, DateTime? hasta) // Todos los filtros son opcionales ("?")
    { // Inicio del método
        var consulta = _db.Pedidos.Include(p => p.Cliente).AsQueryable(); // Consulta de pedidos trayendo también el cliente (para mostrar su nombre)

        if (!string.IsNullOrWhiteSpace(q)) // Si escribieron algo en el buscador...
        { // Inicio del filtro de texto
            var texto = q.Trim().ToLower(); // Normalizamos el texto
            var esNumero = int.TryParse(texto, out var numero); // Si escribieron un número, también buscamos por N° de pedido
            consulta = consulta.Where(p => // Condición WHERE
                (esNumero && p.Id == numero) || // Coincide el número de pedido, o...
                p.Descripcion.ToLower().Contains(texto) || // ...la descripción contiene el texto, o...
                p.Cliente!.Nombre.ToLower().Contains(texto)); // ...el nombre del cliente lo contiene
        } // Fin del filtro de texto

        if (estado.HasValue) // Si eligieron un estado en el combo...
            consulta = consulta.Where(p => p.Estado == estado.Value); // ...solo pedidos en ese estado

        if (desde.HasValue) // Si pusieron fecha "desde"...
        { // Inicio del filtro desde
            var desdeUtc = DateTime.SpecifyKind(desde.Value.Date, DateTimeKind.Utc).AddHours(3); // 00:00 de Argentina = 03:00 UTC
            consulta = consulta.Where(p => p.FechaCreacion >= desdeUtc); // Pedidos creados a partir de ese momento
        } // Fin del filtro desde

        if (hasta.HasValue) // Si pusieron fecha "hasta"...
        { // Inicio del filtro hasta
            var hastaUtc = DateTime.SpecifyKind(hasta.Value.Date.AddDays(1), DateTimeKind.Utc).AddHours(3); // Fin de ese día en Argentina, pasado a UTC
            consulta = consulta.Where(p => p.FechaCreacion < hastaUtc); // Pedidos creados antes de que termine ese día
        } // Fin del filtro hasta

        var pedidos = await consulta.OrderByDescending(p => p.FechaCreacion).ToListAsync(); // Ejecuta la consulta, del más nuevo al más viejo

        ViewBag.Q = q; // Mantiene el texto en el buscador
        ViewBag.Estado = estado; // Mantiene el estado elegido
        ViewBag.Desde = desde?.ToString("yyyy-MM-dd"); // Mantiene la fecha desde (formato que entiende el input de fecha)
        ViewBag.Hasta = hasta?.ToString("yyyy-MM-dd"); // Mantiene la fecha hasta
        return View(pedidos); // Muestra Views/Pedidos/Index.cshtml
    } // Fin del método

    // GET /Pedidos/Details/15 → detalle del pedido + datos de la API externa
    public async Task<IActionResult> Details(int id) // "id" es el número de pedido
    { // Inicio del método
        var pedido = await _db.Pedidos.Include(p => p.Cliente).FirstOrDefaultAsync(p => p.Id == id); // Busca el pedido con su cliente
        if (pedido is null) return NotFound(); // Si no existe, 404

        var (tracking, error) = await _tracking.ObtenerAsync(id); // Consulta la API externa DESDE EL BACKEND (como pide el challenge)
        var modelo = new PedidoDetalleViewModel { Pedido = pedido, Tracking = tracking, ErrorTracking = error }; // Junta todo para la vista
        return View(modelo); // Muestra Views/Pedidos/Details.cshtml
    } // Fin del método

    // GET /Pedidos/Create?clienteId=3 → formulario de nuevo pedido (con el cliente ya elegido si vino en la URL)
    public async Task<IActionResult> Create(int? clienteId) // clienteId es opcional
    { // Inicio del método
        await CargarClientesAsync(clienteId); // Llena el combo de clientes
        return View(new Pedido { ClienteId = clienteId ?? 0 }); // Formulario con un pedido nuevo
    } // Fin del método

    // POST /Pedidos/Create → guarda el pedido nuevo
    [HttpPost] // Solo envíos de formulario
    [ValidateAntiForgeryToken] // Protección contra formularios falsos
    public async Task<IActionResult> Create([Bind("ClienteId,Descripcion,Monto")] Pedido pedido) // Solo aceptamos estos 3 campos
    { // Inicio del método
        if (pedido.ClienteId > 0 && !await _db.Clientes.AnyAsync(c => c.Id == pedido.ClienteId)) // Si mandaron un cliente que no existe...
            ModelState.AddModelError(nameof(Pedido.ClienteId), "El cliente no existe"); // ...error

        if (!ModelState.IsValid) // Si hay errores de validación...
        { // Inicio del bloque de error
            await CargarClientesAsync(pedido.ClienteId); // Volvemos a llenar el combo
            return View(pedido); // Mostramos el formulario con los mensajes
        } // Fin del bloque de error

        pedido.Estado = EstadoPedido.Pendiente; // Todo pedido nuevo arranca Pendiente
        pedido.FechaCreacion = DateTime.UtcNow; // Fecha de creación
        pedido.FechaActualizacion = DateTime.UtcNow; // Fecha de última actualización
        _db.Pedidos.Add(pedido); // Marcamos para insertar
        await _db.SaveChangesAsync(); // INSERT en PostgreSQL
        TempData["Mensaje"] = $"Pedido #{pedido.Id} creado"; // Mensaje de confirmación con el número asignado
        return RedirectToAction(nameof(Details), new { id = pedido.Id }); // Vamos al detalle del pedido nuevo
    } // Fin del método

    // POST /Pedidos/CambiarEstado/15 → cambia el estado desde la pantalla
    [HttpPost] // Solo envíos de formulario
    [ValidateAntiForgeryToken] // Protección contra formularios falsos
    public async Task<IActionResult> CambiarEstado(int id, EstadoPedido estado) // Recibe el pedido y el estado nuevo
    { // Inicio del método
        var pedido = await _db.Pedidos.FindAsync(id); // Busca el pedido
        if (pedido is null) return NotFound(); // Si no existe, 404
        if (!Enum.IsDefined(estado)) return BadRequest("Estado inválido"); // Si mandaron un estado que no existe, error 400

        pedido.Estado = estado; // Asigna el nuevo estado
        pedido.FechaActualizacion = DateTime.UtcNow; // Registra cuándo cambió
        await _db.SaveChangesAsync(); // UPDATE en PostgreSQL
        TempData["Mensaje"] = $"Estado cambiado a {EstadoMapper.Nombre(estado)}"; // Mensaje de confirmación
        return RedirectToAction(nameof(Details), new { id }); // Vuelve al detalle
    } // Fin del método

    // Método auxiliar: arma la lista de clientes para el combo desplegable
    private async Task CargarClientesAsync(int? seleccionado) // "private" = no es una página, solo ayuda a otros métodos
    { // Inicio del método
        var clientes = await _db.Clientes.OrderBy(c => c.Nombre).ToListAsync(); // Todos los clientes ordenados por nombre
        ViewBag.Clientes = new SelectList(clientes, nameof(Cliente.Id), nameof(Cliente.Nombre), seleccionado); // Valor = Id, texto visible = Nombre
    } // Fin del método
} // Fin de la clase

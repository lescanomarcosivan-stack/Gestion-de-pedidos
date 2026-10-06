using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Pedido, EstadoPedido, Roles y ViewModels
using GestionPedidos.Services; // Para ITrackingService, EstadoMapper e IAuditoria
using Microsoft.AspNetCore.Authorization; // Para [Authorize] (permisos por rol)
using Microsoft.AspNetCore.Mvc; // Para Controller y atributos MVC
using Microsoft.AspNetCore.Mvc.Rendering; // Para SelectList (listas desplegables)
using Microsoft.EntityFrameworkCore; // Para Include, ToListAsync, CountAsync, etc.

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Controlador de pedidos: listar con filtros y paginación, crear, ver detalle (con tracking e historial) y cambiar estado
public class PedidosController : Controller // Hereda de Controller
{ // Inicio de la clase
    private const int TamanoPagina = 10; // Pedidos por página en el listado
    private readonly AppDbContext _db; // Acceso a la base
    private readonly ITrackingService _tracking; // Servicio que consulta la API externa de seguimiento
    private readonly IAuditoria _auditoria; // Bitácora de actividad

    public PedidosController(AppDbContext db, ITrackingService tracking, IAuditoria auditoria) // Recibe todo por inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos el DbContext
        _tracking = tracking; // Guardamos el servicio de tracking
        _auditoria = auditoria; // Guardamos la bitácora
    } // Fin del constructor

    // GET /Pedidos?q=...&estado=...&desde=...&hasta=...&pagina=2 → listado con filtros y paginación
    public async Task<IActionResult> Index(string? q, EstadoPedido? estado, DateTime? desde, DateTime? hasta, int pagina = 1) // Todos los filtros son opcionales ("?")
    { // Inicio del método
        var modelo = new PedidosListadoViewModel // Lo que va a recibir la vista
        { // Inicio de los datos
            HayFiltros = !string.IsNullOrWhiteSpace(q) || estado.HasValue || desde.HasValue || hasta.HasValue, // ¿Se aplicó algún filtro?
            ExistenPedidos = await _db.Pedidos.AnyAsync(), // ¿Hay al menos un pedido en la base? (para el mensaje de lista vacía)
            Paginacion = new InfoPaginacion { Pagina = 1, TamanoPagina = TamanoPagina } // Paginación inicial
        }; // Fin de los datos

        ViewBag.Q = q; // Mantiene el texto en el buscador
        ViewBag.Estado = estado; // Mantiene el estado elegido
        ViewBag.Desde = desde?.ToString("yyyy-MM-dd"); // Mantiene la fecha desde (formato que entiende el input de fecha)
        ViewBag.Hasta = hasta?.ToString("yyyy-MM-dd"); // Mantiene la fecha hasta

        if (desde.HasValue && hasta.HasValue && desde.Value.Date > hasta.Value.Date) // Rango de fechas al revés...
        { // Inicio del bloque de error
            modelo.ErrorFiltro = $"La fecha \"Desde\" ({desde:dd/MM/yyyy}) no puede ser posterior a la fecha \"Hasta\" ({hasta:dd/MM/yyyy}). Corregí el rango."; // ...mensaje claro
            return View(modelo); // ...mostramos el error sin consultar la base
        } // Fin del bloque de error

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

        modelo.Paginacion.Total = await consulta.CountAsync(); // Cuántos pedidos cumplen los filtros (SELECT COUNT)
        modelo.Paginacion.Pagina = Math.Clamp(pagina, 1, modelo.Paginacion.TotalPaginas); // Página válida: entre 1 y la última
        modelo.Pedidos = await consulta // Pedidos de la página actual
            .OrderByDescending(p => p.FechaCreacion) // Del más nuevo al más viejo
            .ThenByDescending(p => p.Id) // Desempate estable (para que la paginación no repita filas)
            .Skip((modelo.Paginacion.Pagina - 1) * TamanoPagina) // Salteamos las páginas anteriores (OFFSET)
            .Take(TamanoPagina) // Tomamos una página (LIMIT)
            .ToListAsync(); // Ejecuta la consulta

        return View(modelo); // Muestra Views/Pedidos/Index.cshtml
    } // Fin del método

    // GET /Pedidos/Details/15 → detalle del pedido + seguimiento externo + historial de estados
    public async Task<IActionResult> Details(int id) // "id" es el número de pedido
    { // Inicio del método
        var pedido = await _db.Pedidos.Include(p => p.Cliente).FirstOrDefaultAsync(p => p.Id == id); // Busca el pedido con su cliente
        if (pedido is null) return NotFound(); // Si no existe, 404

        var modelo = new PedidoDetalleViewModel { Pedido = pedido }; // Arrancamos el ViewModel con el pedido
        if (pedido.Estado != EstadoPedido.Cancelado) // Un pedido cancelado no tiene envío activo: no consultamos el seguimiento
        { // Inicio del bloque
            var (tracking, error) = await _tracking.ObtenerAsync(id); // Consulta la API externa DESDE EL BACKEND (como pide el challenge)
            modelo.Tracking = tracking; // Datos de seguimiento (o null)
            modelo.ErrorTracking = error; // Mensaje de error (o null)
        } // Fin del bloque

        modelo.Historial = await _db.HistorialEstados // Historial de cambios de estado
            .Where(h => h.PedidoId == id) // Solo de este pedido
            .OrderByDescending(h => h.Fecha) // Más nuevos primero
            .ThenByDescending(h => h.Id) // Desempate
            .ToListAsync(); // Ejecuta la consulta
        return View(modelo); // Muestra Views/Pedidos/Details.cshtml
    } // Fin del método

    // GET /Pedidos/Create?clienteId=3 → formulario de nuevo pedido (con el cliente ya elegido si vino en la URL)
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> Create(int? clienteId) // clienteId es opcional
    { // Inicio del método
        await CargarClientesAsync(clienteId); // Llena el combo de clientes
        return View(new Pedido { ClienteId = clienteId ?? 0 }); // Formulario con un pedido nuevo
    } // Fin del método

    // POST /Pedidos/Create → guarda el pedido nuevo
    [HttpPost] // Solo envíos de formulario
    [ValidateAntiForgeryToken] // Protección contra formularios falsos
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
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
        await _db.SaveChangesAsync(); // INSERT en PostgreSQL (ahora el pedido tiene Id)

        _db.HistorialEstados.Add(new HistorialEstado { PedidoId = pedido.Id, EstadoNuevo = pedido.Estado, Origen = "Alta", Usuario = User.Identity!.Name! }); // Primer registro del historial
        _auditoria.Registrar(TipoRegistro.Actividad, "Alta de pedido", $"Pedido #{pedido.Id} · {Formato.Moneda(pedido.Monto)}", "Pedido", pedido.Id); // Bitácora
        await _db.SaveChangesAsync(); // Guardamos historial y bitácora juntos
        TempData["Mensaje"] = $"Pedido #{pedido.Id} creado"; // Mensaje de confirmación con el número asignado
        return RedirectToAction(nameof(Details), new { id = pedido.Id }); // Vamos al detalle del pedido nuevo
    } // Fin del método

    // POST /Pedidos/CambiarEstado/15 → cambia el estado desde la pantalla
    [HttpPost] // Solo envíos de formulario
    [ValidateAntiForgeryToken] // Protección contra formularios falsos
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> CambiarEstado(int id, EstadoPedido estado) // Recibe el pedido y el estado nuevo
    { // Inicio del método
        var pedido = await _db.Pedidos.FindAsync(id); // Busca el pedido
        if (pedido is null) return NotFound(); // Si no existe, 404
        if (!Enum.IsDefined(estado)) return BadRequest("Estado inválido"); // Si mandaron un estado que no existe, error 400
        if (pedido.Estado == estado) // Si eligieron el mismo estado que ya tiene...
        { // Inicio del bloque
            TempData["Mensaje"] = "El pedido ya estaba en ese estado; no se registró ningún cambio."; // ...avisamos y no ensuciamos el historial
            return RedirectToAction(nameof(Details), new { id }); // ...y volvemos
        } // Fin del bloque

        var anterior = pedido.Estado; // Guardamos el estado viejo
        pedido.Estado = estado; // Asigna el nuevo estado
        pedido.FechaActualizacion = DateTime.UtcNow; // Registra cuándo cambió
        _db.HistorialEstados.Add(new HistorialEstado { PedidoId = id, EstadoAnterior = anterior, EstadoNuevo = estado, Origen = "Manual", Usuario = User.Identity!.Name! }); // Historial
        _auditoria.Registrar(TipoRegistro.Actividad, "Cambio de estado", $"Pedido #{id}: {EstadoMapper.Nombre(anterior)} → {EstadoMapper.Nombre(estado)}", "Pedido", id); // Bitácora
        await _db.SaveChangesAsync(); // UPDATE + INSERTs en una sola transacción
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

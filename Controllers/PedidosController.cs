using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Pedido, EstadoPedido, Roles y ViewModels
using GestionPedidos.Services; // Para tracking, stock, notificaciones, bitácora, cálculos y exportadores
using Microsoft.AspNetCore.Authorization; // Para [Authorize] (permisos por rol)
using Microsoft.AspNetCore.Mvc; // Para Controller y atributos MVC
using Microsoft.AspNetCore.Mvc.Rendering; // Para SelectList (listas desplegables)
using Microsoft.EntityFrameworkCore; // Para Include, ToListAsync, CountAsync, etc.

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Controlador de pedidos: listar con filtros y paginación, crear con productos, ver detalle, cambiar estado y exportar
public class PedidosController : Controller // Hereda de Controller
{ // Inicio de la clase
    private const int TamanoPagina = 10; // Pedidos por página en el listado
    private readonly AppDbContext _db; // Acceso a la base
    private readonly ITrackingService _tracking; // Servicio que consulta la API externa de seguimiento
    private readonly IAuditoria _auditoria; // Bitácora de actividad
    private readonly IStockService _stock; // Control de stock
    private readonly INotificador _notificador; // Avisos por email

    public PedidosController(AppDbContext db, ITrackingService tracking, IAuditoria auditoria, IStockService stock, INotificador notificador) // Recibe todo por inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos el DbContext
        _tracking = tracking; // Guardamos el servicio de tracking
        _auditoria = auditoria; // Guardamos la bitácora
        _stock = stock; // Guardamos el servicio de stock
        _notificador = notificador; // Guardamos el notificador
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

        modelo.ErrorFiltro = ValidarFechas(desde, hasta); // ¿Rango de fechas al revés?
        if (modelo.ErrorFiltro is not null) return View(modelo); // Mostramos el error sin consultar la base

        var consulta = Filtrar(q, estado, desde, hasta); // Consulta con los filtros (todavía no se ejecuta)
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

    // GET /Pedidos/Exportar?formato=excel|pdf&q=...&estado=... → reporte con los MISMOS filtros del listado (todas las páginas)
    public async Task<IActionResult> Exportar(string formato, string? q, EstadoPedido? estado, DateTime? desde, DateTime? hasta) // Formato y filtros
    { // Inicio del método
        var error = ValidarFechas(desde, hasta); // Mismo control que la pantalla
        if (error is not null) { TempData["Error"] = error; return RedirectToAction(nameof(Index)); } // Si el rango es inválido, volvemos con el error
        var pedidos = await Filtrar(q, estado, desde, hasta).OrderByDescending(p => p.FechaCreacion).ThenByDescending(p => p.Id).ToListAsync(); // Todos los que cumplen
        var vigentes = pedidos.Where(p => p.Estado != EstadoPedido.Cancelado).ToList(); // Los cancelados no suman
        var filtros = new List<string>(); // Descripción de los filtros para el subtítulo
        if (!string.IsNullOrWhiteSpace(q)) filtros.Add($"búsqueda \"{q}\""); // Texto
        if (estado.HasValue) filtros.Add($"estado {EstadoMapper.Nombre(estado.Value)}"); // Estado
        if (desde.HasValue) filtros.Add($"desde {desde:dd/MM/yyyy}"); // Desde
        if (hasta.HasValue) filtros.Add($"hasta {hasta:dd/MM/yyyy}"); // Hasta
        var reporte = new Reporte // Reporte genérico
        { // Inicio de los datos
            Titulo = "Reporte de pedidos", // Título
            Subtitulo = $"Generado el {Formato.Fecha(DateTime.UtcNow)} por {User.Identity!.Name} · {(filtros.Count == 0 ? "sin filtros" : "Filtros: " + string.Join(", ", filtros))}", // Fecha, usuario y filtros
            Columnas = new() // Columnas
            { // Inicio de las columnas
                new("N°", TipoColumna.Entero, 0.5), // Número de pedido
                new("Fecha", TipoColumna.Texto, 1.1), // Fecha de creación
                new("Cliente", TipoColumna.Texto, 2), // Cliente
                new("Detalle", TipoColumna.Texto, 4), // Productos o descripción
                new("Estado", TipoColumna.Texto, 1.1), // Estado
                new("Monto", TipoColumna.Moneda, 1.4) // Total
            }, // Fin de las columnas
            Filas = pedidos.Select(p => new object?[] { p.Id, Formato.FechaCorta(p.FechaCreacion), p.Cliente?.Nombre, p.Descripcion, EstadoMapper.Nombre(p.Estado), p.Monto }).ToList(), // Una fila por pedido
            Totales = new object?[] { null, null, $"{vigentes.Count} pedido(s)", "Total sin cancelados", null, vigentes.Sum(p => p.Monto) } // Total vigente
        }; // Fin del reporte
        var cancelados = pedidos.Count - vigentes.Count; // Cantidad de cancelados en el reporte
        if (cancelados > 0) reporte.Notas.Add($"Cancelados incluidos en el listado (no suman al total): {cancelados} pedido(s) por {Formato.Moneda(pedidos.Where(p => p.Estado == EstadoPedido.Cancelado).Sum(p => p.Monto))}."); // Nota al pie
        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Actividad, "Exportación de pedidos", $"{formato.ToUpperInvariant()} · {pedidos.Count} pedido(s)"); // Bitácora
        var fecha = DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-dd"); // Fecha para el nombre del archivo
        return formato == "pdf" // ¿PDF?
            ? File(ExportadorPdf.Generar(reporte), "application/pdf", $"pedidos_{fecha}.pdf") // PDF
            : File(ExportadorExcel.Generar(reporte), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"pedidos_{fecha}.xlsx"); // Excel
    } // Fin del método

    // GET /Pedidos/Details/15 → detalle del pedido + productos + seguimiento externo + historial de estados
    public async Task<IActionResult> Details(int id) // "id" es el número de pedido
    { // Inicio del método
        var pedido = await _db.Pedidos.Include(p => p.Cliente).Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id); // Busca el pedido con su cliente y sus productos
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

    // GET /Pedidos/Create?clienteId=3 → formulario de nuevo pedido con productos
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> Create(int? clienteId) // clienteId es opcional
    { // Inicio del método
        await CargarListasAsync(clienteId); // Llena los combos de clientes y productos
        return View(new PedidoFormulario { ClienteId = clienteId ?? 0 }); // Formulario con un renglón vacío
    } // Fin del método

    // POST /Pedidos/Create → valida, descuenta stock y guarda el pedido con sus renglones (todo junto)
    [HttpPost] // Solo envíos de formulario
    [ValidateAntiForgeryToken] // Protección contra formularios falsos
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> Create(PedidoFormulario formulario) // Datos del formulario (cliente, renglones, descuentos)
    { // Inicio del método
        var cliente = await _db.Clientes.FindAsync(formulario.ClienteId); // Cliente elegido
        if (formulario.ClienteId > 0 && cliente is null) ModelState.AddModelError(nameof(formulario.ClienteId), "El cliente no existe"); // Cliente inexistente

        var renglones = formulario.Items.Where(i => i.ProductoId > 0).ToList(); // Ignora los renglones sin producto elegido
        if (renglones.Count == 0) ModelState.AddModelError(string.Empty, "Agregá al menos un producto"); // Pedido vacío
        var ids = renglones.Select(i => i.ProductoId).Distinct().ToList(); // Productos elegidos
        var productos = await _db.Productos.Where(p => ids.Contains(p.Id) && p.Activo).ToDictionaryAsync(p => p.Id); // Se leen de la base: el precio NUNCA se toma del navegador
        if (productos.Count != ids.Count) ModelState.AddModelError(string.Empty, "Uno de los productos no existe o está inactivo"); // Producto inválido

        if (!ModelState.IsValid) // Errores de validación
        { // Inicio del bloque de error
            await CargarListasAsync(formulario.ClienteId); // Volvemos a llenar los combos
            return View(formulario); // Mostramos el formulario con los mensajes
        } // Fin del bloque de error

        var pedido = new Pedido // Pedido nuevo
        { // Inicio de los datos
            ClienteId = formulario.ClienteId, // Cliente
            Cliente = cliente, // Navegación (la usa el email)
            Estado = EstadoPedido.Pendiente, // Todo pedido nuevo arranca Pendiente
            DescuentoPorcentaje = formulario.DescuentoPorcentaje, // Descuento general
            FechaCreacion = DateTime.UtcNow, // Fecha de creación
            FechaActualizacion = DateTime.UtcNow // Fecha de última actualización
        }; // Fin de los datos
        foreach (var r in renglones) // Un PedidoItem por renglón
            pedido.Items.Add(new PedidoItem // Renglón
            { // Inicio del renglón
                ProductoId = r.ProductoId, // Producto
                ProductoNombre = productos[r.ProductoId].Nombre, // Copia del nombre
                PrecioUnitario = productos[r.ProductoId].Precio, // Copia del precio ACTUAL de la base
                Cantidad = r.Cantidad, // Unidades
                DescuentoPorcentaje = r.DescuentoPorcentaje // Descuento del renglón
            }); // Fin del renglón
        pedido.Monto = Calculos.TotalPedido(pedido); // Total con descuentos
        pedido.Descripcion = string.IsNullOrWhiteSpace(formulario.Observaciones) ? Calculos.Resumen(pedido) : formulario.Observaciones.Trim(); // Observaciones o resumen automático

        var errores = await _stock.DescontarAsync(pedido, User.Identity!.Name!); // Descuenta stock (si algo no alcanza, no descuenta nada)
        if (errores.Count > 0) // Stock insuficiente
        { // Inicio del bloque
            foreach (var e in errores) ModelState.AddModelError(string.Empty, $"Stock insuficiente — {e}"); // Un mensaje por producto
            await CargarListasAsync(formulario.ClienteId); // Combos
            return View(formulario); // Volvemos con los mensajes (no se guardó nada)
        } // Fin del bloque

        _db.Pedidos.Add(pedido); // Pedido + renglones + movimientos de stock: se guardan en un solo SaveChanges
        _db.HistorialEstados.Add(new HistorialEstado { Pedido = pedido, EstadoNuevo = pedido.Estado, Origen = "Alta", Usuario = User.Identity!.Name! }); // Primer registro del historial
        try { await _db.SaveChangesAsync(); } // INSERTs + UPDATE del stock, todo o nada
        catch (DbUpdateConcurrencyException) // Otro pedido usó el mismo stock en el mismo instante
        { // Inicio del catch
            ModelState.AddModelError(string.Empty, "El stock de un producto cambió mientras cargabas el pedido (otro usuario vendió al mismo tiempo). Revisá las cantidades y volvé a guardar."); // Aviso
            _db.ChangeTracker.Clear(); // Descartamos lo que quedó a medio guardar
            await CargarListasAsync(formulario.ClienteId); // Combos con el stock actualizado
            return View(formulario); // Volvemos sin perder lo que escribió
        } // Fin del catch

        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Actividad, "Alta de pedido", $"Pedido #{pedido.Id} · {pedido.Items.Count} producto(s) · {Formato.Moneda(pedido.Monto)}", "Pedido", pedido.Id); // Bitácora
        _notificador.PedidoCreado(pedido); // Emails (cliente si aceptó, y equipo interno)
        _notificador.StockBajo(_stock.CruzaronMinimo); // Aviso si algún producto llegó al mínimo
        TempData["Mensaje"] = $"Pedido #{pedido.Id} creado"; // Mensaje de confirmación con el número asignado
        return RedirectToAction(nameof(Details), new { id = pedido.Id }); // Vamos al detalle del pedido nuevo
    } // Fin del método

    // POST /Pedidos/CambiarEstado/15 → cambia el estado desde la pantalla (devuelve o vuelve a descontar stock si corresponde)
    [HttpPost] // Solo envíos de formulario
    [ValidateAntiForgeryToken] // Protección contra formularios falsos
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> CambiarEstado(int id, EstadoPedido estado) // Recibe el pedido y el estado nuevo
    { // Inicio del método
        var pedido = await _db.Pedidos.Include(p => p.Cliente).Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id); // Busca el pedido con cliente y productos
        if (pedido is null) return NotFound(); // Si no existe, 404
        if (!Enum.IsDefined(estado)) return BadRequest("Estado inválido"); // Si mandaron un estado que no existe, error 400
        if (pedido.Estado == estado) // Si eligieron el mismo estado que ya tiene...
        { // Inicio del bloque
            TempData["Mensaje"] = "El pedido ya estaba en ese estado; no se registró ningún cambio."; // ...avisamos y no ensuciamos el historial
            return RedirectToAction(nameof(Details), new { id }); // ...y volvemos
        } // Fin del bloque

        var anterior = pedido.Estado; // Guardamos el estado viejo
        var errores = await _stock.AplicarCambioEstadoAsync(pedido, anterior, estado, User.Identity!.Name!); // Cancelar devuelve stock; reabrir vuelve a descontar
        if (errores.Count > 0) // No hay stock para reabrir
        { // Inicio del bloque
            TempData["Error"] = "No se puede reabrir el pedido por falta de stock: " + string.Join("; ", errores); // Explicación
            return RedirectToAction(nameof(Details), new { id }); // Volvemos sin cambios
        } // Fin del bloque
        pedido.Estado = estado; // Asigna el nuevo estado
        pedido.FechaActualizacion = DateTime.UtcNow; // Registra cuándo cambió
        _db.HistorialEstados.Add(new HistorialEstado { PedidoId = id, EstadoAnterior = anterior, EstadoNuevo = estado, Origen = "Manual", Usuario = User.Identity!.Name! }); // Historial
        _auditoria.Registrar(TipoRegistro.Actividad, "Cambio de estado", $"Pedido #{id}: {EstadoMapper.Nombre(anterior)} → {EstadoMapper.Nombre(estado)}", "Pedido", id); // Bitácora
        try { await _db.SaveChangesAsync(); } // UPDATE + INSERTs en una sola transacción
        catch (DbUpdateConcurrencyException) // Stock tocado al mismo tiempo por otro usuario
        { // Inicio del catch
            TempData["Error"] = "El stock cambió mientras hacías el cambio (otro usuario al mismo tiempo). Volvé a intentarlo."; // Aviso
            return RedirectToAction(nameof(Details), new { id }); // Volvemos
        } // Fin del catch
        _notificador.EstadoCambiado(pedido, anterior, estado); // Emails al cliente / equipo
        _notificador.StockBajo(_stock.CruzaronMinimo); // Aviso de stock bajo (al reabrir puede pasar)
        TempData["Mensaje"] = $"Estado cambiado a {EstadoMapper.Nombre(estado)}" + (pedido.Items.Count > 0 && (estado == EstadoPedido.Cancelado || anterior == EstadoPedido.Cancelado) ? (estado == EstadoPedido.Cancelado ? ". El stock se devolvió." : ". El stock se volvió a descontar.") : ""); // Mensaje de confirmación
        return RedirectToAction(nameof(Details), new { id }); // Vuelve al detalle
    } // Fin del método

    // ---------- Auxiliares ----------

    private static string? ValidarFechas(DateTime? desde, DateTime? hasta) // Devuelve un error si "Desde" es posterior a "Hasta"
        => desde.HasValue && hasta.HasValue && desde.Value.Date > hasta.Value.Date // ¿Rango al revés?
            ? $"La fecha \"Desde\" ({desde:dd/MM/yyyy}) no puede ser posterior a la fecha \"Hasta\" ({hasta:dd/MM/yyyy}). Corregí el rango." // Mensaje claro
            : null; // Sin error

    private IQueryable<Pedido> Filtrar(string? q, EstadoPedido? estado, DateTime? desde, DateTime? hasta) // Filtros comunes al listado y a la exportación
    { // Inicio del método
        var consulta = _db.Pedidos.Include(p => p.Cliente).AsQueryable(); // Consulta de pedidos trayendo también el cliente (para mostrar su nombre)
        if (!string.IsNullOrWhiteSpace(q)) // Si escribieron algo en el buscador...
        { // Inicio del filtro de texto
            var texto = q.Trim().ToLower(); // Normalizamos el texto
            var esNumero = int.TryParse(texto, out var numero); // Si escribieron un número, también buscamos por N° de pedido
            consulta = consulta.Where(p => // Condición WHERE
                (esNumero && p.Id == numero) || // Coincide el número de pedido, o...
                p.Descripcion.ToLower().Contains(texto) || // ...la descripción (o el resumen de productos) contiene el texto, o...
                p.Cliente!.Nombre.ToLower().Contains(texto)); // ...el nombre del cliente lo contiene
        } // Fin del filtro de texto
        if (estado.HasValue) consulta = consulta.Where(p => p.Estado == estado.Value); // Filtro por estado
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
        return consulta; // Consulta armada (todavía no ejecutada)
    } // Fin del método

    private async Task CargarListasAsync(int? clienteSeleccionado) // Combos del formulario de pedido
    { // Inicio del método
        var clientes = await _db.Clientes.OrderBy(c => c.Nombre).ToListAsync(); // Todos los clientes ordenados por nombre
        ViewBag.Clientes = new SelectList(clientes, nameof(Cliente.Id), nameof(Cliente.Nombre), clienteSeleccionado); // Valor = Id, texto visible = Nombre
        ViewBag.Productos = await _db.Productos.Where(p => p.Activo).OrderBy(p => p.Nombre).ToListAsync(); // Productos activos (la vista usa precio y stock para calcular en pantalla)
    } // Fin del método
} // Fin de la clase

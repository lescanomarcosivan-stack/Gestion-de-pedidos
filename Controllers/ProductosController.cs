using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Producto, Roles, ViewModels
using GestionPedidos.Services; // Para IStockService, IAuditoria, INotificador, exportadores
using Microsoft.AspNetCore.Authorization; // Para [Authorize]
using Microsoft.AspNetCore.Mvc; // Para Controller
using Microsoft.EntityFrameworkCore; // Para ToListAsync, AnyAsync, DbUpdateConcurrencyException

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Catálogo de productos y control de stock: listar, crear, editar, ingresos/ajustes y exportar
public class ProductosController : Controller // Atiende /Productos/...
{ // Inicio de la clase
    private readonly AppDbContext _db; // Acceso a la base
    private readonly IStockService _stock; // Movimientos de stock
    private readonly IAuditoria _auditoria; // Bitácora
    private readonly INotificador _notificador; // Avisos por email

    public ProductosController(AppDbContext db, IStockService stock, IAuditoria auditoria, INotificador notificador) // Inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos la base
        _stock = stock; // Guardamos el servicio de stock
        _auditoria = auditoria; // Guardamos la bitácora
        _notificador = notificador; // Guardamos el notificador
    } // Fin del constructor

    // GET /Productos?q=texto&stockBajo=true&inactivos=true
    public async Task<IActionResult> Index(string? q, bool stockBajo = false, bool inactivos = false) // Filtros opcionales
    { // Inicio del método
        var productos = await Filtrar(q, stockBajo, inactivos).OrderBy(p => p.Nombre).ToListAsync(); // Consulta con filtros
        ViewBag.Q = q; // Mantener el texto buscado
        ViewBag.StockBajo = stockBajo; // Mantener el filtro
        ViewBag.Inactivos = inactivos; // Mantener el filtro
        ViewBag.HayFiltros = !string.IsNullOrWhiteSpace(q) || stockBajo || inactivos; // ¿Hay filtros? (para "Limpiar filtros")
        ViewBag.ExistenProductos = productos.Count > 0 || await _db.Productos.AnyAsync(); // Distingue "no hay productos" de "sin resultados"
        ViewBag.CantidadStockBajo = await _db.Productos.CountAsync(p => p.Activo && p.Stock <= p.StockMinimo); // Para el aviso de arriba
        return View(productos); // Views/Productos/Index.cshtml
    } // Fin del método

    // GET /Productos/Details/3 → ficha con movimientos y formulario de ingreso/ajuste
    public async Task<IActionResult> Details(int id) // Id del producto
    { // Inicio del método
        var modelo = await ArmarDetalleAsync(id); // Arma el ViewModel
        return modelo is null ? NotFound() : View(modelo); // 404 si no existe
    } // Fin del método

    // GET /Productos/Create → formulario vacío
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public IActionResult Create() => View("Formulario", new Producto()); // Formulario compartido alta/edición

    // POST /Productos/Create → guarda el producto nuevo
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Edicion)] // Formulario, token anti-falsificación y rol
    public async Task<IActionResult> Create([Bind("Codigo,Nombre,Precio,Stock,StockMinimo,Activo")] Producto producto) // Campos aceptados
    { // Inicio del método
        producto.Codigo = producto.Codigo.Trim().ToUpperInvariant(); // Código en mayúsculas, sin espacios
        if (await _db.Productos.AnyAsync(p => p.Codigo == producto.Codigo)) // Código repetido
            ModelState.AddModelError(nameof(Producto.Codigo), "Ya existe un producto con ese código"); // Error en el campo
        if (producto.Stock < 0) ModelState.AddModelError(nameof(Producto.Stock), "El stock inicial no puede ser negativo"); // Validación
        if (!ModelState.IsValid) return View("Formulario", producto); // Volvemos con errores

        var stockInicial = producto.Stock; // Guardamos el stock indicado
        producto.Stock = 0; // Arranca en 0 y el stock inicial entra como movimiento (así el historial cierra)
        _db.Productos.Add(producto); // Marca para insertar
        if (stockInicial > 0) await _stock.MoverAsync(producto, TipoMovimiento.Ingreso, stockInicial, "Stock inicial", User.Identity!.Name!); // Ingreso inicial
        _auditoria.Registrar(TipoRegistro.Actividad, "Alta de producto", $"{producto.Codigo} · {producto.Nombre} · {Formato.Moneda(producto.Precio)}", "Producto", null); // Bitácora
        await _db.SaveChangesAsync(); // Guarda producto + movimiento + bitácora juntos
        TempData["Mensaje"] = $"Producto {producto.Codigo} creado"; // Aviso
        return RedirectToAction(nameof(Details), new { id = producto.Id }); // A la ficha
    } // Fin del método

    // GET /Productos/Edit/3 → formulario con los datos actuales
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> Edit(int id) // Id del producto
    { // Inicio del método
        var producto = await _db.Productos.FindAsync(id); // Busca el producto
        return producto is null ? NotFound() : View("Formulario", producto); // Formulario o 404
    } // Fin del método

    // POST /Productos/Edit/3 → guarda cambios (el stock NO se edita acá: se mueve con ingresos y ajustes)
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Edicion)] // Formulario, token y rol
    public async Task<IActionResult> Edit(int id, [Bind("Codigo,Nombre,Precio,StockMinimo,Activo,Version")] Producto datos) // "Version" viaja oculto: detecta si otro usuario lo cambió mientras tanto
    { // Inicio del método
        var producto = await _db.Productos.FindAsync(id); // Producto actual
        if (producto is null) return NotFound(); // 404
        datos.Codigo = datos.Codigo.Trim().ToUpperInvariant(); // Normalizamos el código
        if (await _db.Productos.AnyAsync(p => p.Codigo == datos.Codigo && p.Id != id)) // Código usado por otro producto
            ModelState.AddModelError(nameof(Producto.Codigo), "Ya existe otro producto con ese código"); // Error
        ModelState.Remove(nameof(Producto.Stock)); // El stock no viaja en este formulario
        if (!ModelState.IsValid) { datos.Id = id; datos.Stock = producto.Stock; return View("Formulario", datos); } // Volvemos con errores

        _db.Entry(producto).Property(p => p.Version).OriginalValue = datos.Version; // Le decimos a EF qué versión vio el usuario: si cambió en la base, falla al guardar
        var cambioPrecio = producto.Precio != datos.Precio ? $" | Precio: {Formato.Moneda(producto.Precio)} → {Formato.Moneda(datos.Precio)}" : ""; // Detalle para la bitácora
        producto.Codigo = datos.Codigo; // Copiamos los campos editables
        producto.Nombre = datos.Nombre; // Nombre
        producto.Precio = datos.Precio; // Precio (los pedidos ya hechos conservan su precio)
        producto.StockMinimo = datos.StockMinimo; // Stock mínimo
        producto.Activo = datos.Activo; // Activo / inactivo
        _auditoria.Registrar(TipoRegistro.Actividad, "Edición de producto", $"{producto.Codigo}{cambioPrecio}", "Producto", id); // Bitácora
        try { await _db.SaveChangesAsync(); } // UPDATE (con control de versión)
        catch (DbUpdateConcurrencyException) // Otro usuario lo modificó mientras este editaba
        { // Inicio del catch
            TempData["Error"] = "Otro usuario modificó este producto mientras lo editabas. Revisá los datos actuales y volvé a intentar."; // Aviso claro
            return RedirectToAction(nameof(Edit), new { id }); // Recarga el formulario con los datos nuevos
        } // Fin del catch
        TempData["Mensaje"] = "Producto actualizado"; // Aviso
        return RedirectToAction(nameof(Details), new { id }); // A la ficha
    } // Fin del método

    // POST /Productos/Mover/3 → ingreso o ajuste de stock
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Edicion)] // Formulario, token y rol
    public async Task<IActionResult> Mover(int id, [Bind(Prefix = "Movimiento")] MovimientoFormulario formulario) // Prefix: los campos vienen como Movimiento.Cantidad, etc.
    { // Inicio del método
        var producto = await _db.Productos.FindAsync(id); // Producto
        if (producto is null) return NotFound(); // 404
        var error = ModelState.IsValid ? await _stock.MoverAsync(producto, formulario.Tipo, formulario.Cantidad, formulario.Motivo?.Trim(), User.Identity!.Name!) : "Datos inválidos"; // Aplica el movimiento (o devuelve por qué no)
        if (error is not null) // No se pudo
        { // Inicio del bloque
            TempData["Error"] = error; // Mensaje en rojo
            return RedirectToAction(nameof(Details), new { id }); // Volvemos a la ficha
        } // Fin del bloque
        _auditoria.Registrar(TipoRegistro.Actividad, $"Stock: {formulario.Tipo}", $"{producto.Codigo}: {(formulario.Cantidad > 0 ? "+" : "")}{formulario.Cantidad} → queda {producto.Stock}. {formulario.Motivo}", "Producto", id); // Bitácora
        try { await _db.SaveChangesAsync(); } // Guarda stock + movimiento + bitácora (con control de versión)
        catch (DbUpdateConcurrencyException) // Alguien movió el stock al mismo tiempo
        { // Inicio del catch
            TempData["Error"] = "El stock cambió mientras hacías el movimiento (otro usuario o un pedido). Revisá el valor actual y repetí la operación."; // Aviso
            return RedirectToAction(nameof(Details), new { id }); // Volvemos
        } // Fin del catch
        _notificador.StockBajo(_stock.CruzaronMinimo); // Si quedó en el mínimo, avisa por email
        TempData["Mensaje"] = $"Stock actualizado: ahora hay {producto.Stock}"; // Aviso
        return RedirectToAction(nameof(Details), new { id }); // A la ficha
    } // Fin del método

    // GET /Productos/Exportar?formato=excel|pdf&... → reporte de stock con los mismos filtros de la pantalla
    public async Task<IActionResult> Exportar(string formato, string? q, bool stockBajo = false, bool inactivos = false) // Formato y filtros
    { // Inicio del método
        var productos = await Filtrar(q, stockBajo, inactivos).OrderBy(p => p.Nombre).ToListAsync(); // Mismos datos que la pantalla
        var reporte = new Reporte // Armamos el reporte genérico
        { // Inicio de los datos
            Titulo = "Reporte de stock", // Título
            Subtitulo = $"Generado el {Formato.Fecha(DateTime.UtcNow)} por {User.Identity!.Name}" + (stockBajo ? " · Solo stock bajo" : "") + (string.IsNullOrWhiteSpace(q) ? "" : $" · Búsqueda: \"{q}\""), // Fecha, usuario y filtros
            Columnas = new() // Columnas
            { // Inicio de las columnas
                new("Código", TipoColumna.Texto, 1), // Código
                new("Producto", TipoColumna.Texto, 3), // Nombre
                new("Precio", TipoColumna.Moneda, 1.3), // Precio unitario
                new("Stock", TipoColumna.Entero, 0.8), // Unidades
                new("Mínimo", TipoColumna.Entero, 0.8), // Stock mínimo
                new("Valorizado", TipoColumna.Moneda, 1.5), // Precio × stock
                new("Estado", TipoColumna.Texto, 1) // OK / Stock bajo / Inactivo
            }, // Fin de las columnas
            Filas = productos.Select(p => new object?[] { p.Codigo, p.Nombre, p.Precio, p.Stock, p.StockMinimo, p.Precio * p.Stock, !p.Activo ? "Inactivo" : p.StockBajo ? "Stock bajo" : "OK" }).ToList(), // Una fila por producto
            Totales = new object?[] { "Total", $"{productos.Count} producto(s)", null, productos.Sum(p => p.Stock), null, productos.Sum(p => p.Precio * p.Stock), null } // Totales
        }; // Fin del reporte
        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Actividad, "Exportación de stock", formato.ToUpperInvariant()); // Bitácora
        return Archivo(reporte, formato, "stock"); // Devuelve el archivo
    } // Fin del método

    // ---------- Auxiliares ----------

    private IQueryable<Producto> Filtrar(string? q, bool stockBajo, bool inactivos) // Filtros comunes a la pantalla y al reporte
    { // Inicio del método
        var consulta = _db.Productos.AsQueryable(); // Todos los productos
        if (!inactivos) consulta = consulta.Where(p => p.Activo); // Por defecto, solo activos
        if (stockBajo) consulta = consulta.Where(p => p.Stock <= p.StockMinimo); // Solo los que hay que reponer
        if (!string.IsNullOrWhiteSpace(q)) // Búsqueda por texto
        { // Inicio del filtro
            var texto = q.Trim().ToLower(); // Normalizamos
            consulta = consulta.Where(p => p.Nombre.ToLower().Contains(texto) || p.Codigo.ToLower().Contains(texto)); // Nombre o código
        } // Fin del filtro
        return consulta; // Consulta armada (todavía no ejecutada)
    } // Fin del método

    private async Task<ProductoDetalleViewModel?> ArmarDetalleAsync(int id) // Datos de la ficha
    { // Inicio del método
        var producto = await _db.Productos.FindAsync(id); // Producto
        if (producto is null) return null; // No existe
        return new ProductoDetalleViewModel // ViewModel
        { // Inicio de los datos
            Producto = producto, // Producto
            Movimientos = await _db.MovimientosStock.Where(m => m.ProductoId == id).OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id).Take(50).ToListAsync(), // Últimos 50 movimientos
            Movimiento = new MovimientoFormulario { ProductoId = id } // Formulario vacío
        }; // Fin de los datos
    } // Fin del método

    private FileContentResult Archivo(Reporte reporte, string formato, string nombre) // Devuelve Excel o PDF según lo pedido
    { // Inicio del método
        var fecha = DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-dd"); // Fecha para el nombre del archivo
        return formato == "pdf" // ¿PDF?
            ? File(ExportadorPdf.Generar(reporte), "application/pdf", $"{nombre}_{fecha}.pdf") // PDF
            : File(ExportadorExcel.Generar(reporte), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{nombre}_{fecha}.xlsx"); // Excel
    } // Fin del método
} // Fin de la clase

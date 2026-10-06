using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Cliente, EstadoPedido, Roles y ClienteListadoViewModel
using GestionPedidos.Services; // Para IAuditoria (bitácora de cambios)
using Microsoft.AspNetCore.Authorization; // Para [Authorize] (permisos por rol)
using Microsoft.AspNetCore.Mvc; // Para Controller, IActionResult, [HttpPost], etc.
using Microsoft.EntityFrameworkCore; // Para ToListAsync, Include, FirstOrDefaultAsync, AnyAsync

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Controlador de clientes: atiende las URLs /Clientes/... (listar, ver, crear, editar)
// Ver: cualquier usuario logueado (regla general de Program.cs). Crear/editar: solo Administrador u Operador
public class ClientesController : Controller // Hereda de Controller: puede devolver vistas (páginas HTML)
{ // Inicio de la clase
    private readonly AppDbContext _db; // Acceso a la base de datos
    private readonly IAuditoria _auditoria; // Bitácora: registra quién cambió qué

    public ClientesController(AppDbContext db, IAuditoria auditoria) // ASP.NET Core entrega estos objetos automáticamente (inyección de dependencias)
    { // Inicio del constructor
        _db = db; // Guardamos la base
        _auditoria = auditoria; // Guardamos la bitácora
    } // Fin del constructor

    // GET /Clientes?q=texto&ciudad=xxx → listado con búsqueda y filtro
    public async Task<IActionResult> Index(string? q, string? ciudad) // Los parámetros vienen de la URL (?q=...&ciudad=...)
    { // Inicio del método
        var consulta = _db.Clientes.AsQueryable(); // Arranca la consulta sobre la tabla Clientes (todavía no va a la base)

        if (!string.IsNullOrWhiteSpace(q)) // Si escribieron algo en el buscador...
        { // Inicio del filtro de texto
            var texto = q.Trim().ToLower(); // Sacamos espacios y pasamos a minúsculas para comparar sin importar mayúsculas
            consulta = consulta.Where(c => // Agregamos la condición WHERE
                c.Nombre.ToLower().Contains(texto) || // El nombre contiene el texto, o...
                c.Email.ToLower().Contains(texto) || // ...el email lo contiene, o...
                (c.Telefono != null && c.Telefono.Contains(texto))); // ...el teléfono lo contiene
        } // Fin del filtro de texto

        if (!string.IsNullOrWhiteSpace(ciudad)) // Si eligieron una ciudad en el combo...
            consulta = consulta.Where(c => c.Ciudad == ciudad); // ...filtramos por esa ciudad exacta

        var clientes = await consulta // Tomamos la consulta armada...
            .OrderBy(c => c.Nombre) // ...ordenada alfabéticamente...
            .Select(c => new ClienteListadoViewModel // ...y traemos solo las columnas que la pantalla necesita
            { // Inicio de la proyección
                Id = c.Id, // Id
                Nombre = c.Nombre, // Nombre
                Email = c.Email, // Email
                Telefono = c.Telefono, // Teléfono
                Ciudad = c.Ciudad, // Ciudad
                CantidadPedidos = c.Pedidos.Count, // PostgreSQL cuenta los pedidos (no los trae todos)
                PedidosAbiertos = c.Pedidos.Count(p => p.Estado != EstadoPedido.Entregado && p.Estado != EstadoPedido.Cancelado) // Cuenta los que siguen en curso
            }) // Fin de la proyección
            .ToListAsync(); // Recién acá se ejecuta el SELECT en la base

        ViewBag.HayFiltros = !string.IsNullOrWhiteSpace(q) || !string.IsNullOrWhiteSpace(ciudad); // true si hay algún filtro aplicado (muestra "Limpiar filtros")
        ViewBag.ExistenClientes = clientes.Count > 0 || await _db.Clientes.AnyAsync(); // Distingue "no hay clientes cargados" de "el filtro no encontró nada"
        ViewBag.Q = q; // Devolvemos el texto buscado para que quede escrito en el buscador
        ViewBag.Ciudad = ciudad; // Devolvemos la ciudad elegida para que quede seleccionada
        ViewBag.Ciudades = await _db.Clientes // Lista de ciudades para el combo de filtro
            .Where(c => c.Ciudad != null && c.Ciudad != "") // Solo clientes que tengan ciudad cargada
            .Select(c => c.Ciudad!) // Nos quedamos solo con la ciudad
            .Distinct() // Sin repetidos
            .OrderBy(c => c) // Ordenadas alfabéticamente
            .ToListAsync(); // Ejecuta la consulta
        return View(clientes); // Muestra Views/Clientes/Index.cshtml con la lista
    } // Fin del método

    // GET /Clientes/Details/5 → ficha del cliente con todos sus pedidos y su historial de cambios
    public async Task<IActionResult> Details(int id) // "id" viene de la URL
    { // Inicio del método
        var cliente = await _db.Clientes // Buscamos en la tabla Clientes...
            .Include(c => c.Pedidos) // ...trayendo también sus pedidos (JOIN)
            .FirstOrDefaultAsync(c => c.Id == id); // ...el que tenga ese Id (o null si no existe)
        if (cliente is null) return NotFound(); // Si no existe, devolvemos error 404
        cliente.Pedidos = cliente.Pedidos.OrderByDescending(p => p.FechaCreacion).ToList(); // Ordenamos los pedidos del más nuevo al más viejo
        ViewBag.Cambios = await _db.RegistrosActividad // Historial de cambios de este cliente (sale de la bitácora)
            .Where(r => r.EntidadTipo == "Cliente" && r.EntidadId == id) // Solo los registros de este cliente
            .OrderByDescending(r => r.Fecha) // Más nuevos primero
            .Take(20) // Los últimos 20
            .ToListAsync(); // Ejecuta la consulta
        return View(cliente); // Muestra Views/Clientes/Details.cshtml
    } // Fin del método

    // GET /Clientes/Create → muestra el formulario vacío
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public IActionResult Create() => View("Formulario", new Cliente()); // Reutiliza la vista "Formulario" con un cliente nuevo (Id = 0)

    // POST /Clientes/Create → recibe el formulario y guarda el cliente nuevo
    [HttpPost] // Este método solo responde a envíos de formulario (POST)
    [ValidateAntiForgeryToken] // Seguridad: verifica que el formulario venga de nuestra propia página (evita ataques CSRF)
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> Create([Bind("Nombre,Email,Telefono,Ciudad")] Cliente cliente) // [Bind] = solo aceptamos estos campos (evita que nos inyecten otros)
    { // Inicio del método
        if (await _db.Clientes.AnyAsync(c => c.Email == cliente.Email)) // Si ya existe un cliente con ese email...
            ModelState.AddModelError(nameof(Cliente.Email), "Ya existe un cliente con ese email"); // ...agregamos un error al campo Email

        if (!ModelState.IsValid) return View("Formulario", cliente); // Si hay errores de validación, volvemos a mostrar el formulario con los mensajes

        cliente.FechaAlta = DateTime.UtcNow; // Registramos la fecha de alta
        _db.Clientes.Add(cliente); // Marcamos el cliente para insertarlo
        await _db.SaveChangesAsync(); // Ejecuta el INSERT en PostgreSQL (ahora el cliente tiene Id)
        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Actividad, "Alta de cliente", $"{cliente.Nombre} ({cliente.Email})", "Cliente", cliente.Id); // Bitácora
        TempData["Mensaje"] = "Cliente creado correctamente"; // Mensaje que se muestra una sola vez en la próxima página
        return RedirectToAction(nameof(Details), new { id = cliente.Id }); // Redirige a la ficha del cliente recién creado
    } // Fin del método

    // GET /Clientes/Edit/5 → muestra el formulario con los datos actuales
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> Edit(int id) // "id" viene de la URL
    { // Inicio del método
        var cliente = await _db.Clientes.FindAsync(id); // Busca por clave primaria
        if (cliente is null) return NotFound(); // Si no existe, 404
        return View("Formulario", cliente); // Muestra el mismo formulario, pero lleno
    } // Fin del método

    // POST /Clientes/Edit/5 → guarda los cambios
    [HttpPost] // Solo para envíos de formulario
    [ValidateAntiForgeryToken] // Protección contra formularios falsos
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> Edit(int id, [Bind("Nombre,Email,Telefono,Ciudad")] Cliente datos) // "datos" trae lo que escribió el usuario
    { // Inicio del método
        var cliente = await _db.Clientes.FindAsync(id); // Buscamos el cliente original en la base
        if (cliente is null) return NotFound(); // Si no existe, 404

        if (await _db.Clientes.AnyAsync(c => c.Email == datos.Email && c.Id != id)) // Si OTRO cliente ya usa ese email...
            ModelState.AddModelError(nameof(Cliente.Email), "Ya existe otro cliente con ese email"); // ...error en el campo Email

        if (!ModelState.IsValid) // Si hay errores...
        { // Inicio del bloque de error
            datos.Id = id; // Conservamos el Id para que el formulario sepa que es una edición
            return View("Formulario", datos); // Volvemos a mostrar el formulario con los mensajes
        } // Fin del bloque de error

        var cambios = new List<string>(); // Lista de cambios para la bitácora (ej. "Email: 'a@x.com' → 'b@x.com'")
        void Comparar(string campo, string? antes, string? despues) { if ((antes ?? "") != (despues ?? "")) cambios.Add($"{campo}: '{antes}' → '{despues}'"); } // Función local: anota el campo si cambió
        Comparar("Nombre", cliente.Nombre, datos.Nombre); // Compara nombre
        Comparar("Email", cliente.Email, datos.Email); // Compara email
        Comparar("Teléfono", cliente.Telefono, datos.Telefono); // Compara teléfono
        Comparar("Ciudad", cliente.Ciudad, datos.Ciudad); // Compara ciudad

        cliente.Nombre = datos.Nombre; // Copiamos los campos editables al cliente original
        cliente.Email = datos.Email; // Email nuevo
        cliente.Telefono = datos.Telefono; // Teléfono nuevo
        cliente.Ciudad = datos.Ciudad; // Ciudad nueva
        if (cambios.Count > 0) // Solo si realmente cambió algo...
            _auditoria.Registrar(TipoRegistro.Actividad, "Edición de cliente", string.Join(" | ", cambios), "Cliente", id); // ...lo registramos (se guarda junto con el UPDATE)
        await _db.SaveChangesAsync(); // EF detecta qué cambió y ejecuta el UPDATE (y el INSERT de la bitácora)
        TempData["Mensaje"] = cambios.Count > 0 ? "Cliente actualizado" : "No había cambios para guardar"; // Mensaje de confirmación
        return RedirectToAction(nameof(Details), new { id }); // Volvemos a la ficha del cliente
    } // Fin del método
} // Fin de la clase

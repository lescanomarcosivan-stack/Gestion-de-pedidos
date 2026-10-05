using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Cliente, EstadoPedido y ClienteListadoViewModel
using Microsoft.AspNetCore.Mvc; // Para Controller, IActionResult, [HttpPost], etc.
using Microsoft.EntityFrameworkCore; // Para ToListAsync, Include, FirstOrDefaultAsync, AnyAsync

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Controlador de clientes: atiende las URLs /Clientes/... (listar, ver, crear, editar)
public class ClientesController : Controller // Hereda de Controller: puede devolver vistas (páginas HTML)
{ // Inicio de la clase
    private readonly AppDbContext _db; // Acceso a la base de datos

    public ClientesController(AppDbContext db) => _db = db; // ASP.NET Core entrega el DbContext automáticamente (inyección de dependencias)

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

    // GET /Clientes/Details/5 → ficha del cliente con todos sus pedidos
    public async Task<IActionResult> Details(int id) // "id" viene de la URL
    { // Inicio del método
        var cliente = await _db.Clientes // Buscamos en la tabla Clientes...
            .Include(c => c.Pedidos) // ...trayendo también sus pedidos (JOIN)
            .FirstOrDefaultAsync(c => c.Id == id); // ...el que tenga ese Id (o null si no existe)
        if (cliente is null) return NotFound(); // Si no existe, devolvemos error 404
        cliente.Pedidos = cliente.Pedidos.OrderByDescending(p => p.FechaCreacion).ToList(); // Ordenamos los pedidos del más nuevo al más viejo
        return View(cliente); // Muestra Views/Clientes/Details.cshtml
    } // Fin del método

    // GET /Clientes/Create → muestra el formulario vacío
    public IActionResult Create() => View("Formulario", new Cliente()); // Reutiliza la vista "Formulario" con un cliente nuevo (Id = 0)

    // POST /Clientes/Create → recibe el formulario y guarda el cliente nuevo
    [HttpPost] // Este método solo responde a envíos de formulario (POST)
    [ValidateAntiForgeryToken] // Seguridad: verifica que el formulario venga de nuestra propia página (evita ataques CSRF)
    public async Task<IActionResult> Create([Bind("Nombre,Email,Telefono,Ciudad")] Cliente cliente) // [Bind] = solo aceptamos estos campos (evita que nos inyecten otros)
    { // Inicio del método
        if (await _db.Clientes.AnyAsync(c => c.Email == cliente.Email)) // Si ya existe un cliente con ese email...
            ModelState.AddModelError(nameof(Cliente.Email), "Ya existe un cliente con ese email"); // ...agregamos un error al campo Email

        if (!ModelState.IsValid) return View("Formulario", cliente); // Si hay errores de validación, volvemos a mostrar el formulario con los mensajes

        cliente.FechaAlta = DateTime.UtcNow; // Registramos la fecha de alta
        _db.Clientes.Add(cliente); // Marcamos el cliente para insertarlo
        await _db.SaveChangesAsync(); // Ejecuta el INSERT en PostgreSQL
        TempData["Mensaje"] = "Cliente creado correctamente"; // Mensaje que se muestra una sola vez en la próxima página
        return RedirectToAction(nameof(Details), new { id = cliente.Id }); // Redirige a la ficha del cliente recién creado
    } // Fin del método

    // GET /Clientes/Edit/5 → muestra el formulario con los datos actuales
    public async Task<IActionResult> Edit(int id) // "id" viene de la URL
    { // Inicio del método
        var cliente = await _db.Clientes.FindAsync(id); // Busca por clave primaria
        if (cliente is null) return NotFound(); // Si no existe, 404
        return View("Formulario", cliente); // Muestra el mismo formulario, pero lleno
    } // Fin del método

    // POST /Clientes/Edit/5 → guarda los cambios
    [HttpPost] // Solo para envíos de formulario
    [ValidateAntiForgeryToken] // Protección contra formularios falsos
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

        cliente.Nombre = datos.Nombre; // Copiamos los campos editables al cliente original
        cliente.Email = datos.Email; // Email nuevo
        cliente.Telefono = datos.Telefono; // Teléfono nuevo
        cliente.Ciudad = datos.Ciudad; // Ciudad nueva
        await _db.SaveChangesAsync(); // EF detecta qué cambió y ejecuta el UPDATE
        TempData["Mensaje"] = "Cliente actualizado"; // Mensaje de confirmación
        return RedirectToAction(nameof(Details), new { id }); // Volvemos a la ficha del cliente
    } // Fin del método
} // Fin de la clase

using GestionPedidos.Data; // Para AppDbContext
using Microsoft.AspNetCore.Mvc; // Para Controller
using Microsoft.EntityFrameworkCore; // Para ToListAsync

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Páginas generales: inicio, historial de webhooks y página de error
public class HomeController : Controller // Hereda de Controller
{ // Inicio de la clase
    private readonly AppDbContext _db; // Acceso a la base

    public HomeController(AppDbContext db) => _db = db; // Inyección de dependencias

    public IActionResult Index() => RedirectToAction("Index", "Clientes"); // GET /Home → redirige al listado de clientes

    // GET /Home/Webhooks → últimos 100 avisos recibidos (útil en la revisión para ver que llegaron)
    public async Task<IActionResult> Webhooks() // Método asíncrono
    { // Inicio del método
        var eventos = await _db.EventosWebhook // Tabla de eventos...
            .OrderByDescending(e => e.FechaRecepcion) // ...del más reciente al más viejo...
            .Take(100) // ...solo los últimos 100...
            .ToListAsync(); // ...ejecuta la consulta
        return View(eventos); // Muestra Views/Home/Webhooks.cshtml
    } // Fin del método

    public IActionResult Error() => View(); // GET /Home/Error → página de error genérica
} // Fin de la clase

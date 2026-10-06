using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para TipoRegistro, Roles e InfoPaginacion
using Microsoft.AspNetCore.Authorization; // Para [Authorize]
using Microsoft.AspNetCore.Mvc; // Para Controller
using Microsoft.EntityFrameworkCore; // Para CountAsync y ToListAsync

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Bitácora: accesos, actividad, webhooks y errores. Solo administradores
[Authorize(Roles = Roles.Administrador)] // Exige rol Administrador
public class RegistroController : Controller // Atiende /Registro
{ // Inicio de la clase
    private const int TamanoPagina = 25; // Filas por página
    private readonly AppDbContext _db; // Acceso a la base

    public RegistroController(AppDbContext db) => _db = db; // Inyección de dependencias

    // GET /Registro?tipo=Error&q=texto&pagina=2
    public async Task<IActionResult> Index(TipoRegistro? tipo, string? q, int pagina = 1) // Filtros opcionales
    { // Inicio del método
        var consulta = _db.RegistrosActividad.AsQueryable(); // Consulta sobre la bitácora
        if (tipo.HasValue) consulta = consulta.Where(r => r.Tipo == tipo.Value); // Filtro por tipo
        if (!string.IsNullOrWhiteSpace(q)) // Filtro de texto
        { // Inicio del filtro
            var texto = q.Trim().ToLower(); // Normalizamos
            consulta = consulta.Where(r => r.Usuario.ToLower().Contains(texto) || r.Accion.ToLower().Contains(texto) || (r.Detalle != null && r.Detalle.ToLower().Contains(texto))); // Busca en usuario, acción o detalle
        } // Fin del filtro

        var total = await consulta.CountAsync(); // Cantidad total que cumple los filtros
        var paginacion = new InfoPaginacion { TamanoPagina = TamanoPagina, Total = total }; // Datos de paginación
        paginacion.Pagina = Math.Clamp(pagina, 1, paginacion.TotalPaginas); // Página válida (entre 1 y la última)
        var registros = await consulta // Filas de la página actual
            .OrderByDescending(r => r.Fecha) // Más nuevos primero
            .Skip((paginacion.Pagina - 1) * TamanoPagina) // Salteamos las páginas anteriores
            .Take(TamanoPagina) // Tomamos una página
            .ToListAsync(); // Ejecuta la consulta

        ViewBag.Tipo = tipo; // Para mantener el filtro elegido
        ViewBag.Q = q; // Para mantener el texto buscado
        ViewBag.Paginacion = paginacion; // Para la vista parcial de paginación
        return View(registros); // Views/Registro/Index.cshtml
    } // Fin del método
} // Fin de la clase

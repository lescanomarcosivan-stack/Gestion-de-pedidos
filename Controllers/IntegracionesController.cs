using System.Security.Cryptography; // Para generar el código "state" al azar
using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Roles y TipoRegistro
using GestionPedidos.Services; // Para IGoogleEmpresa, ColaCalendario e IAuditoria
using Microsoft.AspNetCore.Authorization; // Para [Authorize]
using Microsoft.AspNetCore.Mvc; // Para Controller
using Microsoft.EntityFrameworkCore; // Para ToListAsync

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Pantalla para conectar la cuenta de Google de la EMPRESA (Drive y Calendar). Solo administradores
[Authorize(Roles = Roles.Administrador)] // Toda la clase exige rol Administrador
public class IntegracionesController : Controller // Hereda de Controller
{ // Inicio de la clase
    private const string CookieEstado = "gp_google_estado"; // Cookie temporal con el código "state" (protección CSRF del ida y vuelta)
    private readonly IGoogleEmpresa _google; // Servicio de Google
    private readonly AppDbContext _db; // Base
    private readonly ColaCalendario _calendario; // Cola de Calendar
    private readonly IAuditoria _auditoria; // Bitácora

    public IntegracionesController(IGoogleEmpresa google, AppDbContext db, ColaCalendario calendario, IAuditoria auditoria) // Inyección de dependencias
    { // Inicio del constructor
        _google = google; // Guardamos el servicio
        _db = db; // Guardamos la base
        _calendario = calendario; // Guardamos la cola
        _auditoria = auditoria; // Guardamos la bitácora
    } // Fin del constructor

    private string UrlRetorno => $"{Request.Scheme}://{Request.Host}/Integraciones/Callback"; // A dónde vuelve Google (debe estar cargada en Google Cloud tal cual)

    // GET /Integraciones → estado de la conexión
    public async Task<IActionResult> Index() // Pantalla principal
    { // Inicio del método
        ViewBag.Configurado = _google.Configurado; // ¿Están las credenciales de Google Cloud?
        ViewBag.UrlRetorno = UrlRetorno; // Se muestra para copiarla en Google Cloud
        ViewBag.Archivos = await _db.ArchivosPedido.CountAsync(); // Cuántos archivos hay en Drive
        ViewBag.Eventos = await _db.Pedidos.CountAsync(p => p.CalendarioEventoId != null); // Cuántos pedidos tienen evento en Calendar
        ViewBag.ConFecha = await _db.Pedidos.CountAsync(p => p.FechaEntrega != null && p.Estado != EstadoPedido.Cancelado); // Cuántos deberían tenerlo
        return View(await _google.ObtenerConexionAsync()); // La vista recibe la conexión (o null)
    } // Fin del método

    // POST /Integraciones/Conectar → manda al admin a la pantalla de permiso de Google
    [HttpPost] // Solo por formulario
    [ValidateAntiForgeryToken] // Protección contra formularios falsos
    public IActionResult Conectar() // Inicio del ida y vuelta
    { // Inicio del método
        if (!_google.Configurado) { TempData["Error"] = "Primero cargá las credenciales de Google en Railway (las mismas del login con Google)."; return RedirectToAction(nameof(Index)); } // Sin credenciales no se puede
        var estado = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // Código al azar de un solo uso
        Response.Cookies.Append(CookieEstado, estado, new CookieOptions { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10), Path = "/Integraciones" }); // Lo guardamos en una cookie temporal
        return Redirect(_google.UrlAutorizacion(UrlRetorno, estado)); // Vamos a Google
    } // Fin del método

    // GET /Integraciones/Callback?code=...&state=... → Google vuelve acá con el resultado
    public async Task<IActionResult> Callback(string? code, string? state, string? error) // Parámetros que manda Google
    { // Inicio del método
        var esperado = Request.Cookies[CookieEstado]; // Código que guardamos antes de ir
        Response.Cookies.Delete(CookieEstado, new CookieOptions { Path = "/Integraciones" }); // Se usa una sola vez
        if (!string.IsNullOrEmpty(error)) { TempData["Error"] = error == "access_denied" ? "Cancelaste el permiso en Google. No se conectó nada." : $"Google respondió: {error}"; return RedirectToAction(nameof(Index)); } // El admin canceló o hubo error
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(esperado) || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(state ?? ""), System.Text.Encoding.ASCII.GetBytes(esperado))) // ¿Vuelta falsa o vencida?
        { // Inicio del bloque
            TempData["Error"] = "La respuesta de Google no es válida o venció. Volvé a tocar Conectar."; // Mensaje
            return RedirectToAction(nameof(Index)); // Volvemos
        } // Fin del bloque
        try // Canjeamos el código
        { // Inicio del try
            var email = await _google.CompletarConexionAsync(code, UrlRetorno, User.Identity!.Name!); // Guarda la conexión
            await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Google, "Cuenta de Google conectada", email); // Bitácora
            TempData["Mensaje"] = $"Cuenta {email} conectada. Ya se pueden adjuntar archivos y agendar entregas."; // Confirmación
        } // Fin del try
        catch (Exception ex) when (ex is ErrorGoogle or HttpRequestException or TaskCanceledException) // Error de Google o de red
        { // Inicio del catch
            TempData["Error"] = "No se pudo conectar: " + ex.Message; // Explicación
        } // Fin del catch
        return RedirectToAction(nameof(Index)); // Volvemos a la pantalla
    } // Fin del método

    // POST /Integraciones/Probar → prueba real de Drive y Calendar
    [HttpPost] // Solo por formulario
    [ValidateAntiForgeryToken] // Protección
    public async Task<IActionResult> Probar() // Prueba
    { // Inicio del método
        try { TempData["Mensaje"] = "✓ " + await _google.ProbarAsync(); } // Funciona
        catch (Exception ex) when (ex is ErrorGoogle or HttpRequestException or TaskCanceledException) { TempData["Error"] = "✗ " + ex.Message; } // No funciona: decimos por qué
        return RedirectToAction(nameof(Index)); // Volvemos
    } // Fin del método

    // POST /Integraciones/Sincronizar → pone al día en Calendar todos los pedidos con fecha de entrega
    [HttpPost] // Solo por formulario
    [ValidateAntiForgeryToken] // Protección
    public async Task<IActionResult> Sincronizar() // Útil después de conectar por primera vez
    { // Inicio del método
        var ids = await _db.Pedidos.Where(p => p.FechaEntrega != null || p.CalendarioEventoId != null).Select(p => p.Id).ToListAsync(); // Pedidos con fecha o con evento
        foreach (var id in ids) _calendario.Encolar(id); // A la cola (el trabajador los procesa de a uno)
        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Google, "Sincronización de Calendar", $"{ids.Count} pedido(s) en cola"); // Bitácora
        TempData["Mensaje"] = $"{ids.Count} pedido(s) en cola. En unos segundos se ven en Google Calendar (el resultado queda en Registro, tipo Google)."; // Confirmación
        return RedirectToAction(nameof(Index)); // Volvemos
    } // Fin del método

    // POST /Integraciones/Desconectar → la app deja de usar la cuenta
    [HttpPost] // Solo por formulario
    [ValidateAntiForgeryToken] // Protección
    public async Task<IActionResult> Desconectar() // Desconexión
    { // Inicio del método
        var conexion = await _google.ObtenerConexionAsync(); // Para el registro
        await _google.DesconectarAsync(); // Borra la llave guardada
        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Google, "Cuenta de Google desconectada", conexion?.Email); // Bitácora
        TempData["Mensaje"] = "Cuenta desconectada. Los archivos y eventos ya creados siguen en Google; la app deja de usarlos hasta que vuelvas a conectar."; // Aclaración
        return RedirectToAction(nameof(Index)); // Volvemos
    } // Fin del método
} // Fin de la clase

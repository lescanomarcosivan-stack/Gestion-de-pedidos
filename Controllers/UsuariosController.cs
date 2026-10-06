using System.Security.Claims; // Para leer el Id del usuario logueado
using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Usuario, Roles y TipoRegistro
using GestionPedidos.Services; // Para IAuditoria
using Microsoft.AspNetCore.Authorization; // Para [Authorize]
using Microsoft.AspNetCore.Mvc; // Para Controller
using Microsoft.EntityFrameworkCore; // Para ToListAsync

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Administración de usuarios: aprobar cuentas, cambiar roles, activar/desactivar. Solo administradores
[Authorize(Roles = Roles.Administrador)] // Toda la clase exige rol Administrador
public class UsuariosController : Controller // Atiende /Usuarios/...
{ // Inicio de la clase
    private readonly AppDbContext _db; // Acceso a la base
    private readonly IAuditoria _auditoria; // Bitácora

    public UsuariosController(AppDbContext db, IAuditoria auditoria) // Inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos la base
        _auditoria = auditoria; // Guardamos la bitácora
    } // Fin del constructor

    // GET /Usuarios → listado (primero los pendientes de aprobación)
    public async Task<IActionResult> Index() // Sin filtros (son pocos usuarios)
    { // Inicio del método
        var usuarios = await _db.Usuarios // Tabla Usuarios...
            .OrderBy(u => u.Activo) // ...primero los inactivos (false va antes que true)...
            .ThenBy(u => u.Email) // ...y después por email
            .ToListAsync(); // Ejecuta la consulta
        ViewBag.MiId = IdActual(); // Para no mostrar botones sobre la propia cuenta
        return View(usuarios); // Views/Usuarios/Index.cshtml
    } // Fin del método

    // POST /Usuarios/CambiarRol/5 → asigna otro rol
    [HttpPost, ValidateAntiForgeryToken] // Formulario con token
    public async Task<IActionResult> CambiarRol(int id, string rol) // Usuario y rol nuevo
    { // Inicio del método
        if (!Roles.Todos.Contains(rol)) return BadRequest("Rol inválido"); // Solo los 3 roles conocidos
        if (id == IdActual()) return BadRequest("No podés cambiar tu propio rol"); // Evita que un admin se quite permisos por error
        var usuario = await _db.Usuarios.FindAsync(id); // Buscamos el usuario
        if (usuario is null) return NotFound(); // 404 si no existe
        var anterior = usuario.Rol; // Guardamos el rol viejo para la bitácora
        usuario.Rol = rol; // Asignamos el nuevo
        _auditoria.Registrar(TipoRegistro.Actividad, "Cambio de rol", $"{usuario.Email}: {anterior} → {rol}", "Usuario", usuario.Id); // Bitácora
        await _db.SaveChangesAsync(); // Guardamos (si estaba logueado, su sesión se cierra sola en el próximo pedido)
        TempData["Mensaje"] = $"{usuario.Email} ahora es {rol}"; // Aviso
        return RedirectToAction(nameof(Index)); // Volvemos al listado
    } // Fin del método

    // POST /Usuarios/CambiarActivo/5 → aprueba (activa) o desactiva una cuenta
    [HttpPost, ValidateAntiForgeryToken] // Formulario con token
    public async Task<IActionResult> CambiarActivo(int id) // Usuario a modificar
    { // Inicio del método
        if (id == IdActual()) return BadRequest("No podés desactivar tu propia cuenta"); // Evita quedarse afuera
        var usuario = await _db.Usuarios.FindAsync(id); // Buscamos el usuario
        if (usuario is null) return NotFound(); // 404 si no existe
        usuario.Activo = !usuario.Activo; // Invertimos: activo ↔ inactivo
        _auditoria.Registrar(TipoRegistro.Actividad, usuario.Activo ? "Cuenta activada" : "Cuenta desactivada", usuario.Email, "Usuario", usuario.Id); // Bitácora
        await _db.SaveChangesAsync(); // Guardamos
        TempData["Mensaje"] = $"{usuario.Email} {(usuario.Activo ? "activado" : "desactivado")}"; // Aviso
        return RedirectToAction(nameof(Index)); // Volvemos
    } // Fin del método

    private int IdActual() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0; // Id del administrador que está usando la pantalla
} // Fin de la clase

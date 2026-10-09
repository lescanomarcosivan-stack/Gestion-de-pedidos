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
    private readonly IEnviadorEmail _email; // Envío de emails (Brevo)
    private readonly IConfiguration _config; // Configuración (URL pública)
    private static readonly TimeSpan VigenciaInvitacion = TimeSpan.FromHours(72); // El enlace de invitación sirve 3 días

    public UsuariosController(AppDbContext db, IAuditoria auditoria, IEnviadorEmail email, IConfiguration config) // Inyección de dependencias
    { // Inicio del constructor
        _email = email; // Guardamos el enviador de emails
        _config = config; // Guardamos la configuración
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
        if (usuario.InvitacionPendiente) return BadRequest("La persona tiene que aceptar la invitación"); // Una cuenta invitada se activa sola al elegir su contraseña
        usuario.Activo = !usuario.Activo; // Invertimos: activo ↔ inactivo
        _auditoria.Registrar(TipoRegistro.Actividad, usuario.Activo ? "Cuenta activada" : "Cuenta desactivada", usuario.Email, "Usuario", usuario.Id); // Bitácora
        await _db.SaveChangesAsync(); // Guardamos
        TempData["Mensaje"] = $"{usuario.Email} {(usuario.Activo ? "activado" : "desactivado")}"; // Aviso
        return RedirectToAction(nameof(Index)); // Volvemos
    } // Fin del método

    // GET /Usuarios/Invitar → formulario para invitar a una persona
    public IActionResult Invitar() => View(new InvitarUsuarioViewModel()); // Formulario vacío (rol Operador por defecto)

    // POST /Usuarios/Invitar → crea la cuenta pendiente y manda el email con el enlace
    [HttpPost, ValidateAntiForgeryToken] // Formulario con token
    public async Task<IActionResult> Invitar(InvitarUsuarioViewModel modelo) // Datos del formulario
    { // Inicio del método
        if (!Roles.Todos.Contains(modelo.Rol)) ModelState.AddModelError(nameof(modelo.Rol), "Rol inválido"); // Solo los 3 roles conocidos
        var email = modelo.Email?.Trim().ToLowerInvariant() ?? ""; // Normalizamos (minúsculas, sin espacios)
        var existente = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == email); // ¿Ya hay una cuenta con ese email?
        if (existente is not null) // Si existe...
            ModelState.AddModelError(nameof(modelo.Email), existente.InvitacionPendiente ? "Ya tiene una invitación pendiente: usá «Reenviar» en el listado." : "Ya existe una cuenta con ese email."); // ...explicamos qué hacer
        if (!ModelState.IsValid) return View(modelo); // Volvemos con los errores

        var usuario = new Usuario // Cuenta nueva
        { // Inicio de los datos
            Email = email, // Email
            Nombre = modelo.Nombre.Trim(), // Nombre
            Rol = modelo.Rol, // Rol elegido por el admin
            Activo = false, // No puede entrar hasta aceptar
            InvitacionPendiente = true, // Marca de invitación
            InvitadoPor = User.Identity!.Name // Quién lo invitó
        }; // Fin de los datos
        _db.Usuarios.Add(usuario); // Marcamos para insertar
        var enlace = PrepararInvitacion(usuario); // Código nuevo + enlace
        _auditoria.Registrar(TipoRegistro.Actividad, "Usuario invitado", $"{email} como {usuario.Rol}", "Usuario"); // Bitácora
        await _db.SaveChangesAsync(); // Guardamos
        await EnviarInvitacionAsync(usuario, enlace); // Email (y mensaje en pantalla)
        return RedirectToAction(nameof(Index)); // Volvemos al listado
    } // Fin del método

    // POST /Usuarios/ReenviarInvitacion/5 → nuevo enlace (el anterior deja de servir) y nuevo email
    [HttpPost, ValidateAntiForgeryToken] // Formulario con token
    public async Task<IActionResult> ReenviarInvitacion(int id) // Usuario invitado
    { // Inicio del método
        var usuario = await _db.Usuarios.FindAsync(id); // Buscamos
        if (usuario is null || !usuario.InvitacionPendiente) return NotFound(); // Solo invitaciones pendientes
        var enlace = PrepararInvitacion(usuario); // Código nuevo
        _auditoria.Registrar(TipoRegistro.Actividad, "Invitación reenviada", usuario.Email, "Usuario", usuario.Id); // Bitácora
        await _db.SaveChangesAsync(); // Guardamos
        await EnviarInvitacionAsync(usuario, enlace); // Email
        return RedirectToAction(nameof(Index)); // Volvemos
    } // Fin del método

    // POST /Usuarios/CancelarInvitacion/5 → borra la cuenta pendiente (el enlace deja de servir)
    [HttpPost, ValidateAntiForgeryToken] // Formulario con token
    public async Task<IActionResult> CancelarInvitacion(int id) // Usuario invitado
    { // Inicio del método
        var usuario = await _db.Usuarios.FindAsync(id); // Buscamos
        if (usuario is null || !usuario.InvitacionPendiente) return NotFound(); // Solo invitaciones pendientes (nunca borra cuentas en uso)
        _db.Usuarios.Remove(usuario); // Borramos la cuenta que nunca se usó
        _auditoria.Registrar(TipoRegistro.Actividad, "Invitación cancelada", usuario.Email, "Usuario"); // Bitácora
        await _db.SaveChangesAsync(); // Guardamos
        TempData["Mensaje"] = $"Invitación a {usuario.Email} cancelada."; // Aviso
        return RedirectToAction(nameof(Index)); // Volvemos
    } // Fin del método

    // Genera un código nuevo para la invitación y devuelve el enlace completo
    private string PrepararInvitacion(Usuario usuario) // No guarda: lo hace quien llama
    { // Inicio del método
        var codigo = CodigosSeguros.Generar(); // Código al azar (solo viaja en el email)
        usuario.TokenRecuperacionHash = CodigosSeguros.Hash(codigo); // En la base, solo el hash
        usuario.TokenRecuperacionVence = DateTime.UtcNow.Add(VigenciaInvitacion); // Vence en 72 horas
        var ruta = Url.Action("AceptarInvitacion", "Cuenta", new { email = usuario.Email, token = codigo })!; // /Cuenta/AceptarInvitacion?email=...&token=...
        var basePublica = _config["App:UrlPublica"]; // URL pública fija (evita que alguien manipule el dominio del enlace)
        return string.IsNullOrWhiteSpace(basePublica) ? $"{Request.Scheme}://{Request.Host}{ruta}" : basePublica.TrimEnd('/') + ruta; // Enlace absoluto
    } // Fin del método

    // Envía el email de invitación y deja el mensaje para el administrador
    private async Task EnviarInvitacionAsync(Usuario usuario, string enlace) // Usuario y enlace
    { // Inicio del método
        var nombre = System.Net.WebUtility.HtmlEncode(usuario.Nombre); // Nombre seguro para HTML
        var quien = System.Net.WebUtility.HtmlEncode(User.FindFirstValue("nombre") ?? User.Identity!.Name); // Quién invita
        var html = $"<p>Hola {nombre},</p>" + // Saludo
                   $"<p>{quien} te invitó a usar <strong>Gestión de Pedidos</strong> con el rol <strong>{usuario.Rol}</strong>.</p>" + // Quién y con qué rol
                   $"<p>Para activar tu cuenta y elegir tu contraseña hacé clic acá (el enlace vence en 72 horas):</p>" + // Instrucción
                   $"<p><a href='{enlace}' style='display:inline-block;padding:10px 18px;background:#1e5aa8;color:#fff;text-decoration:none;border-radius:6px'>Aceptar invitación</a></p>" + // Botón
                   $"<p style='font-size:12px;color:#5b6b82'>Si el botón no funciona, copiá este enlace en el navegador:<br>{enlace}</p>" + // Enlace en texto
                   "<p style='font-size:12px;color:#5b6b82'>Si no esperabas esta invitación, ignorá este mensaje.</p>"; // Aclaración
        var enviado = await _email.EnviarAsync(usuario.Email, "Te invitaron a Gestión de Pedidos", html); // Enviamos
        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Email, enviado ? "Email enviado" : "Email NO enviado", $"Invitación → {usuario.Email}", "Usuario", usuario.Id); // Constancia (sin el enlace)
        if (enviado) TempData["Mensaje"] = $"Invitación enviada a {usuario.Email}. Tiene 72 horas para aceptarla."; // Todo bien
        else TempData["EnlaceInvitacion"] = enlace; // No salió el email: el admin ve el enlace para mandarlo él (por WhatsApp, por ejemplo)
        if (!enviado) TempData["Error"] = $"La cuenta de {usuario.Email} quedó creada, pero el email no se pudo enviar (revisá la configuración de Brevo). Podés copiar el enlace de abajo y mandárselo vos."; // Explicación
    } // Fin del método

    private int IdActual() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0; // Id del administrador que está usando la pantalla
} // Fin de la clase

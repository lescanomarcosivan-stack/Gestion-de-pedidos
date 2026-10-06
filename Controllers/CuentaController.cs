using System.Security.Claims; // Claims = datos del usuario que viajan dentro de la cookie de sesión
using System.Security.Cryptography; // Para generar códigos aleatorios seguros y calcular SHA-256
using System.Text; // Para Encoding.UTF8
using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Usuario, Roles y los ViewModels de cuenta
using GestionPedidos.Services; // Para IAuditoria e IEnviadorEmail
using Microsoft.AspNetCore.Authentication; // Para SignInAsync, SignOutAsync, ChallengeAsync
using Microsoft.AspNetCore.Authentication.Cookies; // Nombre del esquema de cookies
using Microsoft.AspNetCore.Authorization; // Para [AllowAnonymous]
using Microsoft.AspNetCore.Identity; // Para IPasswordHasher y PasswordVerificationResult
using Microsoft.AspNetCore.Mvc; // Para Controller
using Microsoft.AspNetCore.RateLimiting; // Para [EnableRateLimiting]
using Microsoft.EntityFrameworkCore; // Para FirstOrDefaultAsync, AnyAsync

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Todo lo relacionado con la cuenta: iniciar/cerrar sesión, crear cuenta, Google y recuperar contraseña
public class CuentaController : Controller // Atiende las URLs /Cuenta/...
{ // Inicio de la clase
    private const int MaximoIntentos = 5; // Contraseñas incorrectas seguidas antes de bloquear
    private static readonly TimeSpan TiempoBloqueo = TimeSpan.FromMinutes(15); // Duración del bloqueo
    private static readonly TimeSpan VigenciaToken = TimeSpan.FromMinutes(30); // Duración del enlace de recuperación

    private readonly AppDbContext _db; // Acceso a la base
    private readonly IPasswordHasher<Usuario> _hasher; // Cifrador/verificador de contraseñas
    private readonly IAuditoria _auditoria; // Bitácora
    private readonly IEnviadorEmail _email; // Envío de emails
    private readonly IConfiguration _config; // Configuración
    private readonly IAuthenticationSchemeProvider _esquemas; // Para saber si Google está configurado

    public CuentaController(AppDbContext db, IPasswordHasher<Usuario> hasher, IAuditoria auditoria, IEnviadorEmail email, IConfiguration config, IAuthenticationSchemeProvider esquemas) // Inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos la base
        _hasher = hasher; // Guardamos el cifrador
        _auditoria = auditoria; // Guardamos la bitácora
        _email = email; // Guardamos el enviador de emails
        _config = config; // Guardamos la configuración
        _esquemas = esquemas; // Guardamos los esquemas de autenticación
    } // Fin del constructor

    // GET /Cuenta/Login → muestra el formulario de inicio de sesión
    [AllowAnonymous] // Esta pantalla tiene que ser pública (si no, nadie podría entrar)
    public async Task<IActionResult> Login(string? returnUrl) // returnUrl = adónde quería ir
    { // Inicio del método
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home"); // Si ya inició sesión, lo mandamos al dashboard
        ViewBag.GoogleHabilitado = await GoogleHabilitadoAsync(); // Para mostrar u ocultar el botón de Google
        return View(new LoginViewModel { ReturnUrl = returnUrl }); // Formulario vacío
    } // Fin del método

    // POST /Cuenta/Login → valida email y contraseña
    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken] // Envío de formulario, público, con token anti-falsificación
    [EnableRateLimiting("login")] // Máximo 10 intentos por minuto por IP (frena ataques de fuerza bruta)
    public async Task<IActionResult> Login(LoginViewModel modelo) // Recibe email, contraseña y returnUrl
    { // Inicio del método
        ViewBag.GoogleHabilitado = await GoogleHabilitadoAsync(); // Por si hay que volver a mostrar el formulario
        if (!ModelState.IsValid) return View(modelo); // Campos vacíos o mal escritos → volvemos con los errores

        var email = modelo.Email.Trim().ToLowerInvariant(); // Normalizamos: sin espacios y en minúsculas
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == email); // Buscamos la cuenta
        const string errorGenerico = "Email o contraseña incorrectos"; // Mismo mensaje en ambos casos: no revelamos si el email existe

        if (usuario is null || usuario.PasswordHash is null) // No existe, o solo entra con Google
        { // Inicio del bloque
            await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Login fallido", $"Email inexistente o sin contraseña: {email}", usuario: email); // Registramos el intento
            ModelState.AddModelError(string.Empty, errorGenerico); // Error general
            return View(modelo); // Volvemos al formulario
        } // Fin del bloque

        if (usuario.BloqueadoHasta > DateTime.UtcNow) // Si la cuenta está bloqueada por intentos fallidos...
        { // Inicio del bloque
            await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Login rechazado: cuenta bloqueada", null, "Usuario", usuario.Id, email); // Registramos
            ModelState.AddModelError(string.Empty, $"Cuenta bloqueada por intentos fallidos. Probá de nuevo después de las {Formato.Fecha(usuario.BloqueadoHasta.Value)[11..]}."); // Le decimos hasta qué hora
            return View(modelo); // Volvemos
        } // Fin del bloque

        var resultado = _hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, modelo.Password); // Compara la contraseña escrita con el hash guardado
        if (resultado == PasswordVerificationResult.Failed) // Contraseña incorrecta
        { // Inicio del bloque
            usuario.IntentosFallidos++; // Sumamos un intento fallido
            var detalle = $"Intento {usuario.IntentosFallidos} de {MaximoIntentos}"; // Texto para la bitácora
            if (usuario.IntentosFallidos >= MaximoIntentos) // Si llegó al máximo...
            { // Inicio del bloque de bloqueo
                usuario.BloqueadoHasta = DateTime.UtcNow.Add(TiempoBloqueo); // ...bloqueamos 15 minutos
                usuario.IntentosFallidos = 0; // ...y reiniciamos el contador
                detalle += " → cuenta bloqueada 15 minutos"; // Lo anotamos
            } // Fin del bloque de bloqueo
            _auditoria.Registrar(TipoRegistro.Acceso, "Login fallido", detalle, "Usuario", usuario.Id, email); // Registramos el intento
            await _db.SaveChangesAsync(); // Guardamos contador y registro juntos
            ModelState.AddModelError(string.Empty, errorGenerico); // Mensaje genérico
            return View(modelo); // Volvemos
        } // Fin del bloque

        if (!usuario.Activo) // Contraseña correcta pero la cuenta no está aprobada
        { // Inicio del bloque
            await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Login rechazado: cuenta inactiva", null, "Usuario", usuario.Id, email); // Registramos
            ModelState.AddModelError(string.Empty, "Tu cuenta está pendiente de aprobación o fue desactivada. Consultá con un administrador."); // Explicación
            return View(modelo); // Volvemos
        } // Fin del bloque

        if (resultado == PasswordVerificationResult.SuccessRehashNeeded) // Si el hash usa un formato viejo...
            usuario.PasswordHash = _hasher.HashPassword(usuario, modelo.Password); // ...lo actualizamos al formato actual (más seguro)

        await IniciarSesionAsync(usuario, "contraseña"); // Creamos la cookie de sesión y registramos el acceso
        return RedirigirLocal(modelo.ReturnUrl); // Lo mandamos adonde quería ir
    } // Fin del método

    // POST /Cuenta/Logout → cierra la sesión
    [HttpPost, ValidateAntiForgeryToken] // Solo por formulario (evita que un enlace malicioso te desloguee)
    public async Task<IActionResult> Logout() // Sin parámetros
    { // Inicio del método
        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Logout"); // Registramos el cierre
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Borra la cookie de sesión
        return RedirectToAction(nameof(Login)); // Vuelve al login
    } // Fin del método

    // GET /Cuenta/Registro → formulario para crear una cuenta
    [AllowAnonymous] // Público
    public IActionResult Registro() => View(new RegistroViewModel()); // Formulario vacío

    // POST /Cuenta/Registro → crea la cuenta (queda pendiente de aprobación)
    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken] // Formulario público con token
    [EnableRateLimiting("login")] // Evita que creen cuentas masivamente
    public async Task<IActionResult> Registro(RegistroViewModel modelo) // Datos del formulario
    { // Inicio del método
        var email = modelo.Email.Trim().ToLowerInvariant(); // Normalizamos el email
        if (await _db.Usuarios.AnyAsync(u => u.Email == email)) // Si ya existe...
            ModelState.AddModelError(nameof(modelo.Email), "Ya existe una cuenta con ese email"); // ...error en el campo
        if (!ModelState.IsValid) return View(modelo); // Si hay errores, volvemos

        var usuario = new Usuario // Nueva cuenta
        { // Inicio de los datos
            Email = email, // Email normalizado
            Nombre = modelo.Nombre.Trim(), // Nombre
            Rol = Roles.Consulta, // Por seguridad, el menor permiso
            Activo = false // Inactiva hasta que un administrador la apruebe
        }; // Fin de los datos
        usuario.PasswordHash = _hasher.HashPassword(usuario, modelo.Password); // Guardamos SOLO el hash de la contraseña
        _db.Usuarios.Add(usuario); // Marcamos para insertar
        await _db.SaveChangesAsync(); // INSERT (ahora tiene Id)
        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Registro de cuenta", "Pendiente de aprobación", "Usuario", usuario.Id, email); // Registramos

        TempData["Mensaje"] = "Cuenta creada. Un administrador tiene que aprobarla antes de que puedas entrar."; // Aviso
        return RedirectToAction(nameof(Login)); // Volvemos al login
    } // Fin del método

    // GET /Cuenta/LoginGoogle → redirige a la pantalla de Google
    [AllowAnonymous] // Público
    public async Task<IActionResult> LoginGoogle(string? returnUrl) // returnUrl = adónde volver después
    { // Inicio del método
        if (!await GoogleHabilitadoAsync()) // Si no se configuraron las credenciales de Google...
        { // Inicio del bloque
            TempData["Error"] = "El inicio de sesión con Google no está configurado en este servidor."; // ...avisamos
            return RedirectToAction(nameof(Login)); // ...y volvemos
        } // Fin del bloque
        var propiedades = new AuthenticationProperties { RedirectUri = Url.Action(nameof(GoogleRespuesta), new { returnUrl }) }; // Después de Google, volver a GoogleRespuesta
        return Challenge(propiedades, "Google"); // "Challenge" = mandar al usuario a autenticarse con Google
    } // Fin del método

    // GET /Cuenta/GoogleRespuesta → Google ya validó al usuario; acá lo buscamos o creamos en nuestra base
    [AllowAnonymous] // Público (todavía no tiene sesión nuestra)
    public async Task<IActionResult> GoogleRespuesta(string? returnUrl) // returnUrl original
    { // Inicio del método
        var externo = await HttpContext.AuthenticateAsync("Externa"); // Leemos lo que dejó Google en la cookie temporal
        await HttpContext.SignOutAsync("Externa"); // Borramos la cookie temporal (ya no hace falta)
        var googleId = externo.Principal?.FindFirstValue(ClaimTypes.NameIdentifier); // Id único de la persona en Google
        var email = externo.Principal?.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant(); // Email verificado por Google
        var nombre = externo.Principal?.FindFirstValue(ClaimTypes.Name) ?? email; // Nombre (o el email si no vino)
        if (!externo.Succeeded || googleId is null || email is null) // Si algo falló...
        { // Inicio del bloque
            await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Login con Google fallido"); // ...registramos
            TempData["Error"] = "No se pudo iniciar sesión con Google."; // ...avisamos
            return RedirectToAction(nameof(Login)); // ...y volvemos
        } // Fin del bloque

        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.GoogleId == googleId || u.Email == email); // Buscamos por Id de Google o por email
        if (usuario is null) // Primera vez que entra
        { // Inicio del bloque
            usuario = new Usuario { Email = email, Nombre = nombre!, GoogleId = googleId, Rol = Roles.Consulta, Activo = false }; // Cuenta nueva sin contraseña, pendiente de aprobación
            _db.Usuarios.Add(usuario); // Marcamos para insertar
            await _db.SaveChangesAsync(); // INSERT
            await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Registro con Google", "Pendiente de aprobación", "Usuario", usuario.Id, email); // Registramos
        } // Fin del bloque
        else if (usuario.GoogleId is null) // Ya tenía cuenta con contraseña y es la primera vez que usa Google
        { // Inicio del bloque
            usuario.GoogleId = googleId; // Vinculamos su cuenta de Google
            await _db.SaveChangesAsync(); // UPDATE
        } // Fin del bloque

        if (!usuario.Activo) // Si no está aprobada...
        { // Inicio del bloque
            TempData["Mensaje"] = "Tu cuenta de Google quedó registrada. Un administrador tiene que aprobarla antes de que puedas entrar."; // ...avisamos
            return RedirectToAction(nameof(Login)); // ...y volvemos al login
        } // Fin del bloque

        await IniciarSesionAsync(usuario, "Google"); // Sesión iniciada
        return RedirigirLocal(returnUrl); // Adonde quería ir
    } // Fin del método

    // GET /Cuenta/OlvideClave → formulario para pedir el enlace de recuperación
    [AllowAnonymous] // Público
    public IActionResult OlvideClave() => View(new OlvideClaveViewModel()); // Formulario vacío

    // POST /Cuenta/OlvideClave → genera el enlace y lo envía por email
    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken] // Formulario público con token
    [EnableRateLimiting("login")] // Evita abusos (envío masivo de emails)
    public async Task<IActionResult> OlvideClave(OlvideClaveViewModel modelo) // Recibe el email
    { // Inicio del método
        if (!ModelState.IsValid) return View(modelo); // Email vacío o inválido
        var email = modelo.Email.Trim().ToLowerInvariant(); // Normalizamos
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == email && u.Activo); // Solo cuentas activas

        if (usuario is not null) // Si existe (pero la respuesta en pantalla es la misma en ambos casos)
        { // Inicio del bloque
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('='); // Código aleatorio imposible de adivinar, apto para URL
            usuario.TokenRecuperacionHash = HashToken(token); // En la base guardamos solo el hash del código
            usuario.TokenRecuperacionVence = DateTime.UtcNow.Add(VigenciaToken); // Vence en 30 minutos
            var enlace = ArmarEnlace(email, token); // Enlace completo que va en el email
            var enviado = await _email.EnviarAsync(email, "Recuperación de contraseña", // Enviamos el email
                $"<p>Hola {System.Net.WebUtility.HtmlEncode(usuario.Nombre)},</p><p>Para elegir una nueva contraseña hacé clic en este enlace (vence en 30 minutos):</p><p><a href=\"{enlace}\">{enlace}</a></p><p>Si no lo pediste, ignorá este mensaje.</p>"); // Contenido HTML
            _auditoria.Registrar(TipoRegistro.Acceso, "Recuperación de contraseña solicitada", enviado ? "Email enviado" : "Email NO enviado (proveedor sin configurar o con error; ver logs del servidor)", "Usuario", usuario.Id, email); // Registramos (sin el enlace)
            await _db.SaveChangesAsync(); // Guardamos token y registro
        } // Fin del bloque
        else // Si no existe
        { // Inicio del bloque
            await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Recuperación pedida para email inexistente o inactivo", null, usuario: email); // Igual lo registramos
        } // Fin del bloque

        return View("OlvideClaveEnviado"); // Siempre la misma respuesta: no revelamos qué emails existen
    } // Fin del método

    // GET /Cuenta/RestablecerClave?email=...&token=... → formulario de contraseña nueva
    [AllowAnonymous] // Público (se llega desde el email)
    public IActionResult RestablecerClave(string email, string token) => View(new RestablecerClaveViewModel { Email = email, Token = token }); // Pasa email y token ocultos al formulario

    // POST /Cuenta/RestablecerClave → valida el código y guarda la contraseña nueva
    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken] // Formulario público con token
    [EnableRateLimiting("login")] // Evita probar códigos al azar
    public async Task<IActionResult> RestablecerClave(RestablecerClaveViewModel modelo) // Datos del formulario
    { // Inicio del método
        if (!ModelState.IsValid) return View(modelo); // Contraseñas inválidas o distintas
        var email = modelo.Email.Trim().ToLowerInvariant(); // Normalizamos
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Email == email && u.Activo); // Buscamos la cuenta

        var valido = usuario?.TokenRecuperacionHash is not null // Tiene un código pendiente...
            && usuario.TokenRecuperacionVence > DateTime.UtcNow // ...que no venció...
            && FirmaWebhook.SonIguales(usuario.TokenRecuperacionHash, HashToken(modelo.Token)); // ...y coincide (comparación en tiempo constante)
        if (!valido) // Si algo no cierra...
        { // Inicio del bloque
            await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Restablecer contraseña: enlace inválido o vencido", null, usuario: email); // ...registramos
            ModelState.AddModelError(string.Empty, "El enlace es inválido o venció. Pedí uno nuevo."); // ...avisamos
            return View(modelo); // ...y volvemos
        } // Fin del bloque

        usuario!.PasswordHash = _hasher.HashPassword(usuario, modelo.Password); // Guardamos el hash de la contraseña nueva
        usuario.TokenRecuperacionHash = null; // El código se usa una sola vez
        usuario.TokenRecuperacionVence = null; // Sin vencimiento pendiente
        usuario.IntentosFallidos = 0; // Reiniciamos intentos
        usuario.BloqueadoHasta = null; // Y desbloqueamos
        _auditoria.Registrar(TipoRegistro.Acceso, "Contraseña restablecida", null, "Usuario", usuario.Id, email); // Registramos
        await _db.SaveChangesAsync(); // Guardamos todo junto
        TempData["Mensaje"] = "Contraseña actualizada. Ya podés iniciar sesión."; // Aviso
        return RedirectToAction(nameof(Login)); // Al login
    } // Fin del método

    // GET /Cuenta/AccesoDenegado → el usuario inició sesión pero su rol no alcanza
    public async Task<IActionResult> AccesoDenegado(string? returnUrl) // returnUrl = pantalla que intentó abrir
    { // Inicio del método
        await _auditoria.RegistrarYGuardarAsync(TipoRegistro.Acceso, "Acceso denegado", returnUrl); // Registramos el intento
        return View(); // Muestra el aviso
    } // Fin del método

    // ---------- Métodos auxiliares ----------

    // Crea la cookie de sesión con los datos del usuario
    private async Task IniciarSesionAsync(Usuario usuario, string metodo) // metodo = "contraseña" o "Google"
    { // Inicio del método
        var claims = new List<Claim> // Datos que viajan (firmados y cifrados) dentro de la cookie
        { // Inicio de la lista
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()), // Id del usuario
            new(ClaimTypes.Name, usuario.Email), // Email (User.Identity.Name)
            new("nombre", usuario.Nombre), // Nombre para mostrar
            new(ClaimTypes.Role, usuario.Rol) // Rol: lo usa [Authorize(Roles = ...)]
        }; // Fin de la lista
        var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme); // Identidad del usuario
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidad)); // Crea la cookie

        usuario.IntentosFallidos = 0; // Reiniciamos intentos fallidos
        usuario.BloqueadoHasta = null; // Quitamos cualquier bloqueo
        usuario.UltimoAcceso = DateTime.UtcNow; // Registramos el último acceso
        _auditoria.Registrar(TipoRegistro.Acceso, "Login exitoso", $"Método: {metodo}", "Usuario", usuario.Id, usuario.Email); // Bitácora
        await _db.SaveChangesAsync(); // Guardamos
    } // Fin del método

    private IActionResult RedirigirLocal(string? returnUrl) // Evita redirecciones a sitios externos (ataque "open redirect")
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("Index", "Home"); // Solo URLs de nuestro sitio

    private async Task<bool> GoogleHabilitadoAsync() => await _esquemas.GetSchemeAsync("Google") is not null; // true si Google fue registrado en Program.cs

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))); // SHA-256 del código (lo que se guarda en la base)

    private string ArmarEnlace(string email, string token) // Arma el enlace absoluto del email
    { // Inicio del método
        var ruta = Url.Action(nameof(RestablecerClave), new { email, token })!; // Ruta relativa con email y código
        var basePublica = _config["App:UrlPublica"]; // URL pública fija (recomendado): evita que alguien manipule el dominio del enlace
        return string.IsNullOrWhiteSpace(basePublica) ? $"{Request.Scheme}://{Request.Host}{ruta}" : basePublica.TrimEnd('/') + ruta; // Si no está configurada, usa el dominio del pedido
    } // Fin del método
} // Fin de la clase

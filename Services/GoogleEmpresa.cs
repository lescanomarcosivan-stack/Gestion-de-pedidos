using System.Net; // Para HttpStatusCode (códigos de respuesta como 404)
using System.Net.Http.Headers; // Para AuthenticationHeaderValue y MediaTypeHeaderValue
using System.Security.Cryptography; // Para AesGcm (cifrado) y SHA256
using System.Text; // Para Encoding.UTF8
using System.Text.Json; // Para leer y escribir JSON
using System.Text.Json.Nodes; // Para JsonNode (leer JSON sin crear clases)
using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para IntegracionGoogle, ArchivoPedido, Pedido
using Microsoft.EntityFrameworkCore; // Para FirstOrDefaultAsync

namespace GestionPedidos.Services; // Namespace de los servicios

// Todas las direcciones de Google en un solo lugar
public static class UrlsGoogle // Clase estática: solo constantes
{ // Inicio de la clase
    public const string Autorizar = "https://accounts.google.com/o/oauth2/v2/auth"; // Pantalla de Google donde el admin da permiso
    public const string Token = "https://oauth2.googleapis.com/token"; // Donde se canjean códigos y llaves por tokens de acceso
    public const string Drive = "https://www.googleapis.com/drive/v3/"; // API de Google Drive
    public const string DriveSubida = "https://www.googleapis.com/upload/drive/v3/files"; // API de Drive para subir contenido
    public const string Calendar = "https://www.googleapis.com/calendar/v3/"; // API de Google Calendar
    public const string PermisoDrive = "https://www.googleapis.com/auth/drive.file"; // Permiso: SOLO los archivos que crea esta app (no ve el resto del Drive)
    public const string PermisoCalendar = "https://www.googleapis.com/auth/calendar.events"; // Permiso: crear, cambiar y borrar eventos del calendario
} // Fin de la clase

// Error "esperable" de Google (sin conexión, permiso revocado, archivo borrado...). El mensaje se puede mostrar al usuario
public class ErrorGoogle : Exception // Hereda de Exception
{ // Inicio de la clase
    public ErrorGoogle(string mensaje) : base(mensaje) { } // Guarda el mensaje
} // Fin de la clase

// Contrato del servicio que habla con Google usando la cuenta de la empresa
public interface IGoogleEmpresa // Los controladores dependen de esta interfaz
{ // Inicio de la interfaz
    bool Configurado { get; } // true si están las credenciales de Google Cloud (las mismas del login con Google)
    Task<IntegracionGoogle?> ObtenerConexionAsync(); // Conexión guardada (o null si no se conectó)
    string UrlAutorizacion(string urlRetorno, string estado); // Dirección de Google para dar permiso
    Task<string> CompletarConexionAsync(string codigo, string urlRetorno, string usuario); // Canjea el código de Google y guarda la conexión; devuelve el email conectado
    Task DesconectarAsync(); // Borra la conexión guardada
    Task<string> ProbarAsync(); // Verifica que Drive y Calendar respondan; devuelve un resumen
    Task<ArchivoPedido> SubirArchivoAsync(int pedidoId, string nombre, string tipoMime, Stream contenido, long tamano, string usuario); // Sube un archivo a Drive
    Task<Stream> DescargarAsync(string driveId); // Descarga el contenido de un archivo de Drive
    Task BorrarArchivoAsync(string driveId); // Borra (manda a la papelera) un archivo de Drive
    Task<string> SincronizarEventoAsync(Pedido pedido); // Deja el evento de Calendar igual al pedido; devuelve qué hizo
} // Fin de la interfaz

// Implementación sin librerías de Google: llamadas HTTP directas a las APIs (OAuth 2.0 + REST)
public class GoogleEmpresa : IGoogleEmpresa // Cumple el contrato
{ // Inicio de la clase
    private const string NombreCarpeta = "Gestión de Pedidos"; // Carpeta que se crea en el Drive de la empresa
    private static readonly SemaphoreSlim Candado = new(1, 1); // Evita pedir dos tokens a la vez (un solo hilo por vez)
    private static string? _tokenAcceso; // Token de acceso en memoria (dura ~1 hora; así no lo pedimos en cada llamada)
    private static DateTime _venceToken; // Cuándo vence ese token
    private static DateTime _conexionDelToken; // A qué conexión pertenece (si se reconecta, se descarta)

    private readonly AppDbContext _db; // Acceso a la base
    private readonly HttpClient _http; // Cliente HTTP (lo crea AddHttpClient en Program.cs)
    private readonly IConfiguration _config; // Configuración (credenciales, calendario, URL pública)
    private readonly string? _clientId; // Id de la app en Google Cloud
    private readonly string? _clientSecret; // Secreto de la app en Google Cloud

    public GoogleEmpresa(AppDbContext db, HttpClient http, IConfiguration config) // Inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos el DbContext
        _http = http; // Guardamos el cliente HTTP
        _config = config; // Guardamos la configuración
        _clientId = config["Autenticacion:Google:ClientId"]; // Mismas credenciales que el login con Google
        _clientSecret = config["Autenticacion:Google:ClientSecret"]; // Secreto (variable de Railway)
    } // Fin del constructor

    public bool Configurado => !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret); // ¿Hay credenciales?

    private string CalendarioId => Uri.EscapeDataString(_config["Google:CalendarioId"] is { Length: > 0 } c ? c : "primary"); // Calendario a usar ("primary" = el principal de la cuenta)

    public Task<IntegracionGoogle?> ObtenerConexionAsync() => _db.IntegracionesGoogle.OrderBy(i => i.Id).FirstOrDefaultAsync(); // Hay una sola fila (o ninguna)

    // ---------- Conexión (OAuth 2.0 "offline": el admin da permiso UNA vez y Google entrega una llave permanente) ----------

    public string UrlAutorizacion(string urlRetorno, string estado) // Arma la dirección de la pantalla de permiso de Google
    { // Inicio del método
        var parametros = new Dictionary<string, string> // Datos que viajan en la dirección
        { // Inicio de los datos
            ["client_id"] = _clientId!, // Quién pide el permiso (nuestra app)
            ["redirect_uri"] = urlRetorno, // A dónde vuelve Google (debe estar cargada en Google Cloud)
            ["response_type"] = "code", // Queremos un código para canjear
            ["scope"] = $"openid email {UrlsGoogle.PermisoDrive} {UrlsGoogle.PermisoCalendar}", // Permisos: saber el email + Drive (solo lo propio) + Calendar
            ["access_type"] = "offline", // "offline" = entregá una llave permanente (refresh token) para usarla sin el admin presente
            ["prompt"] = "consent", // Siempre muestra la pantalla de permiso (así Google siempre entrega la llave)
            ["state"] = estado // Código al azar para comprobar que la vuelta es la nuestra (protección CSRF)
        }; // Fin de los datos
        return UrlsGoogle.Autorizar + "?" + string.Join("&", parametros.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")); // Dirección completa
    } // Fin del método

    public async Task<string> CompletarConexionAsync(string codigo, string urlRetorno, string usuario) // Google volvió con un código: lo canjeamos
    { // Inicio del método
        var json = await PedirTokenAsync(new Dictionary<string, string> // Pedido al servidor de tokens de Google
        { // Inicio de los datos
            ["code"] = codigo, // Código que mandó Google
            ["client_id"] = _clientId!, // Nuestra app
            ["client_secret"] = _clientSecret!, // Nuestro secreto
            ["redirect_uri"] = urlRetorno, // Debe ser la misma que se usó al pedir permiso
            ["grant_type"] = "authorization_code" // Tipo de canje: código de autorización
        }); // Fin del pedido
        var llave = json["refresh_token"]?.GetValue<string>(); // Llave permanente
        if (string.IsNullOrEmpty(llave)) throw new ErrorGoogle("Google no entregó la llave permanente. Probá de nuevo."); // Sin llave no podemos trabajar
        var permisos = json["scope"]?.GetValue<string>() ?? ""; // Permisos que el admin realmente aceptó (puede destildar casillas)
        if (!permisos.Contains(UrlsGoogle.PermisoDrive) || !permisos.Contains(UrlsGoogle.PermisoCalendar)) // ¿Faltó alguno?
            throw new ErrorGoogle("No se aceptaron todos los permisos. Volvé a conectar y marcá las casillas de Google Drive y de Google Calendar."); // Explicación
        var email = EmailDelIdToken(json["id_token"]?.GetValue<string>()) ?? "(cuenta de Google)"; // Qué cuenta se conectó

        _db.IntegracionesGoogle.RemoveRange(await _db.IntegracionesGoogle.ToListAsync()); // Borramos una conexión anterior (si había)
        _db.IntegracionesGoogle.Add(new IntegracionGoogle // Guardamos la nueva
        { // Inicio de los datos
            Email = email, // Cuenta de la empresa
            RefreshTokenCifrado = Cifrar(llave), // La llave se guarda CIFRADA
            FechaConexion = DateTime.UtcNow, // Ahora
            ConectadoPor = usuario // Admin que la conectó
        }); // Fin de los datos
        await _db.SaveChangesAsync(); // Guardamos
        _tokenAcceso = null; // Descartamos cualquier token viejo en memoria
        return email; // Devolvemos el email para mostrarlo
    } // Fin del método

    public async Task DesconectarAsync() // La app deja de usar la cuenta de Google
    { // Inicio del método
        _db.IntegracionesGoogle.RemoveRange(await _db.IntegracionesGoogle.ToListAsync()); // Borramos la llave guardada
        await _db.SaveChangesAsync(); // Guardamos
        _tokenAcceso = null; // Olvidamos el token en memoria
    } // Fin del método

    public async Task<string> ProbarAsync() // Prueba real contra Drive y Calendar
    { // Inicio del método
        var conexion = await ConexionObligatoriaAsync(); // Falla si no hay conexión
        await CarpetaAsync(conexion); // Drive: verifica (o crea) la carpeta de la app
        using var respuesta = await EnviarAsync(new HttpRequestMessage(HttpMethod.Get, $"{UrlsGoogle.Calendar}calendars/{CalendarioId}?fields=summary")); // Calendar: lee el nombre del calendario
        if (!respuesta.IsSuccessStatusCode) throw new ErrorGoogle("Calendar: " + await LeerErrorAsync(respuesta)); // Si falla, explicamos
        var nombreCalendario = (await LeerJsonAsync(respuesta))["summary"]?.GetValue<string>(); // Nombre del calendario
        return $"Drive: carpeta \"{NombreCarpeta}\" lista · Calendar: \"{nombreCalendario}\" accesible"; // Resumen
    } // Fin del método

    // ---------- Google Drive ----------

    public async Task<ArchivoPedido> SubirArchivoAsync(int pedidoId, string nombre, string tipoMime, Stream contenido, long tamano, string usuario) // Sube un archivo
    { // Inicio del método
        var conexion = await ConexionObligatoriaAsync(); // Falla si no hay conexión
        var carpeta = await CarpetaAsync(conexion); // Carpeta de la app
        var datos = JsonSerializer.Serialize(new { name = $"Pedido {pedidoId} - {nombre}", parents = new[] { carpeta }, description = $"Adjunto del pedido #{pedidoId}, subido por {usuario}" }); // Nombre, carpeta y descripción

        var inicio = new HttpRequestMessage(HttpMethod.Post, UrlsGoogle.DriveSubida + "?uploadType=resumable") // Subida "reanudable": paso 1, avisamos qué vamos a subir
        { Content = new StringContent(datos, Encoding.UTF8, "application/json") }; // Datos del archivo
        inicio.Headers.Add("X-Upload-Content-Type", tipoMime); // Tipo del contenido
        inicio.Headers.Add("X-Upload-Content-Length", tamano.ToString()); // Tamaño del contenido
        using var respuestaInicio = await EnviarAsync(inicio); // Google responde con una dirección para subir
        if (!respuestaInicio.IsSuccessStatusCode || respuestaInicio.Headers.Location is null) throw new ErrorGoogle("Drive: " + await LeerErrorAsync(respuestaInicio)); // Error
        var destino = respuestaInicio.Headers.Location; // Dirección de subida

        var subida = new HttpRequestMessage(HttpMethod.Put, destino) { Content = new StreamContent(contenido) }; // Paso 2: mandamos el contenido
        subida.Content.Headers.ContentType = new MediaTypeHeaderValue(tipoMime); // Tipo
        subida.Content.Headers.ContentLength = tamano; // Tamaño
        using var respuesta = await EnviarAsync(subida); // Enviamos
        if (!respuesta.IsSuccessStatusCode) throw new ErrorGoogle("Drive: " + await LeerErrorAsync(respuesta)); // Error
        var id = (await LeerJsonAsync(respuesta))["id"]?.GetValue<string>() ?? throw new ErrorGoogle("Drive no devolvió el id del archivo"); // Id del archivo creado
        return new ArchivoPedido { PedidoId = pedidoId, DriveId = id, Nombre = nombre, TipoMime = tipoMime, Tamano = tamano, Usuario = usuario }; // Datos para guardar en la base
    } // Fin del método

    public async Task<Stream> DescargarAsync(string driveId) // Descarga el contenido
    { // Inicio del método
        var respuesta = await EnviarAsync(new HttpRequestMessage(HttpMethod.Get, $"{UrlsGoogle.Drive}files/{Uri.EscapeDataString(driveId)}?alt=media"), HttpCompletionOption.ResponseHeadersRead); // "alt=media" = el contenido, no los datos; no esperamos a bajar todo para empezar a mandarlo
        if (respuesta.StatusCode == HttpStatusCode.NotFound) { respuesta.Dispose(); throw new ErrorGoogle("El archivo ya no existe en Google Drive (puede que lo hayan borrado desde Drive)."); } // Borrado desde Drive
        if (!respuesta.IsSuccessStatusCode) { var error = await LeerErrorAsync(respuesta); respuesta.Dispose(); throw new ErrorGoogle("Drive: " + error); } // Otro error
        return await respuesta.Content.ReadAsStreamAsync(); // Flujo de bytes: se va pasando al navegador a medida que llega
    } // Fin del método

    public async Task BorrarArchivoAsync(string driveId) // Manda el archivo a la papelera de Drive (se puede recuperar 30 días)
    { // Inicio del método
        var pedido = new HttpRequestMessage(HttpMethod.Patch, $"{UrlsGoogle.Drive}files/{Uri.EscapeDataString(driveId)}") // PATCH = modificar un dato del archivo
        { Content = new StringContent("{\"trashed\":true}", Encoding.UTF8, "application/json") }; // trashed = a la papelera
        using var respuesta = await EnviarAsync(pedido); // Enviamos
        if (respuesta.StatusCode == HttpStatusCode.NotFound) return; // Ya no existía: está bien
        if (!respuesta.IsSuccessStatusCode) throw new ErrorGoogle("Drive: " + await LeerErrorAsync(respuesta)); // Error
    } // Fin del método

    private async Task<string> CarpetaAsync(IntegracionGoogle conexion) // Devuelve la carpeta de la app (la crea si no existe o si la borraron)
    { // Inicio del método
        if (conexion.CarpetaDriveId is { } idGuardado) // ¿Ya la habíamos creado?
        { // Inicio del bloque
            using var r = await EnviarAsync(new HttpRequestMessage(HttpMethod.Get, $"{UrlsGoogle.Drive}files/{Uri.EscapeDataString(idGuardado)}?fields=id,trashed")); // ¿Sigue existiendo?
            if (r.IsSuccessStatusCode && (await LeerJsonAsync(r))["trashed"]?.GetValue<bool>() != true) return idGuardado; // Existe y no está en la papelera
        } // Fin del bloque
        var datos = JsonSerializer.Serialize(new { name = NombreCarpeta, mimeType = "application/vnd.google-apps.folder" }); // En Drive, una carpeta es un "archivo" de tipo carpeta
        using var respuesta = await EnviarAsync(new HttpRequestMessage(HttpMethod.Post, $"{UrlsGoogle.Drive}files?fields=id") { Content = new StringContent(datos, Encoding.UTF8, "application/json") }); // Crear
        if (!respuesta.IsSuccessStatusCode) throw new ErrorGoogle("Drive: " + await LeerErrorAsync(respuesta)); // Error
        conexion.CarpetaDriveId = (await LeerJsonAsync(respuesta))["id"]!.GetValue<string>(); // Guardamos el id de la carpeta
        await _db.SaveChangesAsync(); // En la base
        return conexion.CarpetaDriveId; // La devolvemos
    } // Fin del método

    // ---------- Google Calendar ----------

    public async Task<string> SincronizarEventoAsync(Pedido pedido) // Crea, actualiza o borra el evento según el pedido (no guarda: lo hace quien llama)
    { // Inicio del método
        var debeExistir = pedido.FechaEntrega.HasValue && pedido.Estado != EstadoPedido.Cancelado; // Solo hay evento si tiene fecha y no está cancelado
        var baseEventos = $"{UrlsGoogle.Calendar}calendars/{CalendarioId}/events"; // Dirección de los eventos del calendario

        if (!debeExistir) // No debería haber evento
        { // Inicio del bloque
            if (pedido.CalendarioEventoId is null) return "sin cambios"; // Y no hay: nada que hacer
            using var r = await EnviarAsync(new HttpRequestMessage(HttpMethod.Delete, $"{baseEventos}/{Uri.EscapeDataString(pedido.CalendarioEventoId)}")); // Borramos el evento
            if (!r.IsSuccessStatusCode && r.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone)) throw new ErrorGoogle("Calendar: " + await LeerErrorAsync(r)); // 404/410 = ya estaba borrado
            pedido.CalendarioEventoId = null; // El pedido ya no tiene evento
            return "evento borrado"; // Resultado
        } // Fin del bloque

        var cuerpo = JsonSerializer.Serialize(ArmarEvento(pedido)); // Datos del evento en JSON
        if (pedido.CalendarioEventoId is { } idEvento) // Ya tiene evento: lo actualizamos
        { // Inicio del bloque
            using var r = await EnviarAsync(new HttpRequestMessage(HttpMethod.Put, $"{baseEventos}/{Uri.EscapeDataString(idEvento)}") { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") }); // PUT = reemplazar
            if (r.IsSuccessStatusCode) return "evento actualizado"; // Listo
            if (r.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone)) throw new ErrorGoogle("Calendar: " + await LeerErrorAsync(r)); // Error real
            pedido.CalendarioEventoId = null; // Lo borraron a mano desde Calendar: creamos uno nuevo
        } // Fin del bloque
        using var respuesta = await EnviarAsync(new HttpRequestMessage(HttpMethod.Post, baseEventos) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") }); // POST = crear
        if (!respuesta.IsSuccessStatusCode) throw new ErrorGoogle("Calendar: " + await LeerErrorAsync(respuesta)); // Error
        pedido.CalendarioEventoId = (await LeerJsonAsync(respuesta))["id"]!.GetValue<string>(); // Guardamos el id del evento en el pedido
        return "evento creado"; // Resultado
    } // Fin del método

    private object ArmarEvento(Pedido p) // Datos del evento (todo el día, en la fecha de entrega)
    { // Inicio del método
        var url = _config["App:UrlPublica"]?.TrimEnd('/'); // Dirección pública de la app (para el enlace)
        var lineas = new List<string> { $"Estado: {EstadoMapper.Nombre(p.Estado)}", $"Monto: {Formato.Moneda(p.Monto)}" }; // Descripción del evento
        if (p.Items.Count > 0) lineas.Add("Productos: " + string.Join(", ", p.Items.Select(i => $"{i.Cantidad} x {i.ProductoNombre}"))); // Productos
        else lineas.Add("Detalle: " + p.Descripcion); // Pedidos viejos sin productos
        if (!string.IsNullOrWhiteSpace(url)) lineas.Add($"Ver pedido: {url}/Pedidos/Details/{p.Id}"); // Enlace a la app
        var dia = p.FechaEntrega!.Value; // Fecha de entrega
        return new // Objeto anónimo con la forma que pide Google Calendar
        { // Inicio del evento
            summary = (p.Estado == EstadoPedido.Entregado ? "✓ " : "") + $"Entrega pedido #{p.Id} · {p.Cliente?.Nombre}", // Título (con ✓ si ya se entregó)
            description = string.Join("\n", lineas), // Descripción
            location = p.Cliente?.Ciudad, // Ciudad del cliente
            start = new { date = dia.ToString("yyyy-MM-dd") }, // Evento de día completo: empieza ese día...
            end = new { date = dia.AddDays(1).ToString("yyyy-MM-dd") }, // ...y termina al día siguiente (así lo define Google)
            colorId = p.Estado switch { EstadoPedido.Entregado => "10", EstadoPedido.Enviado => "7", _ => "9" }, // Verde entregado, turquesa enviado, azul el resto
            status = "confirmed" // Confirmado (si estaba borrado, lo restaura)
        }; // Fin del evento
    } // Fin del método

    // ---------- Tokens y llamadas HTTP ----------

    private async Task<IntegracionGoogle> ConexionObligatoriaAsync() // Conexión o error claro
        => await ObtenerConexionAsync() ?? throw new ErrorGoogle("La cuenta de Google de la empresa no está conectada. Un administrador debe conectarla en Integraciones."); // Mensaje para el usuario

    private async Task<string> TokenAccesoAsync() // Token de acceso vigente (lo renueva con la llave permanente cuando vence)
    { // Inicio del método
        var conexion = await ConexionObligatoriaAsync(); // Conexión guardada
        await Candado.WaitAsync(); // Un hilo por vez
        try // Siempre liberamos el candado
        { // Inicio del try
            if (_tokenAcceso is not null && _conexionDelToken == conexion.FechaConexion && DateTime.UtcNow < _venceToken) return _tokenAcceso; // El de memoria sigue sirviendo
            string llave; // Llave permanente descifrada
            try { llave = Descifrar(conexion.RefreshTokenCifrado); } // Desciframos
            catch (CryptographicException) { throw new ErrorGoogle("No se pudo leer la conexión guardada (¿cambió el ClientSecret de Google?). Volvé a conectar la cuenta en Integraciones."); } // Clave de cifrado distinta
            JsonNode json; // Respuesta de Google
            try // Pedimos un token nuevo
            { // Inicio del try
                json = await PedirTokenAsync(new Dictionary<string, string> { ["client_id"] = _clientId!, ["client_secret"] = _clientSecret!, ["refresh_token"] = llave, ["grant_type"] = "refresh_token" }); // Canje de la llave permanente
            } // Fin del try
            catch (ErrorGoogle ex) when (ex.Message.Contains("invalid_grant")) // La llave ya no vale (permiso quitado, contraseña cambiada, app en modo prueba...)
            { // Inicio del catch
                conexion.UltimoError = "Google rechazó el permiso (se quitó o venció). Volvé a conectar la cuenta."; // Lo mostramos en Integraciones
                await _db.SaveChangesAsync(); // Guardamos el aviso
                throw new ErrorGoogle(conexion.UltimoError); // Error para quien llamó
            } // Fin del catch
            _tokenAcceso = json["access_token"]!.GetValue<string>(); // Token nuevo
            _venceToken = DateTime.UtcNow.AddSeconds((json["expires_in"]?.GetValue<int>() ?? 3600) - 120); // Vence un poco antes (margen de 2 minutos)
            _conexionDelToken = conexion.FechaConexion; // De qué conexión es
            if (conexion.UltimoError is not null) { conexion.UltimoError = null; await _db.SaveChangesAsync(); } // Volvió a andar: borramos el aviso
            return _tokenAcceso; // Lo devolvemos
        } // Fin del try
        finally { Candado.Release(); } // Liberamos el candado
    } // Fin del método

    private async Task<JsonNode> PedirTokenAsync(Dictionary<string, string> datos) // POST al servidor de tokens
    { // Inicio del método
        using var respuesta = await _http.PostAsync(UrlsGoogle.Token, new FormUrlEncodedContent(datos)); // Formulario codificado (así lo pide OAuth)
        var json = await LeerJsonAsync(respuesta); // Respuesta en JSON
        if (!respuesta.IsSuccessStatusCode) throw new ErrorGoogle($"Google: {json["error"]} {json["error_description"]}".Trim()); // Ej. "invalid_grant Token has been expired or revoked."
        return json; // Datos del token
    } // Fin del método

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage pedido, HttpCompletionOption opcion = HttpCompletionOption.ResponseContentRead) // Llamada con el token de acceso
    { // Inicio del método
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAccesoAsync()); // "Bearer <token>": así se autentica en las APIs de Google
        return await _http.SendAsync(pedido, opcion); // Enviamos
    } // Fin del método

    private static async Task<JsonNode> LeerJsonAsync(HttpResponseMessage respuesta) // Lee la respuesta como JSON
    { // Inicio del método
        var texto = await respuesta.Content.ReadAsStringAsync(); // Texto de la respuesta
        try { return JsonNode.Parse(texto) ?? new JsonObject(); } // Convertimos
        catch (JsonException) { return new JsonObject { ["error"] = texto.Length > 200 ? texto[..200] : texto }; } // No era JSON: devolvemos el texto como error
    } // Fin del método

    private static async Task<string> LeerErrorAsync(HttpResponseMessage respuesta) // Mensaje de error de una API de Google
    { // Inicio del método
        var json = await LeerJsonAsync(respuesta); // Google responde {"error": {"message": "..."}}
        var mensaje = json["error"] is JsonObject e ? e["message"]?.ToString() : json["error"]?.ToString(); // Tomamos el mensaje
        return $"{(int)respuesta.StatusCode} {mensaje}".Trim(); // Ej. "403 Insufficient Permission"
    } // Fin del método

    private static string? EmailDelIdToken(string? idToken) // El id_token es un JWT: "encabezado.datos.firma" en Base64
    { // Inicio del método
        var partes = idToken?.Split('.'); // Separamos las 3 partes
        if (partes is not { Length: 3 }) return null; // No tiene la forma esperada
        var datos = partes[1].Replace('-', '+').Replace('_', '/'); // Base64 "para URL" → Base64 normal
        datos = datos.PadRight(datos.Length + (4 - datos.Length % 4) % 4, '='); // Completamos el relleno
        try { return JsonNode.Parse(Convert.FromBase64String(datos))?["email"]?.GetValue<string>(); } // Leemos el email (llegó directo de Google por https, no hace falta verificar la firma)
        catch (Exception) { return null; } // Si algo falla, no es grave
    } // Fin del método

    // ---------- Cifrado de la llave permanente (AES-GCM) ----------

    private byte[] ClaveCifrado() => SHA256.HashData(Encoding.UTF8.GetBytes("GestionPedidos.Google|" + _clientSecret)); // Clave de 256 bits derivada del secreto de Google (que vive solo en Railway)

    private string Cifrar(string texto) // Texto → "nonce + etiqueta + cifrado" en Base64
    { // Inicio del método
        var nonce = RandomNumberGenerator.GetBytes(12); // Número al azar distinto en cada cifrado
        var datos = Encoding.UTF8.GetBytes(texto); // Texto en bytes
        var cifrado = new byte[datos.Length]; // Lugar para el resultado
        var etiqueta = new byte[16]; // "Sello" que detecta si alguien modificó el dato
        using var aes = new AesGcm(ClaveCifrado(), 16); // Algoritmo AES-GCM con etiqueta de 16 bytes
        aes.Encrypt(nonce, datos, cifrado, etiqueta); // Ciframos
        return Convert.ToBase64String(nonce.Concat(etiqueta).Concat(cifrado).ToArray()); // Todo junto en texto
    } // Fin del método

    private string Descifrar(string base64) // Proceso inverso
    { // Inicio del método
        var todo = Convert.FromBase64String(base64); // Bytes
        var cifrado = todo[28..]; // Después del nonce (12) y la etiqueta (16)
        var datos = new byte[cifrado.Length]; // Lugar para el texto
        using var aes = new AesGcm(ClaveCifrado(), 16); // Mismo algoritmo
        aes.Decrypt(todo[..12], cifrado, todo[12..28], datos); // Si la clave o el dato no coinciden, lanza CryptographicException
        return Encoding.UTF8.GetString(datos); // Texto original
    } // Fin del método
} // Fin de la clase

using System.Net.Http.Json; // Para PostAsJsonAsync (enviar JSON por HTTP)

namespace GestionPedidos.Services; // Namespace de los servicios

// Contrato para enviar emails (lo usa la recuperación de contraseña)
public interface IEnviadorEmail // Permite cambiar de proveedor sin tocar los controladores
{ // Inicio de la interfaz
    Task<bool> EnviarAsync(string para, string asunto, string html); // Devuelve true si el proveedor aceptó el envío
    bool EstaConfigurado { get; } // true si hay un proveedor real configurado
} // Fin de la interfaz

// Envía emails con la API HTTP de Brevo (Railway bloquea SMTP en los planes gratuitos, por eso se usa HTTP)
public class EnviadorEmail : IEnviadorEmail // Cumple el contrato
{ // Inicio de la clase
    private readonly HttpClient _http; // Cliente HTTP (configurado en Program.cs)
    private readonly IConfiguration _config; // Configuración (clave de la API y remitente)
    private readonly ILogger<EnviadorEmail> _logger; // Log del servidor

    public EnviadorEmail(HttpClient http, IConfiguration config, ILogger<EnviadorEmail> logger) // Inyección de dependencias
    { // Inicio del constructor
        _http = http; // Guardamos el HttpClient
        _config = config; // Guardamos la configuración
        _logger = logger; // Guardamos el logger
    } // Fin del constructor

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(_config["Email:BrevoApiKey"]) && !string.IsNullOrWhiteSpace(_config["Email:Remitente"]); // Hace falta la clave y el remitente

    public async Task<bool> EnviarAsync(string para, string asunto, string html) // Envía un email
    { // Inicio del método
        if (!EstaConfigurado) // Si no hay proveedor configurado...
        { // Inicio del bloque
            _logger.LogWarning("Email NO enviado (falta configurar Email__BrevoApiKey y Email__Remitente). Para: {Para}. Asunto: {Asunto}. Contenido: {Html}", para, asunto, html); // ...lo dejamos en el log del servidor (solo lo ve el dueño en Railway)
            return false; // Avisamos que no se envió
        } // Fin del bloque

        using var mensaje = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email"); // Pedido POST a la API de Brevo
        mensaje.Headers.Add("api-key", _config["Email:BrevoApiKey"]); // Clave de la API (variable de entorno, nunca en el código)
        mensaje.Content = JsonContent.Create(new // Cuerpo JSON con el formato que pide Brevo
        { // Inicio del JSON
            sender = new { email = _config["Email:Remitente"], name = "Gestión de Pedidos" }, // Remitente (debe estar verificado en Brevo)
            to = new[] { new { email = para } }, // Destinatario
            subject = asunto, // Asunto
            htmlContent = html // Contenido en HTML
        }); // Fin del JSON

        try // La red puede fallar
        { // Inicio del try
            var respuesta = await _http.SendAsync(mensaje); // Envía el pedido
            if (respuesta.IsSuccessStatusCode) return true; // 2xx = aceptado
            _logger.LogError("Brevo rechazó el email: {Codigo} {Cuerpo}", (int)respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync()); // Dejamos el motivo en el log
            return false; // No se envió
        } // Fin del try
        catch (Exception ex) // Error de red o timeout
        { // Inicio del catch
            _logger.LogError(ex, "No se pudo conectar con Brevo"); // Log del error
            return false; // No se envió
        } // Fin del catch
    } // Fin del método
} // Fin de la clase

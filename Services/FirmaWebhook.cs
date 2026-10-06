using System.Security.Cryptography; // HMACSHA256 y comparación segura
using System.Text; // Encoding.UTF8 (texto → bytes)

namespace GestionPedidos.Services; // Namespace de los servicios

// Firma HMAC-SHA256: quien envía el webhook "firma" el contenido con una clave secreta compartida.
// Nosotros calculamos la misma firma; si coincide, el aviso viene de alguien que conoce la clave y no fue modificado
public static class FirmaWebhook // Clase estática de ayuda
{ // Inicio de la clase
    public const string HeaderFirma = "X-Webhook-Signature"; // Header donde viaja la firma: "sha256=<hex>"
    public const string HeaderTimestamp = "X-Webhook-Timestamp"; // Header con el momento del envío (segundos Unix)
    public const string HeaderClave = "X-Webhook-Secret"; // Alternativa simple para pruebas manuales: la clave tal cual
    public static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(5); // Avisos con más de 5 minutos se rechazan (evita reenvíos de avisos viejos)

    // Calcula la firma de "timestamp.cuerpo" con la clave secreta
    public static string Calcular(string clave, string timestamp, string cuerpo) // Devuelve "sha256=abcdef..."
    { // Inicio del método
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(clave)); // Algoritmo HMAC con nuestra clave
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{cuerpo}")); // Firma de timestamp + "." + JSON exacto recibido
        return "sha256=" + Convert.ToHexString(bytes).ToLowerInvariant(); // Formato texto hexadecimal en minúsculas
    } // Fin del método

    // Compara dos textos en tiempo constante (evita que un atacante adivine la firma midiendo cuánto tardamos en responder)
    public static bool SonIguales(string a, string b) // true si son idénticos
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b)); // Comparación segura del framework
} // Fin de la clase

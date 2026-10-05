namespace GestionPedidos.Data; // Namespace de la capa de datos

// Clase de ayuda que arma la cadena de conexión a PostgreSQL según dónde corra la app
public static class ConexionDb // "static" = no hace falta crear objetos, se usa directo: ConexionDb.ObtenerCadena(...)
{ // Inicio de la clase
    // Devuelve la cadena de conexión que entiende Npgsql (formato "Host=...;Port=...;...")
    public static string ObtenerCadena(IConfiguration config) // Recibe la configuración (appsettings + variables de entorno)
    { // Inicio del método
        var url = Environment.GetEnvironmentVariable("DATABASE_URL"); // Railway y Render entregan la base en esta variable
        if (!string.IsNullOrWhiteSpace(url)) // Si existe (estamos publicados en la nube)...
            return ConvertirUrl(url); // ...la convertimos al formato de Npgsql

        var cadena = config.GetConnectionString("Default"); // Si no, usamos la de appsettings.json (para correr en tu PC)
        if (string.IsNullOrWhiteSpace(cadena)) // Si tampoco está configurada...
            throw new InvalidOperationException("Falta configurar la base: variable DATABASE_URL o ConnectionStrings:Default"); // ...frenamos con un error claro
        return cadena; // Devolvemos la cadena local
    } // Fin del método

    // Convierte "postgresql://usuario:clave@host:5432/base" a "Host=host;Port=5432;Database=base;..."
    private static string ConvertirUrl(string url) // "private" = solo se usa dentro de esta clase
    { // Inicio del método
        var uri = new Uri(url); // Uri separa la dirección en partes (usuario, host, puerto, ruta)
        var credenciales = uri.UserInfo.Split(':', 2); // "usuario:clave" → ["usuario", "clave"]
        var usuario = Uri.UnescapeDataString(credenciales[0]); // Decodifica caracteres especiales del usuario (%40 → @)
        var clave = credenciales.Length > 1 ? Uri.UnescapeDataString(credenciales[1]) : ""; // Lo mismo para la contraseña (si vino)
        var puerto = uri.Port > 0 ? uri.Port : 5432; // Si la URL no trae puerto, usamos el de PostgreSQL por defecto
        var baseDatos = uri.AbsolutePath.TrimStart('/'); // "/railway" → "railway" (nombre de la base)
        return $"Host={uri.Host};Port={puerto};Database={baseDatos};Username={usuario};Password={clave};SSL Mode=Prefer"; // Arma la cadena final; usa SSL si el servidor lo ofrece
    } // Fin del método
} // Fin de la clase

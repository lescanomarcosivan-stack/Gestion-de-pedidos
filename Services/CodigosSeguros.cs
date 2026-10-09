using System.Security.Cryptography; // Para números al azar seguros y SHA-256
using System.Text; // Para Encoding.UTF8

namespace GestionPedidos.Services; // Namespace de los servicios

// Códigos de un solo uso que viajan por email (invitaciones). En la base se guarda solo su hash
public static class CodigosSeguros // Clase estática de ayuda
{ // Inicio de la clase
    public static string Generar() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('='); // 32 bytes al azar en texto apto para URL (imposible de adivinar)

    public static string Hash(string codigo) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(codigo))); // SHA-256: si alguien lee la base, no puede usar el código
} // Fin de la clase

using System.Globalization; // Para NumberFormatInfo (separadores de miles y decimales)

namespace GestionPedidos.Services; // Namespace de los servicios

// Formatos de pantalla en estilo argentino: $ 1.234,56 y fechas dd/mm/aaaa
public static class Formato // Clase estática: se usa como Formato.Moneda(...)
{ // Inicio de la clase
    // Reglas de números en español: punto para miles, coma para decimales.
    // Se definen a mano (en vez de usar la cultura "es-AR") para que funcione igual en cualquier servidor
    private static readonly NumberFormatInfo Numeros = new() // "readonly" = se crea una vez y no cambia
    { // Inicio de la configuración
        NumberDecimalSeparator = ",", // 1234,56
        NumberGroupSeparator = ".", // 1.234
        NumberGroupSizes = new[] { 3 } // Agrupa de a 3 dígitos
    }; // Fin de la configuración

    public static string Moneda(decimal monto) => "$ " + monto.ToString("N2", Numeros); // 1234.5 → "$ 1.234,50"

    public static string Numero(int valor) => valor.ToString("N0", Numeros); // 12345 → "12.345"

    public static string Porcentaje(decimal valor) => valor.ToString("N1", Numeros) + " %"; // 12.34 → "12,3 %"

    // Muestra una fecha UTC en hora de Argentina (UTC-3 todo el año, sin horario de verano)
    public static string Fecha(DateTime fechaUtc) => fechaUtc.AddHours(-3).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture); // Resta 3 horas y da formato día/mes/año

    public static string Dia(DateOnly? dia) => dia?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—"; // Fecha sin hora (ej. fecha de entrega); "—" si no tiene

    public static DateOnly HoyArgentina() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)); // Día de hoy en Argentina (UTC-3)

    public static string Tamano(long bytes) => bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB" : (bytes / 1024d / 1024d).ToString("N1", Numeros) + " MB"; // 2.500.000 → "2,4 MB"

    public static string FechaCorta(DateTime fechaUtc) => fechaUtc.AddHours(-3).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture); // Solo el día, sin hora
} // Fin de la clase

namespace GestionPedidos.Services; // Namespace de los servicios

// Tipo de dato de una columna (define cómo se muestra y se alinea)
public enum TipoColumna // Lista cerrada
{ // Inicio de la lista
    Texto,  // Alineado a la izquierda
    Entero, // Número sin decimales, a la derecha
    Moneda  // Importe con 2 decimales, a la derecha
} // Fin de la lista

// Una columna del reporte: título, tipo y ancho relativo
public record ColumnaReporte(string Titulo, TipoColumna Tipo = TipoColumna.Texto, double Ancho = 1); // "record" = clase simple de datos

// Reporte genérico: lo arma el controlador y lo dibujan ExportadorExcel y ExportadorPdf (los dos usan los mismos datos)
public class Reporte // Clase de datos
{ // Inicio de la clase
    public string Titulo { get; set; } = string.Empty; // Título grande (ej. "Reporte de pedidos")
    public string Subtitulo { get; set; } = string.Empty; // Línea de abajo (filtros aplicados, fecha de generación)
    public List<ColumnaReporte> Columnas { get; set; } = new(); // Columnas, en orden
    public List<object?[]> Filas { get; set; } = new(); // Una fila = un arreglo de valores (string, int o decimal), mismo orden que las columnas
    public object?[]? Totales { get; set; } // Fila de totales (opcional, va en negrita al final)
    public List<string> Notas { get; set; } = new(); // Líneas de texto al pie (opcional)
} // Fin de la clase

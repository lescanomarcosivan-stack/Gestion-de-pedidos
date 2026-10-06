using System.Globalization; // Para escribir números con punto decimal (formato interno de PDF)
using System.Text; // StringBuilder, Encoding y normalización de tildes

namespace GestionPedidos.Services; // Namespace de los servicios

// Genera un PDF sin librerías externas: escribe directamente el formato PDF (texto, rectángulos y líneas).
// Usa la fuente Helvetica, que todos los lectores de PDF traen incorporada, así no hay que adjuntar fuentes
public static class ExportadorPdf // Clase estática: ExportadorPdf.Generar(reporte)
{ // Inicio de la clase
    private const double Ancho = 842, Alto = 595, Margen = 36; // Hoja A4 apaisada, en puntos (1 punto = 1/72 de pulgada)
    private const double AltoFila = 17, TamanoLetra = 8.5; // Medidas de la tabla

    // Ancho de cada carácter de Helvetica (en milésimas del tamaño de letra), del espacio (32) a la tilde ~ (126)
    private static readonly int[] AnchosHelvetica = { 278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, 1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, 333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584 }; // Tabla estándar de la fuente

    public static byte[] Generar(Reporte r) // Devuelve el archivo listo para descargar
    { // Inicio del método
        var anchoTabla = Ancho - 2 * Margen; // Ancho útil
        var totalPesos = r.Columnas.Sum(c => c.Ancho); // Suma de anchos relativos
        var anchos = r.Columnas.Select(c => c.Ancho / totalPesos * anchoTabla).ToArray(); // Ancho real de cada columna en puntos
        var paginas = new List<StringBuilder>(); // Contenido de cada página
        StringBuilder pagina = null!; // Página actual
        var y = 0.0; // Altura actual de escritura (en PDF el 0 está ABAJO de la hoja)

        void NuevaPagina() // Función local: empieza una hoja nueva con título y encabezados
        { // Inicio de la función
            pagina = new StringBuilder(); // Contenido vacío
            paginas.Add(pagina); // La agregamos a la lista
            y = Alto - Margen; // Arrancamos arriba
            if (paginas.Count == 1) // En la primera hoja: título grande y subtítulo
            { // Inicio del bloque
                Texto(pagina, r.Titulo, Margen, y - 14, 15, negrita: true, color: "0.04 0.18 0.35"); // Título azul marino
                Texto(pagina, r.Subtitulo, Margen, y - 30, 9, color: "0.42 0.49 0.58"); // Subtítulo gris azulado
                y -= 46; // Bajamos
            } // Fin del bloque
            else // En las siguientes: título chico
            { // Inicio del bloque
                Texto(pagina, r.Titulo + " (continuación)", Margen, y - 10, 9, negrita: true, color: "0.04 0.18 0.35"); // Recordatorio
                y -= 22; // Bajamos
            } // Fin del bloque
            Rectangulo(pagina, Margen, y - AltoFila, anchoTabla, AltoFila, "0.89 0.93 0.98"); // Fondo celeste del encabezado
            FilaTabla(pagina, r.Columnas.Select(c => (object?)c.Titulo).ToArray(), y, negrita: true, encabezado: true); // Títulos de columna
            y -= AltoFila; // Bajamos una fila
        } // Fin de la función

        void FilaTabla(StringBuilder p, object?[] valores, double arriba, bool negrita = false, bool encabezado = false) // Función local: dibuja una fila
        { // Inicio de la función
            var x = Margen; // Empieza en el margen izquierdo
            for (var c = 0; c < r.Columnas.Count; c++) // Una celda por columna
            { // Inicio del ciclo
                var tipo = r.Columnas[c].Tipo; // Tipo de la columna
                var texto = c < valores.Length ? Valor(valores[c], tipo) : ""; // Texto a mostrar
                texto = Recortar(texto, anchos[c] - 8, TamanoLetra, negrita); // Que entre en la columna
                var derecha = tipo != TipoColumna.Texto; // Números a la derecha
                var posX = derecha ? x + anchos[c] - 4 - Medir(texto, TamanoLetra, negrita) : x + 4; // Posición horizontal
                Texto(p, texto, posX, arriba - AltoFila + 5.5, TamanoLetra, negrita, encabezado ? "0.04 0.18 0.35" : "0.07 0.14 0.23"); // Escribe la celda
                x += anchos[c]; // Siguiente columna
            } // Fin del ciclo
            Linea(p, Margen, arriba - AltoFila, Margen + anchoTabla, arriba - AltoFila); // Línea fina debajo de la fila
        } // Fin de la función

        NuevaPagina(); // Primera hoja
        foreach (var fila in r.Filas) // Filas de datos
        { // Inicio del ciclo
            if (y - AltoFila < Margen + 18) NuevaPagina(); // Si no entra, hoja nueva
            FilaTabla(pagina, fila, y); // Dibuja la fila
            y -= AltoFila; // Bajamos
        } // Fin del ciclo
        if (r.Filas.Count == 0) // Sin datos
        { // Inicio del bloque
            Texto(pagina, "No hay datos para los filtros elegidos.", Margen + 4, y - 12, 9, color: "0.42 0.49 0.58"); // Aviso
            y -= AltoFila; // Bajamos
        } // Fin del bloque
        if (r.Totales is not null) // Fila de totales
        { // Inicio del bloque
            if (y - AltoFila < Margen + 18) NuevaPagina(); // Hoja nueva si hace falta
            Rectangulo(pagina, Margen, y - AltoFila, anchoTabla, AltoFila, "0.95 0.97 0.99"); // Fondo muy suave
            FilaTabla(pagina, r.Totales, y, negrita: true); // En negrita
            y -= AltoFila; // Bajamos
        } // Fin del bloque
        y -= 8; // Un poco de aire
        foreach (var nota in r.Notas) // Notas al pie
        { // Inicio del ciclo
            if (y - 12 < Margen + 18) NuevaPagina(); // Hoja nueva si hace falta
            Texto(pagina, nota, Margen, y - 10, 8, color: "0.42 0.49 0.58"); // Nota gris
            y -= 12; // Bajamos
        } // Fin del ciclo

        for (var i = 0; i < paginas.Count; i++) // Pie de página en todas las hojas (ahora sabemos el total)
            Texto(paginas[i], $"Página {i + 1} de {paginas.Count} · Gestión de Pedidos", Margen, 18, 7.5, color: "0.42 0.49 0.58"); // "Página 1 de 3"

        return Armar(paginas); // Convierte todo al formato de archivo PDF
    } // Fin del método

    // ---------- Dibujo (operadores del lenguaje PDF) ----------

    private static void Texto(StringBuilder p, string texto, double x, double y, double tamano, bool negrita = false, string color = "0 0 0") // Escribe un texto
        => p.Append($"{color} rg BT /{(negrita ? "F2" : "F1")} {N(tamano)} Tf {N(x)} {N(y)} Td ({Escapar(texto)}) Tj ET\n"); // rg = color, BT/ET = bloque de texto, Tf = fuente, Td = posición, Tj = mostrar

    private static void Rectangulo(StringBuilder p, double x, double y, double ancho, double alto, string color) // Rectángulo relleno
        => p.Append($"{color} rg {N(x)} {N(y)} {N(ancho)} {N(alto)} re f\n"); // re = rectángulo, f = rellenar

    private static void Linea(StringBuilder p, double x1, double y, double x2, double y2) // Línea fina celeste
        => p.Append($"0.84 0.89 0.95 RG 0.5 w {N(x1)} {N(y)} m {N(x2)} {N(y2)} l S\n"); // RG = color de línea, w = grosor, m = mover, l = línea, S = trazar

    // ---------- Texto ----------

    private static string Valor(object? valor, TipoColumna tipo) => valor switch // Convierte el valor a texto según su tipo
    { // Inicio de las opciones
        null => "", // Vacío
        decimal d when tipo == TipoColumna.Moneda => Formato.Moneda(d), // $ 1.234,56
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture), // Otro decimal
        int i => Formato.Numero(i), // 1.234
        _ => valor.ToString() ?? "" // Texto
    }; // Fin de las opciones

    private static double Medir(string texto, double tamano, bool negrita) // Ancho del texto en puntos
    { // Inicio del método
        double total = 0; // Acumulador
        foreach (var c in texto) // Carácter por carácter
        { // Inicio del ciclo
            var basico = c.ToString().Normalize(NormalizationForm.FormD)[0]; // "á" → "a" (misma medida que la letra sin tilde)
            total += basico is >= ' ' and <= '~' ? AnchosHelvetica[basico - 32] : 556; // Medida de la tabla (o una media si no está)
        } // Fin del ciclo
        return total * tamano / 1000 * (negrita ? 1.06 : 1); // La negrita es un poco más ancha
    } // Fin del método

    private static string Recortar(string texto, double maximo, double tamano, bool negrita) // Corta con "..." si no entra
    { // Inicio del método
        if (Medir(texto, tamano, negrita) <= maximo) return texto; // Entra: sin cambios
        while (texto.Length > 1 && Medir(texto + "...", tamano, negrita) > maximo) texto = texto[..^1]; // Saca letras del final hasta que entre
        return texto + "..."; // Agrega los puntos suspensivos
    } // Fin del método

    private static string Escapar(string texto) // Prepara el texto para el PDF
    { // Inicio del método
        var sb = new StringBuilder(); // Acumulador
        foreach (var c in texto.Replace("→", "->").Replace("–", "-").Replace("—", "-").Replace("…", "...")) // Reemplaza símbolos que la fuente básica no tiene
        { // Inicio del ciclo
            if (c is '(' or ')' or '\\') sb.Append('\\'); // Paréntesis y barra invertida se escapan con \
            sb.Append(c <= 255 ? c : '?'); // Caracteres fuera de Latin-1 (emojis, etc.) se reemplazan
        } // Fin del ciclo
        return sb.ToString(); // Texto listo
    } // Fin del método

    private static string N(double numero) => numero.ToString("0.##", CultureInfo.InvariantCulture); // Números con punto decimal (requisito del formato PDF)

    // ---------- Estructura del archivo PDF ----------

    private static byte[] Armar(List<StringBuilder> paginas) // Arma los "objetos" del PDF y la tabla de posiciones (xref)
    { // Inicio del método
        var objetos = new List<string> // Objetos numerados desde 1
        { // Inicio de la lista
            "<< /Type /Catalog /Pages 2 0 R >>", // 1: raíz del documento
            $"<< /Type /Pages /Kids [{string.Join(" ", paginas.Select((_, i) => $"{5 + i * 2} 0 R"))}] /Count {paginas.Count} >>", // 2: lista de páginas
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>", // 3: fuente normal (con tildes y ñ)
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>" // 4: fuente negrita
        }; // Fin de la lista
        foreach (var contenido in paginas) // Por cada hoja: un objeto página y un objeto con su contenido
        { // Inicio del ciclo
            var numeroContenido = objetos.Count + 2; // Número que va a tener el objeto de contenido
            objetos.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(Ancho)} {N(Alto)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {numeroContenido} 0 R >>"); // Página
            var texto = contenido.ToString(); // Dibujo de la página
            objetos.Add($"<< /Length {Encoding.Latin1.GetByteCount(texto)} >>\nstream\n{texto}endstream"); // Contenido (con su largo en bytes)
        } // Fin del ciclo

        using var salida = new MemoryStream(); // Archivo en memoria
        void Escribir(string s) { var b = Encoding.Latin1.GetBytes(s); salida.Write(b, 0, b.Length); } // Escribe texto como bytes Latin-1 (como pide WinAnsi)
        Escribir("%PDF-1.4\n"); // Encabezado: versión del formato
        var posiciones = new List<long>(); // Dónde empieza cada objeto (lo exige el formato)
        for (var i = 0; i < objetos.Count; i++) // Escribe cada objeto
        { // Inicio del ciclo
            posiciones.Add(salida.Position); // Anota la posición
            Escribir($"{i + 1} 0 obj\n{objetos[i]}\nendobj\n"); // Objeto numerado
        } // Fin del ciclo
        var inicioXref = salida.Position; // Dónde empieza la tabla de posiciones
        Escribir($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n"); // Encabezado de la tabla
        foreach (var pos in posiciones) Escribir($"{pos:D10} 00000 n \n"); // Una línea de 20 bytes por objeto
        Escribir($"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R >>\nstartxref\n{inicioXref}\n%%EOF\n"); // Cierre del archivo
        return salida.ToArray(); // Bytes del PDF
    } // Fin del método
} // Fin de la clase

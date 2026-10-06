using System.Globalization; // Para escribir números con punto decimal (formato interno de Excel)
using System.IO.Compression; // ZipArchive: un .xlsx es en realidad un .zip con archivos XML adentro
using System.Security; // SecurityElement.Escape: escapa < > & " ' para XML
using System.Text; // StringBuilder y Encoding

namespace GestionPedidos.Services; // Namespace de los servicios

// Genera un archivo Excel (.xlsx) sin librerías externas.
// Un .xlsx es un ZIP con varios XML (formato "Office Open XML"); acá armamos el mínimo necesario para que Excel lo abra
public static class ExportadorExcel // Clase estática: ExportadorExcel.Generar(reporte)
{ // Inicio de la clase
    // Estilos definidos en styles.xml (el número es la posición en <cellXfs>)
    private const int EstiloNormal = 0, EstiloEncabezado = 1, EstiloMoneda = 2, EstiloNegrita = 3, EstiloMonedaNegrita = 4, EstiloTitulo = 5; // Índices de estilo

    public static byte[] Generar(Reporte reporte) // Devuelve el archivo listo para descargar
    { // Inicio del método
        using var memoria = new MemoryStream(); // El ZIP se arma en memoria
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true)) // Abrimos el ZIP para escribir
        { // Inicio del ZIP
            Escribir(zip, "[Content_Types].xml", TiposDeContenido); // Qué tipo de archivo es cada parte
            Escribir(zip, "_rels/.rels", RelacionesRaiz); // Dónde está el libro
            Escribir(zip, "xl/workbook.xml", Libro); // El libro con una hoja llamada "Reporte"
            Escribir(zip, "xl/_rels/workbook.xml.rels", RelacionesLibro); // Dónde están la hoja y los estilos
            Escribir(zip, "xl/styles.xml", Estilos); // Negrita, fondo celeste y formato de moneda
            Escribir(zip, "xl/worksheets/sheet1.xml", Hoja(reporte)); // Los datos
        } // Fin del ZIP (al cerrarse termina de escribir)
        return memoria.ToArray(); // Bytes del archivo
    } // Fin del método

    // Arma el XML de la hoja: título, subtítulo, encabezados, filas, totales y notas
    private static string Hoja(Reporte r) // Recibe el reporte
    { // Inicio del método
        var xml = new StringBuilder(); // Acumulador de texto
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"); // Encabezado del XML
        xml.Append("<cols>"); // Anchos de columna
        for (var c = 0; c < r.Columnas.Count; c++) // Una definición por columna
            xml.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{(r.Columnas[c].Ancho * 12).ToString("0.#", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>"); // Ancho proporcional
        xml.Append("</cols><sheetData>"); // Empiezan los datos
        var fila = 1; // Número de fila de Excel (empieza en 1)
        xml.Append(Fila(fila++, new object?[] { r.Titulo }, _ => EstiloTitulo)); // Fila 1: título
        xml.Append(Fila(fila++, new object?[] { r.Subtitulo }, _ => EstiloNormal)); // Fila 2: subtítulo
        fila++; // Fila 3 vacía
        xml.Append(Fila(fila++, r.Columnas.Select(c => (object?)c.Titulo).ToArray(), _ => EstiloEncabezado)); // Encabezados con fondo celeste
        foreach (var datos in r.Filas) // Filas de datos
            xml.Append(Fila(fila++, datos, c => r.Columnas[c].Tipo == TipoColumna.Moneda ? EstiloMoneda : EstiloNormal)); // Moneda con formato 1.234,56
        if (r.Totales is not null) // Fila de totales (opcional)
            xml.Append(Fila(fila++, r.Totales, c => r.Columnas[c].Tipo == TipoColumna.Moneda ? EstiloMonedaNegrita : EstiloNegrita)); // En negrita
        if (r.Notas.Count > 0) fila++; // Una fila vacía antes de las notas
        foreach (var nota in r.Notas) // Notas al pie
            xml.Append(Fila(fila++, new object?[] { nota }, _ => EstiloNormal)); // Una nota por fila
        xml.Append("</sheetData></worksheet>"); // Cierre
        return xml.ToString(); // XML completo
    } // Fin del método

    // Arma una fila <row> con sus celdas <c>
    private static string Fila(int numero, object?[] valores, Func<int, int> estilo) // estilo(columna) = índice de estilo de esa celda
    { // Inicio del método
        var xml = new StringBuilder($"<row r=\"{numero}\">"); // Abre la fila
        for (var c = 0; c < valores.Length; c++) // Una celda por valor
        { // Inicio del ciclo
            var referencia = $"{Columna(c)}{numero}"; // Ej. "B5"
            switch (valores[c]) // Según el tipo de dato
            { // Inicio del switch
                case null: break; // Celda vacía: no se escribe
                case decimal d: xml.Append($"<c r=\"{referencia}\" s=\"{estilo(c)}\"><v>{d.ToString(CultureInfo.InvariantCulture)}</v></c>"); break; // Número decimal (Excel siempre usa punto internamente)
                case int i: xml.Append($"<c r=\"{referencia}\" s=\"{estilo(c)}\"><v>{i}</v></c>"); break; // Número entero
                default: xml.Append($"<c r=\"{referencia}\" s=\"{estilo(c)}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(valores[c]!.ToString())}</t></is></c>"); break; // Texto (escapado para XML)
            } // Fin del switch
        } // Fin del ciclo
        return xml.Append("</row>").ToString(); // Cierra la fila
    } // Fin del método

    private static string Columna(int indice) // 0 → "A", 1 → "B", ..., 26 → "AA"
        => indice < 26 ? ((char)('A' + indice)).ToString() : Columna(indice / 26 - 1) + (char)('A' + indice % 26); // Conversión a letras de Excel

    private static void Escribir(ZipArchive zip, string ruta, string contenido) // Agrega un archivo de texto al ZIP
    { // Inicio del método
        using var escritor = new StreamWriter(zip.CreateEntry(ruta, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)); // UTF-8 sin BOM
        escritor.Write(contenido); // Escribe el texto
    } // Fin del método

    // ---------- Partes fijas del formato (siempre iguales) ----------
    private const string TiposDeContenido = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>"; // Tipos MIME de cada parte
    private const string RelacionesRaiz = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>"; // Punto de entrada
    private const string Libro = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Reporte\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"; // Libro con una hoja
    private const string RelacionesLibro = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>"; // Partes del libro
    private const string Estilos = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" // Hoja de estilos
        + "<fonts count=\"3\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"14\"/><color rgb=\"FF0B2E59\"/><name val=\"Calibri\"/></font></fonts>" // Normal, negrita, título azul
        + "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE3EDF9\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" // Sin relleno, obligatorio de Excel, celeste
        + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" // Sin bordes
        + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" // Estilo base
        + "<cellXfs count=\"6\">" // Estilos de celda (el orden coincide con las constantes de arriba)
        + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" // 0 normal
        + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" // 1 encabezado: negrita + celeste
        + "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" // 2 moneda (#.##0,00 según el idioma de Excel)
        + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" // 3 negrita
        + "<xf numFmtId=\"4\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" // 4 moneda en negrita
        + "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" // 5 título
        + "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>"; // Fin de estilos
} // Fin de la clase

using GestionPedidos.Models; // Para usar EstadoPedido

namespace GestionPedidos.Services; // Namespace de los servicios (lógica reutilizable)

// Traduce los estados que llegan de afuera (en inglés, como "DELIVERED") a nuestro enum en español
public static class EstadoMapper // Clase estática: se usa como EstadoMapper.Traducir("DELIVERED")
{ // Inicio de la clase
    // Diccionario: texto recibido → estado interno. Ignora mayúsculas/minúsculas
    private static readonly Dictionary<string, EstadoPedido> Equivalencias = new(StringComparer.OrdinalIgnoreCase) // "readonly" = no se modifica después
    { // Inicio de las equivalencias
        ["PENDING"] = EstadoPedido.Pendiente, // Inglés → Pendiente
        ["PROCESSING"] = EstadoPedido.EnPreparacion, // Inglés → En preparación
        ["IN_PREPARATION"] = EstadoPedido.EnPreparacion, // Variante en inglés → En preparación
        ["SHIPPED"] = EstadoPedido.Enviado, // Inglés → Enviado
        ["IN_TRANSIT"] = EstadoPedido.Enviado, // Variante "en tránsito" → Enviado
        ["DELIVERED"] = EstadoPedido.Entregado, // El ejemplo del PDF → Entregado
        ["CANCELLED"] = EstadoPedido.Cancelado, // Inglés británico → Cancelado
        ["CANCELED"] = EstadoPedido.Cancelado, // Inglés americano → Cancelado
        ["PENDIENTE"] = EstadoPedido.Pendiente, // También aceptamos los nombres en español
        ["ENPREPARACION"] = EstadoPedido.EnPreparacion, // Español sin espacios
        ["EN_PREPARACION"] = EstadoPedido.EnPreparacion, // Español con guion bajo
        ["ENVIADO"] = EstadoPedido.Enviado, // Español
        ["ENTREGADO"] = EstadoPedido.Entregado, // Español
        ["CANCELADO"] = EstadoPedido.Cancelado // Español
    }; // Fin de las equivalencias

    // Intenta traducir; devuelve true si lo logró y deja el resultado en "estado"
    public static bool TryTraducir(string? texto, out EstadoPedido estado) // "out" = parámetro de salida
    { // Inicio del método
        estado = default; // Valor por defecto por si no se encuentra (Pendiente)
        if (string.IsNullOrWhiteSpace(texto)) return false; // Si vino vacío, no se puede traducir
        return Equivalencias.TryGetValue(texto.Trim(), out estado); // Busca en el diccionario (sin espacios sobrantes)
    } // Fin del método

    // Texto amigable para mostrar en pantalla
    public static string Nombre(EstadoPedido estado) => estado switch // "switch" elige según el valor
    { // Inicio de las opciones
        EstadoPedido.EnPreparacion => "En preparación", // Con espacio y tilde para la pantalla
        _ => estado.ToString() // Para el resto alcanza con el nombre del enum
    }; // Fin de las opciones

    // Color de Bootstrap para la etiqueta del estado
    public static string ColorBadge(EstadoPedido estado) => estado switch // Devuelve una clase CSS según el estado
    { // Inicio de las opciones
        EstadoPedido.Pendiente => "bg-secondary", // Gris
        EstadoPedido.EnPreparacion => "bg-info text-dark", // Celeste
        EstadoPedido.Enviado => "bg-primary", // Azul
        EstadoPedido.Entregado => "bg-success", // Verde
        EstadoPedido.Cancelado => "bg-danger", // Rojo
        _ => "bg-light text-dark" // Cualquier otro caso
    }; // Fin de las opciones

    // Muestra una fecha UTC en hora de Argentina (UTC-3 todo el año, sin horario de verano)
    public static string HoraArgentina(DateTime fechaUtc) => fechaUtc.AddHours(-3).ToString("dd/MM/yyyy HH:mm"); // Resta 3 horas y da formato día/mes/año
} // Fin de la clase

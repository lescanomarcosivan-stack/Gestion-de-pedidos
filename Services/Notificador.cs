using System.Net; // Para WebUtility.HtmlEncode (evita que un texto rompa el HTML del email)
using GestionPedidos.Models; // Para Pedido, Producto, EstadoPedido

namespace GestionPedidos.Services; // Namespace de los servicios

// Contrato: qué avisos manda la app
public interface INotificador // Los controladores dependen de esta interfaz
{ // Inicio de la interfaz
    void PedidoCreado(Pedido pedido); // Al cliente ("recibimos tu pedido") y a los usuarios internos ("pedido nuevo")
    void EstadoCambiado(Pedido pedido, EstadoPedido anterior, EstadoPedido nuevo); // Al cliente (enviado, entregado, cancelado) y a internos si se cancela
    void StockBajo(IEnumerable<Producto> productos); // A los usuarios internos
} // Fin de la interfaz

// Arma los emails y los deja en la cola. Nunca envía directamente (no hace esperar al usuario)
public class Notificador : INotificador // Cumple el contrato
{ // Inicio de la clase
    private static readonly EstadoPedido[] EstadosQueAvisanAlCliente = { EstadoPedido.Enviado, EstadoPedido.Entregado, EstadoPedido.Cancelado }; // Cambios que le interesan al cliente
    private readonly ColaEmails _cola; // Cola de envío
    private readonly IConfiguration _config; // Configuración (destinatarios internos, URL pública)

    public Notificador(ColaEmails cola, IConfiguration config) // Inyección de dependencias
    { // Inicio del constructor
        _cola = cola; // Guardamos la cola
        _config = config; // Guardamos la configuración
    } // Fin del constructor

    public void PedidoCreado(Pedido pedido) // Pedido nuevo
    { // Inicio del método
        AlCliente(pedido, $"Recibimos tu pedido #{pedido.Id}", $"<p>Hola {E(pedido.Cliente?.Nombre)},</p><p>Registramos tu pedido <strong>#{pedido.Id}</strong> por <strong>{Formato.Moneda(pedido.Monto)}</strong>.</p>{TablaItems(pedido)}<p>Te vamos a avisar cuando sea enviado.</p>", "Pedido creado"); // Email al cliente
        AInternos($"Pedido nuevo #{pedido.Id} · {pedido.Cliente?.Nombre}", $"<p>Se cargó el pedido <strong>#{pedido.Id}</strong> de <strong>{E(pedido.Cliente?.Nombre)}</strong> por {Formato.Moneda(pedido.Monto)}.</p>{TablaItems(pedido)}{Enlace($"/Pedidos/Details/{pedido.Id}", "Ver pedido")}", "Pedido creado"); // Email interno
    } // Fin del método

    public void EstadoCambiado(Pedido pedido, EstadoPedido anterior, EstadoPedido nuevo) // Cambio de estado (manual o webhook)
    { // Inicio del método
        if (anterior == nuevo) return; // Sin cambio real, sin aviso
        if (EstadosQueAvisanAlCliente.Contains(nuevo)) // Solo los cambios que le importan al cliente
        { // Inicio del bloque
            var texto = nuevo switch // Mensaje según el estado
            { // Inicio de las opciones
                EstadoPedido.Enviado => "ya está en camino", // Enviado
                EstadoPedido.Entregado => "fue entregado. ¡Gracias por tu compra!", // Entregado
                _ => "fue cancelado. Si no lo pediste, respondé este email." // Cancelado
            }; // Fin de las opciones
            AlCliente(pedido, $"Tu pedido #{pedido.Id}: {EstadoMapper.Nombre(nuevo)}", $"<p>Hola {E(pedido.Cliente?.Nombre)},</p><p>Tu pedido <strong>#{pedido.Id}</strong> {texto}</p>", $"Cambio de estado a {EstadoMapper.Nombre(nuevo)}"); // Email al cliente
        } // Fin del bloque
        if (nuevo == EstadoPedido.Cancelado) // Una cancelación también le interesa al equipo
            AInternos($"Pedido #{pedido.Id} cancelado", $"<p>El pedido <strong>#{pedido.Id}</strong> de {E(pedido.Cliente?.Nombre)} por {Formato.Moneda(pedido.Monto)} pasó de {EstadoMapper.Nombre(anterior)} a <strong>Cancelado</strong>. El stock se devolvió automáticamente.</p>{Enlace($"/Pedidos/Details/{pedido.Id}", "Ver pedido")}", "Pedido cancelado"); // Email interno
    } // Fin del método

    public void StockBajo(IEnumerable<Producto> productos) // Productos que bajaron al mínimo
    { // Inicio del método
        var lista = productos.ToList(); // Pasamos a lista para recorrerla
        if (lista.Count == 0) return; // Nada que avisar
        var filas = string.Concat(lista.Select(p => $"<li><strong>{E(p.Nombre)}</strong> ({E(p.Codigo)}): quedan {p.Stock} (mínimo {p.StockMinimo})</li>")); // Un renglón por producto
        AInternos($"Stock bajo: {lista.Count} producto(s)", $"<p>Estos productos llegaron al stock mínimo:</p><ul>{filas}</ul>{Enlace("/Productos?stockBajo=true", "Ver productos con stock bajo")}", "Stock bajo"); // Email interno
    } // Fin del método

    // ---------- Auxiliares ----------

    private void AlCliente(Pedido pedido, string asunto, string cuerpo, string motivo) // Email al cliente del pedido
    { // Inicio del método
        var cliente = pedido.Cliente; // Cliente (debe venir cargado)
        if (cliente is null || !cliente.NotificarPorEmail || string.IsNullOrWhiteSpace(cliente.Email)) return; // Sin consentimiento o sin email: no se envía
        _cola.Encolar(new MensajeEmail(cliente.Email, asunto, Plantilla(asunto, cuerpo), $"{motivo} (cliente)")); // A la cola
    } // Fin del método

    private void AInternos(string asunto, string cuerpo, string motivo) // Email a los usuarios internos configurados
    { // Inicio del método
        var destinatarios = (_config["Notificaciones:EmailsInternos"] ?? "") // Variable Notificaciones__EmailsInternos, ej. "ventas@empresa.com,deposito@empresa.com"
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); // Separa por coma o punto y coma
        foreach (var email in destinatarios) // Un email a cada uno
            _cola.Encolar(new MensajeEmail(email, asunto, Plantilla(asunto, cuerpo), $"{motivo} (interno)")); // A la cola
    } // Fin del método

    private static string TablaItems(Pedido pedido) // Tabla HTML con los productos del pedido
    { // Inicio del método
        if (pedido.Items.Count == 0) return string.Empty; // Pedidos viejos sin productos
        var filas = string.Concat(pedido.Items.Select(i => $"<tr><td>{E(i.ProductoNombre)}</td><td align=\"right\">{i.Cantidad}</td><td align=\"right\">{Formato.Moneda(i.PrecioUnitario)}</td><td align=\"right\">{(i.DescuentoPorcentaje > 0 ? Formato.Porcentaje(i.DescuentoPorcentaje) : "")}</td><td align=\"right\">{Formato.Moneda(i.Subtotal)}</td></tr>")); // Un renglón por producto
        return $"<table cellpadding=\"6\" style=\"border-collapse:collapse;font-size:14px\"><tr style=\"background:#e3edf9\"><th align=\"left\">Producto</th><th>Cant.</th><th>Precio</th><th>Desc.</th><th>Subtotal</th></tr>{filas}</table>"; // Tabla completa
    } // Fin del método

    private string Enlace(string ruta, string texto) // Botón con enlace a la app (solo si se configuró App__UrlPublica)
    { // Inicio del método
        var url = _config["App:UrlPublica"]; // URL pública de la app
        return string.IsNullOrWhiteSpace(url) ? string.Empty : $"<p><a href=\"{url.TrimEnd('/')}{ruta}\" style=\"background:#1e5aa8;color:#fff;padding:8px 14px;border-radius:6px;text-decoration:none\">{texto}</a></p>"; // Botón azul
    } // Fin del método

    private static string Plantilla(string titulo, string cuerpo) // Marco común de todos los emails (encabezado azul)
        => $"<div style=\"font-family:Arial,sans-serif;max-width:600px\"><div style=\"background:#0b2e59;color:#fff;padding:14px 18px;font-size:18px\">📦 Gestión de Pedidos</div><div style=\"padding:18px;color:#13233a\"><h2 style=\"color:#0b2e59;font-size:18px\">{E(titulo)}</h2>{cuerpo}</div></div>"; // HTML del email

    private static string E(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty); // Escapa < > & " para que un nombre no rompa el HTML
} // Fin de la clase

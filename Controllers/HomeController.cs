using System.Text; // Para Encoding.UTF8 (armar el cuerpo del webhook de prueba)
using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para ViewModels, EstadoPedido y Roles
using GestionPedidos.Services; // Para EstadoMapper y FirmaWebhook
using Microsoft.AspNetCore.Authorization; // Para [Authorize] y [AllowAnonymous]
using Microsoft.AspNetCore.Mvc; // Para Controller
using Microsoft.EntityFrameworkCore; // Para ToListAsync, CountAsync, SumAsync

namespace GestionPedidos.Controllers; // Namespace de los controladores

// Páginas generales: dashboard, webhooks (historial y prueba) y página de error
public class HomeController : Controller // Hereda de Controller
{ // Inicio de la clase
    private readonly AppDbContext _db; // Acceso a la base
    private readonly IHttpClientFactory _httpFactory; // Fábrica de clientes HTTP (para llamar a nuestro propio webhook)
    private readonly IConfiguration _config; // Configuración (clave del webhook)

    public HomeController(AppDbContext db, IHttpClientFactory httpFactory, IConfiguration config) // Inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos la base
        _httpFactory = httpFactory; // Guardamos la fábrica HTTP
        _config = config; // Guardamos la configuración
    } // Fin del constructor

    // GET / → Dashboard con pedidos y montos por estado
    public async Task<IActionResult> Index() // Página inicial
    { // Inicio del método
        var agrupado = await _db.Pedidos // Tabla Pedidos...
            .GroupBy(p => p.Estado) // ...agrupada por estado (GROUP BY "Estado")...
            .Select(g => new ResumenEstado { Estado = g.Key, Cantidad = g.Count(), Monto = g.Sum(p => p.Monto) }) // ...contando y sumando en la base
            .ToListAsync(); // Ejecuta la consulta (una sola ida a la base)

        var modelo = new DashboardViewModel // Datos para la vista
        { // Inicio de los datos
            PorEstado = Enum.GetValues<EstadoPedido>() // Todos los estados, en orden...
                .Select(e => agrupado.FirstOrDefault(a => a.Estado == e) ?? new ResumenEstado { Estado = e }) // ...con 0 si no hay pedidos en ese estado
                .ToList(), // Lista final
            TotalClientes = await _db.Clientes.CountAsync(), // Cantidad de clientes
            UltimosCambios = await _db.HistorialEstados // Actividad reciente
                .OrderByDescending(h => h.Fecha) // Más nuevos primero
                .ThenByDescending(h => h.Id) // Desempate
                .Take(8) // Los últimos 8
                .ToListAsync() // Ejecuta la consulta
        }; // Fin de los datos
        var vigentes = modelo.PorEstado.Where(r => r.Estado != EstadoPedido.Cancelado).ToList(); // Todos los estados menos Cancelado
        var cancelados = modelo.PorEstado.First(r => r.Estado == EstadoPedido.Cancelado); // La fila de los cancelados
        modelo.TotalPedidos = vigentes.Sum(r => r.Cantidad); // Total de pedidos SIN los cancelados (sumado en memoria: ya tenemos los datos)
        modelo.MontoTotal = vigentes.Sum(r => r.Monto); // Monto total SIN los cancelados (un pedido anulado no es plata real)
        modelo.PedidosCancelados = cancelados.Cantidad; // Cancelados: se informan aparte
        modelo.MontoCancelado = cancelados.Monto; // Monto cancelado: se informa aparte
        var abiertos = modelo.PorEstado.Where(r => !EstadoMapper.EstaCerrado(r.Estado)).ToList(); // Estados que siguen en curso
        modelo.PedidosAbiertos = abiertos.Sum(r => r.Cantidad); // Cantidad abierta
        modelo.MontoAbierto = abiertos.Sum(r => r.Monto); // Monto abierto
        return View("Dashboard", modelo); // Muestra Views/Home/Dashboard.cshtml
    } // Fin del método

    // GET /Home/Webhooks?resultado=procesados → historial de avisos recibidos
    public async Task<IActionResult> Webhooks(string? resultado) // Filtro opcional: "procesados" o "rechazados"
    { // Inicio del método
        var consulta = _db.EventosWebhook.AsQueryable(); // Tabla de eventos
        if (resultado == "procesados") consulta = consulta.Where(e => e.CodigoRespuesta == 200); // Solo los que actualizaron el pedido
        if (resultado == "rechazados") consulta = consulta.Where(e => e.CodigoRespuesta != 200); // Solo los rechazados
        var eventos = await consulta // Consulta filtrada...
            .OrderByDescending(e => e.FechaRecepcion) // ...del más reciente al más viejo...
            .ThenByDescending(e => e.Id) // ...con desempate...
            .Take(100) // ...solo los últimos 100...
            .ToListAsync(); // ...ejecuta la consulta
        ViewBag.Resultado = resultado; // Para marcar el filtro activo
        ViewBag.Configurado = !string.IsNullOrWhiteSpace(_config["Webhook:Secret"]); // Para avisar si falta la clave
        return View(eventos); // Muestra Views/Home/Webhooks.cshtml
    } // Fin del método

    // GET /Home/ProbarWebhook → formulario para enviar un evento de prueba
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador (modifica pedidos)
    public IActionResult ProbarWebhook() => View(new ProbarWebhookViewModel()); // Formulario con valores de ejemplo

    // POST /Home/ProbarWebhook → arma el JSON, lo firma y lo envía por HTTP REAL a nuestro propio webhook
    [HttpPost, ValidateAntiForgeryToken] // Formulario con token
    [Authorize(Roles = Roles.Edicion)] // Solo Administrador u Operador
    public async Task<IActionResult> ProbarWebhook(ProbarWebhookViewModel modelo) // Datos elegidos en el formulario
    { // Inicio del método
        if (!ModelState.IsValid) return View(modelo); // Pedido inválido → volvemos

        var cuerpo = System.Text.Json.JsonSerializer.Serialize(new { @event = "order.status.changed", orderId = modelo.PedidoId, status = modelo.Estado }); // Mismo JSON que mandaría el sistema externo
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(); // Momento actual en segundos Unix
        var clave = _config["Webhook:Secret"] ?? string.Empty; // Clave configurada en el servidor

        using var mensaje = new HttpRequestMessage(HttpMethod.Post, "api/webhooks/orders") // Pedido POST al webhook
        { // Inicio del pedido
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") // Cuerpo JSON
        }; // Fin del pedido
        if (modelo.Modo != "sin-firma") // En los modos con firma...
        { // Inicio del bloque
            var claveUsada = modelo.Modo == "firma-invalida" ? "clave-equivocada" : clave; // ...usamos la clave real o una falsa (para ver el rechazo)
            modelo.FirmaEnviada = FirmaWebhook.Calcular(claveUsada, timestamp, cuerpo); // Calculamos la firma
            mensaje.Headers.Add(FirmaWebhook.HeaderTimestamp, timestamp); // Header con el timestamp
            mensaje.Headers.Add(FirmaWebhook.HeaderFirma, modelo.FirmaEnviada); // Header con la firma
        } // Fin del bloque

        try // La llamada HTTP podría fallar
        { // Inicio del try
            var respuesta = await _httpFactory.CreateClient("interno").SendAsync(mensaje); // Envía de verdad por HTTP (igual que un sistema externo)
            modelo.CodigoRespuesta = (int)respuesta.StatusCode; // Código recibido
            modelo.RespuestaJson = await respuesta.Content.ReadAsStringAsync(); // Respuesta recibida
        } // Fin del try
        catch (HttpRequestException ex) // Si no se pudo conectar...
        { // Inicio del catch
            modelo.CodigoRespuesta = 0; // ...sin código
            modelo.RespuestaJson = ex.Message; // ...mostramos el motivo
        } // Fin del catch
        modelo.CuerpoEnviado = cuerpo; // Para mostrar qué se envió
        ModelState.Clear(); // Para que el formulario muestre los valores del modelo
        return View(modelo); // Volvemos a la pantalla con el resultado
    } // Fin del método

    [AllowAnonymous] // La página de error tiene que verse aunque no haya sesión
    public IActionResult Error() => View(); // GET /Home/Error → página de error genérica
} // Fin de la clase

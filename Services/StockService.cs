using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para Pedido, Producto, MovimientoStock
using Microsoft.EntityFrameworkCore; // Para ToListAsync

namespace GestionPedidos.Services; // Namespace de los servicios

// Contrato del servicio de stock: todas las entradas y salidas pasan por acá
public interface IStockService // Los controladores dependen de esta interfaz
{ // Inicio de la interfaz
    Task<List<string>> DescontarAsync(Pedido pedido, string usuario); // Venta: descuenta el stock de cada renglón. Devuelve errores (si hay, no toca nada)
    Task DevolverAsync(Pedido pedido, string usuario); // Cancelación: devuelve el stock de cada renglón
    Task<List<string>> AplicarCambioEstadoAsync(Pedido pedido, EstadoPedido anterior, EstadoPedido nuevo, string usuario); // Decide si hay que devolver o volver a descontar
    Task<string?> MoverAsync(Producto producto, TipoMovimiento tipo, int cantidad, string? motivo, string usuario); // Ingreso o ajuste manual. Devuelve un error o null
    List<Producto> CruzaronMinimo { get; } // Productos que en esta operación bajaron al stock mínimo (para avisar por email)
} // Fin de la interfaz

// Implementación. IMPORTANTE: no guarda; el controlador hace un único SaveChanges con el pedido + stock + movimientos (todo o nada)
public class StockService : IStockService // Cumple el contrato
{ // Inicio de la clase
    private readonly AppDbContext _db; // Acceso a la base (el mismo DbContext del controlador)

    public StockService(AppDbContext db) => _db = db; // Inyección de dependencias

    public List<Producto> CruzaronMinimo { get; } = new(); // Se llena durante la operación

    public async Task<List<string>> DescontarAsync(Pedido pedido, string usuario) // Venta
    { // Inicio del método
        var errores = new List<string>(); // Problemas encontrados (ej. stock insuficiente)
        var productos = await CargarProductosAsync(pedido); // Productos del pedido, leídos de la base
        foreach (var grupo in pedido.Items.GroupBy(i => i.ProductoId)) // Agrupa por producto (si el mismo producto está en dos renglones, se suman)
        { // Inicio del ciclo de validación
            var pedidas = grupo.Sum(i => i.Cantidad); // Unidades pedidas de ese producto
            var producto = productos[grupo.Key]; // El producto
            if (producto.Stock < pedidas) // Si no alcanza...
                errores.Add($"{producto.Nombre}: pediste {pedidas} y hay {producto.Stock} en stock"); // ...lo anotamos
        } // Fin del ciclo de validación
        if (errores.Count > 0) return errores; // Si algo no alcanza, no descontamos NADA

        foreach (var grupo in pedido.Items.GroupBy(i => i.ProductoId)) // Ahora sí, descontamos
            Registrar(productos[grupo.Key], TipoMovimiento.Venta, -grupo.Sum(i => i.Cantidad), pedido, usuario, null); // Salida (cantidad negativa)
        return errores; // Lista vacía = todo bien
    } // Fin del método

    public async Task DevolverAsync(Pedido pedido, string usuario) // Cancelación
    { // Inicio del método
        var productos = await CargarProductosAsync(pedido); // Productos del pedido
        foreach (var grupo in pedido.Items.GroupBy(i => i.ProductoId)) // Por cada producto
            Registrar(productos[grupo.Key], TipoMovimiento.Devolucion, grupo.Sum(i => i.Cantidad), pedido, usuario, "Pedido cancelado"); // Entrada (cantidad positiva)
    } // Fin del método

    public async Task<List<string>> AplicarCambioEstadoAsync(Pedido pedido, EstadoPedido anterior, EstadoPedido nuevo, string usuario) // Según el cambio de estado
    { // Inicio del método
        if (pedido.Items.Count == 0) return new(); // Pedidos viejos (sin productos): no tocan stock
        if (nuevo == EstadoPedido.Cancelado && anterior != EstadoPedido.Cancelado) // Se cancela...
        { // Inicio del bloque
            await DevolverAsync(pedido, usuario); // ...devolvemos la mercadería al stock
            return new(); // Sin errores
        } // Fin del bloque
        if (anterior == EstadoPedido.Cancelado && nuevo != EstadoPedido.Cancelado) // Se reabre un pedido cancelado...
            return await DescontarAsync(pedido, usuario); // ...hay que volver a descontar (puede fallar si ya no hay stock)
        return new(); // Cualquier otro cambio (ej. Pendiente → Enviado) no mueve stock: se descontó al crear el pedido
    } // Fin del método

    public Task<string?> MoverAsync(Producto producto, TipoMovimiento tipo, int cantidad, string? motivo, string usuario) // Ingreso o ajuste manual
    { // Inicio del método
        if (tipo is not (TipoMovimiento.Ingreso or TipoMovimiento.Ajuste)) return Task.FromResult<string?>("Solo se permiten ingresos y ajustes manuales"); // Ventas y devoluciones son automáticas
        if (cantidad == 0) return Task.FromResult<string?>("La cantidad no puede ser 0"); // Movimiento vacío
        if (tipo == TipoMovimiento.Ingreso && cantidad < 0) return Task.FromResult<string?>("Un ingreso debe ser positivo; para restar usá un ajuste"); // Coherencia
        if (tipo == TipoMovimiento.Ajuste && string.IsNullOrWhiteSpace(motivo)) return Task.FromResult<string?>("Indicá el motivo del ajuste"); // Los ajustes se justifican
        if (producto.Stock + cantidad < 0) return Task.FromResult<string?>($"El stock no puede quedar negativo (hay {producto.Stock})"); // Control
        Registrar(producto, tipo, cantidad, null, usuario, motivo); // Registra el movimiento
        return Task.FromResult<string?>(null); // Sin error
    } // Fin del método

    // Cambia el stock del producto y deja el movimiento registrado
    private void Registrar(Producto producto, TipoMovimiento tipo, int cantidad, Pedido? pedido, string usuario, string? motivo) // Método auxiliar
    { // Inicio del método
        var estabaBien = !producto.StockBajo; // ¿Estaba por encima del mínimo antes?
        producto.Stock += cantidad; // Suma o resta
        _db.MovimientosStock.Add(new MovimientoStock // Nuevo movimiento
        { // Inicio de los datos
            Producto = producto, // Producto (EF completa el Id al guardar)
            Tipo = tipo, // Tipo
            Cantidad = cantidad, // Cantidad (+ entra / - sale)
            StockResultante = producto.Stock, // Cómo quedó
            Pedido = pedido, // Pedido relacionado (EF completa PedidoId al guardar, aunque el pedido sea nuevo)
            Usuario = usuario, // Quién
            Motivo = motivo // Motivo (en ventas queda vacío: el pedido ya explica el movimiento)
        }); // Fin del movimiento
        if (estabaBien && producto.StockBajo && !CruzaronMinimo.Contains(producto)) CruzaronMinimo.Add(producto); // Si recién bajó del mínimo, lo anotamos para avisar
    } // Fin del método

    // Lee de la base los productos que aparecen en el pedido, en un diccionario Id → Producto
    private async Task<Dictionary<int, Producto>> CargarProductosAsync(Pedido pedido) // Método auxiliar
    { // Inicio del método
        var ids = pedido.Items.Select(i => i.ProductoId).Distinct().ToList(); // Ids sin repetir
        var lista = await _db.Productos.Where(p => ids.Contains(p.Id)).ToListAsync(); // SELECT ... WHERE "Id" IN (...)
        return lista.ToDictionary(p => p.Id); // Diccionario para buscarlos rápido
    } // Fin del método
} // Fin de la clase

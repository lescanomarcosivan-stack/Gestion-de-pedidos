using System.Threading.Channels; // Channel = cola en memoria segura para varios hilos
using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para RegistroActividad
using Microsoft.EntityFrameworkCore; // Para Include y FirstOrDefaultAsync

namespace GestionPedidos.Services; // Namespace de los servicios

// Cola de pedidos cuyo evento de Google Calendar hay que poner al día.
// Los controladores dejan el número de pedido y siguen: el usuario no espera a Google (y si Google falla, el pedido se guarda igual)
public class ColaCalendario // Se registra como Singleton (una sola cola para toda la app)
{ // Inicio de la clase
    private readonly Channel<int> _canal = Channel.CreateUnbounded<int>(); // Cola de números de pedido

    public void Encolar(int pedidoId) => _canal.Writer.TryWrite(pedidoId); // Deja el pedido en la cola (no bloquea)

    public IAsyncEnumerable<int> LeerTodos(CancellationToken cancelacion) => _canal.Reader.ReadAllAsync(cancelacion); // Entrega los pedidos a medida que llegan
} // Fin de la clase

// Trabajador en segundo plano: toma cada pedido de la cola y deja su evento igual al pedido (crear, actualizar o borrar)
public class CalendarioWorker : BackgroundService // Corre todo el tiempo junto con la app
{ // Inicio de la clase
    private readonly ColaCalendario _cola; // La cola
    private readonly IServiceScopeFactory _scopes; // Para crear servicios por cada pedido (DbContext, Google)
    private readonly ILogger<CalendarioWorker> _logger; // Log del servidor

    public CalendarioWorker(ColaCalendario cola, IServiceScopeFactory scopes, ILogger<CalendarioWorker> logger) // Inyección de dependencias
    { // Inicio del constructor
        _cola = cola; // Guardamos la cola
        _scopes = scopes; // Guardamos la fábrica de ámbitos
        _logger = logger; // Guardamos el logger
    } // Fin del constructor

    protected override async Task ExecuteAsync(CancellationToken cancelacion) // Se ejecuta mientras la app esté encendida
    { // Inicio del método
        await foreach (var pedidoId in _cola.LeerTodos(cancelacion)) // Espera pedidos y los procesa de a uno (así nunca hay dos cambios del mismo evento a la vez)
        { // Inicio del ciclo
            using var scope = _scopes.CreateScope(); // Ámbito nuevo por pedido
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // Base
            var google = scope.ServiceProvider.GetRequiredService<IGoogleEmpresa>(); // Servicio de Google
            try // Un pedido que falla no debe frenar a los demás
            { // Inicio del try
                if (!google.Configurado || await google.ObtenerConexionAsync() is null) continue; // Sin cuenta conectada: no hay nada que hacer
                var pedido = await db.Pedidos.Include(p => p.Cliente).Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == pedidoId); // Pedido con cliente y productos (para el texto del evento)
                if (pedido is null) continue; // Ya no existe
                var resultado = await google.SincronizarEventoAsync(pedido); // Crea, actualiza o borra el evento
                if (resultado == "sin cambios") continue; // No hizo nada: no ensuciamos el registro
                db.RegistrosActividad.Add(new RegistroActividad { Tipo = TipoRegistro.Google, Usuario = "sistema", Accion = "Calendar: " + resultado, Detalle = $"Pedido #{pedido.Id}" + (pedido.FechaEntrega is { } f ? $" · entrega {f:dd/MM/yyyy}" : ""), EntidadTipo = "Pedido", EntidadId = pedido.Id }); // Constancia
                await db.SaveChangesAsync(cancelacion); // Guarda el id del evento en el pedido y el registro
            } // Fin del try
            catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested) // Cualquier error salvo el apagado de la app
            { // Inicio del catch
                _logger.LogWarning(ex, "No se pudo sincronizar Google Calendar para el pedido {Pedido}", pedidoId); // Log del servidor
                try // Dejamos constancia visible en la pantalla Registro
                { // Inicio del try
                    db.ChangeTracker.Clear(); // Descartamos cambios a medio hacer
                    db.RegistrosActividad.Add(new RegistroActividad { Tipo = TipoRegistro.Google, Usuario = "sistema", Accion = "Calendar: error", Detalle = $"Pedido #{pedidoId}: {(ex is ErrorGoogle ? ex.Message : ex.GetType().Name + ": " + ex.Message)}", EntidadTipo = "Pedido", EntidadId = pedidoId }); // Motivo
                    await db.SaveChangesAsync(cancelacion); // Guardamos
                } // Fin del try
                catch (Exception ex2) { _logger.LogError(ex2, "Tampoco se pudo registrar el error de Calendar"); } // Si la base también falla, queda en el log
            } // Fin del catch
        } // Fin del ciclo
    } // Fin del método
} // Fin de la clase

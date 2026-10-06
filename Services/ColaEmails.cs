using System.Threading.Channels; // Channel = cola en memoria segura para varios hilos
using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para RegistroActividad

namespace GestionPedidos.Services; // Namespace de los servicios

// Un email pendiente de envío
public record MensajeEmail(string Para, string Asunto, string Html, string Motivo); // "record" = clase simple de solo datos

// Cola de emails: los controladores dejan el mensaje acá y siguen (el usuario no espera a que se mande)
public class ColaEmails // Se registra como Singleton (una sola cola para toda la app)
{ // Inicio de la clase
    private readonly Channel<MensajeEmail> _canal = Channel.CreateUnbounded<MensajeEmail>(); // Cola sin límite de tamaño

    public void Encolar(MensajeEmail mensaje) => _canal.Writer.TryWrite(mensaje); // Deja un mensaje en la cola (no bloquea)

    public IAsyncEnumerable<MensajeEmail> LeerTodos(CancellationToken cancelacion) => _canal.Reader.ReadAllAsync(cancelacion); // Va entregando los mensajes a medida que llegan
} // Fin de la clase

// Trabajador en segundo plano: toma los emails de la cola y los envía de a uno
public class EnvioEmailsWorker : BackgroundService // BackgroundService = tarea que corre todo el tiempo junto con la app
{ // Inicio de la clase
    private readonly ColaEmails _cola; // La cola
    private readonly IServiceScopeFactory _scopes; // Para crear servicios "por mensaje" (DbContext, enviador)
    private readonly ILogger<EnvioEmailsWorker> _logger; // Log del servidor

    public EnvioEmailsWorker(ColaEmails cola, IServiceScopeFactory scopes, ILogger<EnvioEmailsWorker> logger) // Inyección de dependencias
    { // Inicio del constructor
        _cola = cola; // Guardamos la cola
        _scopes = scopes; // Guardamos la fábrica de ámbitos
        _logger = logger; // Guardamos el logger
    } // Fin del constructor

    protected override async Task ExecuteAsync(CancellationToken cancelacion) // Se ejecuta al arrancar la app y no termina hasta que la app se apaga
    { // Inicio del método
        await foreach (var mensaje in _cola.LeerTodos(cancelacion)) // Espera mensajes y los procesa uno por uno
        { // Inicio del ciclo
            try // Un email que falla no debe frenar a los demás
            { // Inicio del try
                using var scope = _scopes.CreateScope(); // Ámbito nuevo por mensaje
                var enviador = scope.ServiceProvider.GetRequiredService<IEnviadorEmail>(); // Enviador (Brevo)
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); // Base, para dejar constancia
                var enviado = await enviador.EnviarAsync(mensaje.Para, mensaje.Asunto, mensaje.Html); // Intenta enviar
                db.RegistrosActividad.Add(new RegistroActividad // Registro en la bitácora (pantalla Registro, tipo Email)
                { // Inicio de los datos
                    Tipo = TipoRegistro.Email, // Tipo Email
                    Usuario = "sistema", // Lo hizo la app, no una persona
                    Accion = enviado ? "Email enviado" : "Email NO enviado", // Resultado
                    Detalle = $"{mensaje.Motivo} → {mensaje.Para} · \"{mensaje.Asunto}\"" + (enviado ? "" : " (proveedor sin configurar o con error; ver logs)") // Qué, a quién y por qué
                }); // Fin del registro
                await db.SaveChangesAsync(cancelacion); // Guardamos
            } // Fin del try
            catch (Exception ex) when (ex is not OperationCanceledException) // Cualquier error salvo el apagado de la app
            { // Inicio del catch
                _logger.LogError(ex, "Error procesando el email para {Para}", mensaje.Para); // Queda en el log y seguimos con el próximo
            } // Fin del catch
        } // Fin del ciclo
    } // Fin del método
} // Fin de la clase

using GestionPedidos.Data; // Para AppDbContext
using GestionPedidos.Models; // Para RegistroActividad y TipoRegistro

namespace GestionPedidos.Services; // Namespace de los servicios

// Contrato del servicio que escribe en la bitácora (accesos, actividad, webhooks y errores)
public interface IAuditoria // Los controladores dependen de esta interfaz
{ // Inicio de la interfaz
    void Registrar(TipoRegistro tipo, string accion, string? detalle = null, string? entidadTipo = null, int? entidadId = null, string? usuario = null); // Agrega el registro (se guarda junto con el próximo SaveChanges)
    Task RegistrarYGuardarAsync(TipoRegistro tipo, string accion, string? detalle = null, string? entidadTipo = null, int? entidadId = null, string? usuario = null); // Agrega y guarda en el momento
} // Fin de la interfaz

// Implementación: usa el mismo DbContext del pedido HTTP, así el registro se guarda junto con el cambio (o ninguno de los dos)
public class Auditoria : IAuditoria // Cumple el contrato IAuditoria
{ // Inicio de la clase
    private readonly AppDbContext _db; // Acceso a la base
    private readonly IHttpContextAccessor _http; // Acceso al pedido HTTP actual (para saber usuario e IP)

    public Auditoria(AppDbContext db, IHttpContextAccessor http) // Inyección de dependencias
    { // Inicio del constructor
        _db = db; // Guardamos el DbContext
        _http = http; // Guardamos el acceso al contexto HTTP
    } // Fin del constructor

    public void Registrar(TipoRegistro tipo, string accion, string? detalle = null, string? entidadTipo = null, int? entidadId = null, string? usuario = null) // Agrega sin guardar
    { // Inicio del método
        var contexto = _http.HttpContext; // Pedido HTTP actual (puede ser null fuera de un pedido)
        _db.RegistrosActividad.Add(new RegistroActividad // Nuevo registro
        { // Inicio de los datos
            Tipo = tipo, // Acceso / Actividad / Webhook / Error
            Accion = Recortar(accion, 120)!, // Qué pasó (recortado al tamaño de la columna)
            Detalle = Recortar(detalle, 2000), // Información extra
            EntidadTipo = entidadTipo, // Tipo de dato afectado
            EntidadId = entidadId, // Id del dato afectado
            Usuario = Recortar(usuario ?? contexto?.User.Identity?.Name ?? "anónimo", 150)!, // Usuario indicado, o el de la sesión, o "anónimo"
            Ip = contexto?.Connection.RemoteIpAddress?.ToString() // IP del cliente (Railway la informa gracias a ForwardedHeaders)
        }); // Fin del registro
    } // Fin del método

    public async Task RegistrarYGuardarAsync(TipoRegistro tipo, string accion, string? detalle = null, string? entidadTipo = null, int? entidadId = null, string? usuario = null) // Agrega y guarda
    { // Inicio del método
        Registrar(tipo, accion, detalle, entidadTipo, entidadId, usuario); // Reutiliza el método anterior
        await _db.SaveChangesAsync(); // INSERT en la base
    } // Fin del método

    private static string? Recortar(string? texto, int maximo) => texto is null || texto.Length <= maximo ? texto : texto[..maximo]; // Corta textos demasiado largos para que entren en la columna
} // Fin de la clase

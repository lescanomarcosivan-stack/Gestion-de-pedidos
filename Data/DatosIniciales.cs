using GestionPedidos.Models; // Para usar Cliente, Pedido, Usuario, etc.
using Microsoft.AspNetCore.Identity; // Para IPasswordHasher (cifrado seguro de contraseñas)
using Microsoft.EntityFrameworkCore; // Para AnyAsync y SaveChangesAsync

namespace GestionPedidos.Data; // Namespace de la capa de datos

// Carga datos de ejemplo la primera vez, así la app no arranca vacía en la revisión
public static class DatosIniciales // Clase estática de ayuda
{ // Inicio de la clase
    // Crea los usuarios de demostración (uno por rol) si todavía no hay usuarios
    public static async Task CargarUsuariosAsync(AppDbContext db, IPasswordHasher<Usuario> hasher, IConfiguration config) // Recibe la base, el cifrador y la configuración
    { // Inicio del método
        if (await db.Usuarios.AnyAsync()) return; // Si ya hay usuarios, no hacemos nada

        var clave = config["Demo:Password"]; // Contraseña de los usuarios demo (variable de entorno Demo__Password)
        if (string.IsNullOrWhiteSpace(clave)) clave = "Demo2026!"; // Si no se configuró, usamos una por defecto (cambiarla en producción)
        var adminEmail = config["Demo:AdminEmail"]; // Email del administrador (variable Demo__AdminEmail), opcional
        if (string.IsNullOrWhiteSpace(adminEmail)) adminEmail = "admin@demo.com"; // Si no se configuró, usamos uno de ejemplo

        var usuarios = new[] // Un usuario por cada rol, para poder probar los permisos
        { // Inicio de la lista
            new Usuario { Email = adminEmail.Trim().ToLowerInvariant(), Nombre = "Administrador", Rol = Roles.Administrador, Activo = true }, // Puede todo
            new Usuario { Email = "operador@demo.com", Nombre = "Operador Demo", Rol = Roles.Operador, Activo = true }, // Carga y modifica
            new Usuario { Email = "consulta@demo.com", Nombre = "Consulta Demo", Rol = Roles.Consulta, Activo = true } // Solo mira
        }; // Fin de la lista
        foreach (var u in usuarios) // Para cada usuario...
            u.PasswordHash = hasher.HashPassword(u, clave); // ...guardamos la contraseña cifrada (nunca el texto real)
        db.Usuarios.AddRange(usuarios); // Marcamos los usuarios para insertarlos
        await db.SaveChangesAsync(); // INSERT en PostgreSQL
    } // Fin del método

    // Crea clientes y pedidos de ejemplo si la base está vacía
    public static async Task CargarAsync(AppDbContext db) // "async Task" = método asíncrono (no bloquea mientras espera a la base)
    { // Inicio del método
        if (await db.Clientes.AnyAsync()) return; // Si ya hay clientes, no hacemos nada (evita duplicar datos)

        var clientes = new List<Cliente> // Lista de clientes de ejemplo
        { // Inicio de la lista
            new() { Nombre = "Ferretería El Tornillo", Email = "compras@eltornillo.com.ar", Telefono = "381-4001122", Ciudad = "San Miguel de Tucumán" }, // Cliente 1
            new() { Nombre = "Almacén Doña Rosa", Email = "rosa@almacenrosa.com.ar", Telefono = "381-4223344", Ciudad = "Yerba Buena" }, // Cliente 2
            new() { Nombre = "Distribuidora Norte", Email = "pedidos@distnorte.com.ar", Telefono = "381-4556677", Ciudad = "Tafí Viejo" }, // Cliente 3
            new() { Nombre = "Librería Cervantes", Email = "info@libcervantes.com.ar", Telefono = "11-45678901", Ciudad = "Buenos Aires" }, // Cliente 4
            new() { Nombre = "Kiosco La Esquina", Email = "laesquina@gmail.com", Telefono = "351-4889900", Ciudad = "Córdoba" } // Cliente 5
        }; // Fin de la lista
        db.Clientes.AddRange(clientes); // Marca los 5 clientes para insertarlos
        await db.SaveChangesAsync(); // Ejecuta el INSERT en PostgreSQL; ahora cada cliente tiene su Id

        var productos = new[] { "Caja de tornillos x500", "Pintura látex 20L", "Resma A4 x10", "Yerba 1kg x12", "Cable 2.5mm x100m" }; // Productos para inventar pedidos
        var estados = Enum.GetValues<EstadoPedido>(); // Todos los estados posibles, para variar
        var pedidos = new List<Pedido>(); // Lista vacía donde vamos a ir agregando pedidos
        for (var i = 1; i <= 25; i++) // Creamos 25 pedidos (así hay más de una página y existe el pedido 15 del PDF)
        { // Inicio del ciclo
            pedidos.Add(new Pedido // Agregamos un pedido nuevo a la lista
            { // Inicio de los datos del pedido
                ClienteId = clientes[i % clientes.Count].Id, // Reparte los pedidos entre los 5 clientes
                Descripcion = productos[i % productos.Length], // Elige un producto de la lista
                Monto = 1500m * i + 0.5m * (i % 3), // Monto inventado con algunos centavos, para ver el formato $ 1.234,50
                Estado = i <= 15 ? EstadoPedido.Pendiente : estados[i % estados.Length], // Los primeros 15 quedan Pendientes para probar el webhook
                FechaCreacion = DateTime.UtcNow.AddDays(-i), // Fechas escalonadas hacia atrás para probar filtros
                FechaActualizacion = DateTime.UtcNow.AddDays(-i) // Misma fecha de actualización que de creación
            }); // Fin del pedido
        } // Fin del ciclo
        db.Pedidos.AddRange(pedidos); // Marca los pedidos para insertarlos
        await db.SaveChangesAsync(); // Ejecuta el INSERT de los pedidos (ahora tienen Id)

        db.HistorialEstados.AddRange(pedidos.Select(p => new HistorialEstado // Cada pedido arranca con un registro de "Alta" en su historial
        { // Inicio de los datos
            PedidoId = p.Id, // Pedido
            Fecha = p.FechaCreacion, // Misma fecha que la creación
            EstadoAnterior = null, // No había estado antes
            EstadoNuevo = p.Estado, // Estado con el que se creó
            Origen = "Alta", // Se creó así
            Usuario = "datos de ejemplo" // No lo creó un usuario real
        })); // Fin del registro
        await db.SaveChangesAsync(); // INSERT del historial
    } // Fin del método
} // Fin de la clase

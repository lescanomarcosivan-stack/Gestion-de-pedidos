using GestionPedidos.Models; // Para usar Cliente, Pedido y EstadoPedido
using Microsoft.EntityFrameworkCore; // Para AnyAsync y SaveChangesAsync

namespace GestionPedidos.Data; // Namespace de la capa de datos

// Carga datos de ejemplo la primera vez, así la app no arranca vacía en la revisión
public static class DatosIniciales // Clase estática de ayuda
{ // Inicio de la clase
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
        for (var i = 1; i <= 20; i++) // Creamos 20 pedidos (así existe el pedido 15 del ejemplo del PDF)
        { // Inicio del ciclo
            pedidos.Add(new Pedido // Agregamos un pedido nuevo a la lista
            { // Inicio de los datos del pedido
                ClienteId = clientes[i % clientes.Count].Id, // Reparte los pedidos entre los 5 clientes
                Descripcion = productos[i % productos.Length], // Elige un producto de la lista
                Monto = 1500m * i, // Monto inventado: 1500, 3000, 4500...
                Estado = i <= 15 ? EstadoPedido.Pendiente : estados[i % estados.Length], // Los primeros 15 quedan Pendientes para probar el webhook
                FechaCreacion = DateTime.UtcNow.AddDays(-i), // Fechas escalonadas hacia atrás para probar filtros
                FechaActualizacion = DateTime.UtcNow.AddDays(-i) // Misma fecha de actualización que de creación
            }); // Fin del pedido
        } // Fin del ciclo
        db.Pedidos.AddRange(pedidos); // Marca los 20 pedidos para insertarlos
        await db.SaveChangesAsync(); // Ejecuta el INSERT de los pedidos
    } // Fin del método
} // Fin de la clase

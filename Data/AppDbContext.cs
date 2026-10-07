using GestionPedidos.Models; // Para poder usar las clases del modelo
using Microsoft.EntityFrameworkCore; // Entity Framework Core: traduce C# a SQL para PostgreSQL

namespace GestionPedidos.Data; // Namespace de la capa de acceso a datos

// El DbContext es "la conexión inteligente" a la base: cada DbSet es una tabla
public class AppDbContext : DbContext // Hereda de DbContext, la clase base de Entity Framework
{ // Inicio de la clase
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { } // Recibe la configuración (cadena de conexión) desde Program.cs

    public DbSet<Cliente> Clientes => Set<Cliente>(); // Tabla "Clientes"
    public DbSet<Pedido> Pedidos => Set<Pedido>(); // Tabla "Pedidos"
    public DbSet<EventoWebhook> EventosWebhook => Set<EventoWebhook>(); // Tabla "EventosWebhook" (avisos recibidos)
    public DbSet<Usuario> Usuarios => Set<Usuario>(); // Tabla "Usuarios" (quién puede entrar y con qué rol)
    public DbSet<HistorialEstado> HistorialEstados => Set<HistorialEstado>(); // Tabla "HistorialEstados" (cambios de estado de cada pedido)
    public DbSet<RegistroActividad> RegistrosActividad => Set<RegistroActividad>(); // Tabla "RegistrosActividad" (accesos, errores, webhooks, actividad)
    public DbSet<Producto> Productos => Set<Producto>(); // Tabla "Productos" (catálogo con stock)
    public DbSet<PedidoItem> PedidoItems => Set<PedidoItem>(); // Tabla "PedidoItems" (renglones de cada pedido)
    public DbSet<MovimientoStock> MovimientosStock => Set<MovimientoStock>(); // Tabla "MovimientosStock" (entradas y salidas de stock)
    public DbSet<ArchivoPedido> ArchivosPedido => Set<ArchivoPedido>(); // Tabla "ArchivosPedido" (adjuntos guardados en Google Drive)
    public DbSet<IntegracionGoogle> IntegracionesGoogle => Set<IntegracionGoogle>(); // Tabla "IntegracionesGoogle" (conexión con la cuenta de la empresa)

    // Nombres de todas las tablas: Program.cs los usa para detectar si la base es de una versión anterior
    public static readonly string[] TablasEsperadas = { "Clientes", "Pedidos", "EventosWebhook", "Usuarios", "HistorialEstados", "RegistrosActividad" }; // Deben coincidir con los DbSet de arriba

    // Se ejecuta una vez al armar el modelo: acá se definen reglas extra de la base
    protected override void OnModelCreating(ModelBuilder modelBuilder) // "override" = reemplaza el método de la clase base
    { // Inicio del método
        modelBuilder.Entity<Cliente>() // Configuración de la tabla Clientes
            .HasIndex(c => c.Email) // Crea un índice sobre la columna Email (búsquedas más rápidas)
            .IsUnique(); // ...y la hace única: no puede haber dos clientes con el mismo email

        modelBuilder.Entity<Pedido>() // Configuración de la tabla Pedidos
            .Property(p => p.Estado) // Sobre la columna Estado...
            .HasConversion<string>() // ...se guarda como texto ("Entregado") en vez de número (3): más legible
            .HasMaxLength(20); // ...con un máximo de 20 caracteres

        modelBuilder.Entity<Pedido>() // Otra vez la tabla Pedidos
            .Property(p => p.Monto) // Sobre la columna Monto...
            .HasPrecision(12, 2); // ...12 dígitos en total y 2 decimales (numeric(12,2) en PostgreSQL)

        modelBuilder.Entity<Pedido>() // Relación entre Pedido y Cliente
            .HasOne(p => p.Cliente) // Cada pedido tiene UN cliente...
            .WithMany(c => c.Pedidos) // ...y cada cliente tiene MUCHOS pedidos
            .HasForeignKey(p => p.ClienteId) // La columna que los une es ClienteId
            .OnDelete(DeleteBehavior.Restrict); // Impide borrar un cliente que tenga pedidos (protege los datos)

        modelBuilder.Entity<Pedido>() // Índice para el filtro por estado
            .HasIndex(p => p.Estado); // Acelera las búsquedas "mostrame los pedidos Pendientes"

        modelBuilder.Entity<Usuario>() // Configuración de la tabla Usuarios
            .HasIndex(u => u.Email) // Índice por email...
            .IsUnique(); // ...único: un email = una cuenta

        modelBuilder.Entity<HistorialEstado>() // Configuración de la tabla HistorialEstados
            .Property(h => h.EstadoNuevo) // Estado nuevo...
            .HasConversion<string>() // ...guardado como texto
            .HasMaxLength(20); // ...máximo 20 caracteres

        modelBuilder.Entity<HistorialEstado>() // Misma tabla
            .Property(h => h.EstadoAnterior) // Estado anterior (puede ser null)...
            .HasConversion<string>() // ...guardado como texto
            .HasMaxLength(20); // ...máximo 20 caracteres

        modelBuilder.Entity<HistorialEstado>() // Relación historial → pedido
            .HasOne(h => h.Pedido) // Cada cambio pertenece a UN pedido...
            .WithMany() // ...y un pedido tiene muchos cambios (sin lista en la clase Pedido, para no alterar su estructura)
            .HasForeignKey(h => h.PedidoId) // Columna que los une
            .OnDelete(DeleteBehavior.Cascade); // Si se borrara el pedido, se borra su historial

        modelBuilder.Entity<HistorialEstado>() // Índice del historial
            .HasIndex(h => h.PedidoId); // Acelera "mostrame el historial del pedido 15"

        modelBuilder.Entity<RegistroActividad>() // Configuración del registro
            .Property(r => r.Tipo) // Tipo de registro...
            .HasConversion<string>() // ...guardado como texto ("Acceso", "Error"...)
            .HasMaxLength(20); // ...máximo 20 caracteres

        modelBuilder.Entity<RegistroActividad>() // Índice del registro
            .HasIndex(r => r.Fecha); // Acelera ordenar por fecha (el listado siempre muestra los más nuevos)

        modelBuilder.Entity<RegistroActividad>() // Otro índice
            .HasIndex(r => new { r.EntidadTipo, r.EntidadId }); // Acelera "cambios del cliente 3"

        modelBuilder.Entity<Pedido>() // Descuento general del pedido
            .Property(p => p.DescuentoPorcentaje) // Columna DescuentoPorcentaje...
            .HasPrecision(5, 2); // ...hasta 100,00

        modelBuilder.Entity<Producto>() // Configuración de Productos
            .HasIndex(p => p.Codigo) // Índice por código...
            .IsUnique(); // ...único: no puede haber dos productos con el mismo código

        modelBuilder.Entity<Producto>() // Precio del producto
            .Property(p => p.Precio) // Columna Precio...
            .HasPrecision(12, 2); // ...numeric(12,2)

        modelBuilder.Entity<PedidoItem>() // Configuración de los renglones
            .Property(i => i.PrecioUnitario) // Precio copiado al vender...
            .HasPrecision(12, 2); // ...numeric(12,2)

        modelBuilder.Entity<PedidoItem>() // Descuento del renglón
            .Property(i => i.DescuentoPorcentaje) // Columna...
            .HasPrecision(5, 2); // ...hasta 100,00

        modelBuilder.Entity<PedidoItem>() // Relación renglón → pedido
            .HasOne(i => i.Pedido) // Cada renglón pertenece a UN pedido...
            .WithMany(p => p.Items) // ...y un pedido tiene MUCHOS renglones
            .HasForeignKey(i => i.PedidoId) // Columna que los une
            .OnDelete(DeleteBehavior.Cascade); // Si se borra el pedido, se borran sus renglones

        modelBuilder.Entity<PedidoItem>() // Relación renglón → producto
            .HasOne(i => i.Producto) // Cada renglón es de UN producto...
            .WithMany() // ...y un producto aparece en muchos renglones
            .HasForeignKey(i => i.ProductoId) // Columna que los une
            .OnDelete(DeleteBehavior.Restrict); // No se puede borrar un producto que ya se vendió

        modelBuilder.Entity<MovimientoStock>() // Configuración de movimientos
            .Property(m => m.Tipo) // Tipo de movimiento...
            .HasConversion<string>() // ...guardado como texto
            .HasMaxLength(20); // ...máximo 20 caracteres

        modelBuilder.Entity<MovimientoStock>() // Relación movimiento → producto
            .HasOne(m => m.Producto) // Cada movimiento es de UN producto...
            .WithMany() // ...y un producto tiene muchos movimientos
            .HasForeignKey(m => m.ProductoId) // Columna que los une
            .OnDelete(DeleteBehavior.Cascade); // Si se borrara el producto, se borran sus movimientos

        modelBuilder.Entity<MovimientoStock>() // Relación movimiento → pedido (opcional)
            .HasOne(m => m.Pedido) // Un movimiento puede venir de UN pedido...
            .WithMany() // ...y un pedido genera varios movimientos
            .HasForeignKey(m => m.PedidoId) // Columna que los une (puede ser null)
            .OnDelete(DeleteBehavior.SetNull); // Si se borrara el pedido, el movimiento queda sin referencia

        modelBuilder.Entity<MovimientoStock>() // Índice de movimientos
            .HasIndex(m => m.ProductoId); // Acelera "movimientos del producto 3"

        modelBuilder.Entity<ArchivoPedido>() // Relación archivo → pedido
            .HasOne(a => a.Pedido) // Cada archivo pertenece a UN pedido...
            .WithMany() // ...y un pedido tiene muchos archivos (sin lista en Pedido, para no cambiar su estructura)
            .HasForeignKey(a => a.PedidoId) // Columna que los une
            .OnDelete(DeleteBehavior.Cascade); // Si se borrara el pedido, se borran sus registros de archivos

        modelBuilder.Entity<ArchivoPedido>() // Índice de archivos
            .HasIndex(a => a.PedidoId); // Acelera "archivos del pedido 15"
    } // Fin del método
} // Fin de la clase

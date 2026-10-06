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
    } // Fin del método
} // Fin de la clase

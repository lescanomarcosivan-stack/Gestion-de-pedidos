using GestionPedidos.Models; // Para poder usar Cliente, Pedido y EventoWebhook
using Microsoft.EntityFrameworkCore; // Entity Framework Core: traduce C# a SQL para PostgreSQL

namespace GestionPedidos.Data; // Namespace de la capa de acceso a datos

// El DbContext es "la conexión inteligente" a la base: cada DbSet es una tabla
public class AppDbContext : DbContext // Hereda de DbContext, la clase base de Entity Framework
{ // Inicio de la clase
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { } // Recibe la configuración (cadena de conexión) desde Program.cs

    public DbSet<Cliente> Clientes => Set<Cliente>(); // Tabla "Clientes"
    public DbSet<Pedido> Pedidos => Set<Pedido>(); // Tabla "Pedidos"
    public DbSet<EventoWebhook> EventosWebhook => Set<EventoWebhook>(); // Tabla "EventosWebhook" (historial de avisos recibidos)

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
    } // Fin del método
} // Fin de la clase

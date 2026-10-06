using System.ComponentModel.DataAnnotations; // Trae los atributos de validación ([Required], [EmailAddress], etc.)
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation; // Trae [ValidateNever] para que MVC no valide ciertas propiedades

namespace GestionPedidos.Models; // Namespace de los modelos (las "tablas" de la aplicación)

// Representa a un cliente. Entity Framework crea una tabla "Clientes" a partir de esta clase
public class Cliente // Clase pública para poder usarla desde controladores y vistas
{ // Inicio de la clase
    public int Id { get; set; } // Clave primaria; PostgreSQL le asigna un número automático (1, 2, 3...)

    [Required(ErrorMessage = "El nombre es obligatorio")] // Validación: no se puede dejar vacío
    [StringLength(120, ErrorMessage = "Máximo 120 caracteres")] // Validación y tamaño de la columna en la base
    [Display(Name = "Nombre")] // Texto que se muestra en las etiquetas del formulario
    public string Nombre { get; set; } = string.Empty; // Nombre o razón social; arranca vacío para no ser null

    [Required(ErrorMessage = "El email es obligatorio")] // Validación: campo obligatorio
    [EmailAddress(ErrorMessage = "El email no tiene un formato válido")] // Validación: debe tener forma de email
    [StringLength(150)] // Tamaño máximo de la columna
    [Display(Name = "Email")] // Etiqueta del formulario
    public string Email { get; set; } = string.Empty; // Email del cliente; en la base es único (ver AppDbContext)

    [StringLength(30)] // Tamaño máximo de la columna
    [Display(Name = "Teléfono")] // Etiqueta del formulario
    public string? Telefono { get; set; } // Teléfono opcional; el "?" indica que puede quedar vacío (null)

    [StringLength(80)] // Tamaño máximo de la columna
    [Display(Name = "Ciudad")] // Etiqueta del formulario
    public string? Ciudad { get; set; } // Ciudad opcional; sirve para filtrar y mostrar

    [Display(Name = "Fecha de alta")] // Etiqueta para mostrar
    public DateTime FechaAlta { get; set; } = DateTime.UtcNow; // Fecha de creación en UTC (PostgreSQL exige UTC)

    [Display(Name = "Recibe notificaciones por email")] // Etiqueta del formulario
    public bool NotificarPorEmail { get; set; } // Consentimiento: solo se le escribe si está marcado (los clientes viejos quedan en false)

    [ValidateNever] // Le dice a MVC: no valides esta lista al recibir el formulario
    public List<Pedido> Pedidos { get; set; } = new(); // Relación 1 a muchos: un cliente tiene muchos pedidos
} // Fin de la clase

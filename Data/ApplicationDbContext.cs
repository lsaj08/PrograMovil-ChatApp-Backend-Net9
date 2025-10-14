using Microsoft.EntityFrameworkCore; // Importa EF Core para manejar bases de datos
using prograweb_chatapp_backend_net9.Models; // Importa el modelo UsuarioConectado

namespace prograweb_chatapp_backend_net9.Data
{
    /// Esta clase representa el contexto de base de datos de Entity Framework Core.
    /// Su función es actuar como intermediario entre la aplicación y la base de datos.
    public class ApplicationDbContext : DbContext
    {
        // Constructor que recibe las opciones configuradas (como cadena de conexión, proveedor SQL, etc.)
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // Esta propiedad representa la tabla "UsuariosConectados" en la base de datos.
        // Entity Framework la usa para leer, escribir, actualizar o eliminar registros.
        public DbSet<UsuariosConectados> UsuariosConectados { get; set; }
    }
}
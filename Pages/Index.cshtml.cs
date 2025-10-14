using Microsoft.AspNetCore.Mvc.RazorPages; // Importa la funcionalidad para Razor Pages
using Microsoft.EntityFrameworkCore; // Importa funcionalidades de Entity Framework Core (para consultas async, LINQ, etc.)
using prograweb_chatapp_backend_net9.Data; // Importa el DbContext definido por ti
using prograweb_chatapp_backend_net9.Models; // Importa el modelo que representa la tabla en la base de datos

// Define el modelo de página asociado a la Razor Page Index.cshtml
public class IndexModel : PageModel
{
    // Inyección de dependencia del DbContext para acceder a la base de datos
    private readonly ApplicationDbContext _context;

    // Constructor que recibe el contexto y lo guarda para uso interno
    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    // Lista pública que estará disponible para la vista (Index.cshtml)
    // Se llena al ejecutar el método OnGetAsync()
    public List<UsuariosConectados> Usuarios { get; set; } = [];

    // Método que se ejecuta automáticamente en GET requests
    // Se usa para cargar los datos al renderizar la página
    public async Task OnGetAsync()
    {
        // Recupera todos los usuarios conectados desde la base de datos
        // y los ordena de más reciente a más antiguo por la fecha de conexión
        Usuarios = await _context.UsuariosConectados
            .OrderByDescending(u => u.FechaConexion)
            .ToListAsync();
    }
}

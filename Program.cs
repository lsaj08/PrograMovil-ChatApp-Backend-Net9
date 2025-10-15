using Microsoft.Azure.SignalR; // Importa SignalR para Azure
using Microsoft.Azure.SignalR.Management;
using Microsoft.EntityFrameworkCore; // EF Core
using prograweb_chatapp_backend_net9.Data; // Contexto de base de datos
using prograweb_chatapp_backend_net9.Hubs; // Hub del chat

var builder = WebApplication.CreateBuilder(args);

// Define el nombre de una política de CORS personalizada
var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

// Configura CORS para permitir que ciertos dominios accedan al backend
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
                      {
                          policy.SetIsOriginAllowed(origin =>
                              origin == "https://ashy-bay-0ea136f10.2.azurestaticapps.net" ||
                              origin == "https://programovil.net" ||
                              origin == "http://localhost:3000" ||
                              origin == "http://10.0.2.2:3000" ||
                              origin == "http://localhost:5173" ||
                              origin == "http://10.0.2.2:5173"
                          )

                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                      });
});

// Registra los servicios necesarios para usar SignalR con Azure SignalR Service
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 64 * 1024;
})
.AddAzureSignalR(builder.Configuration["Azure:SignalR:ConnectionString"]);

// 👉 Registrar ServiceManager para el NegotiateController
builder.Services.AddSingleton<IServiceManager>(sp =>
{
    var connectionString = builder.Configuration["Azure:SignalR:ConnectionString"];
    return (IServiceManager)new ServiceManagerBuilder()
        .WithOptions(option =>
        {
            option.ConnectionString = connectionString;
        })
        .BuildServiceManager();
});

// Registra los controladores para usar API
builder.Services.AddControllers();

// Agrega el contexto de base de datos con cadena de conexión a Azure SQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Registra Razor Pages si se usan
builder.Services.AddRazorPages();

// Crea la aplicación web
var app = builder.Build();

// Configura encabezados de seguridad
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers.Remove("X-Powered-By");
    context.Response.Headers.Remove("Server");
    await next();
});

// Redirige HTTP a HTTPS
app.UseHttpsRedirection();

// Aplica la política de CORS
app.UseCors(MyAllowSpecificOrigins);

// Habilita la autorización (si se usa más adelante)
app.UseAuthorization();

// Mapea los controladores API
app.MapControllers();

// Mapea el endpoint del hub de SignalR
app.MapHub<ChatHub>("/chat");

// Mapea Razor Pages si se usan
app.MapRazorPages();

// Inicia la aplicación
app.Run();

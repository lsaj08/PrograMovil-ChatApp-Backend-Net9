using Microsoft.AspNetCore.SignalR;                    // Para comunicación en tiempo real con SignalR
using Microsoft.Extensions.Logging;                    // Para registrar eventos e información útil
using prograweb_chatapp_backend_net9.Data;             // Para acceder al ApplicationDbContext
using prograweb_chatapp_backend_net9.Models;           // Para usar el modelo UsuariosConectados
using System.Threading;                                // Para operaciones thread-safe como Interlocked
using System.Threading.Tasks;                          // Para operaciones asíncronas
using Microsoft.Extensions.DependencyInjection;        // Para crear un contexto de BD en segundo plano

namespace prograweb_chatapp_backend_net9.Hubs
{
    public class ChatHub : Hub
    {
        /// ⚙️ Contador estático de usuarios conectados
        private static int _connectedUsers = 0;

        private readonly ILogger<ChatHub> _logger;                     // Para registrar eventos y errores
        private readonly IServiceScopeFactory _scopeFactory;           // Para generar un contexto separado en background

        /// 🚀 Constructor del Hub con inyección de dependencias
        /// Solo se inyecta ILogger y IServiceScopeFactory (NO ApplicationDbContext directamente)
        public ChatHub(ILogger<ChatHub> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        /// 🕓 Función reutilizable para obtener la hora actual de Costa Rica en formato ISO 8601
        private string GetCostaRicaTimeIso()
        {
            var crTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time"); // Zona horaria
            var fechaHora = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, crTimeZone);          // Convierte desde UTC
            return fechaHora.ToString("o"); // Devuelve fecha con formato ISO (ej: 2025-08-05T16:34:12.123Z)
        }

        /// ✅ Evento que permite enviar un mensaje a todos los usuarios conectados
        public async Task SendMessage(string user, string message)
        {
            try
            {
                await Clients.All.SendAsync("ReceiveMessage", new
                {
                    user,
                    message,
                    fechaHoraCostaRica = GetCostaRicaTimeIso()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SendMessage ERROR] Error al enviar mensaje");
            }
        }

        /// 📡 Evento automático que se dispara al conectarse un usuario al Hub
        public override async Task OnConnectedAsync()
        {
            Interlocked.Increment(ref _connectedUsers); // Incrementa el contador de usuarios conectados

            var username = Context.GetHttpContext()?.Request.Query["username"].ToString(); // Obtiene el nombre del usuario desde la query

            if (!string.IsNullOrWhiteSpace(username))
            {
                var fechaIso = GetCostaRicaTimeIso();

                //   Guardar la conexión del usuario en la base de datos de forma asíncrona y segura
                //    Utilizamos Task.Run para no bloquear el hilo principal
                //    Creamos un nuevo scope manualmente porque el contexto original puede estar fuera de alcance
                _ = Task.Run(async () =>
                {
                    // Crea un scope de servicios (lifetime independiente)
                    using var scope = _scopeFactory.CreateScope();

                    // Obtiene una nueva instancia del contexto dentro del scope
                    var scopedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    try
                    {
                        var fecha = DateTime.Parse(fechaIso); // Convierte string ISO a DateTime
                        var user = new UsuariosConectados
                        {
                            Username = username,
                            FechaConexion = fecha
                        };

                        scopedContext.UsuariosConectados.Add(user);  // Agrega al contexto
                        await scopedContext.SaveChangesAsync();      // Guarda en la base de datos

                        _logger.LogInformation("[GUARDADO] Usuario {Username} registrado correctamente", username);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[ERROR DB] Falló el guardado de usuario {Username}", username);
                    }
                });

                // 2️⃣ Mensaje de bienvenida solo para el nuevo usuario
                await Clients.Caller.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"¡Bienvenido {username}!",
                    fechaHoraCostaRica = fechaIso
                });

                // 3️⃣ Notifica a todos los demás que un nuevo usuario se ha conectado
                await Clients.Others.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"{username} se ha conectado.",
                    fechaHoraCostaRica = fechaIso
                });

                // 4️⃣ Actualiza el contador de usuarios conectados para todos
                await Clients.All.SendAsync("UpdateUserCount", _connectedUsers);
            }

            await base.OnConnectedAsync();
        }

        /// 🔌 Evento automático que se dispara cuando un usuario se desconecta del Hub
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Interlocked.Decrement(ref _connectedUsers); // Decrementa el contador de usuarios conectados

            var username = Context.GetHttpContext()?.Request.Query["username"].ToString();

            if (!string.IsNullOrWhiteSpace(username))
            {
                var fechaIso = GetCostaRicaTimeIso();

                // Notifica a todos los usuarios que alguien se desconectó
                await Clients.All.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"{username} se ha desconectado.",
                    fechaHoraCostaRica = fechaIso
                });
            }

            // Actualiza el contador en la interfaz
            await Clients.All.SendAsync("UpdateUserCount", _connectedUsers);

            await base.OnDisconnectedAsync(exception);
        }

        // 🔐 Relay de ciphertext (no tocar contenido)
        public Task SendCipher(string payloadJson)
        {
            _logger.LogDebug("SendCipher size={len}", payloadJson?.Length ?? 0);
            return Clients.All.SendAsync("ReceiveCipher", payloadJson);
        }

        // 🔑 Intercambio de claves públicas (relay puro)
        public Task SharePublicKey(string publicKeyJson)
        {
            _logger.LogDebug("SharePublicKey size={len}", publicKeyJson?.Length ?? 0);
            return Clients.All.SendAsync("ReceivePublicKey", publicKeyJson);
        }
    }
}
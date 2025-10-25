using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using prograweb_chatapp_backend_net9.Data;
using prograweb_chatapp_backend_net9.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace prograweb_chatapp_backend_net9.Hubs
{
    public class ChatHub : Hub
    {
        private static int _connectedUsers = 0;
        private readonly ILogger<ChatHub> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public ChatHub(ILogger<ChatHub> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private string GetCostaRicaTimeIso()
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time");
            var nowCr = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            return nowCr.ToString("o");
        }

        // ===== Texto plano (legacy) =====
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
                _logger.LogError(ex, "[SendMessage ERROR]");
            }
        }

        public override async Task OnConnectedAsync()
        {
            Interlocked.Increment(ref _connectedUsers);

            var username = Context.GetHttpContext()?.Request.Query["username"].ToString();
            if (!string.IsNullOrWhiteSpace(username))
            {
                var fechaIso = GetCostaRicaTimeIso();

                _ = Task.Run(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    try
                    {
                        var fecha = DateTime.Parse(fechaIso);
                        db.UsuariosConectados.Add(new UsuariosConectados
                        {
                            Username = username,
                            FechaConexion = fecha
                        });
                        await db.SaveChangesAsync();
                        _logger.LogInformation("[GUARDADO] Usuario {Username} registrado", username);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[ERROR DB] Falló el guardado {Username}", username);
                    }
                });

                await Clients.Caller.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"¡Bienvenido {username}!",
                    fechaHoraCostaRica = fechaIso
                });

                await Clients.Others.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"{username} se ha conectado.",
                    fechaHoraCostaRica = fechaIso
                });

                await Clients.All.SendAsync("UpdateUserCount", _connectedUsers);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Interlocked.Decrement(ref _connectedUsers);

            var username = Context.GetHttpContext()?.Request.Query["username"].ToString();
            if (!string.IsNullOrWhiteSpace(username))
            {
                await Clients.All.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"{username} se ha desconectado.",
                    fechaHoraCostaRica = GetCostaRicaTimeIso()
                });
            }

            await Clients.All.SendAsync("UpdateUserCount", _connectedUsers);
            await base.OnDisconnectedAsync(exception);
        }
        
            public Task SendCipher(string payloadJson)
                => Clients.All.SendAsync("ReceiveCipher", payloadJson);

            public Task SharePublicKey(string publicKeyJson)
                => Clients.All.SendAsync("ReceivePublicKey", publicKeyJson);
        }
}
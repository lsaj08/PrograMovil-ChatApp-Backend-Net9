using Microsoft.AspNetCore.SignalR;                    // Comunicación en tiempo real con SignalR
using Microsoft.Extensions.Logging;                    // Registro de eventos
using prograweb_chatapp_backend_net9.Data;             // Acceso a ApplicationDbContext
using prograweb_chatapp_backend_net9.Models;           // Modelo UsuariosConectados
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace prograweb_chatapp_backend_net9.Hubs
{
    /// <summary>
    /// Hub de chat grupal.
    /// - Publica mensajes de sistema y contador de usuarios.
    /// - Emite UserPresenceChanged para sincronizar presencia en clientes (útil para E2EE).
    /// - Relay de SharePublicKey y SendCipher sin inspeccionar contenido (E2EE extremo a extremo).
    /// - Expone GetOnlineUsers y envía OnlineUsersSnapshot al conectar.
    /// </summary>
    public class ChatHub : Hub
    {
        // Contador de usuarios conectados. Se manipula con Interlocked para concurrencia.
        private static int _connectedUsers = 0;

        // Mapa de conexiones: connectionId -> username (para snapshots y presencia).
        private static readonly ConcurrentDictionary<string, string> _connections = new();

        private readonly ILogger<ChatHub> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public ChatHub(ILogger<ChatHub> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Hora actual de Costa Rica en ISO 8601. Soporta Windows ("Central America Standard Time")
        /// y Linux containers ("America/Costa_Rica"). Si falla, usa UTC como último recurso.
        /// </summary>
        private string GetCostaRicaTimeIso()
        {
            DateTime crNow;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time");
                crNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                try
                {
                    var tzIana = TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");
                    crNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzIana);
                }
                catch
                {
                    crNow = DateTime.UtcNow;
                }
            }
            return crNow.ToString("o");
        }

        /// <summary>
        /// Obtiene el nombre de usuario desde la query (?username=...) y lo normaliza.
        /// </summary>
        private string GetUsernameFromContext()
        {
            var raw = Context.GetHttpContext()?.Request?.Query["username"].ToString() ?? "";
            return (raw ?? string.Empty).Trim();
        }

        /// <summary>
        /// Mensaje de sistema público (no cifrado). Para mensajes de usuarios, usar SendCipher.
        /// </summary>
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
                _logger.LogError(ex, "[SendMessage] Error al enviar mensaje");
            }
        }

        /// <summary>
        /// Al conectarse: incrementa contador, registra presencia en DB (fire-and-forget),
        /// envía mensajes de sistema, presencia y snapshot de usuarios al cliente que entra.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            Interlocked.Increment(ref _connectedUsers);

            var username = GetUsernameFromContext();
            var fechaIso = GetCostaRicaTimeIso();

            if (!string.IsNullOrWhiteSpace(username))
            {
                // Asociar connectionId -> username
                _connections[Context.ConnectionId] = username;

                // Persistir en DB sin bloquear el hilo del Hub
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var scopedContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                        var fecha = DateTime.Parse(fechaIso);
                        var user = new UsuariosConectados
                        {
                            Username = username,
                            FechaConexion = fecha
                        };

                        scopedContext.UsuariosConectados.Add(user);
                        await scopedContext.SaveChangesAsync();

                        _logger.LogInformation("[OnConnected] Usuario {Username} registrado en DB", username);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[OnConnected] Error al registrar usuario {Username} en DB", username);
                    }
                });

                // Bienvenida al que entra
                await Clients.Caller.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"¡Bienvenido {username}!",
                    fechaHoraCostaRica = fechaIso
                });

                // Aviso a los demás
                await Clients.Others.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"{username} se ha conectado.",
                    fechaHoraCostaRica = fechaIso
                });
            }

            // Publicar contador y presencia
            await Clients.All.SendAsync("UpdateUserCount", _connectedUsers);
            await Clients.All.SendAsync("UserPresenceChanged", new { username = username, isOnline = true });

            // Enviar snapshot de usuarios al que entra
            var snapshot = _connections.Values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await Clients.Caller.SendAsync("OnlineUsersSnapshot", snapshot);

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Al desconectarse: decrementa contador, borra connectionId del mapa,
        /// y emite mensajes de sistema, contador y presencia.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Interlocked.Decrement(ref _connectedUsers);

            var username = GetUsernameFromContext();
            var fechaIso = GetCostaRicaTimeIso();

            // Remover conexión del mapa
            _connections.TryRemove(Context.ConnectionId, out _);

            if (!string.IsNullOrWhiteSpace(username))
            {
                await Clients.All.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"{username} se ha desconectado.",
                    fechaHoraCostaRica = fechaIso
                });
            }

            await Clients.All.SendAsync("UpdateUserCount", _connectedUsers);
            await Clients.All.SendAsync("UserPresenceChanged", new { username = username, isOnline = false });

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Devuelve el listado actual de usuarios conectados (únicos y ordenados).
        /// El cliente puede pedirlo al abrir o refrescar el panel de usuarios.
        /// </summary>
        public Task<string[]> GetOnlineUsers()
        {
            var list = _connections.Values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Task.FromResult(list);
        }

        /// <summary>
        /// Relay de ciphertext E2EE. El backend no lee ni modifica el contenido.
        /// El payload JSON debe contener { from, to, iv, cipher } y será entregado como string.
        /// </summary>
        public Task SendCipher(string payloadJson)
        {
            _logger.LogDebug("SendCipher size={len}", payloadJson?.Length ?? 0);
            return Clients.All.SendAsync("ReceiveCipher", payloadJson);
        }

        /// <summary>
        /// Relay de claves públicas E2EE. El backend no interpreta el contenido.
        /// El payload JSON debe contener { username, algorithm, publicKeyB64 } y será entregado como string.
        /// </summary>
        public Task SharePublicKey(string publicKeyJson)
        {
            _logger.LogDebug("SharePublicKey size={len}", publicKeyJson?.Length ?? 0);
            return Clients.All.SendAsync("ReceivePublicKey", publicKeyJson);
        }
    }
}

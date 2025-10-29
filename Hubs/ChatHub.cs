using Microsoft.AspNetCore.SignalR;                    // Comunicación en tiempo real con SignalR
using Microsoft.Extensions.Logging;                    // Registro de eventos
using prograweb_chatapp_backend_net9.Data;             // Acceso a ApplicationDbContext
using prograweb_chatapp_backend_net9.Models;           // Modelo UsuariosConectados
using System;
using System.Threading;
using System.Threading.Tasks;

namespace prograweb_chatapp_backend_net9.Hubs
{
    /// <summary>
    /// Hub de chat grupal.
    /// - Al conectar/desconectar: publica mensajes de sistema, actualiza contador y emite UserPresenceChanged.
    /// - E2EE: el backend solo actúa como relay de claves y ciphertext (no toca contenido).
    /// </summary>
    public class ChatHub : Hub
    {
        // Contador estático de usuarios conectados (protegido con Interlocked)
        private static int _connectedUsers = 0;

        private readonly ILogger<ChatHub> _logger;           // Registro de eventos/errores
        private readonly IServiceScopeFactory _scopeFactory;  // Crear alcances para DbContext en tareas en background

        /// <summary>
        /// Constructor con inyección de dependencias.
        /// </summary>
        public ChatHub(ILogger<ChatHub> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        /// <summary>
        /// Obtiene la hora actual de Costa Rica en formato ISO 8601, con fallback a IANA si aplica.
        /// </summary>
        private string GetCostaRicaTimeIso()
        {
            DateTime crNow;
            try
            {
                // Windows: "Central America Standard Time"
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time");
                crNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                try
                {
                    // Linux/Containers: IANA "America/Costa_Rica"
                    var tzIana = TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");
                    crNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzIana);
                }
                catch
                {
                    // Fallback: UTC (no ideal, pero evita romper)
                    crNow = DateTime.UtcNow;
                }
            }
            // Formato ISO 8601 "o" (incluye fracciones de segundo y offset)
            return crNow.ToString("o");
        }

        /// <summary>
        /// Obtiene el nombre de usuario desde la querystring (?username=...) y lo normaliza.
        /// </summary>
        private string GetUsernameFromContext()
        {
            var raw = Context.GetHttpContext()?.Request?.Query["username"].ToString() ?? "";
            return (raw ?? string.Empty).Trim();
        }

        /// <summary>
        /// Envía un mensaje público a todos (mensaje de texto plano de sistema/diagnóstico).
        /// Para mensajes de chat de usuarios final, se recomienda usar E2EE con SendCipher.
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
        /// Evento automático al conectarse un cliente.
        /// - Incrementa contador.
        /// - Registra conexión en la base de datos (fire-and-forget).
        /// - Envía mensaje de bienvenida al que entra y de conexión al resto.
        /// - Publica contador actualizado.
        /// - Emite UserPresenceChanged { username, isOnline = true } para sincronización de presencia en frontend.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            Interlocked.Increment(ref _connectedUsers);

            var username = GetUsernameFromContext();
            var fechaIso = GetCostaRicaTimeIso();

            if (!string.IsNullOrWhiteSpace(username))
            {
                // Guardar la conexión en la base de datos sin bloquear el hilo del Hub
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

                // Mensaje de bienvenida al nuevo usuario
                await Clients.Caller.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"¡Bienvenido {username}!",
                    fechaHoraCostaRica = fechaIso
                });

                // Notifica a los demás que un usuario se ha conectado
                await Clients.Others.SendAsync("ReceiveMessage", new
                {
                    user = "Sistema",
                    message = $"{username} se ha conectado.",
                    fechaHoraCostaRica = fechaIso
                });
            }

            // Publica contador y presencia
            await Clients.All.SendAsync("UpdateUserCount", _connectedUsers);
            await Clients.All.SendAsync("UserPresenceChanged", new { username = username, isOnline = true });

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Evento automático al desconectarse un cliente.
        /// - Decrementa contador.
        /// - Publica mensaje de sistema de desconexión (si hay username).
        /// - Publica contador actualizado.
        /// - Emite UserPresenceChanged { username, isOnline = false } para que el frontend limpie claves E2EE.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Interlocked.Decrement(ref _connectedUsers);

            var username = GetUsernameFromContext();
            var fechaIso = GetCostaRicaTimeIso();

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
        /// Relay de ciphertext E2EE. El backend no lee ni modifica el contenido.
        /// El payload JSON debe contener { from, to, iv, cipher } y será entregado como string a los clientes.
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

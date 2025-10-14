using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.SignalR.Management;

namespace prograweb_chatapp_backend_net9.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class NegotiateController : ControllerBase
    {
        private readonly IServiceManager _serviceManager;

        public NegotiateController(IServiceManager serviceManager)
        {
            _serviceManager = serviceManager;
        }

        [HttpGet]
        public IActionResult Get()
        {
            // 🔹 Nombre del hub, debe coincidir con el de Program.cs
            var hubName = "chat"; // Asegúrate de que coincida

            // 🔸 Genera la URL del cliente hacia Azure SignalR
            var url = _serviceManager.GetClientEndpoint(hubName);

            // 🔸 Genera el token JWT para autenticar el cliente
            var token = _serviceManager.GenerateClientAccessToken(hubName);

            // 🔸 Devuelve la información tal como la espera la app Android
            return Ok(new
            {
                negotiateVersion = 1,
                url,          // URL de Azure SignalR
                accessToken = token
            });
        }
    }
}
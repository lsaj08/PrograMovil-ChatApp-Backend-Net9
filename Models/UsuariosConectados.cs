namespace prograweb_chatapp_backend_net9.Models
{
    /// Este modelo representa un registro individual en la tabla UsuariosConectados.
    /// Se usa para guardar datos de cada usuario que se conecta al chat.
    public class UsuariosConectados
    {
        public int Id { get; set; }  // Clave primaria única para cada registro (la base de datos la genera automáticamente)

        public string Username { get; set; } = string.Empty; // Nombre del usuario que se conectó

        public DateTime FechaConexion { get; set; } // Fecha y hora exacta en la que el usuario se conectó
    }
}
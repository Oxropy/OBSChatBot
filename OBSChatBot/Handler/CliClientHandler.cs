using System;
using OBSChatBot.Twitch;

namespace OBSChatBot.Handler
{
    public class CliClientHandler : IClientHandler
    {
        public void OnConnect(Client client)
        {
            Console.WriteLine("Connected as '{0}'",  client.User);
        }

        public void OnDisconnect(Client client)
        {
            Console.WriteLine("Disconnected with '{0}'", client.User);
        }
    }
}

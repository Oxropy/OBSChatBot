namespace OBSChatBot.Twitch
{
    public interface IClientHandler
    {
        void OnConnect(Client client);
        void OnDisconnect(Client client);
    }
}

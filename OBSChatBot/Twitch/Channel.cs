namespace OBSChatBot.Twitch
{
    public class Channel
    {
        public Client Client { get; private set; }
        public string Name { get; private set; }
        public IChannelHandler Handler { get; private set; }

        public Channel(Client client, string channel, IChannelHandler handler)
        {
            Client = client;
            Name = channel;
            Handler = handler;
        }

        public void SendMessage(string message)
        {
            Client.SendMessage(Name, message);
        }
    }
}

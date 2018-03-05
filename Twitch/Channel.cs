namespace OBSChatBot.Twitch
{
    public class Channel
    {
        public Client Client { get; private set; }
        public string Name { get; private set; }
        public IChannelHandler Handler { get; private set; }

        public Channel(Client client, string channel, IChannelHandler handler)
        {
            this.Client = client;
            this.Name = channel;
            this.Handler = handler;
        }

        public void LeaveChannel()
        {
            Client.LeaveChannel(Name);
        }

        public void SendMessage(string message)
        {
            Client.SendMessage(Name, message);
        }
    }
}

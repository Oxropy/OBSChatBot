using TwitchLib.Client.Models;
using TwitchLib.Client.Events;
using TwitchLib.Client;

namespace OBSChatBot.Twitch
{
    public class Client
    {
        public string User;
        private IClientHandler ClientHandler;
        private TwitchClient TClient;
        private Channel Channel;

        #region Konstruktor
        public Client(string user, string accesToken, IClientHandler handler)
        {
            User = user;
            ClientHandler = handler;
            var credentials = new ConnectionCredentials(user, accesToken);
            TClient = new TwitchClient();
            TClient.Initialize(credentials);

            TClient.OnJoinedChannel += Client_OnJoinedChannel;
            TClient.OnMessageReceived += Client_OnMessageReceived;
            TClient.OnConnected += Client_OnConnected;
        }
        #endregion

        #region Methoden
        public void Connect()
        {
            TClient.Connect();
        }

        public void Disconnect()
        {
            TClient.Disconnect();
        }

        public Channel JoinChannel(string name, IChannelHandler handler)
        {
            if (Channel == null)
            {
                TClient.JoinChannel(name);
                Channel = new Channel(this, name, handler);
            }

            return Channel;
        }

        public void LeaveChannel(string channel)
        {
            TClient.LeaveChannel(channel);
        }

        public void SendMessage(string channel, string message)
        {
            TClient.SendMessage(channel, message);
        }
        #endregion

        #region Events
        private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            Channel.Handler.OnJoin(Channel);
        }
        
        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            ChatMessage message = e.ChatMessage;
            Channel.Handler.OnMessage(Channel, message);
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            ClientHandler.OnConnect(this);
        }
        #endregion
    }
}

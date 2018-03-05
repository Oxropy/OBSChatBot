using System.Collections.Generic;
using TwitchLib;
using TwitchLib.Models.Client;
using TwitchLib.Events.Client;

namespace OBSChatBot.Twitch
{
    public class Client
    {
        public string User { get; private set; }
        private IClientHandler handler;
        private TwitchClient client;
        private Dictionary<string, Channel> channelDict = new Dictionary<string, Channel>();

        #region Konstruktor
        public Client(string user, string accesToken, IClientHandler handler)
        {
            this.User = user;
            this.handler = handler;
            var credentials = new ConnectionCredentials(user, accesToken);
            client = new TwitchClient(credentials);

            client.OnJoinedChannel += Client_OnJoinedChannel;
            client.OnMessageReceived += Client_OnMessageReceived;
            client.OnUserJoined += Client_OnUserJoined;
            client.OnUserLeft += Client_OnUserLeft;
            client.OnConnected += Client_OnConnected;
            client.OnDisconnected += Client_OnDisconnected;
        }
        #endregion

        #region Methoden
        public void Connect()
        {
            client.Connect();
        }

        public void Disconnect()
        {
            client.Disconnect();
        }

        public Channel JoinChannel(string name, IChannelHandler handler)
        {
            if (channelDict.ContainsKey(name))
            {
                return channelDict[name];
            }

            client.JoinChannel(name);
            var channel = new Channel(this, name, handler);
            channelDict.Add(name, channel);
            return channel;
        }

        public void Join(string name, IChannelHandler handler)
        {
            //...
        }

        public void LeaveChannel(string channel)
        {
            client.LeaveChannel(channel);
        }

        public void SendMessage(string channel, string message)
        {
            client.SendMessage(channel, message);
        }
        #endregion

        #region Events
        private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            Channel channel = channelDict[e.Channel];
            channel.Handler.OnJoin(channel);
        }

        private void Client_OnUserLeft(object sender, OnUserLeftArgs e)
        {
            Channel channel = channelDict[e.Channel];
            channel.Handler.OnUserLeave(channel, e.Username);
        }

        private void Client_OnUserJoined(object sender, OnUserJoinedArgs e)
        {
            Channel channel = channelDict[e.Channel];
            channel.Handler.OnUserJoin(channel, e.Username);
        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            ChatMessage message = e.ChatMessage;
            Channel channel = channelDict[message.Channel];
            channel.Handler.OnMessage(channel, message);
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            handler.OnConnect(this);
        }

        private void Client_OnDisconnected(object sender, OnDisconnectedArgs e)
        {
            handler.OnDisconnect(this);
        }
        #endregion
    }
}

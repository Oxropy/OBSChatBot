using System;
using System.ComponentModel;
using System.Threading;
using OBSChatBot.Twitch;
using System.Linq;
using TwitchLib.Client.Models;
using OBSWebsocketDotNet;

namespace OBSChatBot.Handler
{
    public class CliChannelHandler : IChannelHandler
    {
        public readonly VotingHandler Votings;
        OBSWebsocket Obs;
        
        public CliChannelHandler(VotingHandler votings, OBSWebsocket obs)
        {
            Votings = votings;
            Obs = obs;
        }

        public void OnJoin(Channel channel)
        {
            Console.WriteLine("Joined '{0}'", channel.Name);
        }

        public void OnLeave(Channel channel)
        {
            Console.WriteLine("Leaved '{0}'", channel.Name);
        }

        public void OnMessage(Channel channel, ChatMessage msg)
        {
            Console.WriteLine("{0}: {1}", msg.DisplayName, msg.Message);
            string message = msg.Message;

            if (message.StartsWith("!"))
            {
                Votings.ProcessMessage(msg.Username, message, msg.IsModerator); 
            }
        }

        public void OnUserJoin(Channel channel, string name)
        {
            Console.WriteLine("'{0}' joined", name);
        }

        public void OnUserLeave(Channel channel, string name)
        {
            Console.WriteLine("'{0}' leaved", name);
        }

    }
}

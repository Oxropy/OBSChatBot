﻿using System;
using System.ComponentModel;
using System.Threading;
using OBSChatBot.Twitch;
using System.Linq;
using TwitchLib.Client.Models;

namespace OBSChatBot.Handler
{
    public class CliChannelHandler : IChannelHandler
    {
        public readonly VotingHandler Votings;
        OBSWebsocketHandler ObsHandler;
        
        public CliChannelHandler(VotingHandler votings, OBSWebsocketHandler obsHandler)
        {
            Votings = votings;
            ObsHandler = obsHandler;
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
            string[] parts = message.Split(' ');
            if (parts.Length <= 2) return;

            if (parts[0] == "!vote")
            {
                string action = parts[1];
                string vote = parts[2];

                Votings.AddVote(action, vote);
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

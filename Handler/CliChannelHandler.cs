using System;
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
        BackgroundWorker bg = new BackgroundWorker();
        OBSWebsocketHandler ObsHandler;
        
        public CliChannelHandler(VotingHandler votings, OBSWebsocketHandler obsHandler)
        {
            Votings = votings;
            ObsHandler = obsHandler;
            
            bg.DoWork += new DoWorkEventHandler(bg_DoWork);
            bg.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bg_RunWorkerCompleted);
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

                if (!bg.IsBusy)
                {
                    bg.RunWorkerAsync(Votings.GetVotingInfo(action));
                }
                
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


        private void bg_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Console.WriteLine("Vote ends!");
            Voting voting = (Voting)e.Result;
            var result = voting.GetResult().ToArray();
            voting.ResetVotes();
            
            Console.WriteLine("Winner: {0}", result[0]);
            ObsHandler.SetScene(result[0]);
        }

        private static void bg_DoWork(object sender, DoWorkEventArgs e)
        {
            Voting voting = (Voting)e.Argument;
            Thread.Sleep(voting.Milliseconds);
            e.Result = voting;
        }
    }
}

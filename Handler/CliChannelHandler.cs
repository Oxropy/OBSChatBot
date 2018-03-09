using System;
using System.ComponentModel;
using System.Threading;
using OBSChatBot.Twitch;
using TwitchLib.Models.Client;
using System.Linq;

namespace OBSChatBot.Handler
{
    public class CliChannelHandler : IChannelHandler
    {
        public readonly int Milliseconds;
        public readonly Voting Votes;
        BackgroundWorker bg = new BackgroundWorker();
        
        public CliChannelHandler(Voting votes, int milliseconds)
        {
            Milliseconds = milliseconds;
            Votes = votes;

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
            if (message.StartsWith("!vote "))
            {
                if (!bg.IsBusy)
                {
                    bg.RunWorkerAsync(Milliseconds);
                }

                string vote = message.Remove(0, 6);
                Votes.AddVote(vote);
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


        void bg_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Console.WriteLine("Vote ends!");
            var result = Votes.GetResult().ToList();
            Votes.ResetVotes();

            Console.WriteLine("Winner: {0}", result[0]);
        }

        static void bg_DoWork(object sender, DoWorkEventArgs e)
        {
            Thread.Sleep((int)e.Argument);
        }
    }
}

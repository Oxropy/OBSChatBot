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
        public readonly VotingHandler Votings;
        BackgroundWorker bg = new BackgroundWorker();
        
        public CliChannelHandler(VotingHandler votings, int milliseconds)
        {
            Milliseconds = milliseconds;
            Votings = votings;
            
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
                    bg.RunWorkerAsync(new Tuple<int, string>(Milliseconds, action));
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


        void bg_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Console.WriteLine("Vote ends!");
            var result = Votings.GetResult(e.Result.ToString()).ToList();
            Votings.ResetVoting(e.Result.ToString());

            Console.WriteLine("Winner: {0}", result[0]);
        }

        static void bg_DoWork(object sender, DoWorkEventArgs e)
        {
            Tuple<int, string> info = (Tuple<int, string>)e.Argument;
            Thread.Sleep(info.Item1);
            e.Result = info.Item2;
        }
    }
}

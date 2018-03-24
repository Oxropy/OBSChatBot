using OBSChatBot.Twitch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OBSChatBot
{
    public class Voting
    {
        public readonly Client Client;
        public readonly string Channel;
        public readonly string ActionName;
        public readonly Dictionary<string, int> Votes;
        public readonly int Milliseconds;
        public readonly bool AllowUserMultipleVotes;
        public readonly OBSWebsocketHandler ObsHandler;
        public bool IsVoting { get; private set; }

        public Voting(Client client, string channel, string action, IEnumerable<string> choices, OBSWebsocketHandler obsHandler, int milliseconds, bool allowUserMultipleVotes)
        {
            Client = client;
            Channel = channel;
            ActionName = action;
            ObsHandler = obsHandler;
            Milliseconds = milliseconds;
            AllowUserMultipleVotes = allowUserMultipleVotes;
            Votes = new Dictionary<string, int>();
            foreach (var choice in choices)
            {
                Votes.Add(choice, 0);
            }
        }

        public async void StartVoting()
        {
            IsVoting = true;
            await Task.Delay(Milliseconds);
            IsVoting = false;

            var result = GetResult().ToArray();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < result.Length; i++)
            {
                sb.Append(i + 1);
                sb.Append(" - ");
                sb.Append(result[i].Item1);
                sb.Append(" (");
                sb.Append(result[i].Item2);
                sb.Append(")");
                sb.Append(" | ");
            }
            
            Client.SendMessage(Channel, sb.ToString());
            ResetVotes();

            // TODO: Return result
            if (result.Length > 0 && ActionName == "scene")
            {
                ObsHandler.Obs.SetCurrentScene(result[0].Item1);
            }
        }

        public void AddVote(string vote)
        {
            //TODO: case insensitive
            if (Votes.ContainsKey(vote))
            {
                Votes[vote]++;
            }
        }

        public IEnumerable<Tuple<string, int>> GetResult()
        {
            // Sort by value
            var result = (from pair in Votes orderby pair.Value descending select pair);

            // return keys order by value
            return result.Select(r => new Tuple<string, int>(r.Key, r.Value));
        }

        public void ResetVotes()
        {
            // ToList for new instance
            var keys = Votes.Keys.ToList();

            foreach (var key in keys)
            {
                Votes[key] = 0;
            }
        }

        public void VoteExists()
        {
            Client.SendMessage(Channel, string.Format("Voting '{0}' exists already!", ActionName));
        }
    }

    public class VotingHandler
    {
        public readonly Client Client;
        public readonly string Channel;
        public readonly int DefaultMilliseconds;
        public readonly Dictionary<string, Voting> Votings;

        public VotingHandler(Client client, string channel, int defaultMilliseconds)
        {
            Client = client;
            Channel = channel;
            DefaultMilliseconds = defaultMilliseconds;
            Votings = new Dictionary<string, Voting>();
        }

        public void AddVoting(string action, IEnumerable<string> choices, OBSWebsocketHandler obsHandler, int milliseconds = 0, bool allowUserMultipleVotes = false)
        {
            if (milliseconds == 0)
            {
                milliseconds = DefaultMilliseconds;
            }

            Voting voting = new Voting(Client, Channel, action, choices, obsHandler, milliseconds, allowUserMultipleVotes);

            AddVoting(voting);
        }

        public void AddVoting(Voting voting)
        {
            if (Votings.ContainsKey(voting.ActionName))
            {
                voting.VoteExists();
                return;
            }

            Votings.Add(voting.ActionName, voting);
        }

        public void AddVote(string action, string vote)
        {
            if (Votings.ContainsKey(action))
            {
                Voting voting = Votings[action];

                if (!voting.IsVoting)
                {
                    new Thread(voting.StartVoting).Start();
                }

                Votings[action].AddVote(vote); 
            }
        }

        public IEnumerable<Tuple<string, int>> GetResult(string action)
        {
            return Votings[action].GetResult();
        }

        public void ResetVoting(string action)
        {
            Votings[action].ResetVotes();
        }

        public Voting GetVotingInfo(string voting)
        {
            if (Votings.ContainsKey(voting))
            {
                return Votings[voting]; 
            }

            return new Voting(null, "", "", new string[0], null, 0, false);
        }
    }
}

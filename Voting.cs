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
        public readonly string ActionName;
        public readonly Dictionary<string, int> Votes;
        public int Milliseconds;
        public readonly bool AllowUserMultipleVotes;
        public bool IsVoting { get; private set; }

        private Dictionary<string, string> Choices;
        private List<string> Voters;

        public Voting(string action, IEnumerable<string> choices, int milliseconds, bool allowUserMultipleVotes)
        {
            ActionName = action;
            Milliseconds = milliseconds;
            AllowUserMultipleVotes = allowUserMultipleVotes;
            Voters = new List<string>();
            Votes = new Dictionary<string, int>();
            Choices = new Dictionary<string, string>();

            choices = choices.Distinct();
            foreach (var choice in choices)
            {
                Votes.Add(choice, 0);
                Choices.Add(choice.ToLower(), choice);
            }
        }

        public async Task<VoteResult> DoVoting(Client client, string channel)
        {
            IsVoting = true;
            await Task.Delay(Milliseconds);
            IsVoting = false;

            client.SendMessage(channel, string.Format("Voting '{0}' has ended!", ActionName));

            return new VoteResult(Votes);
        }

        public void AddVote(Vote vote)
        {
            if (AllowUserMultipleVotes || (!AllowUserMultipleVotes && !Voters.Contains(vote.Voter)))
            {
                string lowerVote = vote.Choice.ToLower();
                if (Choices.ContainsKey(lowerVote))
                {
                    Votes[Choices[lowerVote]]++;
                }
            }
        }

        public void ResetVotes()
        {
            Voters.Clear();

            // ToList for new instance
            var keys = Votes.Keys.ToList();

            foreach (var key in keys)
            {
                Votes[key] = 0;
            }
        }
    }

    public class VotingHandler
    {
        public readonly Client Client;
        public readonly string Channel;
        public readonly int DefaultMilliseconds;
        public readonly Dictionary<string, Voting> Votings;
        public readonly OBSWebsocketHandler ObsHandler;

        public VotingHandler(Client client, string channel, OBSWebsocketHandler obsHandler, int defaultMilliseconds)
        {
            Client = client;
            Channel = channel;
            ObsHandler = obsHandler;
            DefaultMilliseconds = defaultMilliseconds;
            Votings = new Dictionary<string, Voting>();
        }

        public void ProcessMessage(string user, string message)
        {
            string[] parts = message.Split(' ');

            if (parts[0] == "!info" && parts.Length == 2) // Info for voting
            {
                Voting vote = GetVotingInfo(parts[1]);
                Client.SendMessage(Channel, string.Format("Action: {0}, Choices: {1}", vote.ActionName, string.Join(" | ", vote.Votes.Keys)));
            }
            else if (parts[0] == "!vote" && parts.Length == 3) // Vote for existing voting
            {
                string action = parts[1];

                Voting voting = Votings[action];
                if (!voting.IsVoting)
                {
                    DoVoting(voting);
                }

                Vote vote = new Vote(user, parts[2]);
                AddVote(voting, vote);
            }
            else if (parts[0] == "!addVoting" && parts.Length == 5) // Create new voting
            {
                string action = parts[1];
                var choices = parts[2].Split('|');
                if (!int.TryParse(parts[3], out int milliseconds)) return;
                bool multiVotes = parts[4] == "0";

                Voting voting = new Voting(action, choices, milliseconds, multiVotes);
                AddVoting(voting);
            }
        }

        public async void DoVoting(Voting voting)
        {
            var t = await voting.DoVoting(Client, Channel);

            var result = t.GetResult();

            StringBuilder sb = new StringBuilder();
            int votePosition = 1;
            foreach (var choice in result)
            {
                sb.Append(votePosition);
                sb.Append(" - ");
                sb.Append(choice.Choice);
                sb.Append(" (");
                sb.Append(choice.Votes);
                sb.Append(")");
                sb.Append(" | ");

                votePosition++;
            }

            Client.SendMessage(Channel, sb.ToString());
            voting.ResetVotes();

            if (votePosition > 1 && voting.ActionName == "scene")
            {
                var winner = result.ToArray()[0];
                ObsHandler.Obs.SetCurrentScene(winner.Choice);
            }
        }

        public void AddVoting(string action, IEnumerable<string> choices, int milliseconds = 0, bool allowUserMultipleVotes = false)
        {
            if (milliseconds == 0)
            {
                milliseconds = DefaultMilliseconds;
            }

            Voting voting = new Voting(action, choices, milliseconds, allowUserMultipleVotes);

            AddVoting(voting);
        }

        public void AddVoting(Voting voting)
        {
            if (Votings.ContainsKey(voting.ActionName))
            {
                Client.SendMessage(Channel, string.Format("Voting '{0}' exists already!", voting.ActionName));
            }

            Votings.Add(voting.ActionName, voting);
        }

        public void RemoveVoting(string action)
        {
            if (Votings.ContainsKey(action))
            {
                Votings.Remove(action);
            }
        }

        public void AddVote(Voting voting, Vote vote)
        {
            if (Votings.ContainsKey(voting.ActionName))
            {
                voting.AddVote(vote);
            }
        }

        public Voting GetVotingInfo(string voting)
        {
            if (Votings.ContainsKey(voting))
            {
                return Votings[voting];
            }

            return new Voting("", new string[0], 0, false);
        }
    }

    public class VoteResult
    {
        public readonly Dictionary<string, int> Votes;

        public VoteResult(Dictionary<string, int> votes)
        {
            Votes = votes;
        }

        public IEnumerable<VoteResultValue> GetResult()
        {
            // Sort by value
            var result = (from pair in Votes orderby pair.Value descending select pair);

            // return keys order by value
            return result.Select(r => new VoteResultValue(r.Key, r.Value));
        }
    }

    public class VoteResultValue
    {
        public readonly string Choice;
        public readonly int Votes;

        public VoteResultValue(string choice, int votes)
        {
            Choice = choice;
            Votes = votes;
        }
    }

    public class Vote
    {
        public readonly string Voter;
        public readonly string Choice;

        public Vote(string voter, string choice)
        {
            Voter = voter;
            Choice = choice;
        }
    }
}

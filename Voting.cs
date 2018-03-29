using OBSChatBot.Twitch;
using OBSWebsocketDotNet;
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
        public Dictionary<string, int> Votes;
        public int Milliseconds;
        public readonly bool AllowUserMultipleVotes;
        public bool IsActive { get; private set; }
        public readonly Action<OBSWebsocket, IEnumerable<VoteResultValue>> AfterVote;
        /// Key: lowercase, Value: scene
        public Dictionary<string, string> Choices;
        private List<string> Voters;

        public Voting(string action, IEnumerable<string> choices, int milliseconds, bool allowUserMultipleVotes, Action<OBSWebsocket, IEnumerable<VoteResultValue>> afterVote = null)
        {
            ActionName = action;
            Milliseconds = milliseconds;
            AllowUserMultipleVotes = allowUserMultipleVotes;
            AfterVote = afterVote;
            Voters = new List<string>();

            Choices = new Dictionary<string, string>();

            choices = choices.Distinct();
            foreach (var choice in choices)
            {
                Choices.Add(choice.ToLower(), choice);
            }

            ResetVotes();
        }

        public void AddVote(Vote vote)
        {
            if (AllowUserMultipleVotes || (!AllowUserMultipleVotes && !Voters.Contains(vote.Voter)))
            {
                string lowerVote = vote.Choice.ToLower();
                if (Choices.ContainsKey(lowerVote))
                {
                    Votes[lowerVote]++;
                }
            }
        }

        public void ResetVotes()
        {
            Votes = new Dictionary<string, int>();
            foreach (var choice in Choices)
            {
                Votes.Add(choice.Key, 0);
            }
        }

        public void SetActive()
        {
            IsActive = true;
        }

        public void SetInActive()
        {
            IsActive = false;
        }

        public void SetNewVotetime(int milliseconds)
        {
            Milliseconds = milliseconds;
        }
    }

    public class VotingHandler
    {
        public readonly Client Client;
        public readonly string Channel;
        public readonly int DefaultMilliseconds;
        public readonly Dictionary<string, Voting> Votings;
        public readonly OBSWebsocket Obs;

        private static Voting emptyVoting = new Voting("", new string[0], 0, false);

        public VotingHandler(Client client, string channel, OBSWebsocket obs, int defaultMilliseconds)
        {
            Client = client;
            Channel = channel;
            Obs = obs;
            DefaultMilliseconds = defaultMilliseconds;
            Votings = new Dictionary<string, Voting>();
        }

        public void ProcessMessage(string user, string message, bool isMod)
        {
            string[] parts = message.Split(' ');

            switch (parts[0])
            {
                case "!info": // Info for voting
                    if (parts.Length == 2)
                    {
                        Voting vote = GetVotingInfo(parts[1]);
                        Client.SendMessage(Channel, string.Format("Action: {0}, Choices: {1}", vote.ActionName, string.Join(" | ", vote.Choices.Values)));
                    }
                    break;
                case "!vote": // Vote for existing voting
                    if (parts.Length == 3)
                    {
                        string action = parts[1];

                        if (Votings.ContainsKey(action))
                        {
                            Voting voting = Votings[action];
                            if (!voting.IsActive)
                            {
                                DoVoting(voting);
                            }

                            Vote vote = new Vote(user, parts[2]);
                            AddVote(voting, vote);
                        }
                    }
                    break;
                case "!addVoting": // Create new voting
                    if (isMod 
                        && parts.Length == 4)
                    {
                        string action = parts[1];
                        var choices = parts[2].Split('|');
                        if (!int.TryParse(parts[3], out int milliseconds)) return;
                        bool multiVotes = false;

                        Voting voting = new Voting(action, choices, milliseconds, multiVotes);
                        AddVoting(voting);
                    }
                    break;
                case "!editVoteTime":
                    if (isMod && parts.Length == 3)
                    {
                        string action = parts[1];
                        if (Votings.ContainsKey(action))
                        {
                            if (int.TryParse(parts[2], out int milliseconds))
                            {
                                if (milliseconds >= 10000)
                                {
                                    Voting voting = Votings[action];
                                    voting.SetNewVotetime(milliseconds);
                                    Client.SendMessage(Channel, string.Format("Votetime for action '{0}' set to {1} sec", action, milliseconds / 1000));
                                }
                            }
                        }
                    }
                    break;
            }
        }

        public async void DoVoting(Voting voting)
        {
            int seconds = voting.Milliseconds / 1000;
            Client.SendMessage(Channel, string.Format("Voting '{0}' has started! Voting runs {1} seconds.", voting.ActionName, seconds));

            voting.SetActive();
            await Task.Delay(voting.Milliseconds);
            var voteResult = new VoteResult(voting.Votes, voting.Choices);
            voting.SetInActive();

            Client.SendMessage(Channel, string.Format("Voting '{0}' has ended!", voting.ActionName));

            var result = voteResult.GetResult();

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

            voting.AfterVote?.Invoke(Obs, result);

            voting.ResetVotes();
        }

        public void AddVoting(string action, IEnumerable<string> choices, int milliseconds = 0, bool allowUserMultipleVotes = false)
        {
            if (milliseconds == 0) milliseconds = DefaultMilliseconds;

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
            // Voting does not exist, return empty voting
            if (!Votings.ContainsKey(voting)) return emptyVoting;

            return Votings[voting];
        }

        public void SetNewVotetime(string voting, int milliseconds)
        {
            if (Votings.ContainsKey(voting))
            {
                Votings[voting].SetNewVotetime(milliseconds);
            }
        }
    }

    public class VoteResult
    {
        public readonly Dictionary<string, int> Votes;
        public readonly Dictionary<string, string> Choices;

        public VoteResult(Dictionary<string, int> votes, Dictionary<string, string> choices)
        {
            Votes = votes;
            Choices = choices;
        }

        public IEnumerable<VoteResultValue> GetResult()
        {
            // Sort by value
            var result = (from pair in Votes orderby pair.Value descending select pair);

            // return keys order by value
            return result.Select(r => new VoteResultValue(Choices[r.Key], r.Value));
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

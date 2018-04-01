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
                case "!votingInfo": // Info for voting
                    if (parts.Length != 2) return;
                    ChatCommands.Info(this, parts[1]);
                    break;
                case "!vote": // Vote for existing voting
                    if (parts.Length != 3) return;
                    ChatCommands.Vote(this, user, parts[1], parts[2]);
                    break;
                case "!addVoting": // Create new voting
                    if (parts.Length != 4 || !isMod || !int.TryParse(parts[3], out int milliseconds)) return;
                    ChatCommands.AddVoting(this, parts[1], parts[2].Split('|'), milliseconds);
                    break;
                case "!editVoteTime": // Change time for voting
                    if (parts.Length != 2 || !isMod || !int.TryParse(parts[3], out milliseconds)) return;
                    ChatCommands.EditVotetime(this, parts[1], milliseconds);
                    break;
                case "!deleteVoting": // Remove voting
                    if (parts.Length != 2 || !isMod) return;
                    ChatCommands.DeleteVoting(this, parts[1]);
                    break;
                case "!votings":
                    ChatCommands.Votings(this);
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
            ShowVotingResult(result);
            
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
                return;
            }

            Votings.Add(voting.ActionName, voting);
        }

        public void RemoveVoting(string action)
        {
            Votings.Remove(action);
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

        public void ShowVotingResult(IEnumerable<VoteResultValue> result)
        {
            StringBuilder sb = new StringBuilder();
            int votePosition = 1;

            var e = result.GetEnumerator();
            if (e.MoveNext())
            {
                var v = e.Current;
                GetChoiceResult(sb, v, votePosition);

                while (e.MoveNext())
                {
                    v = e.Current;
                    sb.Append(" | ");
                    GetChoiceResult(sb, v, votePosition);
                }
            }
            
            Client.SendMessage(Channel, sb.ToString());
        }

        private void GetChoiceResult(StringBuilder sb, VoteResultValue resultValue, int position)
        {
            sb.Append(position);
            sb.Append(" - ");
            sb.Append(resultValue.Choice);
            sb.Append(" (");
            sb.Append(resultValue.Votes);
            sb.Append(")");
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

    public static class ChatCommands
    {
        public static void Info(VotingHandler votingHandler, string action)
        {
            Voting vote = votingHandler.GetVotingInfo(action);
            votingHandler.Client.SendMessage(votingHandler.Channel, string.Format("Action: {0}, Choices: {1}", vote.ActionName, string.Join(" | ", vote.Choices.Values)));
        }

        public static void Vote(VotingHandler votingHandler, string user, string action, string choice)
        {
            if (votingHandler.Votings.ContainsKey(action))
            {
                Voting voting = votingHandler.Votings[action];
                if (!voting.IsActive) votingHandler.DoVoting(voting);

                Vote vote = new Vote(user, choice);
                votingHandler.AddVote(voting, vote);
            }
        }

        public static void AddVoting(VotingHandler votingHandler, string action, string[] choices, int milliseconds, bool isMultiVote = true)
        {
            Voting voting = new Voting(action, choices, milliseconds, isMultiVote);
            votingHandler.AddVoting(voting);
        }

        public static void EditVotetime(VotingHandler votingHandler, string action, int milliseconds)
        {
            if (votingHandler.Votings.ContainsKey(action) && milliseconds >= 10000)
            {
                Voting voting = votingHandler.Votings[action];
                voting.SetNewVotetime(milliseconds);
                votingHandler.Client.SendMessage(votingHandler.Channel, string.Format("Votetime for action '{0}' set to {1} sec", action, milliseconds / 1000));
            }
        }

        public static void DeleteVoting(VotingHandler votingHandler, string action)
        {
            votingHandler.RemoveVoting(action);
        }

        public static void Votings(VotingHandler votingHandler)
        {
            votingHandler.Client.SendMessage(votingHandler.Channel, string.Format("Existing votings: {0}", string.Join(" | ", votingHandler.Votings.Keys.ToArray())));
        }
    }
}

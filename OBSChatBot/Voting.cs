using OBSWebsocketDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwitchLib.Client;

namespace OBSChatBot
{
    public class Voting
    {
        public readonly string ActionName;
        public Dictionary<string, int> Votes;
        public int Milliseconds;
        public bool IsActive;
        public readonly Action<OBSWebsocket, IEnumerable<Tuple<string, int>>> AfterVote;
        /// Key: lowercase, Value: scene
        public Dictionary<string, string> Choices;
        private List<string> Voters;

        public Voting(string action, IEnumerable<string> choices, int milliseconds, Action<OBSWebsocket, IEnumerable<Tuple<string, int>>> afterVote = null)
        {
            ActionName = action;
            Milliseconds = milliseconds;
            AfterVote = afterVote;
            Voters = new List<string>();
            Choices = choices.Distinct().ToDictionary(c => c.ToLower(), c => c );
            ResetVotes();
        }

        public void AddVote(Tuple<string, string> vote)
        {
            string voter = vote.Item1.ToLower();
            if (Voters.Contains(voter)) return;

            string lowerVote = vote.Item2.ToLower();
            if (!Choices.ContainsKey(lowerVote)) return;

            Votes[lowerVote]++;
            Voters.Add(voter);
        }

        public void ResetVotes()
        {
            Votes = new Dictionary<string, int>();
            foreach (var choice in Choices)
            {
                Votes.Add(choice.Key, 0);
            }
        }

        public IEnumerable<Tuple<string, int>> GetResult()
        {
            return Votes.OrderByDescending(r => r.Value).Select(r => new Tuple<string, int>(Choices[r.Key], r.Value));
        }
    }

    public class VotingHandler
    {
        public readonly TwitchClient Client;
        public readonly string Channel;
        public readonly int DefaultMilliseconds;
        public readonly Dictionary<string, Voting> Votings;
        public readonly OBSWebsocket Obs;

        private static Voting emptyVoting = new Voting("", new string[0], 0);

        public VotingHandler(TwitchClient client, OBSWebsocket obs, string channel, int defaultMilliseconds)
        {
            Client = client;
            Obs = obs;
            Channel = channel;
            DefaultMilliseconds = defaultMilliseconds;
            Votings = new Dictionary<string, Voting>();
        }

        public void ProcessMessage(Tuple<bool, string, string[]> message)
        {
            bool isMod = message.Item1;
            string user = message.Item2;
            string[] parts = message.Item3;

            switch (parts[0])
            {
                case "info": // List of commands
                    ChatCommands.Info (this);
                    break;
                case "voteInfo": // Info for voting
                    if (parts.Length != 2) return;
                    ChatCommands.VoteInfo(this, parts[1]);
                    break;
                case "vote": // Vote for existing voting
                    if (parts.Length != 3) return;
                    ChatCommands.Vote(this, user, parts[1], parts[2]);
                    break;
                case "addVoting": // Create new voting
                    if (parts.Length != 4 || !isMod || !int.TryParse(parts[3], out int milliseconds)) return;
                    ChatCommands.AddVoting(this, parts[1], parts[2].Split('|'), milliseconds);
                    break;
                case "editVoteTime": // Change time for voting
                    if (parts.Length != 2 || !isMod || !int.TryParse(parts[3], out milliseconds)) return;
                    ChatCommands.EditVotetime(this, parts[1], milliseconds);
                    break;
                case "deleteVoting": // Remove voting
                    if (parts.Length != 2 || !isMod) return;
                    ChatCommands.DeleteVoting(this, parts[1]);
                    break;
                case "votings": // Existing votings
                    ChatCommands.Votings(this);
                    break;
            }
        }

        public async void DoVoting(Voting voting)
        {
            int seconds = voting.Milliseconds / 1000;
            Client.SendMessage(Channel, string.Format("Voting '{0}' has started! Voting runs {1} seconds.", voting.ActionName, seconds));

            voting.IsActive = true;
            await Task.Delay(voting.Milliseconds);
            voting.IsActive = false;

            Client.SendMessage(Channel, string.Format("Voting '{0}' has ended!", voting.ActionName));

            var result = voting.GetResult();
            ShowVotingResult(result);

            voting.AfterVote?.Invoke(Obs, result);
            voting.ResetVotes();
        }

        public void AddVoting(string action, IEnumerable<string> choices, int milliseconds = 0)
        {
            Voting voting = new Voting(action, choices, milliseconds);
            AddVoting(voting);
        }

        public void AddVoting(Voting voting)
        {
            string action = voting.ActionName.ToLower();
            if (Votings.ContainsKey(action))
            {
                Client.SendMessage(Channel, string.Format("Voting '{0}' exists already!", action));
                return;
            }

            if (voting.Milliseconds == 0) voting.Milliseconds = DefaultMilliseconds;
            Votings.Add(action, voting);
        }

        public void RemoveVoting(string action)
        {
            Votings.Remove(action.ToLower());
        }

        public void AddVote(Voting voting, Tuple<string, string> vote)
        {
            if (!Votings.ContainsKey(voting.ActionName.ToLower())) return;

            voting.AddVote(vote);
        }

        public Voting GetVotingInfo(string voting)
        {
            if (!Votings.ContainsKey(voting)) return emptyVoting;

            return Votings[voting];
        }

        public void SetNewVotetime(string action, int milliseconds)
        {
            action = action.ToLower();
            if (!Votings.ContainsKey(action)) return;

            Votings[action].Milliseconds = milliseconds;
        }

        public void ShowVotingResult(IEnumerable<Tuple<string, int>> result)
        {
            StringBuilder sb = new StringBuilder();
            int votePosition = 1;

            var e = result.GetEnumerator();
            if (e.MoveNext())
            {
                var v = e.Current;
                AppendResultString(sb, v, votePosition);

                while (e.MoveNext())
                {
                    votePosition++;
                    v = e.Current;
                    sb.Append(" | ");
                    AppendResultString(sb, v, votePosition);
                }
            }

            Client.SendMessage(Channel, sb.ToString());
        }

        private void AppendResultString(StringBuilder sb, Tuple<string, int> resultValue, int position)
        {
            sb.Append(position);
            sb.Append(" - ");
            sb.Append(resultValue.Item1);
            sb.Append(" (");
            sb.Append(resultValue.Item2);
            sb.Append(")");
        }
    }

    public static class ChatCommands
    {
        public static void Info(VotingHandler votingHandler)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("!votings: Existing votings | ");
            sb.Append("!voteInfo: Info for voting | ");
            sb.Append("!vote: Vote for existing voting | ");
            sb.Append("!addVoting [Mod]: Create new voting | ");
            sb.Append("!editVoteTime [Mod]: Change time for voting | ");
            sb.Append("!deleteVoting [Mod]: Remove voting");
            votingHandler.Client.SendMessage(votingHandler.Channel, sb.ToString());
        }

        public static void VoteInfo(VotingHandler votingHandler, string action)
        {
            Voting vote = votingHandler.GetVotingInfo(action);
            votingHandler.Client.SendMessage(votingHandler.Channel, string.Format("Action: {0}, Choices: {1}, Vote time: {2} sec", vote.ActionName, string.Join(" | ", vote.Choices.Values), vote.Milliseconds / 1000));
        }

        public static void Vote(VotingHandler votingHandler, string user, string action, string choice)
        {
            if (!votingHandler.Votings.ContainsKey(action)) return;

            Voting voting = votingHandler.Votings[action];
            if (!voting.IsActive) votingHandler.DoVoting(voting);

            var vote = new Tuple<string, string>(user, choice);
            votingHandler.AddVote(voting, vote);
        }

        public static void AddVoting(VotingHandler votingHandler, string action, string[] choices, int milliseconds)
        {
            Voting voting = new Voting(action, choices, milliseconds);
            votingHandler.AddVoting(voting);
        }

        public static void EditVotetime(VotingHandler votingHandler, string action, int milliseconds)
        {
            if (!votingHandler.Votings.ContainsKey(action)) return;

            Voting voting = votingHandler.Votings[action];
            voting.Milliseconds = milliseconds;
            votingHandler.Client.SendMessage(votingHandler.Channel, string.Format("Votetime for action '{0}' set to {1} sec", action, milliseconds / 1000));
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
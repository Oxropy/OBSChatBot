using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OBSChatBot
{
    public class Voting
    {
        public readonly string ActionName;
        public Dictionary<string, int> Votes { get; private set; }
        public readonly int Milliseconds;

        public Voting(string action, IEnumerable<string> choices)
        {
            ActionName = action;
            Votes = new Dictionary<string, int>();
            foreach (var choice in choices)
            {
                Votes.Add(choice, 0);
            }
        }

        public void AddVote(string vote)
        {
            if (Votes.ContainsKey(vote))
            {
                Votes[vote]++;
            }
        }

        public IEnumerable<string> GetResult()
        {
            // Sort by value
            var result = (from pair in Votes orderby pair.Value descending select pair).ToList();

            // return keys order by value
            return result.Select(r => r.Key);
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
    }

    public class VotingHandler
    {
        Dictionary<string, Voting> Votings = new Dictionary<string, Voting>();

        public void AddVoting(string action, IEnumerable<string> choices)
        {
            Voting voting = new Voting(action, choices);

            AddVoting(voting);
        }

        public void AddVoting(Voting voting)
        {
            if (Votings.ContainsKey(voting.ActionName))
            {
                Console.WriteLine("Voting exists already!");
                return;
            }

            Votings.Add(voting.ActionName, voting);
        }

        public void AddVote(string action, string vote)
        {
            if (Votings.ContainsKey(action))
            {
                Votings[action].AddVote(vote); 
            }
        }

        public IEnumerable<string> GetResult(string action)
        {
            return Votings[action].GetResult();
        }

        public void ResetVoting(string action)
        {
            Votings[action].ResetVotes();
        }

        public Voting GetVotingInfo(string voting)
        {
            return Votings[voting];
        }
    }
}

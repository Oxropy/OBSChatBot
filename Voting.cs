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
            string choice = vote;

            if (Votes.ContainsKey(choice))
            {
                Votes[choice]++;
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
            var keys = Votes.Keys;

            foreach (var key in keys)
            {
                Votes[key] = 0;
            }
        }
    }
}

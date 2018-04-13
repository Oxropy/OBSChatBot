using System;
using System.Collections.Generic;
using System.IO;
using OBSChatBot.Authentication;
using OBSWebsocketDotNet;
using System.Linq;
using System.Text.RegularExpressions;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.Client.Events;
using System.Threading;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace OBSChatBot
{
    class Program
    {
        static void Main(string[] args)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OBSChatBot").ToString();
            var config = SetConfigFromFile(directory);

            if (string.IsNullOrWhiteSpace(config.user) || string.IsNullOrWhiteSpace(config.channel) || string.IsNullOrWhiteSpace(config.uri))
            {
                Console.WriteLine("Config missing values!");
                Console.ReadKey();
                return;
            }

            var authResponse = AuthenticateLogin(directory, config);
            if (authResponse != null)
            {
                if (authResponse is FailedAuthentication failure)
                {
                    Console.WriteLine("Authentication Failure: {0}; Reason: {1}", failure.Failure, failure.Reason);
                    Console.ReadKey();
                    return;
                }

                if (authResponse is SuccessfulAuthentication success)
                {
                    RunBot(directory, success.Name, success.Token, config.channel);


                }
            }
        }

        private static IAuthenticationResult AuthenticateLogin(string directory, Config config)
        {
            string user = config.user;

            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            bool newToken = false;
            string accessToken = string.Empty;
            string path = directory + "/Token.txt";
            if (File.Exists(path))
            {
                try
                {   // Open the text file using a stream reader.
                    using (StreamReader sr = new StreamReader(path))
                    {
                        accessToken = sr.ReadLine();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("The file could not be read: {0}", e.Message);
                }
            }

            IAuthenticationResult authResponse;
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                authResponse = new SuccessfulAuthentication(user, accessToken);
            }
            else
            {
                var state = TwitchAuthentication.GenerateState();
                var url = TwitchAuthentication.AuthUri(state, config.clientId, config.redirectHost);
                Console.WriteLine("Log in URL:");
                Console.WriteLine(url);
                var returnedUrl = Console.ReadLine();

                authResponse = TwitchAuthentication.AuthRequest(returnedUrl, state, config.clientId, config.clientSecret, config.redirectUri);
                newToken = true;
            }

            if (authResponse is SuccessfulAuthentication success)
            {
                // When new Token add Token
                if (newToken) File.WriteAllLines(path, new[] { success.Token });

                Console.WriteLine("Authentication Success");
            }

            return authResponse;
        }

        private static async Task<bool> RunBot(string directory, string name, string token, string channelName)
        {
            var source = new TaskCompletionSource<bool>();

            var client = await InitBot(name, token);
            Console.WriteLine("Connected as '{0}'", name);
            var channel = await JoinChannel(client, channelName);
            Console.WriteLine("Joined channel '{0}'", channelName);
            client.SendMessage(channel, "Voting is active!");

            return true;
        }

        private static Task<TwitchClient> InitBot(string name, string token)
        {
            var source = new TaskCompletionSource<TwitchClient>();
            var client = new TwitchClient();

            void Client_OnConnected(object sender, OnConnectedArgs e)
            {
                client.OnConnected -= Client_OnConnected;
                source.SetResult(client);
            }

            client.OnConnected += Client_OnConnected;
            client.Initialize(new ConnectionCredentials(name, token));
            client.Connect();

            return source.Task;
        }

        private static Task<JoinedChannel> JoinChannel(TwitchClient client, string channel)
        {
            var source = new TaskCompletionSource<JoinedChannel>();

            void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
            {
                if (e.Channel == channel)
                {
                    client.OnJoinedChannel -= Client_OnJoinedChannel;
                    source.SetResult(client.GetJoinedChannel(channel));
                }
            }

            client.OnJoinedChannel += Client_OnJoinedChannel;
            client.JoinChannel(channel);
            return source.Task;
        }

        private static void TextHandling(string directory, TwitchClient client, Config config)
        {
            string channel = config.channel;
            int milliseconds = config.time;
            string uri = config.uri;
            string pw = config.pw;
            string scenesRegex = config.scene;

            var obs = new OBSWebsocket();
            obs.Connect(uri, pw);

            VotingHandler votings = new VotingHandler(client, channel, obs, milliseconds);
            // Add Scene voting
            string action = "scene";
            Regex reg = new Regex(scenesRegex);
            List<OBSScene> scenes = obs.ListScenes();
            string[] choices = scenes.Where(s => reg.IsMatch(s.Name)).Select(s => s.Name).ToArray();

            var afterVote = new Action<OBSWebsocket, IEnumerable<Tuple<string, int>>>(ChangeObsScene);
            Voting sceneVote = new Voting(action, choices, milliseconds, afterVote);
            votings.AddVoting(sceneVote);

            SetVotingsFromFile(directory, votings);

            string input;
            // Console commands
            bool exit = false;
            while (!exit)
            {
                input = Console.ReadLine();
                exit = input == "!exit";

                if (input.StartsWith("!info"))
                {
                    string[] info = input.Split(' ');

                    if (info.Length == 2)
                    {
                        Voting vote = votings.GetVotingInfo(info[1]);
                        client.SendMessage(channel, string.Format("Action: {0}, Choices: {1}", vote.ActionName, string.Join(" | ", vote.Votes.Keys)));
                    }
                }
                else if (input == "!addVoting")
                {
                    // Configure vote
                    Console.WriteLine("Voting action:");
                    action = Console.ReadLine();

                    Console.WriteLine("Choices, seperate by '|':");
                    choices = Console.ReadLine().Split('|');

                    milliseconds = GetVotetime();

                    Voting voting = new Voting(action, choices, milliseconds);
                    votings.AddVoting(voting);
                }
            }

            client.LeaveChannel(channel);
            client.Disconnect();
        }

        private static Config SetConfigFromFile(string directory)
        {
            Config config = new Config();

            string path = directory + "/config.json";
            if (File.Exists(path))
            {
                try
                {
                    using (StreamReader r = File.OpenText(path))
                    {
                        string json = r.ReadToEnd();
                        config = JsonConvert.DeserializeObject<Config>(json);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format("Config imoirt error: {0}", ex.Message));
                }
            }
            else
            {
                Console.WriteLine("No config found!");
            }

            return config;
        }

        public struct Config
        {
            public string user { get; set; }
            public int time { get; set; }
            public string channel { get; set; }
            public string uri { get; set; }
            public string pw { get; set; }
            public string scene { get; set; }
            public string clientId { get; set; }
            public string clientSecret { get; set; }
            public string redirectHost { get; set; }
            public string redirectUri { get; set; }
        }

        private static void SetVotingsFromFile(string directory, VotingHandler votingHandler)
        {
            string path = directory + "/votings.json";
            if (File.Exists(path))
            {
                try
                {
                    using (StreamReader r = File.OpenText(path))
                    {
                        string json = r.ReadToEnd();
                        VotingValue[] votings = JsonConvert.DeserializeObject<VotingValue[]>(json);

                        foreach (var voting in votings)
                        {
                            votingHandler.AddVoting(voting.name, voting.choices, voting.time);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format("Voting import error: {0}", ex.Message));
                }
            }
            else
            {
                Console.WriteLine("No votings found!");
            }
        }

        public struct VotingValue
        {
            public string name { get; set; }
            public int time { get; set; }
            public string[] choices { get; set; }
        }

        private static int GetVotetime()
        {
            string input;
            int milliseconds;
            do
            {
                Console.WriteLine("Default vote time in milliseconds:");
                input = Console.ReadLine();
            } while (!int.TryParse(input, out milliseconds));

            return milliseconds;
        }

        private static void ChangeObsScene(OBSWebsocket obs, IEnumerable<Tuple<string, int>> result)
        {
            var winner = result.ToArray()[0];
            obs.SetCurrentScene(winner.Item1);
        }
    }
}

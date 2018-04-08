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
using TwitchLib.Client.Services;
using System.Threading;
using Newtonsoft.Json;

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

            TwitchClient client = AuthenticateLogin(directory, args, config.user);
            if (client != null)
            {
                TextHandling(directory, client, config);
            }
        }

        private static TwitchClient AuthenticateLogin(string directory, string[] args, string user)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            bool newToken = false;
            IAuthenticationResult authResponse;

            string accessToken = string.Empty;

            #region read accesToken
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
            #endregion

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                authResponse = new SuccessfulAuthentication(user, accessToken);
            }
            else
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Too few arguments!");
                    Console.WriteLine("Usage: {0} <client id> <client secret>", Environment.GetCommandLineArgs()[0]);
                    Console.ReadKey();
                    Environment.Exit(1);
                    return null;
                }

                string clientId = args[0];
                string clientSecret = args[1];

                authResponse = TwitchAuthentication.Authenticate(clientId, clientSecret, url =>
                {
                    Console.WriteLine("Log in URL:");
                    Console.WriteLine(url);
                    return Console.ReadLine();
                });
                newToken = true;
            }

            if (authResponse is SuccessfulAuthentication success)
            {
                // When new Token add Token
                if (newToken)
                {
                    File.WriteAllLines(path, new[] { success.Token });
                }

                Console.WriteLine("Authentication Success");

                var credentials = new ConnectionCredentials(success.Name, success.Token);
                var client = new TwitchClient();
                client.OnJoinedChannel += Client_OnJoinedChannel;
                client.OnConnected += Client_OnConnected;

                client.Initialize(credentials);
                //client.ChatThrottler = new MessageThrottler(client, 20, TimeSpan.FromSeconds(30));
                client.Connect();

                while (!client.IsConnected)
                {
                    Thread.Sleep(500);
                    Console.WriteLine("Sleeped 500ms");
                }

                return client;
            }
            else
            {
                var failure = authResponse as FailedAuthentication;
                Console.WriteLine("Authentication Failure: {0}; Reason: {1}", failure.Failure, failure.Reason);

                Console.ReadKey();
            }
            return null;
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

            client.JoinChannel(channel);

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
            public string user;
            public int time;
            public string channel;
            public string uri;
            public string pw;
            public string scene;
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
            public string name;
            public int time;
            public string[] choices;
        }

        private static int GetVotetime()
        {
            Console.WriteLine("Default vote time in milliseconds:");
            string input = Console.ReadLine();

            int milliseconds;
            while (!int.TryParse(input, out milliseconds))
            {
                Console.WriteLine("Default vote time in milliseconds:");
                input = Console.ReadLine();
            }

            return milliseconds;
        }

        private static void ChangeObsScene(OBSWebsocket obs, IEnumerable<Tuple<string, int>> result)
        {
            var winner = result.ToArray()[0];
            obs.SetCurrentScene(winner.Item1);
        }
        
        #region Events
        private static void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            Console.WriteLine("Joined channel '{0}'", e.Channel);
            ((TwitchClient)sender).OnJoinedChannel -= Client_OnJoinedChannel;
        }

        private static void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            Console.WriteLine("Connected as '{0}'", e.BotUsername);
            ((TwitchClient)sender).OnConnected -= Client_OnConnected;
        }
        #endregion
    }
}

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
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Threading;

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

            var token = GetAuthenticationToken(directory, config).Result;
            if (!string.IsNullOrWhiteSpace(token))
            {
                var t = new Thread(() => RunBot(directory, token, config));
                t.Start();

                string input;
                do
                {
                    input = Console.ReadLine();
                } while (input != "!exit");

                t.Abort();
            }
        }

        private static async Task<string> GetAuthenticationToken(string directory, Config config)
        {
            var authResponse = await AuthenticateLogin(directory, config);
            if (authResponse is FailedAuthentication failure)
            {
                Console.WriteLine("Authentication Failure: {0}; Reason: {1}", failure.Failure, failure.Reason);
                Console.ReadKey();
                return string.Empty;
            }

            if (authResponse is SuccessfulAuthentication success && success.Name == config.user)
            {
                return success.Token;
            }
            return null;
        }

        private static Task<IAuthenticationResult> AuthenticateLogin(string directory, Config config)
        {
            var source = new TaskCompletionSource<IAuthenticationResult>();

            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            string accessToken = string.Empty;
            string path = Path.Combine(directory, "Token.txt");
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

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                source.SetResult(new SuccessfulAuthentication(config.user, accessToken));
                return source.Task;
            }

            IAuthenticationResult authResponse;
            var state = TwitchAuthentication.GenerateState();
            var url = TwitchAuthentication.AuthUri(state, config.clientId, config.redirectHost);
            Console.WriteLine("Log in URL:");
            Console.WriteLine(url);
            var returnedUrl = Console.ReadLine();

            authResponse = TwitchAuthentication.AuthRequest(returnedUrl, state, config.clientId, config.clientSecret, config.redirectUri);

            if (authResponse is SuccessfulAuthentication success)
            {
                File.WriteAllLines(path, new[] { success.Token });
                Console.WriteLine("Authentication Success");
            }

            source.SetResult(authResponse);
            return source.Task;
        }

        private static async void RunBot(string directory, string token, Config config)
        {
            var source = new TaskCompletionSource<bool>();

            var obs = await InitObs(config.uri, config.pw);
            Console.WriteLine("OBS connected to web socket!");
            var client = await InitBot(config.channel, token);
            Console.WriteLine("Connected as '{0}'", config.channel);
            var channel = await JoinChannel(client, config.channel);
            Console.WriteLine("Joined channel '{0}'", config.channel);

            var votings = InitVotings(directory, client, obs, config);
            client.SendMessage(channel, "Voting is active!");

            try
            {
                var messages = MessageStream(client);
                var commands = messages.Where(m => m.Message.StartsWith("!")).Select(m => new Tuple<bool, string, string[]>(m.IsModerator, m.Username, m.Message.Substring(1).Split(' ')));

                await commands.ForEachAsync(c => votings.ProcessMessage(c));
            }
            catch (ThreadAbortException)
            {
                client.LeaveChannel(channel);
                client.Disconnect();
            }
        }

        private static IObservable<ChatMessage> MessageStream(TwitchClient client)
        {
            var subject = new Subject<ChatMessage>();

            client.OnMessageReceived += (sender, args) => { subject.OnNext(args.ChatMessage); };

            return subject.AsObservable();
        }

        private static Task<OBSWebsocket> InitObs(string obsUri, string obsPw)
        {
            var source = new TaskCompletionSource<OBSWebsocket>();
            var obs = new OBSWebsocket();

            void ObsConnected(object sender, EventArgs e)
            {
                obs.Connected -= ObsConnected;
                source.SetResult(obs);
            }

            obs.Connected += ObsConnected;
            obs.Connect(obsUri, obsPw);

            return source.Task;
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

        private static VotingHandler InitVotings(string directory, TwitchClient client, OBSWebsocket obs, Config config)
        {
            VotingHandler votings = new VotingHandler(client, obs, config.channel, config.time);
            // Add Scene voting
            string action = "scene";
            Regex reg = new Regex(config.scene);
            List<OBSScene> scenes = obs.ListScenes();
            string[] choices = scenes.Where(s => reg.IsMatch(s.Name)).Select(s => s.Name).ToArray();

            var afterVote = new Action<OBSWebsocket, IEnumerable<Tuple<string, int>>>(ChangeObsScene);
            votings.AddVoting(action, choices, config.time, afterVote);

            SetVotingsFromFile(directory, votings);

            return votings;
        }

        private static Config SetConfigFromFile(string directory)
        {
            Config config = new Config();

            string path = Path.Combine(directory, "config.json");
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
            string path = Path.Combine(directory, "votings.json");
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
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
using TwitchLib.Client.Services;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Enums;
using WebSocketSharp;

namespace OBSChatBot
{
    class Program
    {
        static void Main(string[] args)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OBSChatBot").ToString();
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Console.WriteLine("Path:");
                Console.WriteLine(directory);
                Console.ReadKey();
            }
            var config = SetConfigFromFile(directory);

            if (string.IsNullOrWhiteSpace(config.user) || string.IsNullOrWhiteSpace(config.channel) || string.IsNullOrWhiteSpace(config.uri))
            {
                Console.WriteLine("Config missing values!");
                Console.ReadKey();
                return;
            }

            StartBot(directory, config);
        }

        private static async void StartBot(string directory, Config config)
        {
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

            IAuthenticationResult result;
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                result = new SuccessfulAuthentication(config.user, accessToken);
            }
            else
            {
                result = await TwitchAuthentication.Auth(config.clientId, config.clientSecret, config.redirectUri);
            }

            if (result is SuccessfulAuthentication success)
            {
                await RunBot(success.Name, success.Token, config);
            }
        }

        private static async Task RunBot(string directory, string token, Config config)
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
                var commands = messages.Where(m => m.Message.StartsWith("!")).Select(Command.Parse);

                await commands.ForEachAsync(c =>
                {
                    if (c.Is("exit"))
                    {
                        if (c.IsSender(UserType.Moderator))
                        {
                            QuitBot(client, channel);
                        }
                        else
                        {
                            Whisper(client, c.Message.Username, "You are not an admin!");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Command: {0} -> {1}", c.Name, string.Join(", ", c.Args));
                    }
                });

                await commands.ForEachAsync(c => votings.ProcessMessage(new Tuple<bool, string, string[]>(c.Message.IsModerator, c.Message.Username, c.Args)));
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

            void OnMessage(object sender, OnMessageReceivedArgs args)
            {
                subject.OnNext(args.ChatMessage);
            }

            void OnDisconnect(object sender, OnDisconnectedArgs args)
            {
                client.OnMessageReceived -= OnMessage;
                client.OnDisconnected -= OnDisconnect;
                subject.OnCompleted();
            }

            client.OnMessageReceived += OnMessage;
            client.OnDisconnected += OnDisconnect;

            return subject.AsObservable();
        }

        private static Task Whisper(ITwitchClient client, string receiver, string message)
        {
            var source = new TaskCompletionSource<bool>();

            void EventHandler(object a, OnWhisperSentArgs args)
            {
                if (args.Message == message && args.Receiver == receiver)
                {
                    client.OnWhisperSent -= EventHandler;
                    source.SetResult(true);
                }
            }

            client.OnWhisperSent += EventHandler;
            client.SendWhisper(receiver, message);
            Thread.Sleep(1000);

            return source.Task;
        }

        private static async void QuitBot(ITwitchClient client, JoinedChannel channel)
        {
            await SendMessage(client, channel, "Bye bye!");
            await LeaveChannel(client, channel);
        }

        private static Task<SentMessage> SendMessage(ITwitchClient client, JoinedChannel channel, string message)
        {
            var source = new TaskCompletionSource<SentMessage>();

            void EventHandler(object a, OnMessageSentArgs args)
            {
                var sent = args.SentMessage;
                if (sent.Message == message && sent.Channel == channel.Channel)
                {
                    client.OnMessageSent -= EventHandler;
                    source.SetResult(sent);
                }
            }

            client.OnMessageSent += EventHandler;
            client.SendMessage(channel, message);
            Thread.Sleep(1000);

            return source.Task;
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

            var throttle = new MessageThrottler(client, 20, TimeSpan.FromSeconds(30), applyThrottlingToRawMessages: true);
            client.ChatThrottler = throttle;
            client.WhisperThrottler = throttle;
            throttle.StartQueue();

            client.OnConnectionError += (sender, args) =>
            {
                Console.WriteLine("ERROR: user: {0} error: {1}", args.BotUsername, args.Error.Message);
                Console.WriteLine(args.Error.Exception.ToString());
            };

            client.OnUnaccountedFor += (sender, args) =>
            {
                Console.WriteLine("LIB ERROR: channel: {0} location: {1} raw: {2}", args.Channel, args.Location, args.RawIRC);
            };

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

        private static Task<JoinedChannel> JoinChannel(ITwitchClient client, string channel)
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

        private static Task LeaveChannel(ITwitchClient client, JoinedChannel channel)
        {
            var source = new TaskCompletionSource<bool>();

            void Handler(object a, OnLeftChannelArgs args)
            {
                if (args.Channel == channel.Channel)
                {
                    client.OnLeftChannel -= Handler;
                    source.SetResult(true);
                }
            }

            client.OnLeftChannel += Handler;
            client.LeaveChannel(channel);
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

        private struct Command
        {
            public readonly ChatMessage Message;
            public readonly string Name;
            public readonly string[] Args;
            
            public Command(ChatMessage message, string name, string[] args)
            {
                Message = message;
                Name = name;
                Args = args;
            }

            public bool Is(string name)
            {
                return Name.Equals(name.ToLowerInvariant());
            }

            public bool IsSender(UserType type)
            {
                return Message.UserType >= type;
            }

            public static Command Parse(ChatMessage message, string commandLine)
            {
                var parts = commandLine.Substring(1).Split(' ');
                return new Command(message, parts[0].ToLowerInvariant(), parts.SubArray(1, parts.Length - 1));
            }

            public static Command Parse(ChatMessage message)
            {
                return Parse(message, message.Message);
            }
        }
    }
}
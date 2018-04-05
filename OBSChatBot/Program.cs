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

namespace OBSChatBot
{
    class Program
    {
        static void Main(string[] args)
        {
            TwitchClient client = AuthenticateLogin(args);
            if (client != null)
            {
                TextHandling(client);
            }
        }

        private static TwitchClient AuthenticateLogin(string[] args)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OBSChatBot").ToString();
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Console.WriteLine("Login user: ");
            string user = Console.ReadLine();

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
                //TODO: Refresh access token https://dev.twitch.tv/docs/authentication#refreshing-access-tokens
                //TODO: Token validieren
                authResponse = new SuccessfulAuthentication(user, accessToken);

                //TODO: nicht in else, da es auch aufgerufen werden muss wenn das token nicht mehr gültig ist
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
                    //TODO: save token
                    File.WriteAllLines(path, new[] { success.Token });
                }

                Console.WriteLine("Authentication Success");

                var credentials = new ConnectionCredentials(success.Name, success.Token);
                var client = new TwitchClient();
                client.Initialize(credentials);
                client.Connect();

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

        private static void TextHandling(TwitchClient client)
        {
            client.OnJoinedChannel += Client_OnJoinedChannel;
            client.OnConnected += Client_OnConnected;

            #region Configure
            Console.WriteLine("Connect to channel:");
            string channel = Console.ReadLine();

            int milliseconds = GetVotetime();

            Console.WriteLine("Web socket IP, Default 'ws://localhost:4444':");
            string uri = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(uri))
            {
                uri = "ws://localhost:4444";
            }
            Console.WriteLine("Web socket password: ");
            string pw = Console.ReadLine();
            #endregion

            Console.WriteLine("Regex for OBS scenes:");
            string scenesRegex = Console.ReadLine();
            Regex reg = new Regex(scenesRegex);

            var obs = new OBSWebsocket();
            obs.Connect(uri, pw);

            VotingHandler votings = new VotingHandler(client, channel, obs, milliseconds);
            // Add Scene voting
            string action = "scene";
            List<OBSScene> scenes = obs.ListScenes();
            string[] choices = scenes.Where(s => reg.IsMatch(s.Name)).Select(s => s.Name).ToArray();

            var afterVote = new Action<OBSWebsocket, IEnumerable<Tuple<string, int>>>(ChangeObsScene);
            Voting sceneVote = new Voting(action, choices, milliseconds, afterVote);
            votings.AddVoting(sceneVote);

            client.JoinChannel(channel);

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
            Console.WriteLine("Joined '{0}'", e.Channel);
        }

        private static void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            Console.WriteLine("Connected as '{0}'", e.BotUsername);
        }
        #endregion
    }
}

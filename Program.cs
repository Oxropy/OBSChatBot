using System;
using System.Collections.Generic;
using System.IO;
using OBSChatBot.Authentication;
using OBSChatBot.Handler;
using OBSChatBot.Twitch;
using OBSWebsocketDotNet;
using System.Linq;

namespace OBSChatBot
{
    class Program
    {
        static void Main(string[] args)
        {
            Client client = AuthenticateLogin(args);
            if (client != null)
            {
                TextHandling(client);
            }
        }

        private static Client AuthenticateLogin(string[] args)
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

                CliClientHandler clientHandler = new CliClientHandler();
                Client client = new Client(success.Name, success.Token, clientHandler);
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

        private static void TextHandling(Client client)
        {
            #region Configure
            Console.WriteLine("Connect to channel:");
            string channel = Console.ReadLine();

            Console.WriteLine("Vote time in milliseconds:");
            string input = Console.ReadLine();

            int milliseconds;
            while (!int.TryParse(input, out milliseconds) || milliseconds < 10000)
            {
                Console.WriteLine("Vote time in milliseconds (>= 10000):");
                input = Console.ReadLine();
            }

            Console.WriteLine("Web socket IP:");
            string uri = Console.ReadLine();
            Console.WriteLine("Web socket password: ");
            string pw = Console.ReadLine();
            #endregion

            OBSWebsocketHandler obsHandler = new OBSWebsocketHandler(uri, pw);

            VotingHandler votings = new VotingHandler();
            // Add Scene voting
            string action = "scene";
            List<OBSScene> scenes = obsHandler.GetSceneList();
            string[] choices = scenes.Select(s => s.Name).ToArray();

            Voting sceneVote = new Voting(action, choices, milliseconds, true);
            votings.AddVoting(sceneVote);

            CliChannelHandler channelHandler = new CliChannelHandler(votings, milliseconds, obsHandler);
            client.JoinChannel(channel, channelHandler);

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
                    Voting voting = new Voting(action, choices, 30000, true);
                    votings.AddVoting(voting);
                }
            }

            client.LeaveChannel(channel);
            client.Disconnect();
        }
    }
}

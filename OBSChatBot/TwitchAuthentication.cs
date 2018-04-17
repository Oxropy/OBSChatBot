using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace OBSChatBot.Authentication
{
    public static class TwitchAuthentication
    {
        public static string GenerateState()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static string AuthUri(string state, string clientId, string redirectHost)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query.Add("client_id", clientId);
            query.Add("redirect_uri", redirectHost);
            query.Add("response_type", "code");
            query.Add("scope", "chat_login user_read");
            query.Add("state", state);
            return $"https://api.twitch.tv/kraken/oauth2/authorize?{query}";
        }

        public static async Task<IAuthenticationResult> Auth(string clientId, string clientSecret, string returnUrl)
        {
            var state = GenerateState();
            var authUrl = AuthUri(state, clientId, returnUrl);
            Console.WriteLine("url: {0}", authUrl);

            var httpListener = new HttpListener();
            httpListener.Prefixes.Add(new Uri(new Uri(returnUrl), "/").ToString());
            httpListener.Start();
            var ctx = await httpListener.GetContextAsync();
            var req = ctx.Request;
            var returnedUrl = req.Url;

            var responseText = "Close Me!<script>window.open('', '_self').close()</script>";
            var responseData = Encoding.UTF8.GetBytes(responseText);
            var res = ctx.Response;
            res.StatusCode = 201;
            res.ContentLength64 = responseData.Length;
            res.ContentType = "text/html";
            await res.OutputStream.WriteAsync(responseData, 0, responseData.Length);
            res.OutputStream.Close();

            httpListener.Close();

            return await AuthRequest(returnedUrl, state, clientId, clientSecret, returnUrl);
        }

        private static async Task<IAuthenticationResult> AuthRequest(Uri returnedUrl, string expectedState, string clientId, string clientSecret, string redirectUri)
        {
            var args = HttpUtility.ParseQueryString(returnedUrl.Query);
            if (args["state"] == expectedState)
            {
                try
                {
                    string code = args["code"];
                    var client = new RestClient("https://api.twitch.tv/kraken");

                    var request = new RestRequest("oauth2/token", Method.POST);
                    request.AddParameter("client_id", clientId);
                    request.AddParameter("client_secret", clientSecret);
                    request.AddParameter("code", code);
                    request.AddParameter("grant_type", "authorization_code");
                    request.AddParameter("redirect_uri", redirectUri);
                    request.AddParameter("state", expectedState);

                    var response = client.Execute<TwitchResponse>(request);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var requestUser = new RestRequest("user", Method.GET);
                        client.Authenticator = new OAuth2AuthorizationRequestHeaderAuthenticator(response.Data.access_token);
                        requestUser.AddHeader("Client-ID", clientId);

                        var responseUser = await client.ExecuteTaskAsync<TwitchUserResponse>(requestUser);
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            return new SuccessfulAuthentication(responseUser.Data.name, response.Data.access_token);
                        }
                    }
                    return new FailedAuthentication(AuthenticationFailure.HttpError, response.StatusCode.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: {0}", ex);
                }
            }
            else
            {
                return new FailedAuthentication(AuthenticationFailure.InvalidState);
            }
            return new FailedAuthentication(AuthenticationFailure.Unknown);
        }
        
        public struct TwitchResponse
        {
            public string access_token { get; set; }
            public string refresh_token { get; set; }
            public List<string> scope { get; set; }
        }

        public struct TwitchUserResponse
        {
            public string _id { get; set; }
            public string bio { get; set; }
            public string created_at { get; set; }
            public string display_name { get; set; }
            public string email { get; set; }
            public string email_verified { get; set; }
            public string logo { get; set; }
            public string name { get; set; }
            public string partnered { get; set; }
            public string type { get; set; }
            public string updated_at { get; set; }
        }
    }

    public interface IAuthenticationResult { }

    public struct SuccessfulAuthentication : IAuthenticationResult
    {
        public string Name;
        public string Token;

        public SuccessfulAuthentication(string name, string token)
        {
            Name = name;
            Token = token;
        }
    }

    public struct FailedAuthentication : IAuthenticationResult
    {
        public AuthenticationFailure Failure;
        public string Reason;

        public FailedAuthentication(AuthenticationFailure failure, string reason = "")
        {
            Failure = failure;
            Reason = reason;
        }
    }

    public enum AuthenticationFailure
    {
        InvalidState,
        HttpError,
        Unknown
    }
}
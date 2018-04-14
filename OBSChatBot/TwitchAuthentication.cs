using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Collections.Generic;
using System.Net;
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
            query.Add("scope", "chat_login+user_read");
            query.Add("state", state);
            return $"https://api.twitch.tv/kraken/oauth2/authorize?{query}";
        }

        public static IAuthenticationResult AuthRequest(string returnedUrl, string expectedState, string clientId, string clientSecret, string redirectUri)
        {
            if (returnedUrl.Contains("?")) returnedUrl = returnedUrl.Substring(returnedUrl.IndexOf('?') + 1);
        
            var args = HttpUtility.ParseQueryString(returnedUrl);
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

                        var responseUser = client.Execute<TwitchUserResponse>(requestUser);
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
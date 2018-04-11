using log4net;
using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Web;

namespace OBSChatBot.Authentication
{
    public static class TwitchAuthentication
    {
        static ILog logger = LogManager.GetLogger(typeof(TwitchAuthentication));

        public static string GetState()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static IAuthenticationResult Authenticate(string clientId, string clientSecret, string redirectHost, string redirectUri, Func<string, string> func)
        {
            string state = GetState();
            string url = AuthUri(state, clientId, redirectHost);
            string returnUrl = func(url);
            return AuthRequest(returnUrl, state, clientId, clientSecret, redirectUri);
        }

        public static string AuthUri(string state, string clientId, string redirectHost)
        {
            string scope = "chat_login+user_read";

            StringBuilder sb = new StringBuilder();
            sb.Append("https://api.twitch.tv/kraken/oauth2/authorize");
            sb.AppendFormat("?client_id={0}", clientId);
            sb.AppendFormat("&redirect_uri={0}", redirectHost);
            sb.Append("&response_type=code");
            sb.AppendFormat("&scope={0}", scope);
            sb.AppendFormat("&state={0}", state);

            return sb.ToString();
        }

        public static IAuthenticationResult AuthRequest(string uriQuery, string state, string clientId, string clientSecret, string redirectUri)
        {
            NameValueCollection nvc = HttpUtility.ParseQueryString(uriQuery);

            if (nvc["state"] == state)
            {
                try
                {
                    string code = nvc["code"];

                    var client = new RestClient("https://api.twitch.tv/kraken");

                    #region Request Acces_Token
                    var request = new RestRequest("oauth2/token", Method.POST);
                    request.AddParameter("client_id", clientId);
                    request.AddParameter("client_secret", clientSecret);
                    request.AddParameter("code", code);
                    request.AddParameter("grant_type", "authorization_code");
                    request.AddParameter("redirect_uri", redirectUri);
                    request.AddParameter("state", state);

                    IRestResponse<TwitchResponse> response = client.Execute<TwitchResponse>(request);
                    #endregion
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        #region Request User
                        var requestUser = new RestRequest("user", Method.GET);
                        client.Authenticator = new OAuth2AuthorizationRequestHeaderAuthenticator(response.Data.access_token);
                        requestUser.AddHeader("Client-ID", clientId);

                        IRestResponse<TwitchUserResponse> responseUser = client.Execute<TwitchUserResponse>(requestUser);
                        #endregion
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            return new SuccessfulAuthentication(responseUser.Data.name, response.Data.access_token);
                        }
                    }
                    return new FailedAuthentication(AuthenticationFailure.HttpError, response.StatusCode.ToString());
                }
                catch (Exception ex)
                {
                    logger.ErrorFormat("Error: {0}", ex);
                }
            }
            else
            {
                return new FailedAuthentication(AuthenticationFailure.InvalidState);
            }

            return new FailedAuthentication(AuthenticationFailure.Unknown);
        }
        
#pragma warning disable IDE1006
        public class TwitchResponse
        {
            public string access_token { get; set; }
            public string refresh_token { get; set; }
            public List<string> scope { get; set; }
        }
#pragma warning restore IDE1006

#pragma warning disable IDE1006
        public class TwitchUserResponse
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
#pragma warning restore IDE1006


    }

    public interface IAuthenticationResult { }

    public class SuccessfulAuthentication : IAuthenticationResult
    {
        public string Name;
        public string Token;

        public SuccessfulAuthentication(string name, string token)
        {
            Name = name;
            Token = token;
        }
    }

    public class FailedAuthentication : IAuthenticationResult
    {
        public AuthenticationFailure Failure;
        public string Reason;

        public FailedAuthentication(AuthenticationFailure failure, string reason)
        {
            Failure = failure;
            Reason = reason;
        }

        public FailedAuthentication(AuthenticationFailure failure)
        {
            Failure = failure;
        }
    }

    public enum AuthenticationFailure
    {
        InvalidState,
        HttpError,
        Unknown
    }
}

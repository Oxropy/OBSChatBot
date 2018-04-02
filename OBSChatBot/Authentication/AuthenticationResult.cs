namespace OBSChatBot.Authentication
{

    public interface IAuthenticationResult
    {
    }

    public class SuccessfulAuthentication : IAuthenticationResult
    {
        public string Name;
        public string Token;

        public SuccessfulAuthentication(string name, string token)
        {
            this.Name = name;
            this.Token = token;
        }
    }

    public class FailedAuthentication : IAuthenticationResult
    {
        public AuthenticationFailure Failure;
        public string Reason;

        public FailedAuthentication(AuthenticationFailure failure, string reason)
        {
            this.Failure = failure;
            this.Reason = reason;
        }

        public FailedAuthentication(AuthenticationFailure failure)
        {
            this.Failure = failure;
        }
    }

    public enum AuthenticationFailure
    {
        InvalidState,
        HttpError,
        Unknown
    }
}

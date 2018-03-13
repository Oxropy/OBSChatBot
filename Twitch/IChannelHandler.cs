using TwitchLib.Client.Models;

namespace OBSChatBot.Twitch
{
    public interface IChannelHandler {
        void OnJoin(Channel channel);
        void OnLeave(Channel channel);
        void OnUserJoin(Channel channel, string name);
        void OnUserLeave(Channel channel, string name);
        void OnMessage(Channel channel, ChatMessage msg);
    }
}

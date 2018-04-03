using TwitchLib.Client.Models;

namespace OBSChatBot.Twitch
{
    public interface IChannelHandler {
        void OnJoin(Channel channel);
        void OnMessage(Channel channel, ChatMessage msg);
    }
}

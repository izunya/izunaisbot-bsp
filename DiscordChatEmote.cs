using CP_SDK.Animation;
using CP_SDK.Chat.Interfaces;

namespace IzunaisbotBSP
{
    public class DiscordChatEmote : IChatEmote
    {
        public string Id { get; }
        public string Name { get; }
        public string Uri { get; }
        public int StartIndex { get; }
        public int EndIndex { get; }
        public EAnimationType Animation { get; }

        public DiscordChatEmote(string id, string name, string uri, int startIndex, int endIndex, bool animated)
        {
            Id = id;
            Name = name;
            Uri = uri;
            StartIndex = startIndex;
            EndIndex = endIndex;
            Animation = animated ? EAnimationType.GIF : EAnimationType.NONE;
        }
    }
}
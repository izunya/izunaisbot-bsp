using CP_SDK.Chat.Interfaces;

namespace IzunaisbotBSP
{
    public class DiscordChatChannel : IChatChannel
    {
        public string Id { get; }
        public string Name { get; }
        public bool IsTemp => false;
        public string Prefix => "";
        public bool CanSendMessages => false;
        public bool Live => true;
        public int ViewerCount => 0;

        public DiscordChatChannel(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}

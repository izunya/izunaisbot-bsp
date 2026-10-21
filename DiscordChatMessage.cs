using CP_SDK.Chat.Interfaces;

namespace IzunaisbotBSP
{
    public class DiscordChatMessage : IChatMessage
    {
        public string Id { get; }
        public bool IsSystemMessage => false;
        public bool IsActionMessage => false;
        public bool IsHighlighted => false;
        public bool IsGiganticEmote => false;
        public bool IsPing => false;
        public string Message { get; }
        public IChatUser Sender { get; }
        public IChatChannel Channel { get; }
        public IChatEmote[] Emotes { get; }

        public DiscordChatMessage(string id, string text, IChatUser sender, IChatChannel channel, IChatEmote[] emotes = null)
        {
            Id = id;
            Message = text ?? "";
            Sender = sender;
            Channel = channel;
            Emotes = emotes ?? new IChatEmote[0];
        }
    }
}
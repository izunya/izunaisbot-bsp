using CP_SDK.Chat.Interfaces;

namespace IzunaisbotBSP
{
    public class DiscordChatUser : IChatUser
    {
        public string Id { get; }
        public string UserName { get; }
        public string DisplayName { get; }
        public string PaintedName { get; }
        public string Color { get; }
        public bool IsBroadcaster => false;
        public bool IsModerator => false;
        public bool IsSubscriber => false;
        public bool IsVip => false;
        public IChatBadge[] Badges { get; } = new IChatBadge[0];

        public DiscordChatUser(string id, string userName, string color)
        {
            Id = id;
            UserName = userName ?? id;
            DisplayName = UserName;
            PaintedName = UserName;
            // 디스코드 displayHexColor 는 "#RRGGBB" 또는 null. null 인 경우 디스코드 디폴트.
            Color = string.IsNullOrEmpty(color) ? "#FFFFFF" : color;
        }
    }
}

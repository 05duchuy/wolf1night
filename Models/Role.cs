namespace WolfGameServer.Models
{
    /// <summary>
    /// All roles available in the base One Night Ultimate Werewolf set.
    /// </summary>
    public enum RoleType
    {
        Werewolf,
        Minion,
        Seer,
        Robber,
        Troublemaker,
        Drunk,
        Insomniac,
        Villager
    }

    /// <summary>
    /// Static metadata used to build the Grimoire (rule book) shown to players,
    /// and to drive the fixed night-action order.
    /// </summary>
    public static class RoleInfo
    {
        /// <summary>
        /// Fixed night action order. Villager has no night action and is skipped.
        /// </summary>
        public static readonly RoleType[] NightOrder =
        {
            RoleType.Werewolf,
            RoleType.Minion,
            RoleType.Seer,
            RoleType.Robber,
            RoleType.Troublemaker,
            RoleType.Drunk,
            RoleType.Insomniac
        };

        /// <summary>
        /// Priority order used to build the deck for a given player count.
        /// The first (playerCount + 3) entries are taken to form the deck
        /// (players' cards + 3 middle cards). Always keeps at least one
        /// Werewolf and scales up the special roles as the room grows.
        /// </summary>
        public static readonly RoleType[] DeckPriority =
        {
            RoleType.Werewolf,
            RoleType.Werewolf,
            RoleType.Villager,
            RoleType.Villager,
            RoleType.Villager,
            RoleType.Seer,
            RoleType.Robber,
            RoleType.Troublemaker,
            RoleType.Insomniac,
            RoleType.Drunk,
            RoleType.Minion,
            RoleType.Villager,
            RoleType.Villager
        };

        public static string DisplayName(RoleType role) => role switch
        {
            RoleType.Werewolf => "Sói (Werewolf)",
            RoleType.Minion => "Đồng Bọn (Minion)",
            RoleType.Seer => "Tiên Tri (Seer)",
            RoleType.Robber => "Kẻ Trộm (Robber)",
            RoleType.Troublemaker => "Kẻ Phá Rối (Troublemaker)",
            RoleType.Drunk => "Ma Men (Drunk)",
            RoleType.Insomniac => "Người Mất Ngủ (Insomniac)",
            RoleType.Villager => "Dân Làng (Villager)",
            _ => role.ToString()
        };

        public static string Description(RoleType role) => role switch
        {
            RoleType.Werewolf => "Ban đêm, tất cả Sói mở mắt và nhận biết nhau. Nếu chỉ có một Sói duy nhất, người đó được xem trộm một lá bài ở giữa bàn.",
            RoleType.Minion => "Mở mắt và nhìn biết ai là Sói (Sói không biết mặt Minion). Minion thắng cùng phe Sói dù không phải là Sói.",
            RoleType.Seer => "Có thể xem bài của một người chơi khác, HOẶC xem hai lá bài ở giữa bàn.",
            RoleType.Robber => "Có thể đổi bài của mình với bài của một người chơi khác, sau đó xem lá bài mới của mình.",
            RoleType.Troublemaker => "Đổi bài của hai người chơi khác (không phải của mình) cho nhau, mà không xem bài của họ.",
            RoleType.Drunk => "Đổi bài của mình với một lá bài ở giữa bàn, nhưng KHÔNG được xem lá bài mới đó.",
            RoleType.Insomniac => "Xem lại lá bài hiện tại của chính mình vào cuối đêm, để biết mình có bị đổi bài hay không.",
            RoleType.Villager => "Không có khả năng đặc biệt. Chỉ có thể suy luận và bỏ phiếu vào ban ngày.",
            _ => ""
        };

        /// <summary>
        /// Werewolf-aligned roles for win-condition purposes.
        /// </summary>
        public static bool IsWerewolfTeam(RoleType role) => role is RoleType.Werewolf or RoleType.Minion;

        public static List<RoleType> BuildDeck(int playerCount)
        {
            int totalCards = playerCount + 3;
            var deck = new List<RoleType>();
            for (int i = 0; i < totalCards; i++)
            {
                deck.Add(i < DeckPriority.Length ? DeckPriority[i] : RoleType.Villager);
            }
            return deck;
        }

        /// <summary>
        /// Default role counts (all 8 role types, most at 0) for the given player
        /// count. Used both as the initial lobby suggestion and as the "Reset to
        /// default" action. Total always equals playerCount + 3.
        /// </summary>
        public static Dictionary<RoleType, int> DefaultCounts(int playerCount)
        {
            var counts = Enum.GetValues(typeof(RoleType)).Cast<RoleType>().ToDictionary(r => r, _ => 0);
            foreach (var role in BuildDeck(Math.Clamp(playerCount, 3, 10)))
            {
                counts[role]++;
            }
            return counts;
        }

        /// <summary>Flattens a role-count dictionary (e.g. {Werewolf: 2, Seer: 1, ...}) into a flat deck list.</summary>
        public static List<RoleType> BuildDeckFromCounts(Dictionary<RoleType, int> counts)
        {
            var deck = new List<RoleType>();
            foreach (var kv in counts)
            {
                for (int i = 0; i < kv.Value; i++) deck.Add(kv.Key);
            }
            return deck;
        }

        public static List<object> RoleCountsForClient(Dictionary<RoleType, int> counts)
        {
            // Fixed display order regardless of dictionary enumeration order.
            var order = new[] { RoleType.Werewolf, RoleType.Minion, RoleType.Seer, RoleType.Robber,
                                 RoleType.Troublemaker, RoleType.Drunk, RoleType.Insomniac, RoleType.Villager };
            return order.Select(r => new
            {
                role = r.ToString(),
                name = DisplayName(r),
                count = counts.TryGetValue(r, out var c) ? c : 0
            }).ToList<object>();
        }

        public static List<object> AllRolesForGrimoire()
        {
            var list = new List<object>();
            foreach (RoleType r in Enum.GetValues(typeof(RoleType)))
            {
                list.Add(new { role = r.ToString(), name = DisplayName(r), description = Description(r) });
            }
            return list;
        }
    }
}

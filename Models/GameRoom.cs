using System.Collections.Concurrent;

namespace WolfGameServer.Models
{
    public enum GamePhase
    {
        Lobby,
        Night,
        Day,
        Voting,
        Result
    }

    public class ChatMessage
    {
        public string SenderName { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
    }

    public class GameRoom
    {
        public string RoomCode { get; set; } = "";
        public bool IsPublic { get; set; } = false;
        public string HostConnectionId { get; set; } = "";
        public GamePhase Phase { get; set; } = GamePhase.Lobby;

        public ConcurrentDictionary<string, Player> Players { get; set; } = new();
        public List<RoleType> MiddleCards { get; set; } = new();

        /// <summary>
        /// How many of each role card the host wants in the deck this game.
        /// Visible (read-only) to every player in the lobby; only the host can edit it.
        /// Recomputed automatically to a sensible default whenever the player count
        /// changes, unless the host has manually customized it.
        /// </summary>
        public Dictionary<RoleType, int> RoleCounts { get; set; } = RoleInfo.DefaultCounts(3);
        public bool RoleCountsCustomized { get; set; } = false;

        public List<ChatMessage> ChatHistory { get; set; } = new();

        /// <summary>Pending TaskCompletionSources for the current night step, keyed by connectionId.</summary>
        public ConcurrentDictionary<string, TaskCompletionSource<bool>> PendingActions { get; set; } = new();

        /// <summary>Guards against two people clicking "Start Voting" at once.</summary>
        public bool VotingStarted { get; set; } = false;

        public readonly SemaphoreSlim Lock = new(1, 1);

        public object ToLobbySummary() => new
        {
            roomCode = RoomCode,
            hostName = Players.TryGetValue(HostConnectionId, out var host) ? host.Name : "",
            playerCount = Players.Count,
            maxPlayers = 10
        };
    }
}

namespace WolfGameServer.Models
{
    public class Player
    {
        public string ConnectionId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsHost { get; set; } = false;

        /// <summary>The card the player was originally dealt. Night order is decided by this.</summary>
        public RoleType OriginalRole { get; set; }

        /// <summary>The card the player actually holds right now (may change during the night).</summary>
        public RoleType CurrentRole { get; set; }

        /// <summary>Whether this player has already resolved their night action (or been skipped).</summary>
        public bool HasActed { get; set; } = false;

        /// <summary>ConnectionId of the player this player voted to hang.</summary>
        public string? VoteTargetId { get; set; } = null;

        public object ToPublic() => new { id = ConnectionId, name = Name, isHost = IsHost };
    }
}

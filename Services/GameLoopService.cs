using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using WolfGameServer.Hubs;
using WolfGameServer.Models;

namespace WolfGameServer.Services
{
    /// <summary>
    /// Payload sent by the client when resolving a night action.
    /// Only the fields relevant to the acting role need to be filled in.
    /// </summary>
    public class NightActionPayload
    {
        public string? Mode { get; set; }          // "player" | "middle"  (used by Seer)
        public string? TargetId { get; set; }       // Seer(player mode) / Robber
        public string? TargetId2 { get; set; }       // Troublemaker's 2nd target (TargetId is the 1st)
        public int? MiddleIndex { get; set; }        // Drunk
        public int? MiddleIndex1 { get; set; }       // Seer (middle mode)
        public int? MiddleIndex2 { get; set; }       // Seer (middle mode)
    }

    /// <summary>
    /// Singleton service that owns every room's state and drives the Night -> Day ->
    /// Voting -> Result game loop. All real-time pushes to clients happen from here
    /// via IHubContext, since the loop runs as a background Task outside of any
    /// single Hub method call.
    /// </summary>
    public class GameLoopService
    {
        private readonly IHubContext<GameHub> _hub;
        private const int NightActionSeconds = 15;

        public ConcurrentDictionary<string, GameRoom> Rooms { get; } = new();
        /// <summary>Maps a connectionId to the room code it currently belongs to.</summary>
        public ConcurrentDictionary<string, string> ConnectionRoomMap { get; } = new();

        public GameLoopService(IHubContext<GameHub> hub)
        {
            _hub = hub;
        }

        // ---------------------------------------------------------------
        // Room / lobby management
        // ---------------------------------------------------------------

        public async Task<(GameRoom? room, string? error)> CreateRoomAsync(string connectionId, string playerName, bool isPublic)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return (null, "Tên không được để trống.");

            var room = new GameRoom
            {
                RoomCode = GenerateRoomCode(),
                IsPublic = isPublic,
                HostConnectionId = connectionId
            };

            var player = new Player
            {
                ConnectionId = connectionId,
                Name = playerName.Trim(),
                IsHost = true
            };
            room.Players[connectionId] = player;

            Rooms[room.RoomCode] = room;
            ConnectionRoomMap[connectionId] = room.RoomCode;
            room.RoleCounts = RoleInfo.DefaultCounts(room.Players.Count);

            await _hub.Groups.AddToGroupAsync(connectionId, room.RoomCode);
            await BroadcastPlayerListAsync(room);
            await BroadcastRoleSelectionAsync(room);
            if (isPublic) await BroadcastPublicRoomListAsync();

            return (room, null);
        }

        public async Task<(GameRoom? room, string? error)> JoinRoomAsync(string connectionId, string roomCode, string playerName)
        {
            roomCode = roomCode.Trim().ToUpperInvariant();
            if (!Rooms.TryGetValue(roomCode, out var room))
                return (null, "Không tìm thấy phòng với mã này.");

            if (room.Phase != GamePhase.Lobby)
                return (null, "Ván chơi đã bắt đầu, không thể vào phòng.");

            if (room.Players.Count >= 10)
                return (null, "Phòng đã đủ 10 người chơi.");

            if (string.IsNullOrWhiteSpace(playerName))
                return (null, "Tên không được để trống.");

            var player = new Player
            {
                ConnectionId = connectionId,
                Name = playerName.Trim(),
                IsHost = false
            };
            room.Players[connectionId] = player;
            ConnectionRoomMap[connectionId] = room.RoomCode;
            if (!room.RoleCountsCustomized) room.RoleCounts = RoleInfo.DefaultCounts(room.Players.Count);

            await _hub.Groups.AddToGroupAsync(connectionId, room.RoomCode);
            await BroadcastPlayerListAsync(room);
            await BroadcastRoleSelectionAsync(room);
            if (room.IsPublic) await BroadcastPublicRoomListAsync();

            return (room, null);
        }

        public List<object> GetPublicRooms()
        {
            return Rooms.Values
                .Where(r => r.IsPublic && r.Phase == GamePhase.Lobby)
                .Select(r => r.ToLobbySummary())
                .ToList<object>();
        }

        public async Task<string?> UpdateRoleSelectionAsync(string connectionId, string roomCode, Dictionary<string, int> rawCounts)
        {
            if (!Rooms.TryGetValue(roomCode, out var room)) return "Không tìm thấy phòng.";
            if (room.HostConnectionId != connectionId) return "Chỉ chủ phòng mới có thể tùy chỉnh bộ bài.";
            if (room.Phase != GamePhase.Lobby) return "Không thể tùy chỉnh bộ bài khi ván chơi đã bắt đầu.";

            var newCounts = RoleInfo.DefaultCounts(room.Players.Count).ToDictionary(kv => kv.Key, _ => 0);
            foreach (var kv in rawCounts)
            {
                if (Enum.TryParse<RoleType>(kv.Key, out var role))
                {
                    newCounts[role] = Math.Clamp(kv.Value, 0, 10);
                }
            }

            room.RoleCounts = newCounts;
            room.RoleCountsCustomized = true;
            await BroadcastRoleSelectionAsync(room);
            return null;
        }

        public async Task<string?> ResetRoleSelectionAsync(string connectionId, string roomCode)
        {
            if (!Rooms.TryGetValue(roomCode, out var room)) return "Không tìm thấy phòng.";
            if (room.HostConnectionId != connectionId) return "Chỉ chủ phòng mới có thể tùy chỉnh bộ bài.";
            if (room.Phase != GamePhase.Lobby) return "Không thể tùy chỉnh bộ bài khi ván chơi đã bắt đầu.";

            room.RoleCountsCustomized = false;
            room.RoleCounts = RoleInfo.DefaultCounts(room.Players.Count);
            await BroadcastRoleSelectionAsync(room);
            return null;
        }

        public async Task HandleDisconnectAsync(string connectionId)
        {
            if (!ConnectionRoomMap.TryGetValue(connectionId, out var roomCode)) return;
            await RemovePlayerAsync(connectionId, roomCode, notifyLeaver: false);
        }

        /// <summary>Explicit "leave room" triggered by the player clicking a Home/Leave button.</summary>
        public async Task LeaveRoomAsync(string connectionId, string roomCode)
        {
            await RemovePlayerAsync(connectionId, roomCode, notifyLeaver: true);
        }

        private async Task RemovePlayerAsync(string connectionId, string roomCode, bool notifyLeaver)
        {
            ConnectionRoomMap.TryRemove(connectionId, out _);
            if (!Rooms.TryGetValue(roomCode, out var room)) return;

            room.Players.TryRemove(connectionId, out var leftPlayer);
            await _hub.Groups.RemoveFromGroupAsync(connectionId, roomCode);

            // Resolve any pending night action from the player who left so the loop isn't stuck.
            if (room.PendingActions.TryRemove(connectionId, out var tcs))
                tcs.TrySetResult(true);

            if (room.Players.IsEmpty)
            {
                Rooms.TryRemove(roomCode, out _);
                if (room.IsPublic) await BroadcastPublicRoomListAsync();
                return;
            }

            // Reassign host if needed.
            if (room.HostConnectionId == connectionId)
            {
                var newHost = room.Players.Values.First();
                newHost.IsHost = true;
                room.HostConnectionId = newHost.ConnectionId;
            }

            if (!room.RoleCountsCustomized) room.RoleCounts = RoleInfo.DefaultCounts(room.Players.Count);

            await BroadcastPlayerListAsync(room);
            await BroadcastRoleSelectionAsync(room);
            if (leftPlayer != null)
            {
                await _hub.Clients.Group(roomCode).SendAsync("ReceiveMessage", new
                {
                    senderName = "Hệ thống",
                    text = $"{leftPlayer.Name} đã rời phòng.",
                    isSystem = true
                });
            }
            if (room.IsPublic) await BroadcastPublicRoomListAsync();
            _ = notifyLeaver; // reserved: currently the caller navigates home client-side after the invoke completes.
        }

        /// <summary>Resets a finished room back to the Lobby so the same group of players can play again.</summary>
        public async Task ReturnToLobbyAsync(string roomCode)
        {
            if (!Rooms.TryGetValue(roomCode, out var room)) return;

            room.Phase = GamePhase.Lobby;
            room.VotingStarted = false;
            room.MiddleCards.Clear();
            foreach (var p in room.Players.Values)
            {
                p.VoteTargetId = null;
                p.HasActed = false;
            }

            await _hub.Clients.Group(roomCode).SendAsync("ReturnedToLobby");
            await BroadcastPlayerListAsync(room);
        }

        // ---------------------------------------------------------------
        // Chat
        // ---------------------------------------------------------------

        public async Task SendChatAsync(string connectionId, string roomCode, string text)
        {
            if (!Rooms.TryGetValue(roomCode, out var room)) return;
            if (!room.Players.TryGetValue(connectionId, out var player)) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            var msg = new ChatMessage { SenderName = player.Name, Text = text.Trim() };
            room.ChatHistory.Add(msg);

            await _hub.Clients.Group(roomCode).SendAsync("ReceiveMessage", new
            {
                senderName = msg.SenderName,
                text = msg.Text,
                isSystem = false
            });
        }

        // ---------------------------------------------------------------
        // Game start / night-day-voting loop
        // ---------------------------------------------------------------

        public async Task<string?> StartGameAsync(string connectionId, string roomCode)
        {
            if (!Rooms.TryGetValue(roomCode, out var room)) return "Không tìm thấy phòng.";
            if (room.HostConnectionId != connectionId) return "Chỉ chủ phòng mới có thể bắt đầu ván chơi.";
            if (room.Phase != GamePhase.Lobby) return "Ván chơi đã bắt đầu.";
            if (room.Players.Count < 3 || room.Players.Count > 10)
                return "Số lượng người chơi phải từ 3 đến 10.";

            int required = room.Players.Count + 3;
            int total = room.RoleCounts.Values.Sum();
            if (total != required)
                return $"Tổng số lá bài đang là {total}, nhưng cần đúng {required} lá (số người chơi + 3 lá giữa bàn). Hãy chỉnh lại bộ bài.";
            if (room.RoleCounts.TryGetValue(RoleType.Werewolf, out var wolfCount) && wolfCount < 1)
                return "Ván chơi cần ít nhất 1 lá Sói.";

            // Fire and forget: the loop runs in the background and pushes
            // real-time events via IHubContext for the rest of the game.
            _ = Task.Run(() => RunGameLoopAsync(room));
            return null;
        }

        private async Task RunGameLoopAsync(GameRoom room)
        {
            try
            {
                // 1. Deal roles, using whatever composition the host selected in the lobby.
                var deck = RoleInfo.BuildDeckFromCounts(room.RoleCounts);
                Shuffle(deck);

                var players = room.Players.Values.ToList();
                for (int i = 0; i < players.Count; i++)
                {
                    players[i].OriginalRole = deck[i];
                    players[i].CurrentRole = deck[i];
                }
                room.MiddleCards = deck.Skip(players.Count).Take(3).ToList();
                var deckRoleTypes = deck.ToHashSet();

                // Deck composition is public knowledge in One Night Ultimate Werewolf
                // (players see which roles are in play before the night begins) —
                // it never reveals who holds which card.
                var composition = deck
                    .GroupBy(r => r)
                    .OrderBy(g => Array.IndexOf(RoleInfo.DeckPriority, g.Key))
                    .Select(g => new { role = g.Key.ToString(), name = RoleInfo.DisplayName(g.Key), count = g.Count() })
                    .ToList();
                await _hub.Clients.Group(room.RoomCode).SendAsync("DeckRevealed", composition);

                foreach (var p in players)
                {
                    await _hub.Clients.Client(p.ConnectionId).SendAsync("ReceiveRole", new
                    {
                        role = p.OriginalRole.ToString(),
                        roleName = RoleInfo.DisplayName(p.OriginalRole),
                        isFinal = false
                    });
                }

                room.Phase = GamePhase.Night;
                await _hub.Clients.Group(room.RoomCode).SendAsync("NightStarted", new
                {
                    message = "Đêm đã xuống, cả làng chìm vào giấc ngủ..."
                });

                await Task.Delay(1500);

                // 2. Run each role's night action in fixed order. A role only gets
                // skipped here if it isn't in the deck at all (that's already public
                // knowledge from the DeckRevealed broadcast above). If it IS in the
                // deck but nobody was originally dealt it (i.e. it's one of the 3
                // middle cards), we still wait out the full window below so the
                // round takes the same amount of time either way.
                foreach (var role in RoleInfo.NightOrder)
                {
                    if (!deckRoleTypes.Contains(role)) continue;
                    await RunNightStepAsync(room, role);
                }

                // 3. Night over — reveal final (current) role to each player.
                foreach (var p in room.Players.Values)
                {
                    await _hub.Clients.Client(p.ConnectionId).SendAsync("ReceiveRole", new
                    {
                        role = p.CurrentRole.ToString(),
                        roleName = RoleInfo.DisplayName(p.CurrentRole),
                        isFinal = true
                    });
                }

                room.Phase = GamePhase.Day;
                await _hub.Clients.Group(room.RoomCode).SendAsync("DayStarted", new
                {
                    message = "Trời đã sáng. Hãy cùng tranh luận để tìm ra Sói!"
                });
            }
            catch (Exception ex)
            {
                await _hub.Clients.Group(room.RoomCode).SendAsync("Error", $"Lỗi hệ thống trong ván chơi: {ex.Message}");
            }
        }

        private async Task RunNightStepAsync(GameRoom room, RoleType role)
        {
            var actors = room.Players.Values.Where(p => p.OriginalRole == role).ToList();
            var actorIds = actors.Select(a => a.ConnectionId).ToHashSet();

            // Tell everyone else to wait. When actors.Count == 0 this sends to
            // literally everyone, which is exactly what we want: from the outside,
            // "nobody currently holds this card" must look identical to "someone
            // is quietly acting" — otherwise the silence itself would leak that
            // this role's card is sitting in the middle of the table.
            foreach (var p in room.Players.Values.Where(p => !actorIds.Contains(p.ConnectionId)))
            {
                await _hub.Clients.Client(p.ConnectionId).SendAsync("WaitingForOthers", new
                {
                    message = $"Đang chờ {RoleInfo.DisplayName(role)} hành động..."
                });
            }

            // Fairness rule: every step lasts exactly NightActionSeconds, full stop.
            // We never move on early just because every actor already submitted —
            // if we did, a quick click would make the round noticeably shorter and
            // everyone would learn "someone acted fast just now" for that specific
            // role. A fixed-length window makes every round look identical whether
            // a role is unheld, held by a slow player, or held by a fast one.
            var fullWindow = Task.Delay(TimeSpan.FromSeconds(NightActionSeconds));

            if (actors.Count == 0)
            {
                await fullWindow;
                return;
            }

            foreach (var actor in actors)
            {
                room.PendingActions[actor.ConnectionId] = new TaskCompletionSource<bool>();
                object payload = BuildActionRequestPayload(room, role, actor);
                _ = _hub.Clients.Client(actor.ConnectionId).SendAsync("ReceiveNightActionRequest", payload);
            }

            await fullWindow;

            // Anyone who never submitted within the window is auto-skipped.
            foreach (var actor in actors)
            {
                room.PendingActions.TryRemove(actor.ConnectionId, out _);
                actor.HasActed = true;
            }
        }

        private object BuildActionRequestPayload(GameRoom room, RoleType role, Player actor)
        {
            var otherPlayers = room.Players.Values
                .Where(p => p.ConnectionId != actor.ConnectionId)
                .Select(p => new { id = p.ConnectionId, name = p.Name })
                .ToList();

            return new
            {
                role = role.ToString(),
                roleName = RoleInfo.DisplayName(role),
                description = RoleInfo.Description(role),
                timeLimit = NightActionSeconds,
                players = otherPlayers,
                middleCardCount = room.MiddleCards.Count,
                // Solo werewolf gets to peek a middle card, matching the standard rules.
                isLoneWolf = role == RoleType.Werewolf && room.Players.Values.Count(p => p.OriginalRole == RoleType.Werewolf) == 1
            };
        }

        // ---------------------------------------------------------------
        // Resolving individual night actions (called from the Hub)
        // ---------------------------------------------------------------

        public async Task SubmitNightActionAsync(string connectionId, string roomCode, NightActionPayload payload)
        {
            if (!Rooms.TryGetValue(roomCode, out var room)) return;
            if (!room.Players.TryGetValue(connectionId, out var player)) return;
            if (!room.PendingActions.TryGetValue(connectionId, out var tcs)) return; // not this player's turn (or already resolved)

            try
            {
                await ResolveActionAsync(room, player, payload);
            }
            finally
            {
                tcs.TrySetResult(true);
            }
        }

        private async Task ResolveActionAsync(GameRoom room, Player player, NightActionPayload payload)
        {
            switch (player.OriginalRole)
            {
                case RoleType.Werewolf:
                {
                    var otherWolves = room.Players.Values
                        .Where(p => p.OriginalRole == RoleType.Werewolf && p.ConnectionId != player.ConnectionId)
                        .Select(p => p.Name)
                        .ToList();

                    string resultMsg = otherWolves.Count > 0
                        ? "Đồng bọn Sói của bạn là: " + string.Join(", ", otherWolves)
                        : "Bạn là Sói đơn độc, không có đồng bọn nào khác trong ván này.";

                    await SendActionResult(player.ConnectionId, resultMsg);
                    break;
                }

                case RoleType.Minion:
                {
                    var wolves = room.Players.Values
                        .Where(p => p.OriginalRole == RoleType.Werewolf)
                        .Select(p => p.Name)
                        .ToList();
                    string msg = wolves.Count > 0
                        ? "Các Sói trong ván này là: " + string.Join(", ", wolves)
                        : "Không có Sói nào trong ván này. Bạn đơn độc!";
                    await SendActionResult(player.ConnectionId, msg);
                    break;
                }

                case RoleType.Seer:
                {
                    if (payload?.Mode == "middle" && payload.MiddleIndex1 is int i1 && payload.MiddleIndex2 is int i2
                        && i1 >= 0 && i1 < room.MiddleCards.Count && i2 >= 0 && i2 < room.MiddleCards.Count && i1 != i2)
                    {
                        string msg = $"Hai lá bài giữa bàn bạn xem được: {RoleInfo.DisplayName(room.MiddleCards[i1])} và {RoleInfo.DisplayName(room.MiddleCards[i2])}";
                        await SendActionResult(player.ConnectionId, msg);
                    }
                    else if (payload?.TargetId is string targetId && room.Players.TryGetValue(targetId, out var target))
                    {
                        string msg = $"Bạn đã xem bài của {target.Name}: {RoleInfo.DisplayName(target.CurrentRole)}";
                        await SendActionResult(player.ConnectionId, msg);
                    }
                    else
                    {
                        await SendActionResult(player.ConnectionId, "Bạn đã không chọn hành động nào (hết giờ).");
                    }
                    break;
                }

                case RoleType.Robber:
                {
                    if (payload?.TargetId is string targetId && room.Players.TryGetValue(targetId, out var target) && target.ConnectionId != player.ConnectionId)
                    {
                        (player.CurrentRole, target.CurrentRole) = (target.CurrentRole, player.CurrentRole);
                        string msg = $"Bạn đã đổi bài với {target.Name}. Lá bài mới của bạn là: {RoleInfo.DisplayName(player.CurrentRole)}";
                        await SendActionResult(player.ConnectionId, msg);
                    }
                    else
                    {
                        await SendActionResult(player.ConnectionId, "Bạn đã không đổi bài với ai (hết giờ).");
                    }
                    break;
                }

                case RoleType.Troublemaker:
                {
                    if (payload?.TargetId is string t1id && payload.TargetId2 is string t2id
                        && t1id != t2id
                        && room.Players.TryGetValue(t1id, out var t1) && room.Players.TryGetValue(t2id, out var t2)
                        && t1.ConnectionId != player.ConnectionId && t2.ConnectionId != player.ConnectionId)
                    {
                        (t1.CurrentRole, t2.CurrentRole) = (t2.CurrentRole, t1.CurrentRole);
                        await SendActionResult(player.ConnectionId, $"Bạn đã đổi bài giữa {t1.Name} và {t2.Name} (bạn không được xem bài mới của họ).");
                    }
                    else
                    {
                        await SendActionResult(player.ConnectionId, "Bạn đã không đổi bài của ai (hết giờ).");
                    }
                    break;
                }

                case RoleType.Drunk:
                {
                    if (payload?.MiddleIndex is int mi && mi >= 0 && mi < room.MiddleCards.Count)
                    {
                        (player.CurrentRole, room.MiddleCards[mi]) = (room.MiddleCards[mi], player.CurrentRole);
                        await SendActionResult(player.ConnectionId, "Bạn đã đổi bài của mình với một lá bài ở giữa bàn (bạn không được xem lá bài mới).");
                    }
                    else
                    {
                        await SendActionResult(player.ConnectionId, "Bạn đã không đổi bài (hết giờ).");
                    }
                    break;
                }

                case RoleType.Insomniac:
                {
                    string msg = $"Lá bài hiện tại của bạn là: {RoleInfo.DisplayName(player.CurrentRole)}";
                    await SendActionResult(player.ConnectionId, msg);
                    break;
                }
            }
        }

        private async Task SendActionResult(string connectionId, string message)
        {
            await _hub.Clients.Client(connectionId).SendAsync("ReceiveActionResult", new { message });
        }

        // ---------------------------------------------------------------
        // Voting
        // ---------------------------------------------------------------

        public async Task<string?> RequestStartVotingAsync(string connectionId, string roomCode)
        {
            if (!Rooms.TryGetValue(roomCode, out var room)) return "Không tìm thấy phòng.";
            if (room.Phase != GamePhase.Day) return "Chưa thể bỏ phiếu lúc này.";
            if (room.VotingStarted) return null;

            room.VotingStarted = true;
            room.Phase = GamePhase.Voting;
            await _hub.Clients.Group(roomCode).SendAsync("VotingStarted", new
            {
                message = "Bắt đầu bỏ phiếu! Hãy chọn người bạn nghi ngờ là Sói.",
                players = room.Players.Values.Select(p => new { id = p.ConnectionId, name = p.Name })
            });
            return null;
        }

        public async Task SubmitVoteAsync(string connectionId, string roomCode, string targetId)
        {
            if (!Rooms.TryGetValue(roomCode, out var room)) return;
            if (room.Phase != GamePhase.Voting) return;
            if (!room.Players.TryGetValue(connectionId, out var player)) return;
            if (!room.Players.ContainsKey(targetId)) return;

            player.VoteTargetId = targetId;

            int votedCount = room.Players.Values.Count(p => p.VoteTargetId != null);
            await _hub.Clients.Group(roomCode).SendAsync("VoteProgress", new
            {
                voted = votedCount,
                total = room.Players.Count
            });

            if (votedCount >= room.Players.Count)
            {
                await ResolveVotesAsync(room);
            }
        }

        private async Task ResolveVotesAsync(GameRoom room)
        {
            var tally = room.Players.Values
                .Where(p => p.VoteTargetId != null)
                .GroupBy(p => p.VoteTargetId!)
                .ToDictionary(g => g.Key, g => g.Count());

            int maxVotes = tally.Count > 0 ? tally.Values.Max() : 0;
            var topTargets = tally.Where(kv => kv.Value == maxVotes).Select(kv => kv.Key).ToList();

            string? hangedId = topTargets.Count == 1 ? topTargets[0] : null;
            string winningTeam;
            string? hangedName = null;
            string? hangedRoleName = null;

            if (hangedId != null && room.Players.TryGetValue(hangedId, out var hanged))
            {
                hangedName = hanged.Name;
                hangedRoleName = RoleInfo.DisplayName(hanged.CurrentRole);
                winningTeam = RoleInfo.IsWerewolfTeam(hanged.CurrentRole) ? "Dân Làng" : "Sói";
            }
            else
            {
                // Tie or nobody voted -> nobody dies -> Werewolf team wins by default rule.
                winningTeam = "Sói";
            }

            var reveals = room.Players.Values.Select(p => new
            {
                id = p.ConnectionId,
                name = p.Name,
                originalRole = RoleInfo.DisplayName(p.OriginalRole),
                finalRole = RoleInfo.DisplayName(p.CurrentRole)
            }).ToList();

            var voteTallyNamed = tally.Select(kv => new
            {
                targetName = room.Players.TryGetValue(kv.Key, out var pl) ? pl.Name : "?",
                votes = kv.Value
            }).ToList();

            room.Phase = GamePhase.Result;

            await _hub.Clients.Group(room.RoomCode).SendAsync("ReceiveGameResult", new
            {
                winningTeam,
                hangedName,
                hangedRoleName,
                middleCards = room.MiddleCards.Select(RoleInfo.DisplayName).ToList(),
                reveals,
                voteTally = voteTallyNamed
            });
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private async Task BroadcastPlayerListAsync(GameRoom room)
        {
            await _hub.Clients.Group(room.RoomCode).SendAsync("PlayerListUpdated", new
            {
                roomCode = room.RoomCode,
                isPublic = room.IsPublic,
                hostId = room.HostConnectionId,
                players = room.Players.Values.Select(p => p.ToPublic()).ToList()
            });
        }

        private async Task BroadcastRoleSelectionAsync(GameRoom room)
        {
            int required = room.Players.Count + 3;
            int total = room.RoleCounts.Values.Sum();
            await _hub.Clients.Group(room.RoomCode).SendAsync("RoleSelectionUpdated", new
            {
                counts = RoleInfo.RoleCountsForClient(room.RoleCounts),
                total,
                required,
                isCustomized = room.RoleCountsCustomized
            });
        }

        private async Task BroadcastPublicRoomListAsync()
        {
            await _hub.Clients.All.SendAsync("PublicRoomList", GetPublicRooms());
        }

        private static readonly Random _rng = new();

        private string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            string code;
            do
            {
                code = new string(Enumerable.Range(0, 6).Select(_ => chars[_rng.Next(chars.Length)]).ToArray());
            } while (Rooms.ContainsKey(code));
            return code;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}

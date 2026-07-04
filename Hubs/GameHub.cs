using Microsoft.AspNetCore.SignalR;
using WolfGameServer.Models;
using WolfGameServer.Services;

namespace WolfGameServer.Hubs
{
    public class GameHub : Hub
    {
        private readonly GameLoopService _game;

        public GameHub(GameLoopService game)
        {
            _game = game;
        }

        public async Task CreateRoom(string playerName, bool isPublic)
        {
            var (room, error) = await _game.CreateRoomAsync(Context.ConnectionId, playerName, isPublic);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
                return;
            }
            await Clients.Caller.SendAsync("RoomCreated", new { roomCode = room!.RoomCode });
        }

        public async Task JoinRoom(string roomCode, string playerName)
        {
            var (room, error) = await _game.JoinRoomAsync(Context.ConnectionId, roomCode, playerName);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
                return;
            }
            await Clients.Caller.SendAsync("RoomJoined", new { roomCode = room!.RoomCode });
        }

        public async Task GetPublicRooms()
        {
            await Clients.Caller.SendAsync("PublicRoomList", _game.GetPublicRooms());
        }

        public async Task UpdateRoleSelection(string roomCode, Dictionary<string, int> counts)
        {
            var error = await _game.UpdateRoleSelectionAsync(Context.ConnectionId, roomCode, counts);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
            }
        }

        public async Task ResetRoleSelection(string roomCode)
        {
            var error = await _game.ResetRoleSelectionAsync(Context.ConnectionId, roomCode);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
            }
        }

        public async Task StartGame(string roomCode)
        {
            var error = await _game.StartGameAsync(Context.ConnectionId, roomCode);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
            }
        }

        public async Task SubmitNightAction(string roomCode, NightActionPayload payload)
        {
            await _game.SubmitNightActionAsync(Context.ConnectionId, roomCode, payload);
        }

        public async Task RequestStartVoting(string roomCode)
        {
            var error = await _game.RequestStartVotingAsync(Context.ConnectionId, roomCode);
            if (error != null)
            {
                await Clients.Caller.SendAsync("Error", error);
            }
        }

        public async Task SubmitVote(string roomCode, string targetId)
        {
            await _game.SubmitVoteAsync(Context.ConnectionId, roomCode, targetId);
        }

        public async Task SendChat(string roomCode, string text)
        {
            await _game.SendChatAsync(Context.ConnectionId, roomCode, text);
        }

        public async Task LeaveRoom(string roomCode)
        {
            await _game.LeaveRoomAsync(Context.ConnectionId, roomCode);
        }

        public async Task ReturnToLobby(string roomCode)
        {
            await _game.ReturnToLobbyAsync(roomCode);
        }

        public async Task GetGrimoire()
        {
            await Clients.Caller.SendAsync("GrimoireData", RoleInfo.AllRolesForGrimoire());
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await _game.HandleDisconnectAsync(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}

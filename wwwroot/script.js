// =========================================================
// MA SÓI MỘT ĐÊM — client logic
// =========================================================

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/gamehub")
  .withAutomaticReconnect()
  .build();

// ---------------- Global state ----------------
const state = {
  myName: "",
  roomCode: "",
  isHost: false,
  players: [],       // [{id, name, isHost}]
  hostId: "",
  myRole: null,
  deckComposition: [],
  middleCardCount: 3,
  currentTimer: null,
  naAutoCloseHandle: null,
  votedFor: null,
  troublemakerPicks: [],
  seerMiddlePicks: [],
  seerMode: "player",
  roleSelection: { counts: [], total: 0, required: 0, isCustomized: false }
};

let naConfirmMode = "submit"; // "submit" (resolving an action) | "dismiss" (reading a result)

const ROLE_ICON = {
  Werewolf: "🐺", Minion: "🎭", Seer: "🔮", Robber: "🗡️",
  Troublemaker: "🔀", Drunk: "🍺", Insomniac: "😴", Villager: "🌾"
};

// ---------------- Screen helpers ----------------
function showScreen(id) {
  document.querySelectorAll(".screen").forEach(s => s.classList.remove("active"));
  document.getElementById(id).classList.add("active");
}
function showModal(id) { document.getElementById(id).classList.remove("hidden"); }
function hideModal(id) { document.getElementById(id).classList.add("hidden"); }

// ---------------- Starfield background ----------------
(function starfield() {
  const canvas = document.getElementById("starCanvas");
  const ctx = canvas.getContext("2d");
  let stars = [];
  function resize() {
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
    stars = Array.from({ length: 140 }, () => ({
      x: Math.random() * canvas.width,
      y: Math.random() * canvas.height,
      r: Math.random() * 1.4 + 0.2,
      phase: Math.random() * Math.PI * 2
    }));
  }
  window.addEventListener("resize", resize);
  resize();
  let t = 0;
  function tick() {
    t += 0.02;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = "#dfe9f5";
    for (const s of stars) {
      const twinkle = 0.5 + 0.5 * Math.sin(t + s.phase);
      ctx.globalAlpha = 0.15 + twinkle * 0.5;
      ctx.beginPath();
      ctx.arc(s.x, s.y, s.r, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.globalAlpha = 1;
    requestAnimationFrame(tick);
  }
  tick();
})();

// =========================================================
// LOGIN SCREEN
// =========================================================

document.getElementById("btnShowCreate").onclick = () => {
  document.getElementById("panelCreate").classList.remove("hidden");
  document.getElementById("panelJoin").classList.add("hidden");
};
document.getElementById("btnShowJoin").onclick = () => {
  document.getElementById("panelJoin").classList.remove("hidden");
  document.getElementById("panelCreate").classList.add("hidden");
};

function getNameOrError() {
  const name = document.getElementById("inputName").value.trim();
  if (!name) {
    document.getElementById("loginError").textContent = "Vui lòng nhập tên của bạn.";
    return null;
  }
  return name;
}

document.getElementById("btnCreateConfirm").onclick = async () => {
  const name = getNameOrError();
  if (!name) return;
  state.myName = name;
  const isPublic = document.getElementById("chkPublic").checked;
  await connection.invoke("CreateRoom", name, isPublic);
};

document.getElementById("btnJoinConfirm").onclick = async () => {
  const name = getNameOrError();
  if (!name) return;
  const code = document.getElementById("inputRoomCode").value.trim().toUpperCase();
  if (!code) {
    document.getElementById("loginError").textContent = "Vui lòng nhập mã phòng.";
    return;
  }
  state.myName = name;
  await connection.invoke("JoinRoom", code, name);
};

function renderPublicRooms(rooms) {
  const container = document.getElementById("publicRoomList");
  container.innerHTML = "";
  if (!rooms || rooms.length === 0) {
    container.innerHTML = '<p class="empty-note">Chưa có phòng công khai nào...</p>';
    return;
  }
  rooms.forEach(r => {
    const row = document.createElement("div");
    row.className = "room-row";
    row.innerHTML = `
      <span class="room-info">${r.roomCode} — chủ phòng: ${r.hostName} (${r.playerCount}/${r.maxPlayers})</span>
      <button>Vào</button>`;
    row.querySelector("button").onclick = async () => {
      const name = getNameOrError();
      if (!name) return;
      state.myName = name;
      await connection.invoke("JoinRoom", r.roomCode, name);
    };
    container.appendChild(row);
  });
}

// =========================================================
// LOBBY SCREEN
// =========================================================

function renderLobby() {
  document.getElementById("lobbyRoomCode").textContent = state.roomCode;
  const list = document.getElementById("lobbyPlayerList");
  list.innerHTML = "";
  state.players.forEach(p => {
    const chip = document.createElement("div");
    chip.className = "player-chip" + (p.id === state.hostId ? " host" : "");
    chip.textContent = p.name + (p.id === state.hostId ? " (chủ phòng)" : "");
    list.appendChild(chip);
  });

  const iAmHost = state.hostId === connection.connectionId;
  const startBtn = document.getElementById("btnStartGame");
  const waitNote = document.getElementById("lobbyWaitNote");
  const countOk = state.players.length >= 3 && state.players.length <= 10;
  const rolesOk = state.roleSelection.total === state.roleSelection.required && state.roleSelection.required > 0;
  const canStart = countOk && rolesOk;

  if (iAmHost) {
    startBtn.classList.remove("hidden");
    startBtn.disabled = !canStart;
    if (!countOk) waitNote.textContent = "Cần từ 3 đến 10 người chơi để bắt đầu.";
    else if (!rolesOk) waitNote.textContent = "Tổng số lá bài chưa khớp — hãy chỉnh lại bộ bài bên phải.";
    else waitNote.textContent = "";
  } else {
    startBtn.classList.add("hidden");
    waitNote.textContent = "Đang chờ chủ phòng bắt đầu...";
  }
}

document.getElementById("btnStartGame").onclick = async () => {
  await connection.invoke("StartGame", state.roomCode);
};

function renderRoleSelection() {
  const iAmHost = state.hostId === connection.connectionId;
  const { counts, total, required, isCustomized } = state.roleSelection;

  const badge = document.getElementById("roleTotalBadge");
  badge.textContent = `${total} / ${required} lá`;
  badge.classList.toggle("mismatch", total !== required);

  document.getElementById("roleSelectHint").textContent = iAmHost
    ? "Bạn có thể tăng/giảm số lượng từng lá bài. Tổng số lá phải đúng bằng số người chơi + 3."
    : "Đây là bộ bài chủ phòng đã chọn cho ván này.";

  document.getElementById("btnResetRoleSelection").classList.toggle("hidden", !iAmHost || !isCustomized);

  const list = document.getElementById("roleSelectList");
  list.innerHTML = "";
  counts.forEach(entry => {
    const row = document.createElement("div");
    row.className = "role-select-row";
    row.innerHTML = `
      <span class="rs-name">${ROLE_ICON[entry.role] || ""} ${entry.name}</span>
      <div class="rs-stepper">
        ${iAmHost ? `<button data-delta="-1">−</button>` : ""}
        <span class="rs-count">${entry.count}</span>
        ${iAmHost ? `<button data-delta="1">+</button>` : ""}
      </div>`;
    if (iAmHost) {
      row.querySelectorAll("button").forEach(btn => {
        btn.onclick = () => adjustRoleCount(entry.role, parseInt(btn.dataset.delta, 10));
      });
    }
    list.appendChild(row);
  });
}

async function adjustRoleCount(role, delta) {
  const newCounts = {};
  state.roleSelection.counts.forEach(entry => {
    newCounts[entry.role] = entry.role === role ? Math.max(0, entry.count + delta) : entry.count;
  });
  await connection.invoke("UpdateRoleSelection", state.roomCode, newCounts);
}

document.getElementById("btnResetRoleSelection").onclick = async () => {
  await connection.invoke("ResetRoleSelection", state.roomCode);
};

function resetClientStateToLogin() {
  state.roomCode = "";
  state.isHost = false;
  state.players = [];
  state.hostId = "";
  state.myRole = null;
  state.deckComposition = [];
  state.votedFor = null;
  clearLocalTimer();
  if (state.naAutoCloseHandle) { clearTimeout(state.naAutoCloseHandle); state.naAutoCloseHandle = null; }
  hideModal("modalNightAction");
  hideModal("modalVoting");
  hideModal("modalResult");
  hideModal("modalGrimoire");
  document.getElementById("loginError").textContent = "";
  document.getElementById("chatMessages").innerHTML = "";
  document.getElementById("chatMessagesLobby").innerHTML = "";
  showScreen("screen-login");
  connection.invoke("GetPublicRooms").catch(() => {});
}

async function leaveRoomAndGoHome() {
  const roomCode = state.roomCode;
  resetClientStateToLogin();
  if (roomCode) {
    try { await connection.invoke("LeaveRoom", roomCode); } catch (e) { /* already disconnected, ignore */ }
  }
}

document.getElementById("btnLeaveLobby").onclick = leaveRoomAndGoHome;
document.getElementById("btnLeaveGame").onclick = leaveRoomAndGoHome;
document.getElementById("btnResultHome").onclick = leaveRoomAndGoHome;

// =========================================================
// GAME SCREEN — table & seats
// =========================================================

function renderPlayerRing() {
  const ring = document.getElementById("playerRing");
  ring.innerHTML = "";
  const n = state.players.length;
  const rect = document.querySelector(".table-wrap").getBoundingClientRect();
  const cx = rect.width / 2, cy = rect.height / 2;
  const radius = Math.min(rect.width, rect.height) * 0.42;

  state.players.forEach((p, i) => {
    const angle = (2 * Math.PI * i / n) - Math.PI / 2;
    const x = cx + radius * Math.cos(angle);
    const y = cy + radius * Math.sin(angle);
    const seat = document.createElement("div");
    seat.className = "player-seat";
    seat.style.left = x + "px";
    seat.style.top = y + "px";
    seat.innerHTML = `
      <div class="seat-card" id="seat-${p.id}"></div>
      <div class="seat-name${p.id === state.hostId ? " is-host" : ""}">${p.name}${p.id === connection.connectionId ? " (bạn)" : ""}</div>`;
    ring.appendChild(seat);
  });
}
window.addEventListener("resize", () => { if (document.getElementById("screen-game").classList.contains("active")) renderPlayerRing(); });

function renderMiddleCards() {
  document.getElementById("middleCards").querySelectorAll(".mid-card").forEach(el => {
    el.innerHTML = '<div class="card-back"></div>';
  });
}

function setStatus(msg) {
  document.getElementById("statusBox").textContent = msg;
}

function setPhaseBanner(text, isDay) {
  const banner = document.getElementById("phaseBanner");
  banner.textContent = text;
  banner.classList.toggle("day", !!isDay);
  document.getElementById("moonGlow").classList.toggle("day", !!isDay);
}

// ---------------- Night action timer (visual only; server enforces the real limit) ----------------
function startLocalTimer(seconds) {
  clearLocalTimer();
  const wrap = document.getElementById("timerWrap");
  const fill = document.getElementById("timerFill");
  const secEl = document.getElementById("timerSeconds");
  wrap.classList.remove("hidden");
  let remaining = seconds;
  secEl.textContent = remaining;
  fill.style.width = "100%";
  state.currentTimer = setInterval(() => {
    remaining -= 1;
    secEl.textContent = Math.max(remaining, 0);
    fill.style.width = Math.max((remaining / seconds) * 100, 0) + "%";
    if (remaining <= 0) {
      clearInterval(state.currentTimer);
      state.currentTimer = null;
      wrap.classList.add("hidden");
      onLocalTimerExpired();
    }
  }, 1000);
}
function clearLocalTimer() {
  if (state.currentTimer) clearInterval(state.currentTimer);
  state.currentTimer = null;
  document.getElementById("timerWrap").classList.add("hidden");
}
function onLocalTimerExpired() {
  // Lock further input once time is up (a late click would just be ignored by the
  // server anyway), then give a short grace period for a last-moment result to
  // arrive before auto-closing the modal.
  document.getElementById("btnNaConfirm").disabled = true;
  document.getElementById("naBody").querySelectorAll(".option-btn").forEach(b => { b.style.pointerEvents = "none"; b.style.opacity = "0.5"; });
  if (state.naAutoCloseHandle) clearTimeout(state.naAutoCloseHandle);
  state.naAutoCloseHandle = setTimeout(() => {
    hideModal("modalNightAction");
    state.naAutoCloseHandle = null;
  }, 1500);
}

// =========================================================
// NIGHT ACTION MODAL
// =========================================================

let pendingPayloadBuilder = null;

function openNightActionModal(payload) {
  if (state.naAutoCloseHandle) { clearTimeout(state.naAutoCloseHandle); state.naAutoCloseHandle = null; }
  naConfirmMode = "submit";
  document.getElementById("naRoleName").textContent = `${ROLE_ICON[payload.role] || ""} ${payload.roleName}`;
  document.getElementById("naDescription").textContent = payload.description;
  const body = document.getElementById("naBody");
  body.innerHTML = "";
  const confirmBtn = document.getElementById("btnNaConfirm");
  confirmBtn.classList.remove("hidden");
  confirmBtn.disabled = false;
  pendingPayloadBuilder = () => ({});

  state.troublemakerPicks = [];
  state.seerMiddlePicks = [];
  state.seerMode = "player";

  switch (payload.role) {
    case "Werewolf":
    case "Minion":
    case "Insomniac": {
      confirmBtn.textContent = "Mở Mắt";
      break;
    }
    case "Robber": {
      confirmBtn.textContent = "Xác Nhận Đổi Bài";
      confirmBtn.disabled = true;
      const list = document.createElement("div");
      list.className = "option-list";
      payload.players.forEach(pl => {
        const btn = document.createElement("button");
        btn.className = "option-btn";
        btn.textContent = pl.name;
        btn.onclick = () => {
          list.querySelectorAll(".option-btn").forEach(b => b.classList.remove("selected"));
          btn.classList.add("selected");
          pendingPayloadBuilder = () => ({ targetId: pl.id });
          confirmBtn.disabled = false;
        };
        list.appendChild(btn);
      });
      body.appendChild(list);
      break;
    }
    case "Troublemaker": {
      confirmBtn.textContent = "Xác Nhận Đổi Bài";
      confirmBtn.disabled = true;
      const hintEl = document.createElement("p");
      hintEl.className = "hint";
      hintEl.textContent = "Chọn đúng 2 người chơi để đổi bài cho nhau.";
      body.appendChild(hintEl);
      const list = document.createElement("div");
      list.className = "option-list";
      payload.players.forEach(pl => {
        const btn = document.createElement("button");
        btn.className = "option-btn";
        btn.textContent = pl.name;
        btn.onclick = () => {
          const idx = state.troublemakerPicks.indexOf(pl.id);
          if (idx >= 0) {
            state.troublemakerPicks.splice(idx, 1);
            btn.classList.remove("selected");
          } else if (state.troublemakerPicks.length < 2) {
            state.troublemakerPicks.push(pl.id);
            btn.classList.add("selected");
          }
          confirmBtn.disabled = state.troublemakerPicks.length !== 2;
          pendingPayloadBuilder = () => ({ targetId: state.troublemakerPicks[0], targetId2: state.troublemakerPicks[1] });
        };
        list.appendChild(btn);
      });
      body.appendChild(list);
      break;
    }
    case "Drunk": {
      confirmBtn.textContent = "Xác Nhận Đổi Bài";
      confirmBtn.disabled = true;
      const hintEl = document.createElement("p");
      hintEl.className = "hint";
      hintEl.textContent = "Chọn 1 lá bài ở giữa bàn để đổi (bạn sẽ không biết đó là lá gì).";
      body.appendChild(hintEl);
      const list = document.createElement("div");
      list.className = "option-list";
      for (let i = 0; i < payload.middleCardCount; i++) {
        const btn = document.createElement("button");
        btn.className = "option-btn";
        btn.textContent = `Lá bài giữa bàn #${i + 1}`;
        btn.onclick = () => {
          list.querySelectorAll(".option-btn").forEach(b => b.classList.remove("selected"));
          btn.classList.add("selected");
          pendingPayloadBuilder = () => ({ middleIndex: i });
          confirmBtn.disabled = false;
        };
        list.appendChild(btn);
      }
      body.appendChild(list);
      break;
    }
    case "Seer": {
      confirmBtn.textContent = "Xác Nhận Xem Bài";
      confirmBtn.disabled = true;
      const tabs = document.createElement("div");
      tabs.className = "mode-tabs";
      const tabPlayer = document.createElement("button");
      tabPlayer.textContent = "Xem 1 người chơi";
      tabPlayer.className = "active";
      const tabMiddle = document.createElement("button");
      tabMiddle.textContent = "Xem 2 lá giữa bàn";
      tabs.appendChild(tabPlayer);
      tabs.appendChild(tabMiddle);
      body.appendChild(tabs);

      const listWrap = document.createElement("div");
      body.appendChild(listWrap);

      function renderPlayerMode() {
        listWrap.innerHTML = "";
        confirmBtn.disabled = true;
        const list = document.createElement("div");
        list.className = "option-list";
        payload.players.forEach(pl => {
          const btn = document.createElement("button");
          btn.className = "option-btn";
          btn.textContent = pl.name;
          btn.onclick = () => {
            list.querySelectorAll(".option-btn").forEach(b => b.classList.remove("selected"));
            btn.classList.add("selected");
            pendingPayloadBuilder = () => ({ mode: "player", targetId: pl.id });
            confirmBtn.disabled = false;
          };
          list.appendChild(btn);
        });
        listWrap.appendChild(list);
      }

      function renderMiddleMode() {
        listWrap.innerHTML = "";
        confirmBtn.disabled = true;
        state.seerMiddlePicks = [];
        const hintEl = document.createElement("p");
        hintEl.className = "hint";
        hintEl.textContent = "Chọn đúng 2 lá bài giữa bàn.";
        listWrap.appendChild(hintEl);
        const list = document.createElement("div");
        list.className = "option-list";
        for (let i = 0; i < payload.middleCardCount; i++) {
          const btn = document.createElement("button");
          btn.className = "option-btn";
          btn.textContent = `Lá bài giữa bàn #${i + 1}`;
          btn.onclick = () => {
            const idx = state.seerMiddlePicks.indexOf(i);
            if (idx >= 0) { state.seerMiddlePicks.splice(idx, 1); btn.classList.remove("selected"); }
            else if (state.seerMiddlePicks.length < 2) { state.seerMiddlePicks.push(i); btn.classList.add("selected"); }
            confirmBtn.disabled = state.seerMiddlePicks.length !== 2;
            pendingPayloadBuilder = () => ({ mode: "middle", middleIndex1: state.seerMiddlePicks[0], middleIndex2: state.seerMiddlePicks[1] });
          };
          list.appendChild(btn);
        }
        listWrap.appendChild(list);
      }

      tabPlayer.onclick = () => { tabPlayer.classList.add("active"); tabMiddle.classList.remove("active"); renderPlayerMode(); };
      tabMiddle.onclick = () => { tabMiddle.classList.add("active"); tabPlayer.classList.remove("active"); renderMiddleMode(); };
      renderPlayerMode();
      break;
    }
  }

  showModal("modalNightAction");
  startLocalTimer(payload.timeLimit || 15);
}

document.getElementById("btnNaConfirm").onclick = async () => {
  if (naConfirmMode === "dismiss") {
    if (state.naAutoCloseHandle) { clearTimeout(state.naAutoCloseHandle); state.naAutoCloseHandle = null; }
    hideModal("modalNightAction");
    return;
  }
  const payload = pendingPayloadBuilder ? pendingPayloadBuilder() : {};
  document.getElementById("btnNaConfirm").disabled = true;
  await connection.invoke("SubmitNightAction", state.roomCode, payload);
};

// =========================================================
// VOTING
// =========================================================

document.getElementById("btnStartVoting").onclick = async () => {
  await connection.invoke("RequestStartVoting", state.roomCode);
};

function renderVoteTargets(players) {
  const container = document.getElementById("voteTargets");
  container.innerHTML = "";
  players.forEach(p => {
    const btn = document.createElement("button");
    btn.className = "option-btn";
    btn.textContent = p.name;
    btn.onclick = async () => {
      container.querySelectorAll(".option-btn").forEach(b => b.classList.remove("selected"));
      btn.classList.add("selected");
      state.votedFor = p.id;
      await connection.invoke("SubmitVote", state.roomCode, p.id);
    };
    container.appendChild(btn);
  });
}

// =========================================================
// CHAT
// =========================================================

function appendChatMessage(senderName, text, isSystem) {
  const html = isSystem ? text : `<span class="sender">${senderName}:</span> ${escapeHtml(text)}`;
  ["chatMessages", "chatMessagesLobby"].forEach(id => {
    const box = document.getElementById(id);
    if (!box) return;
    const div = document.createElement("div");
    div.className = "chat-msg" + (isSystem ? " system" : "");
    div.innerHTML = html;
    box.appendChild(div);
    box.scrollTop = box.scrollHeight;
  });
}
function escapeHtml(str) {
  const d = document.createElement("div");
  d.textContent = str;
  return d.innerHTML;
}

async function sendChatFrom(inputId) {
  const input = document.getElementById(inputId);
  const text = input.value.trim();
  if (!text) return;
  input.value = "";
  await connection.invoke("SendChat", state.roomCode, text);
}

document.getElementById("btnSendChat").onclick = () => sendChatFrom("chatInput");
document.getElementById("chatInput").addEventListener("keydown", e => { if (e.key === "Enter") sendChatFrom("chatInput"); });

document.getElementById("btnSendChatLobby").onclick = () => sendChatFrom("chatInputLobby");
document.getElementById("chatInputLobby").addEventListener("keydown", e => { if (e.key === "Enter") sendChatFrom("chatInputLobby"); });

// =========================================================
// GRIMOIRE
// =========================================================

document.getElementById("btnOpenGrimoire").onclick = async () => {
  await connection.invoke("GetGrimoire");
  showModal("modalGrimoire");
};
document.getElementById("btnCloseGrimoire").onclick = () => hideModal("modalGrimoire");

function renderGrimoireInPlay() {
  const container = document.getElementById("grimoireInPlay");
  container.innerHTML = "";
  if (!state.deckComposition.length) {
    container.innerHTML = '<p class="hint">Bộ bài sẽ hiện ra khi ván chơi bắt đầu.</p>';
    return;
  }
  state.deckComposition.forEach(entry => {
    const card = document.createElement("div");
    card.className = "grimoire-card";
    card.innerHTML = `<div class="g-name">${ROLE_ICON[entry.role] || ""} ${entry.name} ×${entry.count}</div>`;
    container.appendChild(card);
  });
}

function renderGrimoireAll(roles) {
  const container = document.getElementById("grimoireAll");
  container.innerHTML = "";
  roles.forEach(r => {
    const card = document.createElement("div");
    card.className = "grimoire-card";
    card.innerHTML = `<div class="g-name">${ROLE_ICON[r.role] || ""} ${r.name}</div><div class="g-desc">${r.description}</div>`;
    container.appendChild(card);
  });
}

// =========================================================
// RESULT MODAL
// =========================================================

document.getElementById("btnBackToLobby").onclick = async () => {
  hideModal("modalResult");
  await connection.invoke("ReturnToLobby", state.roomCode);
};

connection.on("ReturnedToLobby", () => {
  hideModal("modalResult");
  showScreen("screen-lobby");
  renderLobby();
});

// =========================================================
// SIGNALR EVENT HANDLERS
// =========================================================

connection.on("Error", (msg) => {
  const loginScreenActive = document.getElementById("screen-login").classList.contains("active");
  if (loginScreenActive) {
    document.getElementById("loginError").textContent = msg;
  } else {
    appendChatMessage("", `⚠️ ${msg}`, true);
  }
});

connection.on("RoomCreated", (data) => {
  state.roomCode = data.roomCode;
  state.isHost = true;
  showScreen("screen-lobby");
  renderLobby();
});

connection.on("RoomJoined", (data) => {
  state.roomCode = data.roomCode;
  showScreen("screen-lobby");
  renderLobby();
});

connection.on("PublicRoomList", (rooms) => renderPublicRooms(rooms));

connection.on("PlayerListUpdated", (data) => {
  state.players = data.players;
  state.hostId = data.hostId;
  state.roomCode = data.roomCode;
  renderLobby();
  renderRoleSelection();
});

connection.on("RoleSelectionUpdated", (data) => {
  state.roleSelection = data;
  renderRoleSelection();
});

connection.on("ReceiveRole", (data) => {
  state.myRole = data.role;
  document.getElementById("myRoleCard").textContent = ROLE_ICON[data.role] || "?";
  document.getElementById("myRoleName").textContent = data.roleName + (data.isFinal ? " (cuối cùng)" : " (ban đầu)");
});

connection.on("DeckRevealed", (deck) => {
  state.deckComposition = deck;
  renderGrimoireInPlay();
});

connection.on("GrimoireData", (roles) => renderGrimoireAll(roles));

connection.on("NightStarted", (data) => {
  hideModal("modalNightAction");
  clearLocalTimer();
  if (state.naAutoCloseHandle) { clearTimeout(state.naAutoCloseHandle); state.naAutoCloseHandle = null; }
  showScreen("screen-game");
  renderPlayerRing();
  renderMiddleCards();
  setPhaseBanner("🌙 Ban Đêm", false);
  setStatus(data.message);
  document.getElementById("btnStartVoting").classList.add("hidden");
});

connection.on("WaitingForOthers", (data) => {
  // Note: we deliberately do NOT hide the night action modal here. If this
  // player just resolved their own action, their result is being shown in
  // that same modal and must stay on screen until they dismiss it themselves
  // — otherwise the very next role's "waiting" message would instantly wipe
  // out messages like "your fellow wolves are: ...".
  setStatus(data.message);
});

connection.on("ReceiveNightActionRequest", (payload) => {
  setStatus(`Đến lượt bạn: ${payload.roleName}`);
  openNightActionModal(payload);
});

connection.on("ReceiveActionResult", (data) => {
  clearLocalTimer();
  if (state.naAutoCloseHandle) { clearTimeout(state.naAutoCloseHandle); state.naAutoCloseHandle = null; }
  naConfirmMode = "dismiss";
  document.getElementById("naRoleName").textContent = "Kết Quả";
  document.getElementById("naDescription").textContent = "";
  document.getElementById("naBody").innerHTML = `<p>${escapeHtml(data.message)}</p>`;
  const confirmBtn = document.getElementById("btnNaConfirm");
  confirmBtn.textContent = "Đã Rõ";
  confirmBtn.disabled = false;
  confirmBtn.classList.remove("hidden");
  showModal("modalNightAction");
  setStatus(data.message);
});

connection.on("DayStarted", (data) => {
  hideModal("modalNightAction");
  clearLocalTimer();
  if (state.naAutoCloseHandle) { clearTimeout(state.naAutoCloseHandle); state.naAutoCloseHandle = null; }
  setPhaseBanner("☀️ Ban Ngày", true);
  setStatus(data.message);
  document.getElementById("btnStartVoting").classList.remove("hidden");
});

connection.on("VotingStarted", (data) => {
  setPhaseBanner("🗳️ Bỏ Phiếu", true);
  setStatus(data.message);
  document.getElementById("btnStartVoting").classList.add("hidden");
  document.getElementById("voteProgress").textContent = "";
  renderVoteTargets(data.players);
  showModal("modalVoting");
});

connection.on("VoteProgress", (data) => {
  document.getElementById("voteProgress").textContent = `Đã bỏ phiếu: ${data.voted}/${data.total}`;
});

connection.on("ReceiveGameResult", (data) => {
  hideModal("modalVoting");
  const title = data.winningTeam === "Sói" ? "🐺 PHE SÓI CHIẾN THẮNG" : "🌾 PHE DÂN LÀNG CHIẾN THẮNG";
  document.getElementById("resultTitle").textContent = title;
  document.getElementById("resultSummary").textContent = data.hangedName
    ? `${data.hangedName} đã bị treo cổ. Vai trò cuối cùng: ${data.hangedRoleName}.`
    : "Không có ai bị treo cổ (hòa phiếu).";
  document.getElementById("resultMiddle").textContent = "Lá bài giữa bàn: " + data.middleCards.join(", ");

  const revealContainer = document.getElementById("resultReveals");
  revealContainer.innerHTML = "";
  data.reveals.forEach(r => {
    const row = document.createElement("div");
    row.className = "reveal-row";
    row.innerHTML = `<span>${r.name}</span><span>${r.originalRole} → ${r.finalRole}</span>`;
    revealContainer.appendChild(row);
  });

  showModal("modalResult");
});

connection.on("ReceiveMessage", (data) => appendChatMessage(data.senderName, data.text, data.isSystem));

// =========================================================
// STARTUP
// =========================================================

connection.start()
  .then(() => connection.invoke("GetPublicRooms"))
  .catch(err => {
    document.getElementById("loginError").textContent = "Không thể kết nối tới máy chủ. Vui lòng tải lại trang.";
    console.error(err);
  });

// Refresh the public room list periodically as a fallback.
setInterval(() => {
  if (document.getElementById("screen-login").classList.contains("active") && connection.state === signalR.HubConnectionState.Connected) {
    connection.invoke("GetPublicRooms").catch(() => {});
  }
}, 8000);

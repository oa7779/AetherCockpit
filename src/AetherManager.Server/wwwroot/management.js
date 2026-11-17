"use strict";
const el = id => document.getElementById(id);
let session = null, selected = null, actions = [], statuses = [], selectedUpdate = null, fleet = null, page = 0, visible = [], busy = false, expiryTimer, authEpoch = 0;
const pendingRequests = new Set();
const isAdmin = () => session?.account.role === "Administrator";
const canDiagnose = () => isAdmin() || session?.account.role === "Operator";
async function api(path, method = "GET", body) {
    const epoch = authEpoch;
    const controller = new AbortController(), timeout = setTimeout(() => controller.abort(), 15000);
    pendingRequests.add(controller);
    try {
        const response = await fetch("/api/v1/" + path, {
            method, headers: { ...(session && method !== "GET" ? { "X-Aether-CSRF": session.csrfToken } : {}),
                ...(path === "auth/login" ? { "X-Aether-Login": "1" } : {}), ...(body ? { "Content-Type": "application/json" } : {}) },
            ...(body ? { body: JSON.stringify(body) } : {}), cache: "no-store", credentials: "same-origin", signal: controller.signal
        });
        if (epoch !== authEpoch) throw new Error("Sitzung gewechselt; veraltete Antwort verworfen.");
        if (!response.ok) {
            const problem = (response.headers.get("content-type") || "").includes("json") ? await response.json() : null;
            if (response.status === 401) clearSession();
            throw new Error((problem?.title || "Anfrage abgewiesen") + " · HTTP " + response.status +
                (response.status === 401 ? " · Erneut anmelden; Sitzung beendet, gesperrt oder Zugang abgewiesen." : ""));
        }
        const result = response.status === 204 ? null : await response.json();
        if (epoch !== authEpoch) throw new Error("Sitzung gewechselt; veraltete Antwort verworfen.");
        return result;
    } catch (error) {
        if (error.name === "AbortError") throw new Error(epoch === authEpoch ? "Anfragezeitlimit (15 Sekunden) erreicht. Verbindung prüfen und erneut versuchen." : "Sitzung beendet; Anfrage abgebrochen.");
        throw error;
    } finally { clearTimeout(timeout); pendingRequests.delete(controller); }
}
function show(message) { el("status").textContent = message; }
function handle(fn) { return async () => { try { await fn(); } catch (error) { show(error.message); } }; }
function clearSession() {
    authEpoch++;
    for (const controller of pendingRequests) controller.abort();
    session = null; selected = null; selectedUpdate = null; fleet = null; visible = []; statuses = []; actions = [];
    clearTimeout(expiryTimer); stopAnimation();
    el("workspace").hidden = true; el("login").hidden = false; el("password").value = "";
    for (const id of ["output", "update-eula", "audit", "users", "sessions", "devices", "device-detail", "server", "jobs", "reports", "cloud-devices", "cloud-deployments"]) el(id).replaceChildren();
    el("cloud-status").textContent = "";
    el("new-password").value = ""; el("update-plan").hidden = true; el("users-section").hidden = true;
}
function button(text, action, parent) { const b = document.createElement("button"); b.textContent = text; b.onclick = handle(action); parent.append(b); return b; }
async function refresh() {
    if (!session || busy) return;
    busy = true;
    try {
        const [aggregate, audit, accountAudit] = await Promise.all([api("admin/fleet"), api("admin/management/audit"), api("admin/account-audit")]);
        if (!session) return;
        fleet = aggregate; statuses = fleet.devices.map(x => x.management).filter(Boolean);
        if (selected) selected = fleet.devices.find(x => x.device.id === selected.id)?.device ?? null;
        renderFleet(); renderServer();
        el("audit").textContent = [
            ...audit.map(x => ({ at: x.at, text: `${x.at} · ${x.deviceId} · ${x.event}` })),
            ...accountAudit.map(x => ({ at: x.at, text: `${x.at} · ${x.actor} · ${x.event}` }))
        ].sort((a, b) => Date.parse(b.at) - Date.parse(a.at)).slice(0, 500).map(x => x.text).join("\n");
        if (selected) await details();
        if (isAdmin()) await renderUsers();
    } finally { busy = false; }
}
async function details() {
    if (!selected) return;
    const deviceId = selected.id;
    const live = statuses.find(x => x.deviceId === selected.id);
    el("device-title").textContent = selected.machineName;
    el("device-detail").textContent = JSON.stringify({ ...selected, management: live }, null, 2);
    el("approve").disabled = !isAdmin() || selected.state !== "pending";
    el("revoke").disabled = !isAdmin() || selected.state === "revoked";
    el("policy").disabled = !isAdmin() || selected.state !== "approved";
    el("changes").checked = live?.policy.allowChanges ?? false;
    el("auto").checked = live?.policy.allowAutoRepair ?? false;
    el("queue").disabled = !canDiagnose() || selected.state !== "approved";
    const [jobs, reports] = await Promise.all([api("admin/management/jobs?deviceId=" + selected.id),
        api("admin/reports?deviceId=" + selected.id + "&limit=20")]);
    if (selected?.id !== deviceId) return;
    el("jobs").replaceChildren();
    for (const job of jobs) {
        button(job.actionId + " · " + job.state + " · " + job.createdAt, async () => {
            el("output").textContent = JSON.stringify(job, null, 2);
            if (job.actionId === "windows-update-search" && job.state === "completed" && !job.truncated) renderUpdates(job);
        }, el("jobs"));
        if (isAdmin() && job.state === "queued") button("Stornieren", async () => {
            if (confirm("Diesen noch nicht gestarteten Auftrag stornieren?")) {
                await api("admin/management/jobs/" + job.id + "/cancel", "POST"); await details();
            }
        }, el("jobs"));
    }
    el("reports").replaceChildren();
    for (const report of reports.reports) button(report.capturedAt, async () => {
        el("output").textContent = JSON.stringify(await api("admin/reports/" + report.reportId), null, 2);
    }, el("reports"));
}
async function enterWorkspace() {
    actions = await api("admin/management/actions");
    el("actions").replaceChildren();
    for (const action of actions.filter(x => isAdmin() || !x.changesSystem)) { const option = document.createElement("option"); option.value = action.id; option.textContent = action.category + " · " + action.name; el("actions").append(option); }
    el("enrollment").hidden = !isAdmin(); el("users-section").hidden = !isAdmin(); el("queue").hidden = !canDiagnose();
    el("identity").textContent = `${session.account.username} · ${session.account.role} · Ablauf ${new Date(session.expiresAt).toLocaleTimeString()}`;
    expiryTimer = setTimeout(() => { clearSession(); show("Sitzung abgelaufen. Erneut anmelden."); }, Math.max(0, Date.parse(session.expiresAt) - Date.now()));
    el("actions").onchange();
    await refresh(); el("workspace").hidden = false; el("login").hidden = true; show("Verbunden.");
    handle(loadCloudStatus)();
    startAnimation();
}
el("login-form").onsubmit = event => {
    event.preventDefault();
    handle(async () => {
        el("connect").disabled = true;
        try {
            const password = el("password").value; el("password").value = "";
            session = await api("auth/login", "POST", { username: el("username").value, password });
            await enterWorkspace();
        } finally { el("connect").disabled = false; }
    })();
};
el("actions").onchange = () => {
    const action = actions.find(x => x.id === el("actions").value);
    el("action-detail").textContent = action ? `${action.changesSystem ? "SYSTEMÄNDERUNG · Bestätigung erforderlich" : "Diagnose / lesend"}\nWerkzeuge: ${action.requiredCommands.join(", ")}\nAusführung nur bei Verfügbarkeit auf dem Zielgerät.` : "";
};
function renderUpdates(job) {
    const plan = JSON.parse(job.output);
    el("updates").replaceChildren(); el("update-eula").textContent = ""; el("update-plan").hidden = false;
    for (const offer of plan.updates) button(`${offer.title} · ${offer.type} · Revision ${offer.revision}`, async () => {
        selectedUpdate = { deviceId: job.deviceId, sourceJobId: job.id, offer };
        el("update-eula").textContent = `${offer.title}\n${offer.updateId} · Revision ${offer.revision}\n\n${offer.eulaText}`;
        el("install-update").disabled = !isAdmin();
    }, el("updates"));
}
el("install-update").onclick = handle(async () => {
    if (!selectedUpdate || !selected || selected.id !== selectedUpdate.deviceId) throw new Error("Gerät und Updateangebot erneut auswählen.");
    const { offer, sourceJobId } = selectedUpdate;
    if (!confirm(`${selected.machineName}\n${offer.title}\nDie angezeigte Lizenz akzeptieren und genau dieses Update herunterladen und installieren? Bei Treibern Hardware und Wartungsfenster prüfen. Kein automatischer Neustart.`)) return;
    await api("admin/management/devices/" + selected.id + "/jobs", "POST", {
        actionId: "windows-update-install", confirmChanges: true, sourceJobId,
        update: { updateId: offer.updateId, revision: offer.revision, eulaHash: offer.eulaHash, acceptEula: true }
    });
    selectedUpdate = null; el("update-plan").hidden = true; el("install-update").disabled = true;
    show("Einzelnes Update vorgemerkt; Identität und Lizenz werden vor Ausführung erneut geprüft."); await details();
});
el("refresh").onclick = handle(refresh); el("logout").onclick = handle(async () => { await api("auth/logout", "POST"); clearSession(); show("Abgemeldet; Sitzung widerrufen."); });
el("enrollment").onclick = handle(async () => {
    const code = await api("admin/enrollment-codes", "POST");
    show("Einmalcode: " + code.code + " · gültig bis " + code.expiresAt + " · sicher an den Geräteadministrator übergeben.");
});
el("approve").onclick = handle(async () => { if (selected && confirm(selected.machineName + " nach Prüfung der Geräteidentität freigeben?")) { await api("admin/devices/" + selected.id + "/approve", "POST"); await refresh(); } });
el("revoke").onclick = handle(async () => { if (selected && confirm("Gerätezugriff für " + selected.machineName + " sperren? Bereits gestartete Prozesse werden dadurch nicht rückgängig gemacht.")) { await api("admin/devices/" + selected.id + "/revoke", "POST"); await refresh(); } });
el("save-policy").onclick = handle(async () => {
    if (!selected || !confirm("Gerätefreigaben für " + selected.machineName + " ändern?")) return;
    await api("admin/management/devices/" + selected.id + "/policy", "PUT", {
        allowChanges: el("changes").checked, allowAutoRepair: el("auto").checked
    }); await refresh();
});
el("queue").onclick = handle(async () => {
    const action = actions.find(x => x.id === el("actions").value);
    if (!selected || !action) return;
    if (action.id === "windows-update-install") throw new Error("Zuerst Windows-Updatesuche ausführen, den abgeschlossenen Auftrag öffnen und ein Angebot samt Lizenz prüfen.");
    if (!confirm(`${selected.machineName}\n${action.name}\n${action.changesSystem ? "Diese Aktion verändert Windows. Sicherung und Wartungsfenster prüfen." : "Diagnose ausführen und Ausgabe zentral übertragen?"}`)) return;
    await api("admin/management/devices/" + selected.id + "/jobs", "POST", { actionId: action.id, confirmChanges: action.changesSystem });
    show("Auftrag vorgemerkt. Der Client prüft die lokalen Freigaben vor der Ausführung."); await details();
});
function freshness(value) { return ({ fresh: "frisch", stale: "veraltet", "never-observed": "nie beobachtet" })[value] || value; }
async function selectDevice(device) {
    selected = device; selectedUpdate = null;
    el("output").textContent = ""; el("update-eula").textContent = "";
    el("update-plan").hidden = true; el("install-update").disabled = true;
    renderFleet(); await details();
}
function renderFleet() {
    if (!fleet) return;
    const query = el("search").value.trim().toLocaleLowerCase();
    const filter = el("fleet-filter").value;
    const filtered = fleet.devices.filter(x =>
        (filter === "all" || x.liveness === filter || x.device.state === filter) &&
        `${x.device.machineName} ${x.device.id} ${x.device.cockpitVersion}`.toLocaleLowerCase().includes(query));
    const pageSize = el("page-size").value === "24" ? 24 : 48;
    const pages = Math.max(1, Math.ceil(filtered.length / pageSize));
    page = Math.max(0, Math.min(page, pages - 1)); visible = filtered.slice(page * pageSize, (page + 1) * pageSize);
    el("page-info").textContent = `${filtered.length} / ${fleet.devices.length} Geräte · Seite ${page + 1} / ${pages}`;
    el("previous").disabled = page === 0; el("next").disabled = page + 1 >= pages;
    el("devices").replaceChildren();
    for (const item of visible) {
        const b = button(`${item.device.machineName} · ${item.device.state} · Heartbeat ${freshness(item.liveness)} · Bericht ${freshness(item.reportFreshness)}`,
            () => selectDevice(item.device), el("devices"));
        b.className = "device" + (selected?.id === item.device.id ? " selected" : "");
        b.setAttribute("aria-pressed", String(selected?.id === item.device.id));
    }
    drawFleet(0);
}
const bytes = value => value == null ? "nicht verfügbar" : (value / 1073741824).toLocaleString("de-DE", { maximumFractionDigits: 2 }) + " GiB";
function renderServer() {
    const s = fleet.server;
    el("server").replaceChildren();
    const metrics = [
        ["Host CPU", s.hostCpuPercent == null ? "noch nicht verfügbar" : s.hostCpuPercent.toFixed(1) + " %"],
        ["Serverprozess CPU", s.processCpuPercent == null ? "noch nicht verfügbar" : s.processCpuPercent.toFixed(1) + " %"],
        ["Host Prozesse", s.hostProcessCount == null ? "nicht verfügbar" : String(s.hostProcessCount)],
        ["Serverprozess RAM", bytes(s.processWorkingSetBytes)],
        ["Host RAM frei / gesamt", bytes(s.hostMemoryAvailableBytes) + " / " + bytes(s.hostMemoryTotalBytes)],
        ["Host Laufzeit", (s.hostUptimeSeconds / 3600).toFixed(1) + " h"],
        ["Serverprozess Laufzeit", s.processUptimeSeconds == null ? "nicht verfügbar" : (s.processUptimeSeconds / 3600).toFixed(1) + " h"],
        ...s.disks.map(d => [d.name + " · frei / gesamt", d.unavailableReason || bytes(d.availableBytes) + " / " + bytes(d.totalBytes)])
    ];
    for (const [label, value] of metrics) {
        const card = document.createElement("div"); card.className = "metric";
        const title = document.createElement("strong"); title.textContent = label;
        const content = document.createElement("p"); content.textContent = value; card.append(title, content); el("server").append(card);
    }
    const sampleFresh = Date.parse(fleet.generatedAt) - Date.parse(s.sampledAt) <= 15000;
    el("sample-time").textContent = `Stichprobe ${new Date(s.sampledAt).toLocaleString()} · ${sampleFresh ? "frisch" : "veraltet"} · Host-CPU aus Windows-Systemzeiten (Prozessorgruppe); Prozess-CPU über logische Prozessoren normiert. ` +
        (s.unavailableReason ? s.unavailableReason + " · " : "") + "Temperaturen: nicht verfügbar (kein Sensoranbieter).";
}
async function renderUsers() {
    if (document.activeElement.closest("#users-section")) return;
    const [users, sessions] = await Promise.all([api("admin/accounts"), api("admin/sessions")]);
    if (!isAdmin()) return;
    el("users").replaceChildren(); el("sessions").replaceChildren();
    for (const user of users) {
        const row = document.createElement("fieldset");
        const legend = document.createElement("legend"); legend.textContent = `${user.username} · ${user.disabled ? "gesperrt" : "aktiv"}`; row.append(legend);
        const role = document.createElement("select"); role.setAttribute("aria-label", "Rolle für " + user.username);
        for (const name of ["Administrator", "Operator", "Reader"]) { const option = document.createElement("option"); option.textContent = name; role.append(option); }
        role.value = user.role; row.append(role);
        const label = document.createElement("label"); const disabled = document.createElement("input"); disabled.type = "checkbox"; disabled.checked = user.disabled;
        label.append(disabled, document.createTextNode(" Konto sperren")); row.append(label);
        const passwordLabel = document.createElement("label"); passwordLabel.textContent = "Neues Passwort (optional)";
        const password = document.createElement("input"); password.type = "password"; password.autocomplete = "new-password"; password.maxLength = 256;
        passwordLabel.append(password); row.append(passwordLabel);
        button("Änderungen bestätigen", async () => {
            if (!confirm(`${user.username}: Rolle ${role.value}, Sperre ${disabled.checked}. Alle Sitzungen werden widerrufen. Änderungen speichern?`)) return;
            const newPassword = password.value; password.value = "";
            await api("admin/accounts/" + user.id, "PUT", { role: role.value, disabled: disabled.checked, ...(newPassword ? { password: newPassword } : {}) });
            if (user.id === session.account.id) { clearSession(); show("Eigenes Konto geändert. Sitzung widerrufen; erneut anmelden."); return; }
            document.activeElement.blur(); await renderUsers(); show("Konto geändert; Sitzungen widerrufen.");
        }, row);
        el("users").append(row);
    }
    for (const item of sessions) {
        const user = users.find(x => x.id === item.accountId);
        button(`${user?.username || item.accountId} · endet ${new Date(item.expiresAt).toLocaleString()} · Sitzung widerrufen`, async () => {
            if (!confirm("Diese Sitzung wirklich widerrufen?")) return;
            await api("admin/sessions/" + item.id + "/revoke", "POST");
            if (item.id === session.sessionId) { clearSession(); show("Eigene Sitzung widerrufen."); }
            else { document.activeElement.blur(); await renderUsers(); }
        }, el("sessions"));
    }
}
el("create-user").onsubmit = event => {
    event.preventDefault();
    handle(async () => {
        if (!confirm(`Lokales Konto ${el("new-username").value} mit Rolle ${el("new-role").value} anlegen?`)) return;
        const password = el("new-password").value; el("new-password").value = "";
        await api("admin/accounts", "POST", { username: el("new-username").value, password, role: el("new-role").value });
        document.activeElement.blur(); await renderUsers(); show("Benutzer angelegt. Passwort nur über einen sicheren Kanal übergeben.");
    })();
};
async function loadCloudStatus() {
    const status = await api("admin/cloud/status");
    el("cloud-status").textContent = status.message;
    el("cloud-refresh").disabled = !status.configured;
}
async function loadCloudData() {
    const status = await api("admin/cloud/status");
    el("cloud-status").textContent = status.message;
    if (!status.configured) {
        el("cloud-devices").replaceChildren();
        el("cloud-deployments").replaceChildren();
        return;
    }
    el("cloud-refresh").disabled = true;
    try {
        const [devicePage, deploymentPage] = await Promise.all([
            api("admin/cloud/devices"), api("admin/cloud/deployments")
        ]);
        if (!session) return;
        el("cloud-devices").replaceChildren();
        for (const device of devicePage.devices) {
            const row = document.createElement("fieldset");
            const legend = document.createElement("legend");
            legend.textContent = `${device.deviceName || "Gerätename nicht gemeldet"} · ${device.operatingSystem || "OS unbekannt"} ${device.osVersion || ""}`;
            row.append(legend);
            const details = document.createElement("p");
            details.textContent = `Compliance: ${device.complianceState || "nicht gemeldet"} · Verwaltung: ${device.managementState || "nicht gemeldet"} · Letzte Synchronisierung: ${device.lastSyncDateTime ? new Date(device.lastSyncDateTime).toLocaleString() : "nicht gemeldet"}`;
            row.append(details);
            if (isAdmin()) button("Intune-Synchronisierung anfordern", async () => {
                if (!confirm(`${device.deviceName}\nEine Synchronisierung bei Intune anfordern? Dadurch können die bereits konfigurierten Intune-Richtlinien und Aktionen beim nächsten Gerätekontakt wirksam werden.`)) return;
                await api(`admin/cloud/devices/${device.id}/sync`, "POST");
                show("Intune hat die Synchronisierungsanforderung angenommen.");
                await loadCloudData();
            }, row);
            el("cloud-devices").append(row);
        }
        el("cloud-deployments").replaceChildren();
        for (const deployment of deploymentPage.deployments) {
            const row = document.createElement("fieldset");
            const legend = document.createElement("legend");
            legend.textContent = `Bereitstellung ${deployment.id} · ${deployment.effectiveState || "Status unbekannt"}`;
            row.append(legend);
            const details = document.createElement("p");
            details.textContent = `Angefordert: ${deployment.requestedState || "nicht gemeldet"} · Katalogeintrag: ${deployment.catalogEntryId || "nicht gemeldet"} · geändert: ${deployment.lastModifiedDateTime ? new Date(deployment.lastModifiedDateTime).toLocaleString() : "nicht gemeldet"}`;
            row.append(details);
            if (isAdmin()) {
                const paused = deployment.requestedState
                    ? deployment.requestedState === "paused" : deployment.effectiveState === "paused";
                button(paused ? "Fortsetzen" : "Pausieren", async () => {
                    const action = paused ? "fortsetzen" : "pausieren";
                    if (!confirm(`Windows Autopatch\nBereitstellung ${deployment.id}\n\nDiese Bereitstellung ${action}? Die Änderung wirkt auf ihre zugewiesene Zielgruppe.`)) return;
                    await api(`admin/cloud/deployments/${deployment.id}/state`, "POST", { paused: !paused });
                    show(`Autopatch-Anforderung zum ${action} wurde angenommen.`);
                    await loadCloudData();
                }, row);
            }
            el("cloud-deployments").append(row);
        }
        if (devicePage.hasMore || deploymentPage.hasMore)
            el("cloud-status").textContent += " Angezeigt werden höchstens 500 Einträge je Liste.";
        if (!devicePage.devices.length && !deploymentPage.deployments.length)
            el("cloud-status").textContent += " Graph hat keine Intune-Geräte oder Autopatch-Bereitstellungen zurückgegeben.";
    } finally { el("cloud-refresh").disabled = false; }
}
el("cloud-refresh").onclick = handle(loadCloudData);
for (const id of ["search", "fleet-filter", "page-size"]) el(id).addEventListener("input", () => { page = 0; renderFleet(); });
el("previous").onclick = () => { page--; renderFleet(); };
el("next").onclick = () => { page++; renderFleet(); };

const browserDark = matchMedia("(prefers-color-scheme: dark)");
const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
let themeChoice = "neon", animation = 0, lastFrame = 0, hits = [];
try { themeChoice = localStorage.getItem("aether-manager-theme") || "neon"; } catch (error) { if (error.name !== "SecurityError") throw error; }
if (!["neon", "modern-auto", "modern-light", "modern-dark"].includes(themeChoice)) themeChoice = "neon";
el("theme").value = themeChoice;
function applyTheme() {
    document.documentElement.dataset.theme = themeChoice === "modern-auto" ? (browserDark.matches ? "modern-dark" : "modern-light") : themeChoice;
    drawFleet(0);
}
el("theme").onchange = () => {
    themeChoice = el("theme").value;
    try { localStorage.setItem("aether-manager-theme", themeChoice); } catch (error) { if (error.name !== "SecurityError" && error.name !== "QuotaExceededError") throw error; }
    applyTheme();
};
browserDark.addEventListener("change", applyTheme);
function drawFleet(time) {
    const canvas = el("fleet-map"), width = Math.max(280, canvas.clientWidth || 900);
    const columns = Math.min(8, Math.max(2, Math.floor(width / 125))), rows = Math.max(1, Math.ceil(visible.length / columns));
    const height = 80 + rows * 76, scale = Math.min(devicePixelRatio || 1, 2);
    if (canvas.width !== Math.round(width * scale) || canvas.height !== Math.round(height * scale)) {
        canvas.width = Math.round(width * scale); canvas.height = Math.round(height * scale); canvas.style.height = height + "px";
    }
    const ctx = canvas.getContext("2d"); ctx.setTransform(scale, 0, 0, scale, 0, 0); ctx.clearRect(0, 0, width, height);
    const styles = getComputedStyle(document.documentElement);
    const text = styles.getPropertyValue("--text").trim(), accent = styles.getPropertyValue("--accent").trim(), panel = styles.getPropertyValue("--panel").trim();
    ctx.fillStyle = text; ctx.font = "14px Segoe UI"; ctx.textAlign = "center";
    ctx.fillText(`AETHER Server · ${visible.length} Installationen`, width / 2, 20);
    ctx.font = "11px Segoe UI"; ctx.fillText("Logische Zuordnung · Animation ist keine Verkehrsmessung", width / 2, 38);
    hits = [];
    visible.forEach((item, index) => {
        const x = (index % columns + .5) * width / columns, y = 80 + Math.floor(index / columns) * 76;
        const color = item.liveness === "fresh" ? accent : styles.getPropertyValue("--muted").trim();
        ctx.strokeStyle = color; ctx.globalAlpha = .3; ctx.setLineDash([4, 9]); ctx.lineDashOffset = -time / 160;
        ctx.beginPath(); ctx.moveTo(width / 2, 44); ctx.lineTo(x, y); ctx.stroke();
        ctx.setLineDash([]); ctx.globalAlpha = 1; ctx.fillStyle = panel;
        ctx.fillRect(x - 48, y - 19, 96, 38); ctx.strokeStyle = selected?.id === item.device.id ? styles.getPropertyValue("--selected").trim() : color;
        ctx.lineWidth = selected?.id === item.device.id ? 3 : 1; ctx.strokeRect(x - 48, y - 19, 96, 38); ctx.lineWidth = 1;
        ctx.fillStyle = text; ctx.font = "12px Segoe UI";
        ctx.fillText(item.device.machineName.length > 13 ? item.device.machineName.slice(0, 12) + "…" : item.device.machineName, x, y - 1);
        ctx.fillStyle = color; ctx.font = "10px Segoe UI"; ctx.fillText(freshness(item.liveness), x, y + 13);
        hits.push({ left: x - 48, right: x + 48, top: y - 19, bottom: y + 19, device: item.device });
    });
}
el("fleet-map").onclick = event => {
    const rect = el("fleet-map").getBoundingClientRect(), x = event.clientX - rect.left, y = event.clientY - rect.top;
    const hit = hits.find(h => x >= h.left && x <= h.right && y >= h.top && y <= h.bottom);
    if (hit) handle(() => selectDevice(hit.device))();
};
function stopAnimation() { cancelAnimationFrame(animation); animation = 0; }
function startAnimation() {
    stopAnimation();
    if (!session || document.hidden || reducedMotion.matches) { drawFleet(0); return; }
    function frame(time) {
        if (time - lastFrame >= 50) { drawFleet(time); lastFrame = time; }
        animation = requestAnimationFrame(frame);
    }
    animation = requestAnimationFrame(frame);
}
document.addEventListener("visibilitychange", () => { startAnimation(); if (session && !document.hidden) handle(refresh)(); });
reducedMotion.addEventListener("change", startAnimation);
new ResizeObserver(() => drawFleet(0)).observe(el("fleet-map"));
applyTheme();
setInterval(() => { if (session && !document.hidden && !document.activeElement.closest("#policy, #users-section, #update-plan")) handle(refresh)(); }, 30000);
handle(async () => {
    try { session = await api("auth/session"); } catch (error) { if (!error.message.includes("HTTP 401")) throw error; return; }
    await enterWorkspace();
})();

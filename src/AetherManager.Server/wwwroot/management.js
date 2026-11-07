"use strict";
const el = id => document.getElementById(id);
let token = "", selected = null, actions = [], statuses = [], selectedUpdate = null;
async function api(path, method = "GET", body) {
    const response = await fetch("/api/v1/" + path, {
        method, headers: { Authorization: "Bearer " + token, ...(body ? { "Content-Type": "application/json" } : {}) },
        ...(body ? { body: JSON.stringify(body) } : {}), cache: "no-store", credentials: "omit"
    });
    if (!response.ok) {
        if (response.status === 401) logout();
        throw new Error("Anfrage abgewiesen: HTTP " + response.status + ". Freigaben und Gerätestatus prüfen.");
    }
    return response.status === 204 ? null : await response.json();
}
function show(message) { el("status").textContent = message; }
function handle(fn) { return async () => { try { await fn(); } catch (error) { show(error.message); } }; }
function logout() { token = ""; selected = null; selectedUpdate = null; el("workspace").hidden = true; el("login").hidden = false; el("token").value = ""; el("output").textContent = ""; el("update-eula").textContent = ""; }
function button(text, action, parent) { const b = document.createElement("button"); b.textContent = text; b.onclick = handle(action); parent.append(b); return b; }
async function refresh() {
    const [fleet, live, audit] = await Promise.all([api("admin/devices"), api("admin/management/devices"), api("admin/management/audit")]);
    statuses = live;
    el("devices").replaceChildren();
    for (const device of fleet.devices) {
        const status = live.find(x => x.deviceId === device.id);
        const online = status?.lastSeenAt && Date.now() - Date.parse(status.lastSeenAt) < 120000;
        const b = button(device.machineName + " · " + device.state + " · " + (online ? "online" : "kein aktueller Heartbeat"),
            async () => { selected = device; selectedUpdate = null; el("update-plan").hidden = true; el("install-update").disabled = true; await details(); }, el("devices"));
        b.className = "device" + (selected?.id === device.id ? " selected" : "");
        if (selected?.id === device.id) selected = device;
    }
    el("audit").textContent = audit.map(x => `${x.at} · ${x.deviceId} · ${x.event}`).join("\n");
    if (selected) await details();
}
async function details() {
    const live = statuses.find(x => x.deviceId === selected.id);
    el("device-title").textContent = selected.machineName;
    el("device-detail").textContent = JSON.stringify({ ...selected, management: live }, null, 2);
    el("approve").disabled = selected.state !== "pending";
    el("revoke").disabled = selected.state === "revoked";
    el("policy").disabled = selected.state !== "approved";
    el("changes").checked = live?.policy.allowChanges ?? false;
    el("auto").checked = live?.policy.allowAutoRepair ?? false;
    el("queue").disabled = selected.state !== "approved";
    const [jobs, reports] = await Promise.all([api("admin/management/jobs?deviceId=" + selected.id),
        api("admin/reports?deviceId=" + selected.id + "&limit=20")]);
    el("jobs").replaceChildren();
    for (const job of jobs) {
        button(job.actionId + " · " + job.state + " · " + job.createdAt, async () => {
            el("output").textContent = JSON.stringify(job, null, 2);
            if (job.actionId === "windows-update-search" && job.state === "completed" && !job.truncated) renderUpdates(job);
        }, el("jobs"));
        if (job.state === "queued") button("Stornieren", async () => {
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
el("connect").onclick = handle(async () => {
    token = el("token").value.trim(); el("token").value = "";
    actions = await api("admin/management/actions");
    el("actions").replaceChildren();
    for (const action of actions) { const option = document.createElement("option"); option.value = action.id; option.textContent = action.category + " · " + action.name; el("actions").append(option); }
    el("actions").onchange();
    await refresh(); el("workspace").hidden = false; el("login").hidden = true; show("Verbunden.");
});
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
        el("install-update").disabled = false;
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
el("refresh").onclick = handle(refresh); el("logout").onclick = logout;
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
setInterval(() => { if (token && !document.hidden && !document.activeElement.closest("#policy")) handle(refresh)(); }, 30000);

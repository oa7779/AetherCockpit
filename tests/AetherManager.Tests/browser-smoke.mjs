import fs from "node:fs/promises";
import path from "node:path";
import assert from "node:assert/strict";
import { chromium } from "playwright";

const [credentialsFile, screenshotDirectory] = process.argv.slice(2);
if (!credentialsFile || !screenshotDirectory) throw new Error("Usage: node browser-smoke.mjs <FIXTURE_READY credentials file> <workspace screenshot directory>");
const screenshots = path.resolve(screenshotDirectory);
if (path.relative(process.cwd(), screenshots).startsWith("..") || path.isAbsolute(path.relative(process.cwd(), screenshots)))
    throw new Error("Screenshots must remain inside the workspace.");
const credentials = JSON.parse(await fs.readFile(credentialsFile, "utf8"));
const url = new URL(credentials.url);
assert.equal(url.hostname, "localhost");
assert.equal(url.protocol, "https:");
assert.equal(credentials.fixtureDeviceCount, 48);
assert.ok(Date.parse(credentials.expiresAt) > Date.now());
await fs.mkdir(screenshots, { recursive: true });
const browser = await chromium.launch({ headless: true });
const pageErrors = [];
let screenshotCount = 0;
async function context() {
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1000 } });
    ctx.on("page", page => page.on("pageerror", error => pageErrors.push(error.message)));
    return ctx;
}
async function login(page, username) {
    await page.goto(credentials.url);
    await page.locator("#username").fill(username);
    await page.locator("#password").fill(credentials.password);
    await page.locator("#connect").click();
    await page.locator("#workspace").waitFor({ state: "visible" });
    await page.waitForFunction(() => document.querySelectorAll("#devices .device").length === 48);
}
async function screenshot(page, name) {
    await page.screenshot({ path: path.join(screenshots, name + ".png"), fullPage: true });
    screenshotCount++;
}
try {
    const adminContext = await context();
    const admin = await adminContext.newPage();
    await admin.goto(credentials.url);
    await screenshot(admin, "neon-login");
    await login(admin, credentials.administrator);
    assert.equal(await admin.locator("#devices .device").count(), 48);
    assert.ok(await admin.locator("#users-section").isVisible());
    await admin.waitForFunction(() => document.querySelector("#cloud-status").textContent.includes("AETHER_GRAPH_TENANT_ID"));
    assert.ok(await admin.locator("#cloud-section").isVisible());
    assert.ok(await admin.locator("#cloud-refresh").isDisabled());
    assert.ok(await admin.locator("#page-info").innerText().then(x => x.includes("48 / 48")));
    await screenshot(admin, "neon-fleet-48");
    await admin.locator("#page-size").selectOption("24");
    assert.equal(await admin.locator("#devices .device").count(), 24);
    await admin.locator("#next").click();
    assert.ok((await admin.locator("#devices .device").first().innerText()).includes("FIXTURE-PC-25"));
    await screenshot(admin, "neon-pagination-24");
    await admin.locator("#page-size").selectOption("48");
    await admin.locator("#devices .device").nth(1).click();
    await admin.waitForFunction(() => document.querySelector("#device-title").textContent === "FIXTURE-PC-02");
    await admin.locator("#reports button").first().click();
    await admin.waitForFunction(() => document.querySelector("#output").textContent.includes("synthetic") || document.querySelector("#output").textContent.includes("Synthetic"));
    await screenshot(admin, "neon-details-report");
    for (const theme of ["modern-light", "modern-dark"]) {
        await admin.locator("#theme").selectOption(theme);
        assert.equal(await admin.evaluate(() => document.documentElement.dataset.theme), theme);
        await admin.locator("#devices .device").nth(1).click();
        await admin.locator("#reports button").first().click();
        await screenshot(admin, theme + "-fleet-details-users");
        await admin.reload();
        await admin.locator("#workspace").waitFor({ state: "visible" });
        assert.equal(await admin.locator("#theme").inputValue(), theme);
    }
    await admin.locator("#search").fill("FIXTURE-PC-48");
    await admin.waitForFunction(() => document.querySelectorAll("#devices .device").length === 1);
    await screenshot(admin, "modern-dark-search");
    await admin.locator("#search").fill("");
    await admin.locator("#fleet-filter").selectOption("never-observed");
    const filtered = await admin.locator("#devices .device").count();
    assert.ok(filtered > 0 && filtered < 48);
    await admin.locator("#fleet-filter").selectOption("all");
    await admin.setViewportSize({ width: 390, height: 844 });
    await admin.emulateMedia({ reducedMotion: "reduce" });
    await screenshot(admin, "modern-dark-mobile-reduced-motion");
    assert.ok(await admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
    await admin.setViewportSize({ width: 1440, height: 1000 });
    const ownFieldset = admin.locator("#users fieldset").filter({ has: admin.locator("legend", { hasText: credentials.administrator }) });
    await ownFieldset.locator("select").selectOption("Reader");
    admin.once("dialog", dialog => dialog.accept());
    await ownFieldset.locator("button").click();
    await admin.waitForFunction(() => document.querySelector("#status").textContent.includes("HTTP 409"));
    await screenshot(admin, "modern-dark-last-admin-safeguard");
    await ownFieldset.locator("select").selectOption("Administrator");

    const operatorContext = await context();
    const operatorPage = await operatorContext.newPage();
    await login(operatorPage, credentials.operator);
    assert.ok(await operatorPage.locator("#users-section").isHidden());
    assert.ok(await operatorPage.locator("#enrollment").isHidden());
    await operatorPage.waitForFunction(() => document.querySelector("#cloud-status").textContent.includes("AETHER_GRAPH_TENANT_ID"));
    assert.ok(await operatorPage.locator("#cloud-refresh").isDisabled());
    assert.equal(await operatorPage.locator('#actions option[value="dns-cache-clear"]').count(), 0);
    assert.equal(await operatorPage.locator('#actions option[value="inventory"]').count(), 1);
    await operatorPage.locator("#devices .device").nth(1).click();
    assert.ok(await operatorPage.locator("#queue").isVisible());
    await screenshot(operatorPage, "operator-read-only-catalog");

    const readerContext = await context();
    const reader = await readerContext.newPage();
    await login(reader, credentials.reader);
    assert.ok(await reader.locator("#queue").isHidden());
    assert.ok(await reader.locator("#users-section").isHidden());
    await reader.waitForFunction(() => document.querySelector("#cloud-status").textContent.includes("AETHER_GRAPH_TENANT_ID"));
    assert.ok(await reader.locator("#cloud-refresh").isDisabled());
    await screenshot(reader, "reader-fleet");
    const readerSession = await reader.evaluate(async () => (await fetch("/api/v1/auth/session")).json());
    await admin.evaluate(async id => {
        const session = await (await fetch("/api/v1/auth/session")).json();
        const response = await fetch(`/api/v1/admin/sessions/${id}/revoke`, {
            method: "POST", headers: { "X-Aether-CSRF": session.csrfToken }
        });
        if (!response.ok) throw new Error("Fixture session revoke failed " + response.status);
    }, readerSession.sessionId);
    await reader.locator("#refresh").click();
    await reader.locator("#login").waitFor({ state: "visible" });
    await screenshot(reader, "reader-revoked-session");
    await operatorPage.locator("#logout").click();
    await operatorPage.locator("#login").waitFor({ state: "visible" });
    assert.equal(await operatorPage.evaluate(async () => (await fetch("/api/v1/auth/session")).status), 401);
    assert.deepEqual(pageErrors, []);
    console.log(`PASS browser fixture checks; ${screenshotCount} screenshots; no Windows action executed.`);
} finally { await browser.close(); }

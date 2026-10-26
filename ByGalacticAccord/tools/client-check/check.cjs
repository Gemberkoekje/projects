// The browser check for the Blazor client (plan §2: "Checked in a browser").
//
//   node check.cjs [--site <published wwwroot>] [--out <screenshot folder>] [--fingerprint "<desktop fingerprint>"]
//
// With no arguments it publishes the client (Release) and asks the soak harness for the desktop
// fingerprint, so one command does everything; CI and local runs are the same. It serves the published
// client, drives it in Chromium through the main screen's flows, screenshots each step, and fails on any
// console error, page error or failed request. It also runs the engine speed probe (?bench=20000) and
// checks that WebAssembly reached the same state as desktop .NET (plan risks R4 and R6). Exit code 0
// means every check passed.

'use strict';

const childProcess = require('child_process');
const fs = require('fs');
const http = require('http');
const path = require('path');
const { chromium } = require('playwright');

const SEED = 42;
const BENCH_TICKS = 20000;
const project = path.resolve(__dirname, '..', '..');

function option(name) {
    const i = process.argv.indexOf(name);
    return i >= 0 ? process.argv[i + 1] : null;
}

function dotnet(...args) {
    return childProcess.execFileSync('dotnet', args, { cwd: project, encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] });
}

let root = option('--site');
if (!root) {
    const publish = path.join(project, 'artifacts', 'client-check', 'publish');
    console.log(`Publishing the client to ${publish} …`);
    dotnet('publish', 'src/ByGalacticAccord.Client', '-c', 'Release', '-o', publish, '--nologo', '-v', 'q');
    root = path.join(publish, 'wwwroot');
}

let expectedFingerprint = option('--fingerprint');
if (!expectedFingerprint) {
    console.log('Running the same world on desktop .NET for the fingerprint …');
    // The fingerprint is the command's last line; anything before it is build output.
    const lines = dotnet('run', '--project', 'src/ByGalacticAccord.Cli', '-c', 'Release', '--', 'fingerprint', String(BENCH_TICKS), '--seed', String(SEED))
        .split(/\r?\n/).filter(line => line.trim() !== '');
    expectedFingerprint = lines[lines.length - 1].trim();
}

root = path.resolve(root);
const shots = path.resolve(option('--out') || path.join(project, 'artifacts', 'client-check', 'screenshots'));
fs.mkdirSync(shots, { recursive: true });

const types = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json',
    '.wasm': 'application/wasm',
    '.dat': 'application/octet-stream',
    '.dll': 'application/octet-stream',
    '.pdb': 'application/octet-stream',
    '.woff2': 'font/woff2',
    '.png': 'image/png',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
    '.txt': 'text/plain; charset=utf-8',
};

// A static file server for the published client; unknown paths without an extension get index.html.
function serve() {
    const server = http.createServer((req, res) => {
        const url = new URL(req.url, 'http://localhost');
        let file = path.normalize(path.join(root, decodeURIComponent(url.pathname)));
        if (!file.startsWith(root)) {
            res.writeHead(403).end();
            return;
        }

        if (fs.existsSync(file) && fs.statSync(file).isDirectory()) {
            file = path.join(file, 'index.html');
        }

        if (!fs.existsSync(file)) {
            if (path.extname(file) !== '') {
                res.writeHead(404).end();
                return;
            }

            file = path.join(root, 'index.html');
        }

        res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
        fs.createReadStream(file).pipe(res);
    });

    return new Promise(resolve => server.listen(0, '127.0.0.1', () => resolve(server)));
}

const failures = [];
const results = [];

function check(condition, message) {
    if (condition) {
        results.push('ok    ' + message);
    } else {
        results.push('FAIL  ' + message);
        failures.push(message);
    }
}

async function tick(page) {
    return Number(await page.getAttribute('.screen', 'data-tick'));
}

// Run the clock until the game reaches a tick, resuming whenever an alert stops it.
async function runUntil(page, target, timeoutMs) {
    const started = Date.now();
    while (await tick(page) < target) {
        if (Date.now() - started > timeoutMs) {
            return false;
        }

        const resume = page.locator('.alert button');
        if (await resume.count() > 0) {
            await resume.click();
            await page.click('button[data-speed="X16"]');
        }

        await page.waitForTimeout(100);
    }

    return true;
}

async function main() {
    const server = await serve();
    const base = `http://127.0.0.1:${server.address().port}`;
    const browser = await chromium.launch();
    const context = await browser.newContext({ viewport: { width: 1440, height: 1240 }, colorScheme: 'dark' });
    const page = await context.newPage();
    const problems = [];
    page.on('console', m => { if (m.type() === 'error') problems.push('console: ' + m.text()); });
    page.on('pageerror', e => problems.push('page error: ' + e.message));
    page.on('requestfailed', r => problems.push('request failed: ' + r.url() + ' ' + (r.failure()?.errorText ?? '')));

    try {
        // 1. The main screen loads, paused, with a populated board.
        let started = Date.now();
        await page.goto(`${base}/?seed=${SEED}`);
        await page.waitForSelector('.screen[data-ready="true"]', { timeout: 120000 });
        const loadMs = Date.now() - started;
        await page.waitForTimeout(300); // let the fonts settle before the screenshot
        const date = (await page.textContent('[data-testid="date"]')).trim();
        check(date === 'Day 1 · 06:00', `starts at Day 1 · 06:00 (shows "${date}", loaded in ${loadMs} ms)`);
        check(await page.getAttribute('button[data-speed="Paused"]', 'aria-pressed') === 'true', 'starts paused');
        const rows = await page.locator('.board-row').count();
        check(rows > 0, `the board lists open contracts (${rows})`);
        const kinds = (await page.locator('.board-row .kind').allTextContents()).map(k => k.trim());
        check(kinds.every(k => /^(Sale · (wanted|for sale)|Haul)$/.test(k)), `each row names its kind (${[...new Set(kinds)].join(', ')})`);
        check(await page.locator('[data-testid="ledger"]').count() === 1, 'the ledger is on the screen');
        check(await page.locator('svg.map .node').count() === 7, 'the map draws seven locations');
        check(await page.locator('svg.map .route').count() === 9, 'the map draws nine routes');
        check(await page.locator('svg.map .ship').count() === 10, 'the map draws ten ships');
        await page.screenshot({ path: path.join(shots, '01-start.png'), fullPage: true });

        // 2. Selecting a contract opens it beside the map and draws its route.
        const takeable = page.locator('.board-row:not(.blocked)').filter({ has: page.locator('.take.tone-Gain') }).first();
        await takeable.click();
        await page.waitForSelector('[data-testid="take-home"]');
        const takeHome = (await page.textContent('[data-testid="take-home"]')).trim();
        check(/^−?₵[\d,]+$/.test(takeHome), `the contract panel shows a take-home (${takeHome})`);
        check(await page.locator('svg.map .leg').count() > 0, 'the selected contract is drawn on the map');
        check(await page.locator('.contract-panel .term').count() >= 2, 'the terms read as sentences');
        await page.screenshot({ path: path.join(shots, '02-contract.png'), fullPage: true });

        // 3. Accepting it signs it through the engine, while paused, and the ledger lists it.
        await page.click('.contract-panel .button.primary');
        const outcome = (await page.textContent('.contract-panel .outcome')).trim();
        check(outcome.startsWith('Signed for'), `accepting signs it ("${outcome}")`);
        const firstLog = (await page.locator('.log-line .link-text').first().textContent()).trim();
        check(firstLog.startsWith('You '), `the log records it ("${firstLog.slice(0, 80)}…")`);
        const signed = await page.locator('[data-testid="ledger-contracts"] .ledger-line').count();
        check(signed >= 1, `the ledger lists your contracts (${signed})`);

        // 4. The clock runs at 16×; ships move.
        const before = await tick(page);
        const shipBefore = await page.getAttribute('.ship.yours circle', 'cx');
        await page.click('button[data-speed="X16"]');
        started = Date.now();
        const reached = await runUntil(page, before + 48, 30000);
        const seconds = (Date.now() - started) / 1000;
        const after = await tick(page);
        check(reached, `16× runs two game days (${after - before} hours in ${seconds.toFixed(1)} s)`);
        const shipAfter = await page.getAttribute('.ship.yours circle', 'cx');
        check(shipBefore !== shipAfter, `your ship moved (x ${shipBefore} → ${shipAfter})`);
        await page.screenshot({ path: path.join(shots, '03-running.png'), fullPage: true });

        // 5. Paused, the ledger shows what you own and where: the goods bought for the sale, aboard.
        await page.click('button[data-speed="Paused"]');
        await page.waitForTimeout(200);
        const owned = (await page.locator('[data-testid="possessions"] .ledger-line').allTextContents()).map(t => t.trim());
        check(owned.some(t => t.includes('hold')), `the ledger shows your goods aboard ("${(owned[0] ?? '').slice(0, 80)}")`);
        await page.locator('[data-testid="ledger"]').screenshot({ path: path.join(shots, '04-ledger.png') });

        // 6. The light theme uses the same layout from the other token set.
        await page.selectOption('.theme select', 'light');
        check(await page.getAttribute('html', 'data-theme') === 'light', 'the light theme applies');
        await page.waitForTimeout(200);
        await page.screenshot({ path: path.join(shots, '05-light.png'), fullPage: true });
        await page.selectOption('.theme select', 'system');

        // 7. At phone width nothing scrolls sideways.
        await page.setViewportSize({ width: 390, height: 844 });
        await page.waitForTimeout(200);
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        check(overflow <= 0, `no sideways scroll at 390 px (${overflow} px over)`);
        await page.screenshot({ path: path.join(shots, '06-phone.png'), fullPage: true });

        // 8. The engine speed probe, and the same state as desktop .NET.
        await page.setViewportSize({ width: 1024, height: 600 });
        await page.goto(`${base}/?bench=${BENCH_TICKS}&seed=${SEED}`);
        await page.waitForSelector('[data-testid="bench"][data-done="true"]', { timeout: 300000 });
        const fingerprint = (await page.textContent('[data-testid="bench-fingerprint"]')).trim();
        const ms = Number(await page.getAttribute('[data-testid="bench-fingerprint"]', 'data-ms'));
        results.push(`info  WebAssembly runs ${BENCH_TICKS} game hours in ${ms} ms: ${Math.round(BENCH_TICKS * 1000 / ms)} hours a second (16× needs 16)`);
        check(BENCH_TICKS * 1000 / ms >= 16 * 10, 'the engine runs at least ten times faster than 16× needs');
        check(fingerprint === expectedFingerprint, 'WebAssembly reaches the same state as desktop .NET after 20,000 hours');
        if (fingerprint !== expectedFingerprint) {
            results.push('      browser: ' + fingerprint);
            results.push('      desktop: ' + expectedFingerprint);
        }
    } catch (e) {
        failures.push('the check itself failed: ' + e.message);
        results.push('FAIL  ' + e.message);
        await page.screenshot({ path: path.join(shots, 'error.png'), fullPage: true }).catch(() => {});
    } finally {
        check(problems.length === 0, `no console errors, page errors or failed requests (${problems.length})`);
        for (const p of problems) {
            results.push('      ' + p);
        }

        await browser.close();
        server.close();
    }

    console.log(results.join('\n'));
    console.log(`\nScreenshots in ${shots}`);
    if (failures.length > 0) {
        console.log(`\n${failures.length} check(s) failed.`);
        process.exit(1);
    }

    console.log('\nAll browser checks passed.');
}

main().catch(e => {
    console.error(e);
    process.exit(1);
});

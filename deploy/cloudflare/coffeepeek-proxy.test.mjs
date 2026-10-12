import assert from "node:assert/strict";
import test from "node:test";
import worker from "./coffeepeek-proxy.mjs";

const token = "123456:fake-test-token";
const env = { TELEGRAM_MODERATION_BOT_TOKEN: token };
const origin = "https://relay.example";

function telegramRequest(authorization = `Bearer ${token}`) {
  return new Request(`${origin}/telegram/sendMessage`, {
    method: "POST",
    headers: { authorization, "content-type": "application/json", "x-private": "do-not-forward" },
    body: JSON.stringify({ chat_id: "-123", text: "Test notification" }),
  });
}

test("Gemini menu parsing keeps its URL, query and body", async (t) => {
  const path = "/v1beta/models/test-model:generateContent?key=fake-key";
  const body = JSON.stringify({ contents: [{ parts: [{ text: "Test menu" }] }] });
  t.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, `https://generativelanguage.googleapis.com${path}`);
    assert.equal(options.method, "POST");
    assert.equal(options.headers.get("content-type"), "application/json");
    assert.equal(options.headers.get("authorization"), null);
    assert.equal(await new Response(options.body).text(), body);
    return new Response("gemini-response");
  });
  const response = await worker.fetch(new Request(`${origin}${path}`, {
    method: "POST", headers: { "content-type": "application/json", authorization: "private" }, body,
  }), {});
  assert.equal(await response.text(), "gemini-response");
});

test("authenticated Telegram POST forwards only to the fixed Bot API endpoint", async (t) => {
  const upstream = t.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, `https://api.telegram.org/bot${token}/sendMessage`);
    assert.equal(options.method, "POST");
    assert.equal(options.redirect, "error");
    assert.deepEqual(options.headers, { "content-type": "application/json" });
    assert.deepEqual(await new Response(options.body).json(), { chat_id: "-123", text: "Test notification" });
    return Response.json({ ok: true, result: { message_id: 1 } });
  });
  const response = await worker.fetch(telegramRequest(), env);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { ok: true, result: { message_id: 1 } });
  assert.equal(upstream.mock.callCount(), 1);
});

test("missing or wrong credentials never call Telegram", async (t) => {
  const upstream = t.mock.method(globalThis, "fetch", () => assert.fail("unexpected upstream request"));
  for (const authorization of ["", "Bearer wrong-token"]) {
    const response = await worker.fetch(telegramRequest(authorization), env);
    assert.equal(response.status, 401);
  }
  assert.equal((await worker.fetch(telegramRequest(), {})).status, 503);
  assert.equal(upstream.mock.callCount(), 0);
});

test("other Telegram methods and arbitrary proxy targets are not exposed", async (t) => {
  const upstream = t.mock.method(globalThis, "fetch", () => assert.fail("unexpected upstream request"));
  for (const [method, path] of [["GET", "/telegram/sendMessage"], ["POST", "/telegram/getMe"],
    ["POST", `/bot${token}/sendMessage`], ["POST", "/https://example.com/"]]) {
    assert.equal((await worker.fetch(new Request(`${origin}${path}`, { method }), env)).status, 404);
  }
  assert.equal(upstream.mock.callCount(), 0);
});

test("Telegram errors remain errors so the backend retains the notification", async (t) => {
  t.mock.method(globalThis, "fetch", async () => Response.json({ ok: false, error_code: 429 }, { status: 429 }));
  const response = await worker.fetch(telegramRequest(), env);
  assert.equal(response.status, 429);
  assert.deepEqual(await response.json(), { ok: false, error_code: 429 });
});

test("network failures do not expose the bot token", async (t) => {
  t.mock.method(globalThis, "fetch", async () => { throw new Error(`failed https://api.telegram.org/bot${token}/sendMessage`); });
  const response = await worker.fetch(telegramRequest(), env);
  assert.equal(response.status, 502);
  assert.equal(await response.text(), "Telegram upstream unavailable");
});

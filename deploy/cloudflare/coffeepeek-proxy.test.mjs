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
    assert.equal(options.redirect, "manual");
    assert.deepEqual(options.headers, { "content-type": "application/json" });
    assert.deepEqual(await new Response(options.body).json(), { chat_id: "-123", text: "Test notification" });
    return Response.json({ ok: true, result: { message_id: 1 } });
  });
  const response = await worker.fetch(telegramRequest(), env);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { ok: true, result: { message_id: 1 } });
  assert.equal(upstream.mock.callCount(), 1);
});

test("photo uploads and callback polling keep their bodies and media types", async (t) => {
  const seen = [];
  t.mock.method(globalThis, "fetch", async (url, options) => {
    seen.push({ url, contentType: options.headers["content-type"],
      body: await new Response(options.body).text() });
    return Response.json({ ok: true, result: [] });
  });
  const photo = "--test-boundary\r\nphoto bytes\r\n--test-boundary--";
  const photoResponse = await worker.fetch(new Request(`${origin}/telegram/sendPhoto`, {
    method: "POST",
    headers: { authorization: `Bearer ${token}`,
      "content-type": "multipart/form-data; boundary=test-boundary" },
    body: photo,
  }), env);
  const updatesResponse = await worker.fetch(new Request(`${origin}/telegram/getUpdates`, {
    method: "POST",
    headers: { authorization: `Bearer ${token}`, "content-type": "application/json" },
    body: JSON.stringify({ offset: 42, timeout: 20, allowed_updates: ["callback_query"] }),
  }), env);
  assert.equal(photoResponse.status, 200);
  assert.equal(updatesResponse.status, 200);
  assert.equal(seen[0].url, `https://api.telegram.org/bot${token}/sendPhoto`);
  assert.equal(seen[0].contentType, "multipart/form-data; boundary=test-boundary");
  assert.equal(seen[0].body, photo);
  assert.equal(seen[1].url, `https://api.telegram.org/bot${token}/getUpdates`);
  assert.deepEqual(JSON.parse(seen[1].body), {
    offset: 42, timeout: 20, allowed_updates: ["callback_query"],
  });
});

test("GPT menu parsing forwards authorization, model, photos and structured output unchanged", async (t) => {
  const payload = {
    model: "gpt-4.1-mini", temperature: 0,
    messages: [{ role: "user", content: [{ type: "image_url", image_url: { url: "data:image/jpeg;base64,AQID", detail: "high" } }] }],
    response_format: { type: "json_schema", json_schema: { name: "cafe_menu", strict: true, schema: { type: "object" } } },
  };
  const result = { choices: [{ message: { content: '{"drinks":[]}' } }] };
  t.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, "https://api.openai.com/v1/chat/completions");
    assert.equal(options.method, "POST");
    assert.equal(options.redirect, "manual");
    assert.deepEqual(options.headers, { "content-type": "application/json", authorization: "Bearer sk-test" });
    assert.deepEqual(await new Response(options.body).json(), payload);
    return Response.json(result);
  });
  const response = await worker.fetch(new Request(`${origin}/v1/chat/completions`, {
    method: "POST", headers: { authorization: "Bearer sk-test", "x-private": "do-not-forward" },
    body: JSON.stringify(payload),
  }), env);
  assert.deepEqual(await response.json(), result);
});

test("GPT route requires a key and does not accept unrelated OpenAI endpoints", async (t) => {
  const upstream = t.mock.method(globalThis, "fetch", () => assert.fail("unexpected upstream request"));
  for (const authorization of ["", "Basic invalid", "Bearer "]) {
    const response = await worker.fetch(new Request(`${origin}/v1/chat/completions`, {
      method: "POST", headers: { authorization }, body: "{}",
    }), env);
    assert.equal(response.status, 401);
  }
  assert.equal((await worker.fetch(new Request(`${origin}/v1/models`), env)).status, 404);
  assert.equal(upstream.mock.callCount(), 0);
});

test("GPT route preserves API errors and sanitizes network failures", async (t) => {
  const makeRequest = () => new Request(`${origin}/v1/chat/completions`, {
    method: "POST", headers: { authorization: "Bearer sk-test" }, body: "{}",
  });
  const upstream = t.mock.method(globalThis, "fetch", async () => Response.json({ error: { code: "rate_limit_exceeded" } }, { status: 429 }));
  const response = await worker.fetch(makeRequest(), env);
  assert.equal(response.status, 429);
  assert.deepEqual(await response.json(), { error: { code: "rate_limit_exceeded" } });
  upstream.mock.mockImplementation(async () => { throw new Error("private details sk-test"); });
  const failed = await worker.fetch(makeRequest(), env);
  assert.equal(failed.status, 502);
  assert.equal(await failed.text(), "OpenAI upstream unavailable");
});

test("missing or wrong credentials never call Telegram", async (t) => {
  const upstream = t.mock.method(globalThis, "fetch", () => assert.fail("unexpected upstream request"));
  for (const authorization of ["", "Bearer wrong-token"]) {
    for (const method of ["sendMessage", "sendPhoto", "getUpdates", "answerCallbackQuery"]) {
      const response = await worker.fetch(new Request(`${origin}/telegram/${method}`, {
        method: "POST", headers: { authorization }, body: "{}",
      }), env);
      assert.equal(response.status, 401);
    }
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

test("upstream redirects are neither followed nor returned to credential-bearing clients", async (t) => {
  const upstream = t.mock.method(globalThis, "fetch", async (_, options) => {
    assert.equal(options.redirect, "manual");
    return new Response(null, { status: 307, headers: { location: "https://untrusted.example/" } });
  });
  for (const request of [telegramRequest(), new Request(`${origin}/v1/chat/completions`, {
    method: "POST", headers: { authorization: "Bearer sk-test" }, body: "{}",
  })]) {
    const response = await worker.fetch(request, env);
    assert.equal(response.status, 502);
    assert.equal(response.headers.get("location"), null);
    assert.match(await response.text(), /upstream redirect refused$/);
  }
  assert.equal(upstream.mock.callCount(), 2);
});

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "POST" && url.pathname === "/telegram/sendMessage") {
      const token = env.TELEGRAM_MODERATION_BOT_TOKEN;
      if (!token) return new Response("Telegram relay is not configured", { status: 503 });
      if (request.headers.get("authorization") !== `Bearer ${token}`)
        return new Response("Unauthorized", { status: 401 });

      try {
        return await fetch(`https://api.telegram.org/bot${token}/sendMessage`, {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: request.body,
          redirect: "error",
        });
      } catch {
        // Upstream errors can contain the Bot API URL and its secret token.
        return new Response("Telegram upstream unavailable", { status: 502 });
      }
    }

    if (request.method === "POST" && url.pathname === "/v1/chat/completions") {
      const authorization = request.headers.get("authorization");
      if (!authorization || !/^Bearer\s+\S+$/i.test(authorization))
        return new Response("Unauthorized", { status: 401 });

      try {
        return await fetch("https://api.openai.com/v1/chat/completions", {
          method: "POST",
          headers: { "content-type": "application/json", authorization },
          body: request.body,
          redirect: "error",
        });
      } catch {
        return new Response("OpenAI upstream unavailable", { status: 502 });
      }
    }

    const ok =
      request.method === "POST" &&
      url.pathname.startsWith("/v1beta/models/") &&
      url.pathname.includes(":generateContent");
    if (!ok) return new Response("Not found", { status: 404 });

    const target =
      "https://generativelanguage.googleapis.com" + url.pathname + url.search;
    const headers = new Headers();
    const contentType = request.headers.get("content-type");
    if (contentType) headers.set("content-type", contentType);

    return fetch(target, { method: "POST", headers, body: request.body });
  },
};

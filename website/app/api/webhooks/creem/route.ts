import { createHmac, timingSafeEqual } from "node:crypto";

type CreemWebhook = {
  id?: string;
  eventType?: string;
  object?: {
    id?: string;
    status?: string;
    metadata?: Record<string, unknown>;
    order?: { id?: string; amount?: number; currency?: string; status?: string };
    product?: { id?: string; name?: string; price?: number; currency?: string };
  };
};

function json(body: Record<string, unknown>, status = 200) {
  return Response.json(body, { status, headers: { "Cache-Control": "no-store" } });
}

function hasValidSignature(rawBody: string, signature: string | null, secret: string): boolean {
  if (!signature || !/^[a-f\d]{64}$/i.test(signature)) return false;
  const expected = createHmac("sha256", secret).update(rawBody).digest();
  const received = Buffer.from(signature, "hex");
  return received.length === expected.length && timingSafeEqual(received, expected);
}

export async function POST(request: Request) {
  const webhookSecret = process.env.CREEM_WEBHOOK_SECRET;
  if (!webhookSecret) return json({ error: "Creem webhook is not configured." }, 503);

  const rawBody = await request.text();
  if (rawBody.length > 256_000) return json({ error: "Request too large." }, 413);
  if (!hasValidSignature(rawBody, request.headers.get("creem-signature"), webhookSecret)) {
    return json({ error: "Invalid webhook signature." }, 401);
  }

  let event: CreemWebhook;
  try {
    event = JSON.parse(rawBody) as CreemWebhook;
  } catch {
    return json({ error: "Invalid webhook payload." }, 400);
  }
  if (event.eventType !== "checkout.completed") return json({ received: true });

  const checkout = event.object;
  const metadata = checkout?.metadata;
  const clientId = metadata?.analytics_client_id;
  if (metadata?.analytics_consent !== "granted" || typeof clientId !== "string" || !/^[0-9]+\.[0-9]+$/.test(clientId)) {
    return json({ received: true, analytics: "not-consented" });
  }

  const measurementId = "G-NW89B3FEEX";
  const apiSecret = process.env.GA4_API_SECRET;
  if (!apiSecret) return json({ error: "GA4 server-side tracking is not configured." }, 503);

  const order = checkout?.order;
  const product = checkout?.product;
  const transactionId = order?.id || checkout?.id;
  if (!transactionId) return json({ error: "Creem order ID is missing." }, 400);
  const amount = typeof order?.amount === "number" ? order.amount / 100
    : typeof product?.price === "number" ? product.price / 100 : 6;
  const currency = (order?.currency || product?.currency || "USD").toUpperCase();

  try {
    const response = await fetch(`https://www.google-analytics.com/mp/collect?measurement_id=${measurementId}&api_secret=${encodeURIComponent(apiSecret)}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        client_id: clientId,
        events: [{
          name: "purchase",
          params: {
            transaction_id: transactionId,
            currency,
            value: amount,
            items: [{ item_id: product?.id || "keeply-lifetime", item_name: product?.name || "Keeply Lifetime License", price: amount, quantity: 1 }],
          },
        }],
      }),
      cache: "no-store",
      signal: AbortSignal.timeout(10000),
    });
    if (!response.ok) return json({ error: "Google Analytics did not accept the purchase event." }, 502);
    return json({ received: true, analytics: "purchase-recorded" });
  } catch {
    return json({ error: "Google Analytics could not be reached." }, 502);
  }
}

import { NextResponse } from "next/server";

const defaultProductId = "prod_6z7buEm087gA7b7rLpO94B";
const defaultCheckoutUrl = "https://www.creem.io/payment/prod_6z7buEm087gA7b7rLpO94B";

export async function POST(request: Request) {
  const apiKey = process.env.CREEM_API_KEY;
  if (!apiKey) {
    return NextResponse.json({ error: "Checkout is not configured." }, { status: 503 });
  }

  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return NextResponse.json({ error: "Invalid checkout request." }, { status: 400 });
  }
  const analyticsClientId = typeof body === "object" && body !== null && "analyticsClientId" in body
    ? (body as { analyticsClientId?: unknown }).analyticsClientId
    : undefined;
  const includeAnalytics = typeof analyticsClientId === "string" && /^[0-9]+\.[0-9]+$/.test(analyticsClientId);

  const checkoutLink = process.env.CREEM_CHECKOUT_URL || defaultCheckoutUrl;
  const productFromLink = checkoutLink.match(/prod_[A-Za-z0-9]+/)?.[0];
  const productId = process.env.CREEM_PRODUCT_ID || productFromLink || defaultProductId;
  const siteUrl = (process.env.NEXT_PUBLIC_SITE_URL || "https://www.trykeeply.live").replace(/\/$/, "");
  const base = (process.env.CREEM_API_BASE || "https://api.creem.io").replace(/\/$/, "");

  try {
    const response = await fetch(`${base}/v1/checkouts`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "x-api-key": apiKey },
      body: JSON.stringify({
        product_id: productId,
        success_url: `${siteUrl}/thank-you?checkout_id={checkout_id}`,
        ...(includeAnalytics ? { metadata: { analytics_consent: "granted", analytics_client_id: analyticsClientId } } : {}),
      }),
      cache: "no-store",
      signal: AbortSignal.timeout(15000),
    });
    const result: unknown = await response.json().catch(() => ({}));
    if (!response.ok || typeof result !== "object" || result === null || !("checkout_url" in result) || typeof result.checkout_url !== "string") {
      return NextResponse.json({ error: "Creem could not create checkout." }, { status: 502 });
    }
    return NextResponse.json({ checkoutUrl: result.checkout_url }, { headers: { "Cache-Control": "no-store" } });
  } catch {
    return NextResponse.json({ error: "Creem could not be reached." }, { status: 502 });
  }
}

"use client";

const measurementId = "G-NW89B3FEEX";
const consentStorageKey = "keeply-analytics-consent";

declare global {
  interface Window {
    gtag?: (...args: unknown[]) => void;
  }
}

type PurchaseButtonProps = {
  fallbackUrl: string;
};

export function PurchaseButton({ fallbackUrl }: PurchaseButtonProps) {
  async function beginCheckout(analyticsClientId?: string) {
    window.gtag?.("event", "begin_checkout", {
      currency: "USD",
      value: 6,
      items: [{ item_id: "keeply-lifetime", item_name: "Keeply Lifetime License", price: 6, quantity: 1 }],
    });

    try {
      const response = await fetch("/api/checkout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(analyticsClientId ? { analyticsClientId } : {}),
      });
      const result = await response.json() as { checkoutUrl?: unknown };
      if (!response.ok || typeof result.checkoutUrl !== "string") throw new Error("Checkout setup failed.");
      window.location.assign(result.checkoutUrl);
    } catch {
      // Keep the hosted payment link available if API checkout setup is unavailable.
      window.location.assign(fallbackUrl);
    }
  }

  function handleClick(event: React.MouseEvent<HTMLAnchorElement>) {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

    const consentGranted = window.localStorage.getItem(consentStorageKey) === "granted";
    if (!consentGranted || !window.gtag) return;

    event.preventDefault();
    let started = false;
    const startOnce = (clientId?: unknown) => {
      if (started) return;
      started = true;
      void beginCheckout(typeof clientId === "string" ? clientId : undefined);
    };

    window.gtag("get", measurementId, "client_id", (clientId: unknown) => startOnce(clientId));
    window.setTimeout(() => startOnce(), 400);
  }

  return (
    <a className="button buy" href={fallbackUrl} onClick={handleClick} data-ga-location="pricing_lifetime">
      Get lifetime access <span>→</span>
    </a>
  );
}

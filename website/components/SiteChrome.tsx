import type { ReactNode } from "react";
import { microsoftStoreUrl } from "../lib/store";

export function SiteHeader({ home = false }: { home?: boolean }) {
  return (
    <header className="nav wrap">
      <a className="brand" href="/">
        <img className="brandmark" src="/keeply-logo.svg" alt="" /> Keeply
      </a>
      {home ? (
        <>
          <nav>
          <a href="#how" data-ga-event="cta_click" data-ga-location="header">How it works</a>
          <a href="#features" data-ga-event="cta_click" data-ga-location="header">Features</a>
          <a href="#pricing" data-ga-event="cta_click" data-ga-location="header">Pricing</a>
          <a href="#privacy" data-ga-event="cta_click" data-ga-location="header">Privacy</a>
          </nav>
          <a className="button small" href={microsoftStoreUrl} target="_blank" rel="noopener noreferrer" data-ga-event="store_listing_click" data-ga-location="header">
            Microsoft Store <span>↗</span>
          </a>
        </>
      ) : (
        <a className="text-link" href="/">← Back home</a>
      )}
    </header>
  );
}

export function SiteFooter() {
  const supportEmail = process.env.SUPPORT_EMAIL || "trykeeply@gmail.com";
  return (
    <footer className="footer">
      <div className="wrap foot-inner">
        <a className="brand" href="/">
          <img className="brandmark" src="/keeply-logo.svg" alt="" /> Keeply
        </a>
        <span>Made for calmer photo folders.</span>
        <div>
          <a href="/privacy">Privacy</a>
          <a href="/terms">Terms</a>
          <a href="/refunds">Refunds</a>
          <a href={`mailto:${supportEmail}`}>Contact</a>
        </div>
      </div>
    </footer>
  );
}

export function SiteFrame({ children, home = false }: { children: ReactNode; home?: boolean }) {
  return (
    <>
      <SiteHeader home={home} />
      {children}
      <SiteFooter />
    </>
  );
}

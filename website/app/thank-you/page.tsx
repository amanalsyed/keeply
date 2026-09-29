import type { Metadata } from "next";
import { SiteFrame } from "../../components/SiteChrome";

export const metadata: Metadata = {
  title: "Thank you for your purchase — Keeply",
  description: "Your Keeply lifetime license purchase is complete. Find your license key and activate Keeply.",
  robots: { index: false, follow: false },
};

export default function ThankYouPage() {
  const supportEmail = process.env.SUPPORT_EMAIL || "trykeeply@gmail.com";

  return (
    <SiteFrame>
      <main className="success-page wrap">
        <section className="success-card" aria-labelledby="success-title">
          <div className="success-check" aria-hidden="true">✓</div>
          <p className="eyebrow">KEEPly LIFETIME</p>
          <h1 id="success-title">Thank you for choosing Keeply!</h1>
          <p className="success-intro">Your payment is complete. Your license key is sent by email through Creem.</p>

          <div className="license-instructions">
            <span className="instruction-number">NEXT STEP</span>
            <h2>Activate Keeply in a few seconds</h2>
            <ol>
              <li>Check the inbox for the email address you used at checkout. Look in spam or junk if you don’t see it.</li>
              <li>Copy the license key from the Creem email.</li>
              <li>Open Keeply, choose <strong>Activate</strong>, paste the key, and activate online.</li>
            </ol>
          </div>

          <a className="button success-download" href="/downloads/Keeply-Setup.exe" download="Keeply-Setup.exe">Download Keeply for Windows <span>↓</span></a>
          <p className="success-help">Already installed Keeply? Open it and paste your key. Need help? <a href={`mailto:${supportEmail}`}>{supportEmail}</a>.</p>
        </section>
      </main>
    </SiteFrame>
  );
}

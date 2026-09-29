import type { Metadata } from "next";
import { PolicyPage } from "../../components/PolicyPage";

export const metadata: Metadata = { title: "Refunds — Keeply" };

export default function RefundsPage() {
  const supportEmail = process.env.SUPPORT_EMAIL || "trykeeply@gmail.com";

  return <PolicyPage title="Refund policy">
    <p><strong>Effective date: September 28, 2026.</strong> We offer a 14-calendar-day refund period for the Keeply lifetime license. A buyer who emails us within 14 calendar days of the purchase date will receive a full refund through Creem. No reason is required. This voluntary policy does not limit refund or cancellation rights that apply under consumer law.</p>

    <h2>How to request a refund</h2>
    <ol>
      <li>Email <a href={`mailto:${supportEmail}`}>{supportEmail}</a> within 14 calendar days of purchase. Include the purchase email, Creem order reference, and the reason for the request. Do not send photo files or folder paths.</li>
      <li>We will review the request and submit eligible refunds through Creem. Creem is the merchant of record and processes the payment refund. If you cannot resolve a purchase issue with us, contact Creem through the <a href="https://www.creem.io/contact" target="_blank" rel="noreferrer">refund support page</a> or the Customer Portal.</li>
    </ol>

    <h2>After a refund</h2>
    <p>Once a refund is approved, the lifetime license is no longer licensed for use. Keeply stores an activation locally to support offline use, so an offline PC may remain locally unlocked until it reconnects and the activation is deactivated. Contact support if you need help with a refunded license.</p>
    <p>Creem processes the refund to the original payment method when available. The time for the credit to appear depends on the payment provider and your bank. Any rights that cannot be waived under the laws that apply to your purchase remain in effect. See the <a href="https://www.creem.io/buyer-terms" target="_blank" rel="noreferrer">Creem Buyer Terms</a> for its purchase and refund process.</p>
  </PolicyPage>;
}

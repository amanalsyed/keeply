import type { Metadata } from "next";
import { PolicyPage } from "../../components/PolicyPage";

export const metadata: Metadata = { title: "Terms — Keeply" };

export default function TermsPage() {
  const supportEmail = process.env.SUPPORT_EMAIL || "trykeeply@gmail.com";
  const governingLaw = process.env.GOVERNING_LAW;

  return <PolicyPage title="Terms of use">
    <p><strong>Effective date: September 28, 2026.</strong> These terms cover your use of Keeply, a Windows desktop photo-triage app provided by Keeply's publisher ("we," "us," or "our"). By installing or using the app, you agree to these terms. Creem handles the purchase transaction as merchant of record; the terms shown by Creem at checkout also apply to that transaction.</p>

    <h2>What Keeply does</h2>
    <p>Keeply displays supported images from a folder you select. Press K to keep a photo in place, T to send it to the Windows Recycle Bin, or A to copy it to an album folder you choose. Undo may reverse the most recent action when Windows and the files permit it. You are responsible for choosing the correct folder and reviewing actions before using the app.</p>

    <h2>Free use</h2>
    <p>The free edition may be used with up to three unique eligible folders on one Windows profile. Each folder may contain up to 100 supported top-level photos (JPG, JPEG, PNG, or WebP). Empty folders do not count, and reopening one of the same three folders does not use another slot. The allowance is stored on your PC as hashes of normalized folder paths.</p>

    <h2>Lifetime license</h2>
    <p>The lifetime license is a one-time US$6 purchase, subject to the price, currency, taxes, and terms shown at Creem checkout. It removes the folder and photo-count limits and may be activated on up to three PCs. It is not a subscription and does not automatically renew. An activated PC can continue offline while its activation is stored locally. Online activation and deactivation require internet access; an offline PC cannot receive an activation transfer or status change until it reconnects. The license is for the purchaser's use on their activated devices and may not be resold, shared, or transferred to another person.</p>

    <h2>Creem purchases</h2>
    <p>Creem acts as merchant of record for purchases through its checkout and handles payment processing, receipts, applicable checkout taxes, and payment-related refund processing. The purchase is also subject to the <a href="https://www.creem.io/buyer-terms" target="_blank" rel="noreferrer">Creem Buyer Terms</a>. Where applicable consumer rights or Creem's checkout terms provide protections that differ from this page, those protections apply.</p>

    <h2>Your responsibilities and files</h2>
    <p>Keep backups of important photos. Trash uses the Windows Recycle Bin, but Windows may remove items or prevent an undo. Album copies files and may create a numbered filename to avoid overwriting an existing file. You are responsible for your source files, selected folders, and destinations.</p>
    <p>Keeply processes photos locally and does not upload them. License activation sends the license key and a generated device name to the license service for validation with Creem. Do not use the app to access files without permission or in violation of law. You may not redistribute, sell, or sublicense the app or bypass license activation, except where applicable law does not allow that restriction.</p>

    <h2>Updates and availability</h2>
    <p>We may change, improve, or discontinue features. The lifetime license grants ongoing use of the licensed app; it does not promise that every future feature, update, or version will be provided. Keeply is provided as available. We do not promise uninterrupted operation or that every file can be previewed, moved, restored, or undone on every Windows system.</p>

    <h2>Warranty and liability</h2>
    <p>To the extent permitted by law, the app is provided "as is" without warranties that cannot be excluded by contract. Nothing in these terms removes consumer rights or limits liability where applicable law does not allow it. To the extent the law permits, we are not responsible for indirect or consequential loss arising from use of the app. This does not limit responsibility for deliberate misconduct or other liability that cannot legally be limited.</p>

    <h2>Changes and applicable law</h2>
    <p>We may revise these terms when the app or its services change and will update the effective date. {governingLaw ? `These terms are governed by the laws of ${governingLaw}, subject to mandatory consumer protections where you live.` : "These terms apply subject to the laws and mandatory consumer protections that apply to you. Any dispute will be handled by a court with jurisdiction under applicable law."}</p>

    <h2>Support</h2>
    <p>For app or license support, email <a href={`mailto:${supportEmail}`}>{supportEmail}</a>. For payment questions, see the contact details on your Creem receipt or <a href="https://www.creem.io/contact" target="_blank" rel="noreferrer">contact Creem</a>.</p>
  </PolicyPage>;
}

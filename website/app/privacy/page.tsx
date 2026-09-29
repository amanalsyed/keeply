import type { Metadata } from "next";
import { PolicyPage } from "../../components/PolicyPage";

export const metadata: Metadata = { title: "Privacy — Keeply" };

export default function PrivacyPage() {
  const supportEmail = process.env.SUPPORT_EMAIL || "trykeeply@gmail.com";

  return <PolicyPage title="Privacy">
    <p><strong>Effective date: September 28, 2026.</strong> This notice explains how Keeply's publisher handles information when you use Keeply and this website.</p>

    <h2>Photos and folders stay on your PC</h2>
    <p>Keeply opens and processes photos locally. It does not upload image contents, filenames, or selected folder paths to us or to a Keeply photo library. Keep leaves a file where it is; Trash uses the Windows Recycle Bin; Album copies the photo to the folder you choose.</p>
    <p>To remember the free allowance, Keeply stores hashes of normalized folder paths on your PC, along with local license state. The app does not send those hashes or paths to its license service. An activated license key is protected with Windows DPAPI and stored with its activation identifier in your local Windows profile. The local state is kept under <code>%LOCALAPPDATA%\PhotoKeepKill</code>. Removing it may make an activation seat require online deactivation before it can be transferred.</p>

    <h2>License activation</h2>
    <p>If you activate or deactivate a license, the app sends the license key and a randomly generated device name to Keeply's license endpoint. The endpoint forwards the request to Creem to validate or deactivate the activation. It does not intentionally save the license key, device name, folder paths, or photo data. Vercel hosts the website and license endpoint and may process request metadata such as IP address, URL path, and timestamps under its own privacy practices.</p>

    <h2>Purchases</h2>
    <p>For purchases through Creem, Creem acts as merchant of record and processes checkout, payment, order, and related buyer information. We receive or can access information needed to support the product and license, such as a license key and activation status. See <a href="https://www.creem.io/privacy" target="_blank" rel="noreferrer">Creem's Privacy Notice</a> for its data practices.</p>

    <h2>Website data and cookies</h2>
    <p>This site does not currently include advertising pixels, third-party analytics scripts, or Keeply-set tracking cookies. Vercel may process technical and request data to deliver and protect the site. If you follow a checkout link, Creem's site and its own privacy and cookie practices apply.</p>

    <h2>Retention and choices</h2>
    <p>Photo data remains in its original folders or in folders you select. Local folder hashes and license state remain on your PC until you remove the app's local data. The license endpoint does not keep an application database of activation requests; Creem and Vercel may retain records under their own legal obligations, settings, and policies. For privacy questions or requests, email <a href={`mailto:${supportEmail}`}>{supportEmail}</a>. For information Creem handles as merchant of record, you may also contact <a href="https://www.creem.io/contact" target="_blank" rel="noreferrer">Creem support</a>.</p>

    <h2>Service providers</h2>
    <p>Vercel hosts this website and its license API; Creem handles checkout and license validation. See <a href="https://vercel.com/legal/privacy-notice" target="_blank" rel="noreferrer">Vercel's Privacy Notice</a> and <a href="https://www.creem.io/privacy" target="_blank" rel="noreferrer">Creem's Privacy Notice</a>.</p>

    <h2>Changes</h2>
    <p>We may update this notice when Keeply's data practices change. We will update the effective date when we publish a revision.</p>
  </PolicyPage>;
}

# Keeply website and license API

This is a Next.js App Router site designed for Vercel. Run `npm install`, copy `.env.example` to `.env.local`, then use `npm run dev` and open http://localhost:3000. Run `npm run typecheck` and `npm run build` before publishing.

## Before launch

1. Configure a Creem one-time $6 product with license keys, a maximum of three activations, and no expiry. Copy its hosted checkout URL into `CREEM_CHECKOUT_URL`. Set the product/payment link's post-payment success URL to `https://www.trykeeply.live/thank-you` so buyers land on the Keeply thank-you page after paying.
2. Set the Creem private API key as `CREEM_API_KEY` in the server environment only. Never put this secret in the app, a client-side variable, or a public file.
3. Set `KEEPLY_LICENSE_SIGNING_PRIVATE_KEY` to the PEM private key stored at `%LOCALAPPDATA%\PhotoKeepKill\licensing\keeply-license-signing-private.pem` in the server environment. This key signs offline lifetime license tokens; its matching public key is embedded in Keeply. Keep the private key server-only, do not commit it, and do not expose it through a `NEXT_PUBLIC_` variable. The key must match the public key in `LicenseTokenVerifier.cs`.
4. For purchase analytics, add a Creem webhook at `https://www.trykeeply.live/api/webhooks/creem` for `checkout.completed`. Add its signing secret as `CREEM_WEBHOOK_SECRET` in Vercel, and add a GA4 Measurement Protocol API secret as `GA4_API_SECRET`. These secrets are server-only. GA4 purchase events are sent only when the buyer allowed analytics before starting checkout.
5. Set `SUPPORT_EMAIL` and `PUBLISHER_NAME`. Review the policy pages and set `GOVERNING_LAW` if you want to state a particular jurisdiction. Configure the Creem product terms to match Keeply's 14-calendar-day refund policy.
6. Push the project to a Git provider, import it into Vercel, and set the Vercel project root to `website`. Add the environment variables in the Vercel project settings for Preview and Production. Vercel provides the deployment HTTPS URL. Configure `licensing.json` next to the desktop executable with the public base URL and hosted checkout URL:

   `{ "apiBaseUrl": "https://www.trykeeply.live/", "checkoutUrl": "https://www.creem.io/payment/your-checkout" }`

7. In the Creem test environment, test purchase delivery, activation limit, deactivation, invalid keys, and the exact API response fields before enabling live checkout. Keep the Creem private key only in Vercel server environment variables; never use a `NEXT_PUBLIC_` prefix for it.

The website's app-install buttons point to Keeply's public [Microsoft Store listing](https://apps.microsoft.com/store/detail/9PL19QH8CKJ0?cid=DevShareMCLPCS). The Windows installer is also served directly at `/downloads/Keeply-Setup.exe` as a website download option. The Windows release script copies the built installer into `website/public/downloads`.

## License API

`POST /api/license/activate` accepts `{ "key": "...", "instanceName": "..." }`. The service checks the key and device limit with Creem, then returns an RSA-signed lifetime token tied to that activation ID. The desktop app verifies that signature against its embedded public key before saving the activation. It calls `POST /api/license/validate` at startup and about every 24 hours while running; successful checks refresh the signed token, while an inactive/revoked result removes the local activation. If the PC is offline or the service is unavailable, the last valid signed token remains usable. `POST /api/license/deactivate` accepts `{ "key": "...", "instanceId": "..." }`. The server does not store license keys or photo/folder data. Online deactivation is required to transfer an activation. Existing unsigned activations are checked online and migrated without consuming another activation seat.

The site uses Next.js Route Handlers for checkout, Creem webhooks, and its license API, so the browser never receives Creem or GA4 server secrets. Purchase events are sent only for verified `checkout.completed` webhooks whose checkout metadata includes analytics consent and a GA client ID.

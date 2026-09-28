# Keeply website and license API

This is a Next.js App Router site designed for Vercel. Run `npm install`, copy `.env.example` to `.env.local`, then use `npm run dev` and open http://localhost:3000. Run `npm run typecheck` and `npm run build` before publishing.

## Before launch

1. Configure a Creem one-time $6 product with license keys, a maximum of three activations, and no expiry. Copy its hosted checkout URL into `CREEM_CHECKOUT_URL`.
2. Set the Creem private API key as `CREEM_API_KEY` in the server environment only. Never put this secret in the app, a client-side variable, or a public file.
3. Set `SUPPORT_EMAIL` and `PUBLISHER_NAME`. Review the policy pages and set `GOVERNING_LAW` if you want to state a particular jurisdiction. Configure the Creem product terms to match Keeply's 14-calendar-day refund policy.
4. Push the project to a Git provider, import it into Vercel, and set the Vercel project root to `website`. Add the environment variables in the Vercel project settings for Preview and Production. Vercel provides the deployment HTTPS URL. Configure `licensing.json` next to the desktop executable with the public base URL and hosted checkout URL:

   `{ "apiBaseUrl": "https://your-domain.example/", "checkoutUrl": "https://www.creem.io/payment/your-checkout" }`

5. In the Creem test environment, test purchase delivery, activation limit, deactivation, invalid keys, and the exact API response fields before enabling live checkout. Keep the Creem private key only in Vercel server environment variables; never use a `NEXT_PUBLIC_` prefix for it.

The Windows installer is served directly from the same website at `/downloads/Keeply-Setup.exe`. The Windows release script copies the built installer into `website/public/downloads`, and the site's download buttons use that same-origin path so the browser downloads it without sending visitors to a different website.

## License API

`POST /api/license/activate` accepts `{ "key": "...", "instanceName": "..." }`. `POST /api/license/deactivate` accepts `{ "key": "...", "instanceId": "..." }`. The server forwards only these licensing requests to Creem. It does not store license keys or photo/folder data. Activated app installations store the license key protected with Windows DPAPI and trust that activation offline indefinitely. An offline installation cannot learn about later remote revocation until it reconnects; deactivation requires connectivity.

The site uses Next.js Route Handlers for its license API, so the browser never receives the Creem private API key. Site checkout and live activation remain unconfigured until the Creem product and Vercel environment variables are ready.

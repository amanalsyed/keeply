import { SiteFrame } from "../components/SiteChrome";

export default function HomePage() {
  const checkoutUrl = process.env.CREEM_CHECKOUT_URL || "https://www.creem.io/payment/prod_6z7buEm087gA7b7rLpO94B";
  const downloadUrl = "/downloads/Keeply-Setup.exe";
  const downloadText = "Download for Windows";

  return (
    <SiteFrame home>
      <main>
        <section className="hero wrap">
          <div className="hero-copy">
            <div className="eyebrow"><i /> LOCAL PHOTO TRIAGE FOR WINDOWS</div>
            <h1>A lighter photo folder starts with one key.</h1>
            <p>Move through a big backlog one photo at a time. Keep it, send it to the Recycle Bin, or copy it into an album—without uploading your pictures anywhere.</p>
            <div className="hero-actions">
              <a className="button" href={downloadUrl} download="Keeply-Setup.exe">Try it free <span>→</span></a>
              <a className="text-link" href="#how">See how it works <span>↓</span></a>
            </div>
            <div className="trust"><span>✓ Windows desktop app</span><span>✓ Your photos stay local</span><span>✓ No account required</span></div>
          </div>
          <div className="hero-video">
            <iframe
              src="https://www.youtube-nocookie.com/embed/FwgZaTbbZLI?rel=0"
              title="Keeply photo sorting demo"
              allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
              referrerPolicy="strict-origin-when-cross-origin"
              allowFullScreen
            />
          </div>
        </section>

        <section className="proof wrap"><div><strong>One photo</strong><span>on screen at a time</span></div><div><strong>Three keys</strong><span>to clear the queue</span></div><div><strong>Zero uploads</strong><span>photos stay on your PC</span></div></section>

        <section id="how" className="section wrap">
          <div className="section-head"><div className="eyebrow">SIMPLE BY DESIGN</div><h2>From cluttered folder to<br />clear decisions.</h2></div>
          <div className="steps">
            <article><span className="step-no">01</span><h3>Choose a folder</h3><p>Pick a folder or drag it into the app. Keeply scans images directly inside it.</p></article>
            <article><span className="step-no">02</span><h3>Make the call</h3><p>Keep with K. Send to the Recycle Bin with T. Copy to a regular album folder with A.</p></article>
            <article><span className="step-no">03</span><h3>Keep moving</h3><p>Every choice advances to the next photo. Undo a recent decision or navigate with arrow keys.</p></article>
          </div>
        </section>

        <section className="privacy-band" id="privacy"><div className="wrap privacy-inner"><div><div className="eyebrow">PRIVATE BY DEFAULT</div><h2>Your photos stay yours.</h2><p>Images are opened and processed on your Windows PC. Keep leaves originals in place, Trash uses the Windows Recycle Bin, and Album copies to a folder you choose. There is no photo cloud or proprietary library.</p></div><div className="privacy-stamp"><span>LOCAL</span><b>100%</b><span>PHOTO PROCESSING</span></div></div></section>

        <section id="pricing" className="section wrap pricing">
          <div className="section-head"><div className="eyebrow">STRAIGHTFORWARD PRICING</div><h2>Start free. Unlock the whole folder.</h2><p>Try the full workflow on three folders. Upgrade once when you’re ready to sort without limits.</p></div>
          <div className="plans">
            <article className="plan"><div className="plan-label">FREE</div><div className="price">$0</div><p>Everything you need to see if it fits your workflow.</p><ul><li>3 unique folders on this PC</li><li>Up to 100 supported photos per folder</li><li>Keep, Recycle Bin, and album actions</li><li>Undo and keyboard shortcuts</li><li>Local processing, no account</li></ul><a className="button outline" href={downloadUrl} download="Keeply-Setup.exe">{downloadText}</a></article>
            <article className="plan featured"><div className="ribbon">ONE-TIME PURCHASE</div><div className="plan-label">LIFETIME</div><div className="price">$6 <small>once</small></div><p>One purchase. Keep sorting without limits.</p><ul><li>Unlimited folders and photos</li><li>Lifetime license</li><li>Activate on up to 3 PCs</li><li>Activated PCs work offline</li><li>All Free features included</li></ul><a className="button buy" href={checkoutUrl}>Get lifetime access <span>→</span></a><div className="checkout-note">Secure checkout powered by Creem</div></article>
          </div>
        </section>

        <section className="faq wrap"><div><div className="eyebrow">GOOD TO KNOW</div><h2>Questions,<br />answered.</h2></div><div className="faq-list">
          <details><summary>Does the app upload my photos?</summary><p>No. Photo previews and file actions stay on your Windows PC. License activation sends the key and a random installation name to the licensing service; it does not send folder paths or image data.</p></details>
          <details><summary>What happens when I press Trash?</summary><p>The selected photo is sent to the Windows Recycle Bin, where Windows can restore it. The app’s Undo can restore its latest trash action when the item is still available.</p></details>
          <details><summary>Can I use my license without internet?</summary><p>Internet is needed for initial activation and online deactivation. After activation, that PC stays unlocked while offline.</p></details>
          <details><summary>How do I move my license to another PC?</summary><p>Deactivate a PC while online, then activate on the new PC. Creem enforces the three-PC activation limit.</p></details>
          <details><summary>What image types are supported?</summary><p>JPG, JPEG, PNG, and WebP. WebP preview support depends on the Windows WebP Image Extension.</p></details>
        </div></section>
      </main>
    </SiteFrame>
  );
}

import { SiteFrame } from "../components/SiteChrome";
import { PurchaseButton } from "../components/PurchaseButton";
import { microsoftStoreUrl } from "../lib/store";

export default function HomePage() {
  const checkoutUrl = process.env.CREEM_CHECKOUT_URL || "https://www.creem.io/payment/prod_6z7buEm087gA7b7rLpO94B";
  const downloadText = "Get it from Microsoft Store";

  return (
    <SiteFrame home>
      <main>
        <section className="hero wrap" data-reveal>
          <div className="hero-copy">
            <div className="eyebrow"><i /> LOCAL PHOTO TRIAGE FOR WINDOWS</div>
            <h1>Sort your photos,<br /><span>one at a time.</span></h1>
            <p>Open a folder and review each photo. Press K to keep it, T to send it to the Recycle Bin, or A to copy it to an album. Your photos stay on your PC.</p>
            <div className="hero-actions">
              <a className="button" href={microsoftStoreUrl} target="_blank" rel="noopener noreferrer" data-ga-event="store_listing_click" data-ga-location="hero">Get it from Microsoft Store <span>→</span></a>
              <a className="text-link" href="#how" data-ga-event="cta_click" data-ga-location="hero">See how it works <span>↓</span></a>
            </div>
            <div className="trust"><span>✓ Windows desktop app</span><span>✓ Your photos stay local</span><span>✓ No account required</span></div>
          </div>
          <div className="hero-gallery">
            <div className="hero-video">
              <iframe
                src="https://www.youtube-nocookie.com/embed/FwgZaTbbZLI?rel=0"
                title="Keeply photo sorting demo"
                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                referrerPolicy="strict-origin-when-cross-origin"
                allowFullScreen
              />
            </div>
          </div>
        </section>

        <section className="proof wrap" data-reveal><div><strong>One photo</strong><span>on screen at a time</span></div><div><strong>Three keys</strong><span>to clear the queue</span></div><div><strong>Zero uploads</strong><span>photos stay on your PC</span></div></section>

        <section id="how" className="section wrap" data-reveal>
          <div className="section-head"><div className="eyebrow">SIMPLE BY DESIGN</div><h2>From cluttered folder to<br />clear decisions.</h2></div>
          <div className="steps">
            <article><span className="step-no">01</span><h3>Choose a folder</h3><p>Pick a folder or drag it into the app. Keeply scans images directly inside it.</p></article>
            <article><span className="step-no">02</span><h3>Make the call</h3><p>Keep with K. Send to the Recycle Bin with T. Copy to a regular album folder with A.</p></article>
            <article><span className="step-no">03</span><h3>Keep moving</h3><p>Every choice advances to the next photo. Undo a recent decision or navigate with arrow keys.</p></article>
          </div>
        </section>

        <section id="updates" className="section wrap updates" data-reveal>
          <div className="section-head"><div className="eyebrow">WHAT’S NEW · VERSION 1.0.4</div><h2>More ways to finish your photo backlog.</h2><p>Keeply now helps with the whole cleanup: sort, compare, compress, and convert—right on your Windows PC.</p></div>
          <div className="update-grid">
            <article className="update-card"><span className="update-type">FIND THE BEST SHOT</span><h3>Review duplicate groups</h3><p>Find exact copies and visually similar shots, compare them side by side, then choose which photos to send to the Recycle Bin. Keeply never deletes a group automatically.</p></article>
            <article className="update-card"><span className="update-type">MAKE SMALLER COPIES</span><h3>Bulk compress photos</h3><p>Compress a folder of images into a separate destination. Choose JPEG quality and keep your originals exactly where they are.</p></article>
            <article className="update-card"><span className="update-type">CHANGE FORMATS</span><h3>Convert to WebP, JPEG, or PNG</h3><p>Convert image folders in one run. Choose lossy or lossless WebP, set JPEG quality, or use PNG when you need transparency.</p></article>
            <article className="update-card"><span className="update-type">PICK UP LATER</span><h3>Resume where you stopped</h3><p>Return to a sorting session and continue from your saved progress instead of starting over.</p></article>
            <article className="update-card"><span className="update-type">SORT FASTER</span><h3>Quick folder keys</h3><p>Assign folders to number keys 1–9, then send a photo to the right destination with one press.</p></article>
            <article className="update-card"><span className="update-type">MORE IMAGE TYPES</span><h3>Work with more formats</h3><p>Keeply supports JPG, JPEG, PNG, WebP, BMP, GIF, and TIFF for its photo workflows. Animated GIFs and multi-page TIFFs are preserved or skipped by tools that cannot safely process all frames or pages.</p></article>
          </div>
          <p className="updates-note">Every tool works locally. Converted and compressed files are saved as copies, and duplicate removal always requires your choice.</p>
        </section>

        <section className="privacy-band" id="privacy" data-reveal><div className="wrap privacy-inner"><div><div className="eyebrow">PRIVATE BY DEFAULT</div><h2>Your photos stay yours.</h2><p>Images are opened and processed on your Windows PC. Keep leaves originals in place, Trash uses the Windows Recycle Bin, and Album copies to a folder you choose. There is no photo cloud or proprietary library.</p></div><div className="privacy-stamp"><span>LOCAL</span><b>100%</b><span>PHOTO PROCESSING</span></div></div></section>

        <section id="pricing" className="section wrap pricing" data-reveal>
          <div className="section-head"><div className="eyebrow">STRAIGHTFORWARD PRICING</div><h2>Start free. Unlock unlimited sorting.</h2><p>Try Keeply’s sorting workflow on up to three folders. Upgrade once when you’re ready to sort without folder or photo-count limits.</p></div>
          <div className="plans">
            <article className="plan"><div className="plan-label">FREE</div><div className="price">$0</div><p>Everything you need to see if it fits your workflow.</p><ul><li>3 unique folders on this PC</li><li>Up to 100 supported photos per folder</li><li>Keep, Recycle Bin, and album actions</li><li>Undo and keyboard shortcuts</li><li>Local processing, no account</li></ul><a className="button outline" href={microsoftStoreUrl} target="_blank" rel="noopener noreferrer" data-ga-event="store_listing_click" data-ga-location="pricing_free">{downloadText}</a></article>
            <article className="plan featured"><div className="ribbon">ONE-TIME PURCHASE</div><div className="plan-label">LIFETIME</div><div className="price">$6 <small>once</small></div><p>One purchase. Keep sorting without limits.</p><ul><li>Unlimited folders and photos</li><li>Lifetime license</li><li>Activate on up to 3 PCs</li><li>Activated PCs work offline</li><li>All Free features included</li></ul><PurchaseButton fallbackUrl={checkoutUrl} /><div className="checkout-note">Secure checkout powered by Creem</div></article>
          </div>
          <p className="guarantee-note"><strong>14-day money-back guarantee.</strong> If Keeply isn’t right for you, request a full refund within 14 calendar days of purchase. <a href="/refunds">See refund policy</a>.</p>
        </section>

        <section className="faq wrap" data-reveal><div><div className="eyebrow">GOOD TO KNOW</div><h2>Questions,<br />answered.</h2></div><div className="faq-list">
          <details><summary>Does the app upload my photos?</summary><p>No. Photo previews and file actions stay on your Windows PC. License activation sends the key and a random installation name to the licensing service; it does not send folder paths or image data.</p></details>
          <details><summary>What happens when I press Trash?</summary><p>The selected photo is sent to the Windows Recycle Bin, where Windows can restore it. The app’s Undo can restore its latest trash action when the item is still available.</p></details>
          <details><summary>Can I use my license without internet?</summary><p>Internet is needed for initial activation and online deactivation. After activation, that PC stays unlocked while offline.</p></details>
          <details><summary>How do I move my license to another PC?</summary><p>Deactivate a PC while online, then activate on the new PC. Creem enforces the three-PC activation limit.</p></details>
          <details><summary>Does Keeply have a money-back guarantee?</summary><p>Yes. Request a full refund within 14 calendar days of purchase by emailing our support team. See the <a href="/refunds">refund policy</a> for details.</p></details>
          <details><summary>Can Keeply compress and convert images?</summary><p>Yes. Bulk Compress creates smaller copies in another folder. Convert Images creates WebP, JPEG, or PNG copies. Your originals are not changed; animated GIFs and multi-page TIFFs are skipped when a tool cannot safely preserve all frames or pages.</p></details>
          <details><summary>How does duplicate finding work?</summary><p>Keeply groups exact copies and visually similar shots so you can compare them. You select what to move to the Windows Recycle Bin; nothing is removed automatically.</p></details>
          <details><summary>What image types are supported?</summary><p>JPG, JPEG, PNG, WebP, BMP, GIF, and TIFF. WebP preview support depends on the Windows WebP Image Extension. Animated GIFs and multi-page TIFFs preview their first frame or page.</p></details>
        </div></section>
      </main>
    </SiteFrame>
  );
}

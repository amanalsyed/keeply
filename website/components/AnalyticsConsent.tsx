"use client";

import { useEffect, useState } from "react";
import Script from "next/script";

const measurementId = "G-NW89B3FEEX";
const consentStorageKey = "keeply-analytics-consent";

export function AnalyticsConsent() {
  const [choice, setChoice] = useState<"granted" | "denied" | null>(null);
  const [isOpen, setIsOpen] = useState(false);

  useEffect(() => {
    const savedChoice = window.localStorage.getItem(consentStorageKey);
    if (savedChoice === "granted" || savedChoice === "denied") {
      setChoice(savedChoice);
    } else {
      setIsOpen(true);
    }

    const openSettings = () => setIsOpen(true);
    window.addEventListener("keeply:privacy-settings", openSettings);
    return () => window.removeEventListener("keeply:privacy-settings", openSettings);
  }, []);

  function saveChoice(nextChoice: "granted" | "denied") {
    window.localStorage.setItem(consentStorageKey, nextChoice);
    if (nextChoice === "denied" && choice === "granted") {
      const analyticsCookies = document.cookie.split(";").map((cookie) => cookie.trim().split("=")[0]).filter((name) => name === "_ga" || name.startsWith("_ga_"));
      for (const name of analyticsCookies) {
        document.cookie = `${name}=; Max-Age=0; path=/; SameSite=Lax`;
        document.cookie = `${name}=; Max-Age=0; path=/; domain=${window.location.hostname}; SameSite=Lax`;
        document.cookie = `${name}=; Max-Age=0; path=/; domain=.trykeeply.live; SameSite=Lax`;
      }
      window.location.reload();
      return;
    }
    setChoice(nextChoice);
    setIsOpen(false);
  }

  return (
    <>
      {choice === "granted" && (
        <>
          <Script
            src={`https://www.googletagmanager.com/gtag/js?id=${measurementId}`}
            strategy="afterInteractive"
          />
          <Script id="keeply-google-analytics" strategy="afterInteractive">
            {`
              window.dataLayer = window.dataLayer || [];
              function gtag(){window.dataLayer.push(arguments);}
              gtag('js', new Date());
              gtag('config', '${measurementId}', {
                allow_google_signals: false,
                allow_ad_personalization_signals: false
              });
            `}
          </Script>
        </>
      )}

      <button
        type="button"
        className="analytics-settings"
        onClick={() => setIsOpen(true)}
        aria-label="Change analytics privacy choices"
      >
        Privacy choices
      </button>

      {isOpen && (
        <aside className="analytics-banner" aria-label="Analytics privacy choices">
          <div>
            <strong>Help us improve Keeply</strong>
            <p>Allow Google Analytics to collect general website usage information, such as pages viewed and browser/device type. Your photos and folders are never included.</p>
            <a href="/privacy">Read our privacy notice</a>
          </div>
          <div className="analytics-actions">
            <button type="button" className="analytics-reject" onClick={() => saveChoice("denied")}>Reject analytics</button>
            <button type="button" className="analytics-accept" onClick={() => saveChoice("granted")}>Allow analytics</button>
          </div>
        </aside>
      )}
    </>
  );
}

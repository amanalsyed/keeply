import type { Metadata } from "next";
import { Poppins } from "next/font/google";
import { ScrollReveal } from "../components/ScrollReveal";
import { AnalyticsConsent } from "../components/AnalyticsConsent";
import "../site.css";

const poppins = Poppins({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  display: "swap",
  variable: "--font-poppins",
});

export const metadata: Metadata = {
  metadataBase: new URL("https://www.trykeeply.live"),
  verification: {
    google: "ko6lTMYRgQP26YnDoBNNGnL41zaow8acpoJIcZ8S34Y",
  },
  title: "Keeply — Sort your photos, one at a time",
  description:
    "A simple Windows app to keep, trash, or save photos to an album. Your photos stay on your PC.",
  icons: { icon: "/keeply-logo.svg" },
  openGraph: {
    type: "website",
    url: "/",
    siteName: "Keeply",
    title: "Keeply — Sort your photos, one at a time",
    description:
      "A simple Windows app to keep, trash, or save photos to an album. Your photos stay on your PC.",
    images: [
      {
        url: "/og-image.png",
        width: 1200,
        height: 630,
        alt: "Keeply — Sort your photos, one at a time. Your photos stay on your PC.",
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: "Keeply — Sort your photos, one at a time",
    description:
      "A simple Windows app to keep, trash, or save photos to an album. Your photos stay on your PC.",
    images: ["/og-image.png"],
  },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className={poppins.variable}>
      <body>
        <ScrollReveal />
        <AnalyticsConsent />
        {children}
      </body>
    </html>
  );
}

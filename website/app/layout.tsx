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
  title: "Keeply — Organize, sort, compare, compress, and convert photos",
  description:
    "Organize large photo libraries by date, camera, location, and more. Sort, find duplicates, compress, and convert photos locally on Windows.",
  icons: { icon: "/keeply-logo.svg" },
  openGraph: {
    type: "website",
    url: "/",
    siteName: "Keeply",
    title: "Keeply — Organize, sort, compare, compress, and convert photos",
    description:
      "Organize large photo libraries by date, camera, location, and more. Sort, find duplicates, compress, and convert photos locally on Windows.",
    images: [
      {
        url: "/og-image.png",
        width: 1200,
        height: 630,
        alt: "Keeply — Organize, sort, compare, compress, and convert photos locally on Windows.",
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: "Keeply — Organize, sort, compare, compress, and convert photos",
    description:
      "Organize large photo libraries by date, camera, location, and more. Sort, find duplicates, compress, and convert photos locally on Windows.",
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

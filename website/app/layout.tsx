import type { Metadata } from "next";
import { Poppins } from "next/font/google";
import { ScrollReveal } from "../components/ScrollReveal";
import "../site.css";

const poppins = Poppins({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  display: "swap",
  variable: "--font-poppins",
});

export const metadata: Metadata = {
  title: "Keeply — A lighter photo folder",
  description:
    "A fast, private way to sort a folder of photos. Keep, trash, or album each photo with one key.",
  icons: { icon: "/keeply-logo.svg" },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className={poppins.variable}>
      <body>
        <ScrollReveal />
        {children}
      </body>
    </html>
  );
}

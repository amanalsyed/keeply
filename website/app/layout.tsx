import type { Metadata } from "next";
import "../site.css";

export const metadata: Metadata = {
  title: "Keeply — A lighter photo folder",
  description:
    "A fast, private way to sort a folder of photos. Keep, trash, or album each photo with one key.",
  icons: { icon: "/keeply-logo.svg" },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}

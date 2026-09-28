import type { ReactNode } from "react";
import { SiteFrame } from "./SiteChrome";

export function PolicyPage({ title, children }: { title: string; children: ReactNode }) {
  return (
    <SiteFrame>
      <main className="legal wrap">
        <div className="eyebrow">POLICY</div>
        <h1>{title}</h1>
        {children}
      </main>
    </SiteFrame>
  );
}

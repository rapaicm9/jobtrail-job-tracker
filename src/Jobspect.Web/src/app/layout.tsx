import type { Metadata } from "next";
import localFont from "next/font/local";
import "./globals.css";

// Self-hosted rather than fetched: the Content-Security-Policy allows
// `font-src 'self'` and nothing else, so a font from a CDN would be blocked at
// runtime. One variable file covers every weight the UI uses. Licence in
// ../styles/fonts/OFL.txt.
const geistSans = localFont({
  src: "../styles/fonts/geist-variable.woff2",
  variable: "--font-geist-sans",
  weight: "100 900",
  display: "swap",
});

export const metadata: Metadata = {
  title: "Jobspect",
  description: "Track every job application through one pipeline.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${geistSans.variable} h-full antialiased`}>
      <body className="min-h-full flex flex-col">{children}</body>
    </html>
  );
}

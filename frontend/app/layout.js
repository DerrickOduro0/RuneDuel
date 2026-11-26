import "./fonts.css";
import "./globals.css";
import "./parchment.css";
import "./refine.css";

export const metadata = {
  title: "Rune Duel",
  description: "Climb the ladder. Master your class."
};

export const viewport = { width: "device-width", initialScale: 1, themeColor: "#0b0e13" };

export default function RootLayout({ children }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}

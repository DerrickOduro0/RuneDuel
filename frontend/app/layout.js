import "./globals.css";
import "./parchment.css";

export const metadata = {
  title: "Rune Duel",
  description: "Climb the ladder. Master your class."
};

export default function RootLayout({ children }) {
  return (
    <html lang="en">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="" />
        <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Cormorant+Garamond:wght@500;600;700&family=Noto+Sans+Runic&display=swap" />
      </head>
      <body>{children}</body>
    </html>
  );
}

// API calls go to the game server. Locally that is the API on port 5187;
// on Vercel, set API_URL to the Render address (for example https://rune-duel-api.onrender.com).
const apiUrl = (process.env.API_URL || "http://127.0.0.1:5187").replace(/\/+$/, "");

const nextConfig = {
  async rewrites() {
    return [
      {
        source: "/api/v1/:path*",
        destination: `${apiUrl}/api/v1/:path*`
      }
    ];
  }
};

export default nextConfig;

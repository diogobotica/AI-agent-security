import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Do not reveal the framework in responses.
  poweredByHeader: false,
  reactStrictMode: true,
};

export default nextConfig;

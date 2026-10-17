import { defineConfig } from "cf/config";

// Static site for emberpc.uk. Observability stays off so no request logs are kept beyond what Cloudflare needs to serve the site.
export default defineConfig({
  worker: {
    name: "emberpc",
    compatibilityDate: "2026-10-06",
    domains: ["emberpc.uk", "www.emberpc.uk"],
  },
});

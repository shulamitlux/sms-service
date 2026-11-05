# sms-web

Angular UI for the SMS service. See the root README.

- `npm start` – dev server on http://localhost:4200, `/api` is proxied to http://localhost:8080 (`proxy.conf.json`).
- `npm run build` – production build (used by the Dockerfile, served by nginx which proxies `/api` to the `api` container).

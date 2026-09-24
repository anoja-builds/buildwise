# React + Vite

## Quality responsive regression tests

The browser suite renders the existing application with explicit API fixtures at
390px, 768px, and 1440px. It checks real layout geometry, local table scrolling,
NCR forms, long corrective actions, expanded agent output, and sidebar navigation.
It does not connect to an API or database. Unit/component tests remain available
through `npm test -- --run`.

```sh
npm ci
npx playwright install chromium
npm run test:responsive
```

To use an already-installed Chrome on Windows PowerShell instead:

```powershell
$env:PLAYWRIGHT_CHANNEL = 'chrome'
npm.cmd run test:responsive
```

The suite starts and stops its own Vite server on `127.0.0.1:5191`; that port must
be free. It overrides `VITE_API_BASE_URL` only for the test process and intercepts
every API request. Screenshots and the JSON report go to the repository's ignored
`logs/quality-responsive/` and `logs/quality-responsive-results.json`.

This template provides a minimal setup to get React working in Vite with HMR and some Oxlint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the Oxlint configuration

If you are developing a production application, we recommend using TypeScript with type-aware lint rules enabled. Check out the [TS template](https://github.com/vitejs/vite/tree/main/packages/create-vite/template-react-ts) for information on how to integrate TypeScript and Oxlint's TypeScript related rules in your project.

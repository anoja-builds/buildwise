# Quality responsive overflow fix

24 September 2026. Branch: `feature/quality-inspection`.

## Cause and correction

The shared `.stack` class uses CSS Grid without defining its columns. The Quality
workspace inherited an implicit column with an automatic minimum size. The shared
`.data-table` has `min-width: 700px`; its intrinsic minimum propagated through the
card into that grid column. At a 390px viewport, the workspace was 358px wide but
the cards expanded to 750px, producing the observed 766px document width.

`AppLayout` already allows its main content column to shrink with `min-width: 0`.
The shared `.table-wrap` already supplies `overflow-x: auto`. Neither needed a
change. The regression test reproduced the original **376px horizontal overflow**
before applying the fix.

The only production-style change is:

```css
.quality-workspace { min-width: 0; grid-template-columns: minmax(0, 1fr); }
```

The zero minimum lets the quality grid column fit the available width. Wide tables
now scroll inside their existing wrappers. No content is hidden, table minimum
width is preserved, and the page itself no longer scrolls horizontally. Shared
styles, AppLayout, React components, navigation logic, authentication, backend
rules, and schema were not changed in this task.

## Actual verification

| Check | Result |
| --- | --- |
| Pre-fix Chrome regression at 390px | Failed as expected: 376px overflow, total width 766px |
| Chrome at 390px | All 3 browser tests passed; full-page inspection screenshot is 390px wide |
| Chrome at 768px | All 3 browser tests passed; full-page inspection screenshot is 768px wide |
| Chrome at 1440px | All 3 browser tests passed; 268px desktop sidebar and main-column position preserved |
| `npm.cmd run test:responsive` with `PLAYWRIGHT_CHANNEL=chrome` | **9 passed**, 0 failed/skipped/flaky; 20.9 seconds; exit 0 |
| `npm.cmd test -- --run` | **54 passed**, 8 files; exit 0 |
| `npm.cmd run lint` | Exit 0; 7 existing warnings in procurement/shared authentication, none added by this fix |
| `npm.cmd run build` | Passed; 58 modules transformed |
| `git diff --check` | Passed |

The nine real-browser tests cover history/detail tables, NCR list/create/detail,
long corrective-action references, editable controls, expanded structured agent
output, error states, and sidebar/Back/Cancel navigation at all three widths.
They assert document and element geometry and verify that overflow is not hidden
on the page or its ancestors. At narrow widths they scroll the table and reach
its last-column action without scrolling the document. Desktop tables fit without
horizontal scrolling.

These are layout regressions using explicit API fixtures in the real React app,
not a fresh live backend/provider integration test. Every API request is
intercepted; no database was accessed. The earlier live API/PostgreSQL browser
smoke results remain in [quality-browser-smoke.md](quality-browser-smoke.md).
Successful live agent analysis is still unverified; this CSS change does not
change that limitation or the previously noted active-sidebar reselection behavior.

The browser runner initially stalled while terminating its test server inside the
Windows sandbox. The final approved run outside the sandbox completed all nine
tests and server teardown normally. The existing Node/jsdom engine-version warning
also remains; package versions unrelated to Playwright were not changed.

## Screenshots and raw report

The corrected phone, tablet, and desktop inspection screenshots were visually
reviewed. Test screenshots use layout fixtures, not live database records.

- [390px inspection](../../logs/quality-responsive/quality-responsive.pw.js-h-43853--and-navigation-still-works-phone-390/inspection.png)
- [768px inspection](../../logs/quality-responsive/quality-responsive.pw.js-h-43853--and-navigation-still-works-tablet-768/inspection.png)
- [1440px inspection](../../logs/quality-responsive/quality-responsive.pw.js-h-43853--and-navigation-still-works-desktop-1440/inspection.png)
- [JSON browser test report](../../logs/quality-responsive-results.json)

These artifacts are local files under ignored `logs/`; attach them to a PR manually
if desired. Running the browser suite regenerates them.

## Files changed for this fix

Earlier uncommitted quality implementation changes were preserved. This follow-up
changed only:

| File | Purpose |
| --- | --- |
| [quality.css](../../web/buildwise-web/src/Features/quality/quality.css) | Scoped grid sizing fix and cause comment |
| [quality-responsive.pw.js](../../web/buildwise-web/e2e/quality-responsive.pw.js) | Nine real-browser layout/navigation regressions across three widths |
| [playwright.config.js](../../web/buildwise-web/playwright.config.js) | Isolated test server, viewports, browser selection, artifact paths |
| [package.json](../../web/buildwise-web/package.json) | Test-only Playwright dependency and `test:responsive` command |
| [package-lock.json](../../web/buildwise-web/package-lock.json) | Lock Playwright test dependencies |
| [README.md](../../web/buildwise-web/README.md) | Browser regression setup and execution instructions |
| [quality-responsive-fix.md](quality-responsive-fix.md) | This verification report |

Ready for review: the responsive defect is fixed and the requested checks pass.
No commit, push, branch switch, or merge was performed.

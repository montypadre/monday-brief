# Accessibility

Target: WCAG 2.1 Level AA. Verified by keyboard walkthrough, Lighthouse, and a VoiceOver pass.

## What was built in

| Criterion | How |
|---|---|
| 1.1.1 Non-text content | Chart has a text alternative: a full data table under it, plus a descriptive `aria-label` |
| 1.3.1 Info and relationships | Semantic headings, one `h1`, `caption` and `scope` on every table, product names as row headers |
| 1.4.1 Use of color | Deltas varry a symbol and the words "up"/"down"; chart series differ by dash pattern as well as color |
| 1.4.10 Reflow | Single-column layout, relative units, no horizontal scroll at 200% zoom except inside data tables |
| 2.1.1 Keyboard | Every control reachable and operable by keyboard; no traps |
| 2.4.1 Bypass blocks | Skip link as the first focusable element |
| 2.4.7 Focus visible | 3px outline with offset on all focusable elements |
| 2.5.8 Target size | Interactive targets at least 44px tall |
| 3.3.2 Labels | Every input has a visible label; the radio group is a `fieldset` with a `legend` |
| 4.1.3 Status messages | Answers announce through an `aria-live="polite"` region without moving focus |
| 2.3.3 Animation | Chart animation and transitions disabled under `prefers-reduced-motion` |

## Contrast, measured

Measured with the WebAIM contrast checker against the page background .`#F6F7F5`.

| Use | Color | Ratio | Requirement | Result |
|---|---|---|---|---|
| Labels, hints, alert text | `#4A5A57` | 6.75:1 | 4.5:1 | AA pass |
| Declines | `#9B3B2E` | 6.38:1 | 4.5:1 | AA pass |
| Increases | `#3F6B4A` | 5.72:1 | 4.5:1 | AA pass |
| Button label on teal | `#FFFFFF` on `#1F4D46` | 9.5:1 | 4.5:1 | AA pass |
| Alert bar (non-text) | `#A9741B` | 3.75:1 | 3:1 | Pass |

The three text colors pass AA but not AAA (7:1). Darkening them to AAA would push the greens and reds close to black and lose the distinction between rising and falling figures, which color helps carry alongside the ▲/▼ symbols and words.

Lighthouse accessibility score on the deployed site: 100 (mobile), 100 (desktop).

## Dates in messages

Alerts and brief write dates as "August 24 to August 30, 2026" rather than `2026-08-24..2026-08-30`, because screen readers read the latter digit by digit.

## Known limits

- Chart tooltips are mouse and touch only; the data table carries the same figures for keyboard and screen reader users.
- Not tested with JAWS or NVDA on Windows.
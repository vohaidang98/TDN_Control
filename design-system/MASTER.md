# TDN Control — desktop dashboard, v1.0.2

Direction informed by UI UX Pro Max: software operations dashboard; native WinForms controls, solid surfaces rather than blur. Keep the existing TDN Media logo and gold brand accent.

- Background: #0D121C; cards: #171F2D; hover: #202C3D; borders: #2E3C50.
- Text: #F8FAFC; secondary: #A6B5CC; accent/action: #F4C76B; success: #68DDB0.
- Use Segoe UI with Vietnamese support, 9.5–11.5 pt body and 22–25 pt titles/metrics.
- Sidebar 190 logical pixels. Main area: header, summary, filter/search, scrollable app list, fixed Render LAN status, scan timestamp.
- App cards show name, installed/LAN versions, textual state, update badge and original actions. Preserve busy/update locks and original action events.
- Search/filter affects visible rows only; update-all continues to operate on the full catalog. Summary counts use the full catalog of visible-to-user apps.
- Use native Button subclasses with hover/pressed/disabled/focus states; do not replace buttons with unselectable panels.
- Keep configuration-hidden apps excluded from the dashboard and settings picker.
- Check 1080×740, 1280×860, 1366×768, self-update banner, empty and no-match states. Render status has its own scroll region and a detailed read-only dialog.

## v1.0.3
Remove the fixed Render LAN card from the applications page. Keep Render LAN in the sidebar as a dedicated details dialog; give the app list the reclaimed height.

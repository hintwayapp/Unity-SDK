# Changelog

All notable changes to this project will be documented in this file.  
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-04-13

### Added
- Initial UPM package release (`io.hintway.unity-sdk`)
- `AnalyticsManager` singleton MonoBehaviour with:
  - Immediate and batched event tracking
  - Auto-flush with configurable interval
  - Manual flush API (`FlushManualBatch`)
  - Custom per-event metadata via `SetCustomData`
  - Tenant validation on startup
  - Graceful flush on application quit / focus loss / pause
  - GDPR-friendly opt-out via `SetAnalyticsEnabled(false)`
- Assembly Definition Files for `Runtime` and `Editor`
- Custom Inspector with **Add to Scene** and **Documentation** shortcuts (`Tools → Hintway`)
- Basic Usage sample importable via Package Manager

# InfraSight Client Multi-Platform Setup

This project now supports one shared runtime flow for Quest and Android mobile.

## 1) Add provider components in the main scene

- Add `TrackablesManager` on a scene object (or keep existing).
- Add `QuestTrackableProvider` on the same object.
- Add `AndroidArCoreTrackableProvider` on the same object.
- In `TrackablesManager`, assign:
  - `Quest Provider Behaviour` -> `QuestTrackableProvider`
  - `Android Provider Behaviour` -> `AndroidArCoreTrackableProvider`
- In `AndroidArCoreTrackableProvider`, assign `ARTrackedImageManager`.

## 2) Define symbols by build profile

Use Project Settings -> Player -> Scripting Define Symbols:

- Quest build profile: `INFRASIGHT_HAS_META_MRUK`
- Android mobile build profile: `INFRASIGHT_HAS_ARFOUNDATION`
- If both packages are present and you want both code paths available in one profile, include both symbols.

## 3) Event wiring (Quest)

- Wire MRUK trackable added/removed callbacks to:
  - `QuestTrackableProvider.OnTrackableAdded`
  - `QuestTrackableProvider.OnTrackableRemoved`

## 4) Android AR flow

- Add AR Foundation objects (`AR Session` + `XR Origin`).
- On `XR Origin`, add `ARTrackedImageManager` and assign a `Reference Image Library`.
- Use reference image names as QR payload IDs (`QR_Sphere`, `QR_Cube`, etc.).

## 5) Build notes

- `Packages/manifest.json` includes AR Foundation + ARCore packages.
- Android manifest now includes camera permission and no longer requires VR headtracking for install.
